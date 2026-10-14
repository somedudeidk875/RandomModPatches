using System;
using System.Reflection;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//HWJ's RitualModificationGlobalNPC.PreAI runs for every active NPC every tick and recalculates two answers that cant change within a tick
//the two are
//   if (npc.townNPC)
//       this.isSonHere = NPC.AnyNPCs(ModContent.NPCType<TheSon>());
//
//   Mod isCalamityLoaded;
//   if (!ModLoader.TryGetMod("CalamityMod", ref isCalamityLoaded) && (npc.type == 379 || ...) && ...)
//NPC.AnyNPCs walks all 200 NPC slots and it is called once per town NPC with every one of them calculating the same world-level answer 
//a developed world has ~15-20 town NPCs so that is three to four thousand slot visits per tick to get one boolean
//the Calamity lookup is worse placed than it is expensive since it sits before the cheap npc.type test
//this means every NPC in the world pays a string-keyed mod lookup every tick for an answer that is set once the game has loaded

namespace SOTSPerfPatch.Patches.ContinentOfJourney;

internal sealed class CacheRitualChecksPatch : PerfPatch
{
	public override string Name => "Homeward Journey: cache per-tick NPC checks";

	public override string TargetMod => "ContinentOfJourney";

	public override bool ClientOnly => false;

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string ModName = "ContinentOfJourney";
		private const string GlobalNPCType = "ContinentOfJourney.NPCs.RitualModificationGlobalNPC";
		private const string DownedBossesType = "ContinentOfJourney.DownedBossSystem";

		//the instance is typed as object because this mod cant reference HWJ's types
		private delegate bool orig_PreAI(object self, NPC npc);

		private static FieldInfo isSonHere;
		private static FieldInfo downedMechBrainBoss;
		private static int theSonType;
		private static bool calamityLoaded;

		private static uint scannedTick = uint.MaxValue;
		private static bool sonAlive;

		private static bool Enabled => PerfPatchConfig.Instance?.CacheRitualChecks ?? false;

		public static void Install()
		{
			Type global = HookHelper.ModType(ModName, GlobalNPCType);
			Type downedBosses = HookHelper.ModType(ModName, DownedBossesType);

			isSonHere = global.GetField("isSonHere", BindingFlags.Instance | BindingFlags.Public)
				?? throw new MissingFieldException($"{GlobalNPCType}.isSonHere was not found.");

			downedMechBrainBoss = downedBosses.GetField("downedMechBrainBoss", BindingFlags.Static | BindingFlags.Public)
				?? throw new MissingFieldException($"{DownedBossesType}.downedMechBrainBoss was not found.");

			if (!ModContent.TryFind(ModName, "TheSon", out ModNPC son))
				throw new TypeLoadException($"{ModName}/TheSon was not found.");

			theSonType = son.Type;
			calamityLoaded = ModLoader.HasMod("CalamityMod");

			var preAI = HookHelper.Method(global, "PreAI", typeof(NPC));
			MonoModHooks.Add(preAI, OnPreAI);
		}

		public static void Reset()
		{
			isSonHere = null;
			downedMechBrainBoss = null;
			scannedTick = uint.MaxValue;
		}

		private static bool OnPreAI(orig_PreAI orig, object self, NPC npc)
		{
			if (!Enabled)
				return orig(self, npc);

			if (npc.townNPC)
				isSonHere.SetValue(self, SonAlive());

			if (!calamityLoaded && IsRemovedWithoutCalamity(npc.type) && !MechBrainBossDowned()) {
				npc.active = false;
				npc.life = 0;
				return false;
			}

			return true;
		}

		//the four vanilla NPCs the mod removes when Calamity is not loaded
		private static bool IsRemovedWithoutCalamity(int type)
		{
			return type == 379 || type == 438 || type == 437 || type == 399;
		}

		private static bool SonAlive()
		{
			if (scannedTick != Main.GameUpdateCount) {
				scannedTick = Main.GameUpdateCount;
				sonAlive = NPC.AnyNPCs(theSonType);
			}

			return sonAlive;
		}

		private static bool MechBrainBossDowned() => (bool)downedMechBrainBoss.GetValue(null);
	}
}
