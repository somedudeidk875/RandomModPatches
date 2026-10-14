using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

// Inventory Items Visual Tweaks ships a general particle framework (GuidaSharedCode.DrawHooks) that
// detours fourteen of Terraria's draw methods so other code can draw at any point in the draw order.
// Each detour does this, every frame:
//
//   Main.spriteBatch.End();
//   GetClip(out rectangle, out rasterizer);          // reads ScissorRectangle and RasterizerState
//   Main.spriteBatch.Begin(...);
//   OnDraw_SomeLayer?.Invoke(layer);
//   Main.spriteBatch.End();
//   SetClip(rectangle, rasterizer);                  // writes both back
//
// That adds up to roughly 28 End/Begin pairs and 28 scissor and rasterizer writes per frame. Every
// End on a non-empty batch issues a draw call, and every scissor or rasterizer write is a pipeline
// state change, so this is not free even with nothing to draw.
//
// None of it is gated. ParticleManager subscribes its Render method to all seventeen layers at load,
// so the ?.Invoke is never null, and Render's own "particles.Count == 0" early-out only happens after
// the batch work has already been done.
//
// This skips six of the fourteen when the particle system holds nothing at all. Those six are the ones
// that begin and end their own batch and restore the clip without leaving a different batch behind for
// the original method, so skipping them is the same as running them with nothing to draw. The other
// eight end Terraria's batch and begin a replacement with their own parameters before calling the
// original, which changes how vanilla draws, so they are left alone.
namespace SOTSPerfPatch.Patches.InventoryVisualTweaks;

internal sealed class SkipIdleParticleLayersPatch : PerfPatch
{
	public override string Name => "Visual Tweaks: skip idle particle layers";

	public override string TargetMod => "InventoryVisualTweaks";

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string ModName = "InventoryVisualTweaks";
		private const string DrawHooksType = "GuidaSharedCode.DrawHooks";
		private const string ManagerType = "GuidaSharedCode.ParticleManager";

		// The six draw detours, plus the per-tick update. Each handler's first parameter is the vanilla
		// original, because these methods are themselves the mod's detour handlers.
		private delegate void orig_SolidTiles(object self, On_Main.orig_DoDraw_Tiles_Solid orig, Main main);
		private delegate void orig_PlayersBehindNPCs(object self, On_Main.orig_DrawPlayers_BehindNPCs orig, Main main);
		private delegate void orig_Projectiles(object self, On_Main.orig_DrawProjectiles orig, Main main);
		private delegate void orig_Players(object self, On_Main.orig_DrawPlayers_AfterProjectiles orig, Main main);
		private delegate void orig_Dust(object self, On_Main.orig_DrawDust orig, Main main);
		private delegate void orig_Interface(object self, On_Main.orig_DrawInterface orig, Main main, GameTime gameTime);
		private delegate void orig_PostUpdateDusts(object self);

		private static PropertyInfo managerInstance;
		private static PropertyInfo particleCount;

		private static bool Enabled => PerfPatchConfig.Instance?.SkipIdleParticleLayers ?? false;

