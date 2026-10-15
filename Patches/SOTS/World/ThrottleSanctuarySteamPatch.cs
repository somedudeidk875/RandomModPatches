using Microsoft.Xna.Framework;
using SOTS;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace SOTSPerfPatch.Patches.SOTS.World;

//in the Sanctuary, SOTS samples 90 random tiles per tick (30 with its lag-reduction options) and spawns up to three steam gores per water-surface hit
//this can keep the gore pool full

internal sealed class ThrottleSanctuarySteamPatch : PerfPatch
{
	public override string Name => "Sanctuary steam throttle";

	public override string TargetMod => "SOTS";

	public override void Apply() => Hooks.Install();

	[JITWhenModsEnabled("SOTS")]
	private static class Hooks
	{
		private delegate void orig_TryDoingLiquidScreenEffects();

		public static void Install()
		{
			var target = HookHelper.Method(typeof(SOTSWorld), nameof(SOTSWorld.TryDoingLiquidScreenEffects));
			HookHelper.Method(typeof(SOTSWorld), nameof(SOTSWorld.LiquidScreenEffect), typeof(int), typeof(int));
			MonoModHooks.Add(target, OnTryDoingLiquidScreenEffects);
		}

		private static void OnTryDoingLiquidScreenEffects(orig_TryDoingLiquidScreenEffects orig)
		{
			PerfPatchConfig config = PerfPatchConfig.Instance;
			if (config == null || !config.ThrottleSanctuarySteam) {
				orig();
				return;
			}

			if (Main.netMode == NetmodeID.Server)
				return;

			if (CountActiveGores() >= config.SteamGoreCap)
				return;

			var sotsConfig = global::SOTS.SOTS.Config;
			bool reduced = sotsConfig != null && (sotsConfig.SanctuaryLagReduction || sotsConfig.lowFidelityMode);
			int samples = reduced ? config.SteamSamplesReduced : config.SteamSamples;
			if (samples <= 0)
				return;

			//same sampling area as the original
			Point center = Main.LocalPlayer.Center.ToTileCoordinates();
			var area = new Rectangle(
				center.X - Main.buffScanAreaWidth / 2,
				center.Y - Main.buffScanAreaHeight / 2,
				Main.buffScanAreaWidth,
				Main.buffScanAreaHeight);
			area = WorldUtils.ClampToWorld(area);

			for (int i = 0; i < samples; i++)
				SOTSWorld.LiquidScreenEffect(Main.rand.Next(area.Left, area.Right), Main.rand.Next(area.Top, area.Bottom));
		}

		private static int CountActiveGores()
		{
			int count = 0;
			for (int i = 0; i < Main.maxGore; i++) {
				if (Main.gore[i].active)
					count++;
			}
			return count;
		}
	}
}
