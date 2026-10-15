using System;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//VCMM decides which track to play by asking whether certain NPCs are near a player, through MusicUtilities.NPCNearby(params int[] npcTypes)
//   foreach (int npcType in npcTypes)
//   {
//       double angerFactor = CultistsAngered() ? 2 : 8;
//       int npcIndex = NPC.FindFirstNPC(npcType);
//       if (npcIndex < 0 || npcIndex >= Main.maxNPCs || !Main.npc[npcIndex].active)
//           continue;
//       ...distance checks against every player slot are here...
//   }
//CultistsAngered() and NPC.FindFirstNPC() go through all 200 NPC slots and they are inside the loop
//this means every single npcType are asked about costs two full passes over Main.npc
//MusicFlags.PreUpdate asks ~75 npcTypes every tick and almost none of it is gated the
//old one's check alone passes 30 types and the boss music scenes add about 25 more because IsSceneEffectActive runs for every registered scene every tick
//that is about 100 npcTypes a ticks so about 40,000 NPC slot visits a tick or 2.4 million a second in a normal world where none of those NPCs might even exist
//this was by far the largest per tick performance dropper out of all the mods i looked at lol

namespace SOTSPerfPatch.Patches.UnCalamityModMusic;

internal sealed class SkipAbsentNPCScansPatch : PerfPatch
{
	public override string Name => "Vanilla Calamity Music: skip scans for absent NPCs";

	public override string TargetMod => "UnCalamityModMusic";

	public override bool ClientOnly => false;

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string ModName = "UnCalamityModMusic";
		private const string UtilitiesType = "UnCalamityModMusic.Common.MusicUtilities";

		private delegate bool orig_NPCNearby(int[] npcTypes);
		private delegate bool orig_CultistsAngered();

		//which npc types are in a slot right now
		private static bool[] present;
		private static bool built;
		private static uint builtTick;

		private static bool angerKnown;
		private static uint angerTick;
		private static bool angered;

		private static bool Enabled => PerfPatchConfig.Instance?.SkipAbsentNPCScans ?? false;

		public static void Install()
		{
			Type utilities = HookHelper.ModType(ModName, UtilitiesType);

			var npcNearby = HookHelper.Method(utilities, "NPCNearby", typeof(int[]));
			var cultistsAngered = HookHelper.Method(utilities, "CultistsAngered");

			MonoModHooks.Add(npcNearby, OnNPCNearby);
			MonoModHooks.Add(cultistsAngered, OnCultistsAngered);
		}

		public static void Reset()
		{
			present = null;
			built = false;
			angerKnown = false;
		}

		private static bool OnNPCNearby(orig_NPCNearby orig, int[] npcTypes)
		{
			if (!Enabled || npcTypes == null || npcTypes.Length == 0)
				return orig(npcTypes);

			Rebuild();

			int live = 0;
			for (int i = 0; i < npcTypes.Length; i++) {
				if (Exists(npcTypes[i]))
					live++;
			}

			//none of them are in the world so the original would have skipped every one of them
			if (live == 0)
				return false;

			//all of them are so there is nothing to drop
			if (live == npcTypes.Length)
				return orig(npcTypes);

			//the order of what is left is the same as it was so the original still comes back the same way
			var remaining = new int[live];
			int next = 0;

			for (int i = 0; i < npcTypes.Length; i++) {
				int type = npcTypes[i];
				if (Exists(type))
					remaining[next++] = type;
			}

			return orig(remaining);
		}

		private static bool OnCultistsAngered(orig_CultistsAngered orig)
		{
			if (!Enabled)
				return orig();

			if (!angerKnown || angerTick != Main.GameUpdateCount) {
				angerKnown = true;
				angerTick = Main.GameUpdateCount;
				angered = orig();
			}

			return angered;
		}

		private static bool Exists(int type) => (uint)type < (uint)present.Length && present[type];

		private static void Rebuild()
		{
			if (built && builtTick == Main.GameUpdateCount)
				return;

			present ??= new bool[NPCLoader.NPCCount];
			Array.Clear(present, 0, present.Length);

			NPC[] npcs = Main.npc;
			for (int i = 0; i < Main.maxNPCs; i++) {
				int type = npcs[i].type;
				if ((uint)type < (uint)present.Length)
					present[type] = true;
			}

			built = true;
			builtTick = Main.GameUpdateCount;
		}
	}
}