		public static void Install()
		{
			Type drawHooks = HookHelper.ModType(ModName, DrawHooksType);
			Type manager = HookHelper.ModType(ModName, ManagerType);

			managerInstance = manager.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
				?? throw new MissingMemberException($"{ManagerType}.Instance was not found.");

			particleCount = manager.GetProperty("ParticleCount", BindingFlags.Instance | BindingFlags.Public)
				?? throw new MissingMemberException($"{ManagerType}.ParticleCount was not found.");

			var solidTiles = HookHelper.Method(drawHooks, "Draw_BeforeSolidTiles",
				typeof(On_Main.orig_DoDraw_Tiles_Solid), typeof(Main));
			var playersBehindNPCs = HookHelper.Method(drawHooks, "Draw_BeforePlayersBehindNPCs",
				typeof(On_Main.orig_DrawPlayers_BehindNPCs), typeof(Main));
			var projectiles = HookHelper.Method(drawHooks, "Draw_BeforeProjectiles",
				typeof(On_Main.orig_DrawProjectiles), typeof(Main));
			var players = HookHelper.Method(drawHooks, "Draw_BeforePlayers",
				typeof(On_Main.orig_DrawPlayers_AfterProjectiles), typeof(Main));
			var dust = HookHelper.Method(drawHooks, "Draw_BeforeAndAfterDust",
				typeof(On_Main.orig_DrawDust), typeof(Main));
			var interfaceLayer = HookHelper.Method(drawHooks, "Draw_OnInterface",
				typeof(On_Main.orig_DrawInterface), typeof(Main), typeof(GameTime));
			var postUpdateDusts = HookHelper.Method(manager, "PostUpdateDusts");

			MonoModHooks.Add(solidTiles, OnSolidTiles);
			MonoModHooks.Add(playersBehindNPCs, OnPlayersBehindNPCs);
			MonoModHooks.Add(projectiles, OnProjectiles);
			MonoModHooks.Add(players, OnPlayers);
			MonoModHooks.Add(dust, OnDust);
			MonoModHooks.Add(interfaceLayer, OnInterface);
			MonoModHooks.Add(postUpdateDusts, OnPostUpdateDusts);
		}

		public static void Reset()
		{
			managerInstance = null;
			particleCount = null;
		}

		// False only when the whole particle system is empty. ParticleCount covers every layer plus the
		// pending queue, so zero means there is nothing to update, light or draw anywhere. Anything that
		// cannot be read falls back to running the original.
		private static bool AnyParticles()
		{
			object instance = managerInstance.GetValue(null);
			if (instance == null)
				return false;

			return (int)particleCount.GetValue(instance) > 0;
		}

		private static void OnSolidTiles(orig_SolidTiles handler, object self, On_Main.orig_DoDraw_Tiles_Solid orig, Main main)
		{
			if (!Enabled || AnyParticles())
				handler(self, orig, main);
			else
				orig(main);
		}

		private static void OnPlayersBehindNPCs(orig_PlayersBehindNPCs handler, object self, On_Main.orig_DrawPlayers_BehindNPCs orig, Main main)
		{
			if (!Enabled || AnyParticles())
				handler(self, orig, main);
			else
				orig(main);
		}

		private static void OnProjectiles(orig_Projectiles handler, object self, On_Main.orig_DrawProjectiles orig, Main main)
		{
			if (!Enabled || AnyParticles())
				handler(self, orig, main);
			else
				orig(main);
		}

		private static void OnPlayers(orig_Players handler, object self, On_Main.orig_DrawPlayers_AfterProjectiles orig, Main main)
		{
			if (!Enabled || AnyParticles())
				handler(self, orig, main);
			else
				orig(main);
		}

		private static void OnDust(orig_Dust handler, object self, On_Main.orig_DrawDust orig, Main main)
		{
			if (!Enabled || AnyParticles())
				handler(self, orig, main);
			else
				orig(main);
		}

		private static void OnInterface(orig_Interface handler, object self, On_Main.orig_DrawInterface orig, Main main, GameTime gameTime)
		{
			if (!Enabled || AnyParticles())
				handler(self, orig, main, gameTime);
			else
				orig(main, gameTime);
		}

		// PostUpdateDusts runs UpdateParticles and UpdateParticleLighting every tick. The lighting pass
		// walks GetAllParticles(), which is a SelectMany, and UpdateParticles finishes by recalculating
		// the count with particlesByLayer.Values.Sum(...). Both allocate LINQ iterators every tick even
		// with nothing alive. ParticleCount of zero means the layers and the pending queue are both
		// empty, so there is nothing to flush, update or light.
		private static void OnPostUpdateDusts(orig_PostUpdateDusts orig, object self)
		{
			if (!Enabled || AnyParticles())
				orig(self);
		}
	}
}
