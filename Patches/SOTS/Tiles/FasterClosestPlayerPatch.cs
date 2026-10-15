using Microsoft.Xna.Framework;
using SOTS.Items.Chaos;
using SOTS.Items.Planetarium.Blocks;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Patches.SOTS.Tiles;

//phase ore and hardlight blocks call closestPlayer for every visible tile every frame from their draw, lighting and ambient-effect hooks
//in multiplayer that loops over all 255 player slots each time

internal sealed class FasterClosestPlayerPatch : PerfPatch
{
	public override string Name => "Fast closestPlayer for phase/hardlight tiles";

	public override string TargetMod => "SOTS";

	public override void Apply() => Hooks.Install();

	[JITWhenModsEnabled("SOTS")]
	private static class Hooks
	{
		private delegate int orig_ClosestPlayer(int i, int j, ref float minDist);

		private static readonly int[] activePlayers = new int[Main.maxPlayers];
		private static int activeCount;
		private static uint lastRefreshUpdate;
		private static bool everRefreshed;

		private static bool Enabled => PerfPatchConfig.Instance?.FastClosestPlayer ?? false;

		public static void Install()
		{
			var phaseOre = HookHelper.Method(typeof(PhaseOreTile), nameof(PhaseOreTile.closestPlayer),
				typeof(int), typeof(int), typeof(float).MakeByRefType());
			var hardlight = HookHelper.Method(typeof(HardlightBlockTile), nameof(HardlightBlockTile.closestPlayer),
				typeof(int), typeof(int), typeof(float).MakeByRefType());

			everRefreshed = false;
			MonoModHooks.Add(phaseOre, OnClosestPlayer);
			MonoModHooks.Add(hardlight, OnClosestPlayer);
		}

		//mirrors the original
		private static int OnClosestPlayer(orig_ClosestPlayer orig, int i, int j, ref float minDist)
		{
			if (!Enabled)
				return orig(i, j, ref minDist);

			RefreshActivePlayers();

			var tileCenter = new Vector2(i * 16 + 8, j * 16 + 8);
			int closest = -1;
			for (int k = 0; k < activeCount; k++) {
				Player player = Main.player[activePlayers[k]];
				if (!player.active || player.dead)
					continue;

				float distance = (player.Center - tileCenter).Length();
				if (distance < minDist) {
					minDist = distance;
					closest = player.whoAmI;
					if (Main.netMode == NetmodeID.SinglePlayer)
						break;
				}
			}
			return closest;
		}

		private static void RefreshActivePlayers()
		{
			if (everRefreshed && lastRefreshUpdate == Main.GameUpdateCount)
				return;

			everRefreshed = true;
			lastRefreshUpdate = Main.GameUpdateCount;
			activeCount = 0;
			for (int k = 0; k < Main.maxPlayers; k++) {
				if (Main.player[k].active)
					activePlayers[activeCount++] = k;
			}
		}
	}
}
