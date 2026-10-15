using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SOTS.Projectiles.Celestial;
using SOTS.Projectiles.Pyramid;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Patches.SOTS.Rendering;

//four SOTS projectiles draw a darkness overlay by building a screen-sized Texture2D on the CPU
//each time they rebuild it they create a new texture and drop the old one without disposing it
//the projectiles are:
//   PharaohShade (rebuilds it almost every frame while the player moves)
//   SubspaceEye and SubspaceEyeWall (rebuild it a few times each)
//   ClairvoyanceShade (rebuilds it occasionally)
//undisposed textures stay in video memory until the garbage collector finalizes their small C# wrappers
//this can take a very long time so video memory fills up.

internal sealed class FreeDarknessTexturesPatch : PerfPatch
{
	public override string Name => "Shade overlay texture leak fix";

	public override string TargetMod => "SOTS";

	public static int TexturesFreed { get; private set; }

	public override void Apply() => Hooks.Install();

	internal static void OnUpdate() => Hooks.Tick();

	[JITWhenModsEnabled("SOTS")]
	private static class Hooks
	{
		private delegate bool orig_PreDraw(ModProjectile self, ref Color lightColor);

		private static readonly Dictionary<ModProjectile, Texture2D> lastTexture = new();
		private static readonly List<Texture2D> pendingDispose = new();
		private static readonly List<ModProjectile> deadOwners = new();
		private static Dictionary<System.Type, FieldInfo> fields;
		private static bool installed;

		private static bool Enabled => PerfPatchConfig.Instance?.FixShadeTextureLeaks ?? false;

		public static void Install()
		{
			//find every texture field and PreDraw method first
			fields = new Dictionary<System.Type, FieldInfo>();
			fields[typeof(PharaohShade)] = TextureField(typeof(PharaohShade), "TheShadow");
			fields[typeof(SubspaceEye)] = TextureField(typeof(SubspaceEye), "ShadowTexture");
			fields[typeof(SubspaceEyeWall)] = TextureField(typeof(SubspaceEyeWall), "ShadowTexture");
			fields[typeof(ClairvoyanceShade)] = TextureField(typeof(ClairvoyanceShade), "shadowTexture");

			var preDraws = new List<MethodInfo>();
			foreach (System.Type type in fields.Keys)
				preDraws.Add(HookHelper.Method(type, "PreDraw", typeof(Color).MakeByRefType()));

			foreach (MethodInfo preDraw in preDraws)
				MonoModHooks.Add(preDraw, OnPreDraw);

			lastTexture.Clear();
			pendingDispose.Clear();
			TexturesFreed = 0;
			installed = true;
		}

		private static FieldInfo TextureField(System.Type type, string fieldName)
		{
			FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (field == null || field.FieldType != typeof(Texture2D))
				throw new System.MissingFieldException($"{type.FullName}.{fieldName} (Texture2D) was not found. SOTS may have been updated.");

			return field;
		}

		private static bool OnPreDraw(orig_PreDraw orig, ModProjectile self, ref Color lightColor)
		{
			bool result = orig(self, ref lightColor);

			if (!Enabled || !fields.TryGetValue(self.GetType(), out FieldInfo field))
				return result;

			var current = (Texture2D)field.GetValue(self);
			if (lastTexture.TryGetValue(self, out Texture2D previous) && previous != null && !ReferenceEquals(previous, current))
				pendingDispose.Add(previous);

			lastTexture[self] = current;
			return result;
		}

		public static void Tick()
		{
			if (!installed)
				return;

			foreach (Texture2D texture in pendingDispose) {
				if (!texture.IsDisposed) {
					texture.Dispose();
					TexturesFreed++;
				}
			}
			pendingDispose.Clear();

			foreach (KeyValuePair<ModProjectile, Texture2D> pair in lastTexture) {
				ModProjectile owner = pair.Key;
				Projectile projectile = owner.Projectile;
				bool gone = projectile == null || !projectile.active || !ReferenceEquals(projectile.ModProjectile, owner);
				if (!gone)
					continue;

				deadOwners.Add(owner);
				if (pair.Value != null && Enabled)
					pendingDispose.Add(pair.Value);
			}

			foreach (ModProjectile owner in deadOwners)
				lastTexture.Remove(owner);
			deadOwners.Clear();
		}
	}
}
