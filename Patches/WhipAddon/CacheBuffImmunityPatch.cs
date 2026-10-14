using System;
using System.Collections.Generic;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//Calamity Simple Whip Addon clears its own buff immunities so its whip debuffs can always be applied
//it does that in WhipGripGlobalNPC.PostAI which runs for every active NPC every tick (shown here)
//   for (int i = 0; i < BuffLoader.BuffCount; i++)
//   {
//       ModBuff buff = BuffLoader.GetBuff(i);
//       if (buff?.Mod == ModContent.GetInstance<CalamitySimpleWhipAddon>())
//           npc.buffImmune[i] = false;
//   }

namespace SOTSPerfPatch.Patches.WhipAddon;

internal sealed class CacheBuffImmunityPatch : PerfPatch
{
	public override string Name => "Whip addon: cache buff immunity list";

	public override string TargetMod => "CalamitySimpleWhipAddon";

	public override bool ClientOnly => false;

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string ModName = "CalamitySimpleWhipAddon";
		private const string GlobalNPCType = "CalamitySimpleWhipAddon.Content.Common.GlobalNPCs.WhipGripGlobalNPC";

		//the instance is typed as object because this mod cannot reference the addon's types
		private delegate void orig_PostAI(object self, NPC npc);

		private static int[] buffIds;

		private static bool Enabled => PerfPatchConfig.Instance?.CacheWhipBuffImmunity ?? false;

		public static void Install()
		{
			Type global = HookHelper.ModType(ModName, GlobalNPCType);
			var postAI = HookHelper.Method(global, "PostAI", typeof(NPC));
			MonoModHooks.Add(postAI, OnPostAI);
		}

		public static void Reset() => buffIds = null;

		private static void OnPostAI(orig_PostAI orig, object self, NPC npc)
		{
			if (!Enabled) {
				orig(self, npc);
				return;
			}

			int[] ids = buffIds ??= CollectBuffIds();
			bool[] immune = npc.buffImmune;

			for (int i = 0; i < ids.Length; i++) {
				int id = ids[i];
				if (id < immune.Length)
					immune[id] = false;
			}
		}

		private static int[] CollectBuffIds()
		{
			Mod whipAddon = ModLoader.GetMod(ModName);
			var ids = new List<int>();

			for (int i = 0; i < BuffLoader.BuffCount; i++) {
				ModBuff buff = BuffLoader.GetBuff(i);
				if (buff != null && buff.Mod == whipAddon)
					ids.Add(i);
			}

			return ids.ToArray();
		}
	}
}
