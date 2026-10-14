using SOTS;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Patches.SOTS.Rendering;

//oh my goddd another method that loops over all NPCs (for time-freeze effects, chains and archeologist)
//it then blits pixelated NPC trail target 
//vanilla calls DrawNPCs twice per frame so all of that happened twice.

internal sealed class DrawNPCEffectsOncePatch : PerfPatch
{
	public override string Name => "Single SOTS NPC draw pass";

	public override string TargetMod => "SOTS";

	public override void Apply() => Hooks.Install();

	private static class Hooks
	{
		private delegate void orig_SotsDrawNPCs(On_Main.orig_DrawNPCs orig, Main self, bool behindTiles);

		private static bool Enabled => PerfPatchConfig.Instance?.SkipDuplicateNPCPass ?? false;

		public static void Install()
		{
			var target = HookHelper.Method(typeof(SOTSDetours), "Main_DrawNPCs",
				typeof(On_Main.orig_DrawNPCs), typeof(Main), typeof(bool));
			MonoModHooks.Add(target, OnSotsDrawNPCs);
		}

		private static void OnSotsDrawNPCs(orig_SotsDrawNPCs sotsOrig, On_Main.orig_DrawNPCs vanillaOrig, Main self, bool behindTiles)
		{
			if (behindTiles && Enabled) {
				vanillaOrig(self, behindTiles);
				return;
			}

			sotsOrig(vanillaOrig, self, behindTiles);
		}
	}
}
