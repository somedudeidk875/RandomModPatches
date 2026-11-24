using System;
using System.Collections;
using System.Reflection;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//(i mightve hit the big one with this)
//three of the mod's full screen effects run whether or not anything is feeding them
//CosmosMetaball and ShakerSludgeMetaball both detour DoDraw_DrawNPCsOverTiles and both call content.Request() without restrictions
//the request makes vanilla prepare their render target which clears a screen sized target and runs their draw delegate
//then the handler binds a shader and blits that whole target over the screen
//the cosmos delegate also runs (shown below):
//	Main.projectile.Where(n => n.active && n.type == ModContent.ProjectileType<StellarBlackHole>())
//this allocates a LINQ chain and calls a generic type lookup once per projectile slot and the shaker sludge delegate runs the same shape of loop with an empty body
//so two screen sized clears, two screen sized shader passes and two thousand slot scans a frame for a boss attack and a weapon that are not usually anywhere nearby
//DarkSludge is worse because it pays twice a frame
//DrawShapes on CheckMonoliths does a lot of stuff like
//  allocate a screen sized render target the first time it runs and then clears it every frame
//  scans all projectile slots through LINQ with ModContent.ProjectileType inside the predicate
//  and walks the mod's particle list through a Concat and a Where
//DrawSludge on DoDraw_Tiles_Solid then requests three assets and uploads six shader parameters and blits that target scaled over the screen every frame
//it does this even when the target it just cleared is entirely transparent

namespace SOTSPerfPatch.Patches.CalamityHunt;

internal sealed class SkipEmptyScreenPasses : PerfPatch
{
	public override string Name => "Hunt of the Old God: skip empty full screen passes";

	public override string TargetMod => "CalamityHunt";

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string ModName = "CalamityHunt";
		private const string CosmosType = "CalamityHunt.Common.Graphics.RenderTargets.CosmosMetaball";
		private const string ShakerType = "CalamityHunt.Common.Graphics.RenderTargets.ShakerSludgeMetaball";
		private const string SludgeType = "CalamityHunt.Content.Projectiles.Weapons.Ranged.DarkSludge";
		private const string ChunkType = "CalamityHunt.Content.Particles.DarkSludgeChunk";
		private const string RendererType = "CalamityHunt.Common.Systems.Particles.DefaultParticleRenderer";
		private const string ModClassType = "CalamityHunt.CalamityHunt";

		private delegate void orig_DrawTarget(object self, On_Main.orig_DoDraw_DrawNPCsOverTiles orig, Main main);
		private delegate void orig_Shapes(object self, On_Main.orig_CheckMonoliths orig);
		private delegate void orig_Sludge(object self, On_Main.orig_DoDraw_Tiles_Solid orig, Main main);

		private static Type rendererType;
		private static Type chunkType;
		private static FieldInfo deferredList;
		private static FieldInfo immediateList;

		private static PropertyInfo cosmosParticles;
		private static PropertyInfo shakerParticles;
		private static PropertyInfo modParticles;

		//resolved on first use rather than at load because other mods' content is not registered yet while patches are being applied
		private static int blackHoleId = -1;
		private static int darkSludgeId = -1;

		private static uint cosmosTick = uint.MaxValue;
		private static bool cosmosNeeded;
		private static uint shakerTick = uint.MaxValue;
		private static bool shakerNeeded;
		private static uint sludgeTick = uint.MaxValue;
		private static bool sludgeNeeded;

		private static bool Enabled => PerfPatchConfig.Instance?.SkipEmptyHuntDraws ?? false;

