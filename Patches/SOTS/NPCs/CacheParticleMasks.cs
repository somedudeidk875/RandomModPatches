using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using SOTS.NPCs.Boss.Curse;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria.ModLoader;

//PharaohsCurse.SpawnPassiveDust is how every part of the Pharaoh fight spawns its foam
//it picks spawn points off the opaque pixels of a sprite and to find them it does this every single call:
//	Color[] data = new Color[width * height];
//	texture.GetData(data);
//	for (int i = 0; i < width * height; i++)
//		if (data[i].A >= 255 && Main.rand.NextBool(rate)) ...
//Texture2D.GetData is a readback
//it stalls the pipeline until the card is done then copies the whole texture back across the bus into managed memory
//it is one of the slowest things a frame can do and the array it copies into is allocated fresh every call
//Pharaoh's own sprite is large enough that the array lands on the large object heap so every call is also a gen2 sized piece of garbage
//the callers add up fast
//the boss itself calls it five times a tick and the following all call it once or twice per instance per tick: 
//  CurseWave
//  GasBlast 
//  CurseArm 
//  CurseRing 
//  SmallGas 
//so a fight with a few of those out is ten to twenty full texture readbacks and large object allocations every tick

namespace SOTSPerfPatch.Patches.SOTS.NPCs;

internal sealed class CacheParticleMasks : PerfPatch
{
	public override string Name => "Pharaoh's Curse particle mask readback fix";

	public override string TargetMod => "SOTS";

	public override bool ClientOnly => true;

	private static readonly Dictionary<Texture2D, Color[]> masks = new();

	public override void Apply() => Hooks.Install();

	internal static void Reset() => masks.Clear();

	private static bool Enabled => PerfPatchConfig.Instance?.CacheBossParticleMasks ?? false;

	//replaces the new Color[width * height] with the pixels this texture was already read back for
	private static Color[] Mask(int length, Texture2D texture)
	{
		if (!Enabled || texture == null)
			return new Color[length];

		if (masks.TryGetValue(texture, out Color[] mask))
			return mask;

		mask = new Color[texture.Width * texture.Height];
		texture.GetData(mask);
		masks[texture] = mask;
		return mask;
	}

	//replaces the GetData call 
	private static void Read(Texture2D texture, Color[] into)
	{
		if (texture == null || into == null)
			return;

		if (!Enabled || !masks.TryGetValue(texture, out Color[] mask) || !ReferenceEquals(mask, into))
			texture.GetData(into);
	}

	[JITWhenModsEnabled("SOTS")]
	private static class Hooks
	{
		public static void Install()
		{
			var spawn = HookHelper.Method(typeof(PharaohsCurse), nameof(PharaohsCurse.SpawnPassiveDust),
				typeof(Texture2D), typeof(Vector2), typeof(float), typeof(List<CurseFoam>),
				typeof(float), typeof(int), typeof(int), typeof(float), typeof(float));

			//the edit pushes argument zero as the texture which is only right while this is static
			if (!spawn.IsStatic)
				throw new InvalidOperationException("SpawnPassiveDust is no longer static. SOTS may have been updated.");

			MonoModHooks.Modify(spawn, EditSpawn);
		}

		//two single instruction swaps both of which leave the stack exactly as it was
		private static void EditSpawn(ILContext il)
		{
			var c = new ILCursor(il);

			int arrays = 0;
			while (c.TryGotoNext(MoveType.Before, i => i.MatchNewarr<Color>()))
			{
				arrays++;
				c.Index++;
			}

			if (arrays != 1)
				throw new InvalidOperationException(
					$"Expected exactly one Color array in SpawnPassiveDust, found {arrays}. SOTS may have been updated.");

			c.Index = 0;
			c.GotoNext(MoveType.Before, i => i.MatchNewarr<Color>());
			c.Remove();
			c.Emit(OpCodes.Ldarg_0);
			c.EmitDelegate<Func<int, Texture2D, Color[]>>(Mask);

			int reads = 0;
			c.Index = 0;
			while (c.TryGotoNext(MoveType.Before, i => IsGetData(i)))
			{
				reads++;
				c.Index++;
			}

			if (reads != 1)
				throw new InvalidOperationException(
					$"Expected exactly one GetData call in SpawnPassiveDust, found {reads}. SOTS may have been updated.");

			c.Index = 0;
			c.GotoNext(MoveType.Before, i => IsGetData(i));
			c.Remove();
			c.EmitDelegate<Action<Texture2D, Color[]>>(Read);
		}

		private static bool IsGetData(Instruction i) =>
			(i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call)
			&& i.Operand is MethodReference method
			&& method.Name == "GetData"
			&& method.Parameters.Count == 1;
	}
}
