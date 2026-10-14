using Microsoft.Xna.Framework;
using NoxusBoss.Content.Tiles.GenesisComponents;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//WOTG draws genesis grass into a render target every single frame through a subscriber on Luminance's render target loop 
//before it knows whether any genesis grass is even on screen it scans every tile on screen while building a dictionary and a list per frame.

namespace SOTSPerfPatch.Patches.Noxus;

internal sealed class SkipEmptyGenesisGrassPatch : PerfPatch
{
	public override string Name => "Genesis grass: skip when none on screen";

	public override string TargetMod => "NoxusBoss";

	public override void Apply() => Hooks.Install();

	[JITWhenModsEnabled("NoxusBoss")]
	private static class Hooks
	{
		private delegate void orig_RenderToGrassTarget();

		//resolved once
		private static int grassTileId = -1;

		private static bool Enabled => PerfPatchConfig.Instance?.SkipEmptyGenesisGrass ?? false;

		public static void Install()
		{
			var render = HookHelper.Method(typeof(GenesisGrassTargetManager), "RenderToGrassTarget");
			grassTileId = ModContent.TryFind("NoxusBoss", "GenesisGrass", out ModTile grass) ? grass.Type : -1;
			MonoModHooks.Add(render, OnRenderToGrassTarget);
		}

		private static void OnRenderToGrassTarget(orig_RenderToGrassTarget orig)
		{
			if (!Enabled || grassTileId < 0 || AnyGrassOnScreen())
				orig();
		}

		private static bool AnyGrassOnScreen()
		{
			int left = (int)(Main.screenPosition.X / 16f);
			int right = left + (int)(Main.screenWidth / 16f) + 1;
			int top = (int)(Main.screenPosition.Y / 16f);
			int bottom = top + (int)(Main.screenHeight / 16f) + 1;

			if (left < 0)
				left = 0;
			if (top < 0)
				top = 0;
			if (right > Main.maxTilesX)
				right = Main.maxTilesX;
			if (bottom > Main.maxTilesY)
				bottom = Main.maxTilesY;

			for (int i = left; i < right; i++) {
				for (int j = top; j < bottom; j++) {
					Tile tile = Main.tile[i, j];
					if (tile.HasTile && tile.TileType == grassTileId)
						return true;
				}
			}
			return false;
		}
	}
}
