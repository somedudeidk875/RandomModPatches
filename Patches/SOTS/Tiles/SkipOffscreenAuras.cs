using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SOTS.Items.Conduit;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//ConduitHelper.PreDrawBeforePlayers walks every conduit in the world every frame and calls DrawConduitAura on each one
//the beams a conduit draws to the player fade out with distance and bail early but the aura does not look at distance at all
//it goes straight into the godray branch of DrawGlowingParts which per conduit per frame does the following (shown below)
//	Main.spriteBatch.End();
//	Main.spriteBatch.Begin(...);
//	five GodrayShader.Parameters[...].SetValue(...) calls
//	GodrayShader.CurrentTechnique.Passes[0].Apply();
//	Main.spriteBatch.Draw(...);
//	Main.spriteBatch.End();
//	Main.spriteBatch.Begin(...);
//	three more Main.spriteBatch.Draw(...) calls
//a finished setup has one conduit per element so that is eight shader passes and sixteen batch restarts a frame which are paid from anywhere in the world
//the godray is about 110 pixels across so when the conduit is off screen every one of those draws lands outside the view and none of it is seen

namespace SOTSPerfPatch.Patches.SOTS.Tiles;

internal sealed class SkipOffscreenAuras : PerfPatch
{
	public override string Name => "Skip off screen conduit auras";

	public override string TargetMod => "SOTS";

	public override void Apply() => Hooks.Install();

	[JITWhenModsEnabled("SOTS")]
	private static class Hooks
	{
		private delegate void orig_DrawConduitAura(ConduitCounterTE self, int i, int j);

		private static bool Enabled => PerfPatchConfig.Instance?.SkipOffscreenConduitAuras ?? false;

		public static void Install()
		{
			var aura = HookHelper.Method(typeof(ConduitCounterTE), nameof(ConduitCounterTE.DrawConduitAura),
				typeof(int), typeof(int));

			MonoModHooks.Add(aura, OnDrawConduitAura);
		}

		//the screen rectangle plus a margin wider than the godray
		private static bool OnScreen(int i, int j)
		{
			const float margin = 192f;
			Vector2 position = new Vector2(i * 16 + 8, j * 16 + 8) - Main.screenPosition;

			return position.X >= -margin
				&& position.Y >= -margin
				&& position.X <= Main.screenWidth + margin
				&& position.Y <= Main.screenHeight + margin;
		}

		//the state the godray branch leaves behind (copied from the end of DrawGlowingParts)
		private static void RestartBatch()
		{
			Main.spriteBatch.End();
			Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, Main.DefaultSamplerState,
				DepthStencilState.None, Main.Camera.Rasterizer, null, Main.Camera.GameViewMatrix.TransformationMatrix);
		}

		private static void OnDrawConduitAura(orig_DrawConduitAura orig, ConduitCounterTE self, int i, int j)
		{
			if (!Enabled || OnScreen(i, j)) {
				orig(self, i, j);
				return;
			}

			//a conduit with nothing dissolving around it returns before touching the batch anyways
			if (self.DissolvingTileCount > 0)
				RestartBatch();
		}
	}
}
