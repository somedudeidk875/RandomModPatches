using System;
using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//IIVT ships a general particle framework (GuidaSharedCode.DrawHooks) that detours fourteen of Terrarias draw methods so other code can draw at any point in the draw order
//each detour does the following every frame:
//   Main.spriteBatch.End();
//   GetClip(out rectangle, out rasterizer);          reads ScissorRectangle and RasterizerState
//   Main.spriteBatch.Begin(...);
//   OnDraw_SomeLayer?.Invoke(layer);
//   Main.spriteBatch.End();
//   SetClip(rectangle, rasterizer);                  writes both back
//that adds up to roughly 28 end/begin pairs and 28 scissor and rasterizer writes per frame
//every end on a non-empty batch issues a draw call, and every scissor or rasterizer write is a pipeline state change
//so this is not free even with nothing to draw none of it is gated
//ParticleManager puts its render method to all seventeen layers at load so the ?.Invoke is never null
//this means that render's own particles.Count == 0 early-out only happens after the batch work has already been done

namespace SOTSPerfPatch.Patches.InventoryVisualTweaks;

internal sealed class SkipIdleParticlesPatch : PerfPatch
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
		private static FieldInfo particlesByLayer;
		private static FieldInfo particlesToAdd;

		private static bool Enabled => PerfPatchConfig.Instance?.SkipIdleParticleLayers ?? false;

		public static void Install()
		{
			Type drawHooks = HookHelper.ModType(ModName, DrawHooksType);
			Type manager = HookHelper.ModType(ModName, ManagerType);

			managerInstance = manager.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
				?? throw new MissingMemberException($"{ManagerType}.Instance was not found.");

			particlesByLayer = manager.GetField("particlesByLayer", BindingFlags.Instance | BindingFlags.Public)
				?? throw new MissingFieldException($"{ManagerType}.particlesByLayer was not found.");

			particlesToAdd = manager.GetField("particlesToAdd", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingFieldException($"{ManagerType}.particlesToAdd was not found.");

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
			particlesByLayer = null;
			particlesToAdd = null;
		}

		//false only when the whole particle system is empty (nothing waiting in the queue and nothing in any layer)
		//this reads the two collections directly rather than the manager's ParticleCount because AddParticle appends to particlesToAdd without recalculating that count
		private static bool AnyParticles()
		{
			object instance = managerInstance.GetValue(null);
			if (instance == null)
				return true;

			if (particlesToAdd.GetValue(instance) is ICollection pending && pending.Count > 0)
				return true;

			if (particlesByLayer.GetValue(instance) is not IDictionary byLayer)
				return true;

			foreach (DictionaryEntry entry in byLayer) {
				if (entry.Value is ICollection layer && layer.Count > 0)
					return true;
			}

			return false;
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

		private static void OnPostUpdateDusts(orig_PostUpdateDusts orig, object self)
		{
			if (!Enabled || AnyParticles())
				orig(self);
		}
	}
}
