using System;
using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//Macrocosm detours four of Terraria's draw methods for its particles and three more for its rockets and none of them are gated 
//every frame (whether or not there is anything to draw) each one does the following:
//	state.SaveState(spriteBatch);
//	spriteBatch.End();
//	spriteBatch.Begin(...);
//	DrawParticles(SomeLayer);
//	spriteBatch.End();
//	spriteBatch.Begin(state);
//DrawNPCs is called twice a frame so that adds up to ~8 end/begin pairs a frame for the particles alone
//every end on a batch that has anything in it is a draw call and every Begin is a pipeline state change so this is not free with nothing to draw
//DrawParticles also opens with List<Particle> alphaBlendDrawers = new() before it looks at whether any particle is on that layer so it allocates a list per layer per frame as well
//(~6 or more a frame with an empty world)

//(i have absolutely no idea if this will actually work in the long run)

namespace SOTSPerfPatch.Patches.Macrocosm;

internal sealed class SkipEmptyDrawsPatch : PerfPatch
{
	public override string Name => "Macrocosm: skip empty particle and rocket draws";

	public override string TargetMod => "Macrocosm";

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string ModName = "Macrocosm";
		private const string ParticlesType = "Macrocosm.Common.Drawing.Particles.ParticleManager";
		private const string RocketsType = "Macrocosm.Content.Rockets.RocketManager";

		//each handler's first parameter is the vanilla original because these are Macrocosm's own detour handlers
		private delegate void orig_DrawNPCs(object self, On_Main.orig_DrawNPCs orig, Main main, bool behindTiles);
		private delegate void orig_DrawDust(object self, On_Main.orig_DrawDust orig, Main main);
		private delegate void orig_DrawBlack(object self, On_Main.orig_DrawBlack orig, Main main, bool force);
		private delegate void orig_BehindTiles(object self, On_Main.orig_DoDraw_DrawNPCsBehindTiles orig, Main main);
		private delegate void orig_PostDrawInterface(object self, SpriteBatch spriteBatch);

		private static PropertyInfo particles;
		private static PropertyInfo rocketCount;

		private static uint rocketsCheckedTick = uint.MaxValue;
		private static bool rocketsPresent;

		private static bool Enabled => PerfPatchConfig.Instance?.SkipEmptyMacrocosmDraws ?? false;

		public static void Install()
		{
			Type particleManager = HookHelper.ModType(ModName, ParticlesType);
			Type rocketManager = HookHelper.ModType(ModName, RocketsType);

			particles = particleManager.GetProperty("Particles", BindingFlags.Static | BindingFlags.Public)
				?? throw new MissingMemberException($"{ParticlesType}.Particles was not found.");

			rocketCount = rocketManager.GetProperty("InCurrentSubworldRocketCount", BindingFlags.Static | BindingFlags.Public)
				?? throw new MissingMemberException($"{RocketsType}.InCurrentSubworldRocketCount was not found.");

			var particleNPCs = HookHelper.Method(particleManager, "DrawParticles_NPCs",
				typeof(On_Main.orig_DrawNPCs), typeof(Main), typeof(bool));
			var particleDust = HookHelper.Method(particleManager, "DrawParticles_Dust",
				typeof(On_Main.orig_DrawDust), typeof(Main));
			var particleTiles = HookHelper.Method(particleManager, "DrawParticles_Tiles",
				typeof(On_Main.orig_DrawBlack), typeof(Main), typeof(bool));
			var particleInterface = HookHelper.Method(particleManager, "PostDrawInterface", typeof(SpriteBatch));

			var rocketNPCs = HookHelper.Method(rocketManager, "DrawRockets_NPCs",
				typeof(On_Main.orig_DrawNPCs), typeof(Main), typeof(bool));
			var rocketDust = HookHelper.Method(rocketManager, "DrawRockets_OverlaysAfterDusts",
				typeof(On_Main.orig_DrawDust), typeof(Main));
			var rocketBehindTiles = HookHelper.Method(rocketManager, "On_Main_DoDraw_DrawNPCsBehindTiles",
				typeof(On_Main.orig_DoDraw_DrawNPCsBehindTiles), typeof(Main));

			MonoModHooks.Add(particleNPCs, OnParticleNPCs);
			MonoModHooks.Add(particleDust, OnParticleDust);
			MonoModHooks.Add(particleTiles, OnParticleTiles);
			MonoModHooks.Add(particleInterface, OnParticleInterface);

			MonoModHooks.Add(rocketNPCs, OnRocketNPCs);
			MonoModHooks.Add(rocketDust, OnRocketDust);
			MonoModHooks.Add(rocketBehindTiles, OnRocketBehindTiles);
		}

		public static void Reset()
		{
			particles = null;
			rocketCount = null;
			rocketsCheckedTick = uint.MaxValue;
		}

		//anything that cant be read falls back to running the original
		private static bool AnyParticles()
		{
			if (particles.GetValue(null) is not ICollection list)
				return true;

			return list.Count > 0;
		}

		//this one counts with LINQ over all 256 rocket slots so it is asked once a tick rather than once per draw call
		private static bool AnyRockets()
		{
			if (rocketsCheckedTick == Main.GameUpdateCount)
				return rocketsPresent;

			rocketsCheckedTick = Main.GameUpdateCount;
			rocketsPresent = rocketCount.GetValue(null) is not int count || count > 0;
			return rocketsPresent;
		}

		private static void OnParticleNPCs(orig_DrawNPCs handler, object self, On_Main.orig_DrawNPCs orig, Main main, bool behindTiles)
		{
			if (!Enabled || AnyParticles())
				handler(self, orig, main, behindTiles);
			else
				orig(main, behindTiles);
		}

		private static void OnParticleDust(orig_DrawDust handler, object self, On_Main.orig_DrawDust orig, Main main)
		{
			if (!Enabled || AnyParticles())
				handler(self, orig, main);
			else
				orig(main);
		}

		private static void OnParticleTiles(orig_DrawBlack handler, object self, On_Main.orig_DrawBlack orig, Main main, bool force)
		{
			if (!Enabled || AnyParticles())
				handler(self, orig, main, force);
			else
				orig(main, force);
		}

		//a ModSystem hook rather than a detour so there is no original to fall through to
		private static void OnParticleInterface(orig_PostDrawInterface handler, object self, SpriteBatch spriteBatch)
		{
			if (!Enabled || AnyParticles())
				handler(self, spriteBatch);
		}

		private static void OnRocketNPCs(orig_DrawNPCs handler, object self, On_Main.orig_DrawNPCs orig, Main main, bool behindTiles)
		{
			if (!Enabled || AnyRockets())
				handler(self, orig, main, behindTiles);
			else
				orig(main, behindTiles);
		}

		private static void OnRocketDust(orig_DrawDust handler, object self, On_Main.orig_DrawDust orig, Main main)
		{
			if (!Enabled || AnyRockets())
				handler(self, orig, main);
			else
				orig(main);
		}

		private static void OnRocketBehindTiles(orig_BehindTiles handler, object self, On_Main.orig_DoDraw_DrawNPCsBehindTiles orig, Main main)
		{
			if (!Enabled || AnyRockets())
				handler(self, orig, main);
			else
				orig(main);
		}
	}
}
