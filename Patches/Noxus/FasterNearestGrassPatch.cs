using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using MonoMod.Cil;
using NoxusBoss.Content.Tiles.GenesisComponents;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria.ModLoader;

//while drawing genesis grass WOTG finds the nearest grass tile to each on screen tile with grassPositions.OrderBy(distance).First() 
//that sorts the whole list and allocates while doing it once per tile thousands of sorts per frame once genesis exists

namespace SOTSPerfPatch.Patches.Noxus;

internal sealed class FasterNearestGrassPatch : PerfPatch
{
	public override string Name => "Genesis grass: nearest tile without sorting";

	public override string TargetMod => "NoxusBoss";

	public override void Apply() => Hooks.Install();

	[JITWhenModsEnabled("NoxusBoss")]
	private static class Hooks
	{
		public static void Install()
		{
			//the sort lives in a compiler generated method, so the exact name is not stable
			int patched = 0;
			foreach (MethodInfo method in CandidateMethods()) {
				if (!CallsOrderBy(method))
					continue;

				try {
					MonoModHooks.Modify(method, ReplaceSortWithMinimum);
					patched++;
				}
				catch (Exception)
				{
				
				}
			}

			if (patched == 0)
				throw new InvalidOperationException("The nearest-grass sort was not found. Wrath of the Gods may have been updated.");
		}

		//every method of the grass renderer and of its compiler generated nested classes. 
		private static List<MethodInfo> CandidateMethods()
		{
			const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
			var methods = new List<MethodInfo>();
			var types = new List<Type> { typeof(GenesisGrassTargetManager) };
			types.AddRange(typeof(GenesisGrassTargetManager).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));

			foreach (Type type in types) {
				foreach (MethodInfo method in type.GetMethods(All)) {
					if (!method.IsAbstract && method.GetMethodBody() != null)
						methods.Add(method);
				}
			}
			return methods;
		}

		//cheap pre-filter so only methods that really call OrderBy are hooked
		private static bool CallsOrderBy(MethodInfo method)
		{
			byte[] il;
			try {
				il = method.GetMethodBody()?.GetILAsByteArray();
			}
			catch (Exception) {
				return false;
			}

			if (il == null)
				return false;

			for (int i = 0; i + 4 < il.Length; i++) {
				//0x28 is call
				if (il[i] != 0x28)
					continue;

				try {
					int token = BitConverter.ToInt32(il, i + 1);
					if (method.Module.ResolveMethod(token)?.Name == "OrderBy")
						return true;
				}
				catch (Exception) {
				}
			}
			return false;
		}

		private static void ReplaceSortWithMinimum(ILContext context)
		{
			var cursor = new ILCursor(context);

			bool found = cursor.TryGotoNext(MoveType.Before,
				i => i.MatchCall(out MethodReference ordering) && ordering.Name == "OrderBy",
				i => i.MatchCall(out MethodReference first) && first.Name == "First");

			if (!found)
				throw new InvalidOperationException("No OrderBy/First pair here.");

			cursor.Remove();
			cursor.Remove();
			cursor.EmitDelegate<Func<IEnumerable<Vector2>, Func<Vector2, float>, Vector2>>(NearestByKey);
		}

		public static Vector2 NearestByKey(IEnumerable<Vector2> points, Func<Vector2, float> distanceOf)
		{
			if (!(PerfPatchConfig.Instance?.FasterGenesisGrassNearest ?? true))
				return System.Linq.Enumerable.First(System.Linq.Enumerable.OrderBy(points, distanceOf));

			bool any = false;
			Vector2 nearest = Vector2.Zero;
			float nearestDistance = 0f;

			foreach (Vector2 point in points) {
				float distance = distanceOf(point);
				if (!any || distance < nearestDistance) {
					any = true;
					nearest = point;
					nearestDistance = distance;
				}
			}

			if (!any)
				throw new InvalidOperationException("Sequence contains no elements.");

			return nearest;
		}
	}
}
