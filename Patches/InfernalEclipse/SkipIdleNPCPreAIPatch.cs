using System;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

//InfernalGlobalNPC.PreAI runs for every active NPC every tick and walks all 255 player slots looking for players within range
//it does that before checking what kind of NPC it is but everything it can actually do is limited to the following:
//   Golem (Ragnarok mode)
//   the Profaned Guardian healer crystal
//   bosses (if Thorium is installed)
//u can see how performant that would be

namespace SOTSPerfPatch.Patches.InfernalEclipse;

internal sealed class SkipIdleNPCPreAIPatch : PerfPatch
{
	public override string Name => "Infernal Eclipse: skip idle NPC pre-AI checks";

	public override string TargetMod => "InfernalEclipseAPI";

	public override bool ClientOnly => false;

	public override void Apply() => Hooks.Install();

	private static class Hooks
	{
		private const string GlobalNPCType = "InfernalEclipseAPI.Common.GlobalNPCs.InfernalGlobalNPC";

		//the instance is typed as object because this mod cant reference IEOR's types
		private delegate bool orig_PreAI(object self, NPC npc);

		private static int healerCrystalId;

		private static bool Enabled => PerfPatchConfig.Instance?.GateInfernalNPCPreAI ?? false;

		public static void Install()
		{
			Type global = HookHelper.ModType("InfernalEclipseAPI", GlobalNPCType);
			var preAI = HookHelper.Method(global, "PreAI", typeof(NPC));

			//infernum is a hard dependency of IEOR so this should always resolve but u never know
			if (!ModContent.TryFind("InfernumMode", "HealerShieldCrystal", out ModNPC crystal))
				throw new TypeLoadException("InfernumMode/HealerShieldCrystal was not found.");

			healerCrystalId = crystal.Type;

			MonoModHooks.Add(preAI, OnPreAI);
		}

		private static bool OnPreAI(orig_PreAI orig, object self, NPC npc)
		{
			if (!Enabled || npc.boss || npc.type == NPCID.Golem || npc.type == healerCrystalId)
				return orig(self, npc);

			return true;
		}
	}
}