		public static void Install()
		{
			Type cosmos = HookHelper.ModType(ModName, CosmosType);
			Type shaker = HookHelper.ModType(ModName, ShakerType);
			Type sludge = HookHelper.ModType(ModName, SludgeType);
			Type modClass = HookHelper.ModType(ModName, ModClassType);

			rendererType = HookHelper.ModType(ModName, RendererType);
			chunkType = HookHelper.ModType(ModName, ChunkType);

			deferredList = rendererType.GetField("deferredParticles", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMemberException($"{RendererType}.deferredParticles was not found.");

			immediateList = rendererType.GetField("immediateParticles", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMemberException($"{RendererType}.immediateParticles was not found.");

			cosmosParticles = Particles(cosmos, CosmosType);
			shakerParticles = Particles(shaker, ShakerType);
			modParticles = Particles(modClass, ModClassType);

			var cosmosTarget = HookHelper.Method(cosmos, "DrawTarget",
				typeof(On_Main.orig_DoDraw_DrawNPCsOverTiles), typeof(Main));
			var shakerTarget = HookHelper.Method(shaker, "DrawTarget",
				typeof(On_Main.orig_DoDraw_DrawNPCsOverTiles), typeof(Main));
			var sludgeShapes = HookHelper.Method(sludge, "DrawShapes",
				typeof(On_Main.orig_CheckMonoliths));
			var sludgeDraw = HookHelper.Method(sludge, "DrawSludge",
				typeof(On_Main.orig_DoDraw_Tiles_Solid), typeof(Main));

			MonoModHooks.Add(cosmosTarget, OnCosmosTarget);
			MonoModHooks.Add(shakerTarget, OnShakerTarget);
			MonoModHooks.Add(sludgeShapes, OnSludgeShapes);
			MonoModHooks.Add(sludgeDraw, OnSludgeDraw);
		}

		private static PropertyInfo Particles(Type owner, string name) =>
			owner.GetProperty("Particles", BindingFlags.Static | BindingFlags.Public)
				?? throw new MissingMemberException($"{name}.Particles was not found.");

		//also drops the Type keys so the other mod's assembly can unload
		public static void Reset()
		{
			rendererType = null;
			chunkType = null;
			deferredList = null;
			immediateList = null;
			cosmosParticles = null;
			shakerParticles = null;
			modParticles = null;
			blackHoleId = -1;
			darkSludgeId = -1;
			cosmosTick = uint.MaxValue;
			shakerTick = uint.MaxValue;
			sludgeTick = uint.MaxValue;
		}

		//the two lists a default renderer keeps its live particles in
		private static bool Lists(object renderer, out IList deferred, out IList immediate)
		{
			deferred = null;
			immediate = null;

			if (renderer == null || deferredList == null || immediateList == null)
				return false;

			if (rendererType == null || !rendererType.IsInstanceOfType(renderer))
				return false;

			deferred = deferredList.GetValue(renderer) as IList;
			immediate = immediateList.GetValue(renderer) as IList;
			return deferred != null && immediate != null;
		}

		private static bool AnyParticles(PropertyInfo renderer)
		{
			if (renderer == null)
				return true;

			if (!Lists(renderer.GetValue(null), out IList deferred, out IList immediate))
				return true;

			return deferred.Count > 0 || immediate.Count > 0;
		}

		//reads the real lists instead of the Particles property which allocates a Concat every time it is touched
		private static bool AnySludgeChunk()
		{
			if (chunkType == null || modParticles == null)
				return true;

			if (!Lists(modParticles.GetValue(null), out IList deferred, out IList immediate))
				return true;

			for (int i = 0; i < deferred.Count; i++) {
				if (chunkType.IsInstanceOfType(deferred[i]))
					return true;
			}

			for (int i = 0; i < immediate.Count; i++) {
				if (chunkType.IsInstanceOfType(immediate[i]))
					return true;
			}

			return false;
		}

		//a type that cannot be found comes back as zero which makes every caller run the handler
		private static int ProjectileId(string internalName)
		{
			if (!ModLoader.TryGetMod(ModName, out Mod mod))
				return 0;

			try {
				return mod.Find<ModProjectile>(internalName).Type;
			}
			catch {
				return 0;
			}
		}

		private static bool AnyBlackHole()
		{
			if (blackHoleId < 0)
				blackHoleId = ProjectileId("StellarBlackHole");

			if (blackHoleId <= 0)
				return true;

			Projectile[] projectiles = Main.projectile;
			for (int i = 0; i < Main.maxProjectiles; i++) {
				if (projectiles[i].active && projectiles[i].type == blackHoleId)
					return true;
			}

			return false;
		}

		//the same test the mod's own loop makes
		private static bool AnyDarkSludge()
		{
			if (darkSludgeId < 0)
				darkSludgeId = ProjectileId("DarkSludge");

			if (darkSludgeId <= 0)
				return true;

			Projectile[] projectiles = Main.projectile;
			for (int i = 0; i < Main.maxProjectiles; i++) {
				Projectile projectile = projectiles[i];
				if (projectile.active && projectile.type == darkSludgeId && projectile.ai[0] > 0)
					return true;
			}

			return false;
		}

		private static bool CosmosNeeded()
		{
			if (cosmosTick == Main.GameUpdateCount)
				return cosmosNeeded;

			cosmosTick = Main.GameUpdateCount;
			cosmosNeeded = AnyParticles(cosmosParticles) || AnyBlackHole();
			return cosmosNeeded;
		}

		private static bool ShakerNeeded()
		{
			if (shakerTick == Main.GameUpdateCount)
				return shakerNeeded;

			shakerTick = Main.GameUpdateCount;
			shakerNeeded = AnyParticles(shakerParticles);
			return shakerNeeded;
		}

		private static bool SludgeNeeded()
		{
			if (sludgeTick == Main.GameUpdateCount)
				return sludgeNeeded;

			sludgeTick = Main.GameUpdateCount;
			sludgeNeeded = AnyDarkSludge() || AnySludgeChunk();
			return sludgeNeeded;
		}

		private static void OnCosmosTarget(orig_DrawTarget handler, object self, On_Main.orig_DoDraw_DrawNPCsOverTiles orig, Main main)
		{
			if (!Enabled || CosmosNeeded())
				handler(self, orig, main);
			else
				orig(main);
		}

		private static void OnShakerTarget(orig_DrawTarget handler, object self, On_Main.orig_DoDraw_DrawNPCsOverTiles orig, Main main)
		{
			if (!Enabled || ShakerNeeded())
				handler(self, orig, main);
			else
				orig(main);
		}

		private static void OnSludgeShapes(orig_Shapes handler, object self, On_Main.orig_CheckMonoliths orig)
		{
			if (!Enabled || SludgeNeeded())
				handler(self, orig);
			else
				orig();
		}

		private static void OnSludgeDraw(orig_Sludge handler, object self, On_Main.orig_DoDraw_Tiles_Solid orig, Main main)
		{
			if (!Enabled || SludgeNeeded())
				handler(self, orig, main);
			else
				orig(main);
		}
	}
}
