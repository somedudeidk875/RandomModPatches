using Microsoft.Xna.Framework.Graphics;
using SOTS.Common;
using SOTS.FakePlayer;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Common.Systems;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Patches.SOTS.Rendering;

//SOTS renders four full-screen "magic water" render targets every frame (clear, draw, full-screen shader pass into a temp target, copy back) 
//it then composites three of them to the screen with another full-screen shader pass each even when nothing uses them

internal sealed class SkipUnusedWaterLayersPatch : PerfPatch
{
	public override string Name => "Water layer gate";

	public override string TargetMod => "SOTS";

	public override void Apply() => Hooks.Install();

	private static class Hooks
	{
		private delegate void orig_SetupGreenscreens(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice);

		private delegate void orig_DrawWaterLayer(SpriteBatch spriteBatch, ref RenderTarget2D renderTarget, bool returnToGameZoomMatrix, SpriteEffects effects);

		//frames to keep running after the layer stops being needed so the targets end up cleared
		private const int GraceFrames = 3;

		private static int framesSinceNeeded;
		private static bool layerActive = true;

		private static bool Enabled => PerfPatchConfig.Instance?.GateWaterLayer ?? false;

		public static void Install()
		{
			var setup = HookHelper.Method(typeof(GreenScreenManager), nameof(GreenScreenManager.SetupGreenscreens),
				typeof(SpriteBatch), typeof(GraphicsDevice));
			var drawLayer = HookHelper.Method(typeof(GreenScreenManager), nameof(GreenScreenManager.DrawWaterLayer),
				typeof(SpriteBatch), typeof(RenderTarget2D).MakeByRefType(), typeof(bool), typeof(SpriteEffects));

			framesSinceNeeded = 0;
			layerActive = true;
			MonoModHooks.Add(setup, OnSetupGreenscreens);
			MonoModHooks.Add(drawLayer, OnDrawWaterLayer);
		}

		private static void OnSetupGreenscreens(orig_SetupGreenscreens orig, SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
		{
			if (!Enabled) {
				layerActive = true;
				orig(spriteBatch, graphicsDevice);
				return;
			}

			if (IsWaterLayerNeeded())
				framesSinceNeeded = 0;
			else if (framesSinceNeeded <= GraceFrames)
				framesSinceNeeded++;

			layerActive = framesSinceNeeded <= GraceFrames;
			if (layerActive)
				orig(spriteBatch, graphicsDevice);
		}

		private static void OnDrawWaterLayer(orig_DrawWaterLayer orig, SpriteBatch spriteBatch, ref RenderTarget2D renderTarget, bool returnToGameZoomMatrix, SpriteEffects effects)
		{
			//skipping leaves the sprite batch exactly as the caller had it which is what we want
			if (Enabled && !layerActive)
				return;

			orig(spriteBatch, ref renderTarget, returnToGameZoomMatrix, effects);
		}

		private static bool IsWaterLayerNeeded()
		{
			if (SOTSEntityTracker.AnyWaterLayerProjectile)
				return true;

			var particles = global::SOTS.ParticleHelper.waterParticles;
			if (particles != null && particles.Count > 0)
				return true;

			for (int i = 0; i < Main.maxPlayers; i++) {
				Player player = Main.player[i];
				if (player.active && FakeModPlayer.ModPlayer(player).hasHydroFakePlayer)
					return true;
			}

			return false;
		}
	}
}
