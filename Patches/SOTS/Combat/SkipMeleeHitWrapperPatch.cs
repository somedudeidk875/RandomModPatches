using Microsoft.Xna.Framework;
using SOTS;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Patches.SOTS.Combat;

//SOTS checks for Zombie Hand every call of PlayerItemCheck_MeleeHitNPCs and allocates a bool[200] (and does a lot more stuff)
internal sealed class SkipMeleeHitWrapperPatch : PerfPatch
{
	public override string Name => "Melee hit fast path";

	public override string TargetMod => "SOTS";

	public override bool ClientOnly => false;

	public override void Apply() => Hooks.Install();

	private static class Hooks
	{
		private delegate void orig_SotsMeleeHitNPCs(On_Player.orig_ItemCheck_MeleeHitNPCs orig, Player self, Item sItem, Rectangle itemRectangle, int originalDamage, float knockBack);

		private static bool Enabled => PerfPatchConfig.Instance?.MeleeHitFastPath ?? false;

		public static void Install()
		{
			var target = HookHelper.Method(typeof(SOTSDetours), "Player_ItemCheck_MeleeHitNPCs",
				typeof(On_Player.orig_ItemCheck_MeleeHitNPCs), typeof(Player), typeof(Item), typeof(Rectangle), typeof(int), typeof(float));
			MonoModHooks.Add(target, OnSotsMeleeHitNPCs);
		}

		private static void OnSotsMeleeHitNPCs(orig_SotsMeleeHitNPCs sotsOrig, On_Player.orig_ItemCheck_MeleeHitNPCs vanillaOrig,
			Player self, Item sItem, Rectangle itemRectangle, int originalDamage, float knockBack)
		{
			if (Enabled && !SOTSPlayer.ModPlayer(self).CanKillNPC) {
				vanillaOrig(self, sItem, itemRectangle, originalDamage, knockBack);
				return;
			}

			sotsOrig(vanillaOrig, self, sItem, itemRectangle, originalDamage, knockBack);
		}
	}
}
