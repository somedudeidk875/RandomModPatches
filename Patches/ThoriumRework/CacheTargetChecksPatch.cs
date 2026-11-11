using System;
using System.Collections.Concurrent;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//Thorium Helliem decides whether each of its globals applies to an entity by comparing the entity's type against a list of names looked up in other mods every single time (here we go again)
//ArmorChanges is the worst (as shown):
//   return ModLoader.TryGetMod("ThoriumMod", ref thorium)
//       && (item.type == thorium.Find<ModItem>("DreamWeaversHelmet").Type
//       || item.type == thorium.Find<ModItem>("DreamWeaversHood").Type
//       ... twenty of them ...
//       && ModContent.GetInstance<CompatConfig>().postDoGRag;
//the || chain short circuits on a match so an item that matches nothing (which is nearly all of them) pays for all twenty string keyed lookups
//the two treasure bag globals do eleven each and the rest add a few more
//AppliesToEntity runs every time an item is given its defaults and that is far more often than it sounds
//this basically includes every clone, shop refresh and in particular the tooltip pass (which clones the hovered item through every loaded mod) 
//hovering over an inventory slot is where this shows up

namespace SOTSPerfPatch.Patches.ThoriumRework;

internal sealed class CacheTargetChecksPatch : PerfPatch
{
	public override string Name => "Thorium Rework: cache which entities the reworks apply to";

	public override string TargetMod => "ThoriumRework";

	public override bool ClientOnly => false;

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string ModName = "ThoriumRework";

		private static readonly string[] ItemGlobals = {
			"ThoriumRework.ArmorChanges",
			"ThoriumRework.TreasureBags",
			"ThoriumRework.TreasureBagsWithCalamity",
			"ThoriumRework.StringInstruments",
			"ThoriumRework.TRuCompatibility",
		};

		private static readonly string[] NPCGlobals = {
			"ThoriumRework.BossChanges.BossOverrides",
			"ThoriumRework.BossChanges.BoreanStriderRework",
		};

		private delegate bool orig_ItemApplies(object self, Item entity, bool lateInstantiation);
		private delegate bool orig_NPCApplies(object self, NPC entity, bool lateInstantiation);

		//keyed by the global doing the asking (the entity type) and the flag so that one global that disagrees about an item never see the other's answer to that same item
		private static readonly ConcurrentDictionary<(Type Global, int Type, bool Late), bool> cache = new();

		private static bool Enabled => PerfPatchConfig.Instance?.CacheReworkTargetChecks ?? false;

		public static void Install()
		{
			int hooked = 0;

			foreach (string typeName in ItemGlobals) {
				Type global = HookHelper.ModType(ModName, typeName);
				var applies = HookHelper.Method(global, "AppliesToEntity", typeof(Item), typeof(bool));
				MonoModHooks.Add(applies, OnItemApplies);
				hooked++;
			}

			foreach (string typeName in NPCGlobals) {
				Type global = HookHelper.ModType(ModName, typeName);
				var applies = HookHelper.Method(global, "AppliesToEntity", typeof(NPC), typeof(bool));
				MonoModHooks.Add(applies, OnNPCApplies);
				hooked++;
			}

			if (hooked == 0)
				throw new InvalidOperationException("No Thorium Rework globals were hooked.");
		}

		//also drops the Type keys so the other mod's assembly can unload
		public static void Reset() => cache.Clear();

		private static bool OnItemApplies(orig_ItemApplies orig, object self, Item entity, bool lateInstantiation)
		{
			if (!Enabled)
				return orig(self, entity, lateInstantiation);

			var key = (self.GetType(), entity.type, lateInstantiation);
			if (cache.TryGetValue(key, out bool applies))
				return applies;

			applies = orig(self, entity, lateInstantiation);
			cache[key] = applies;
			return applies;
		}

		private static bool OnNPCApplies(orig_NPCApplies orig, object self, NPC entity, bool lateInstantiation)
		{
			if (!Enabled)
				return orig(self, entity, lateInstantiation);

			var key = (self.GetType(), entity.type, lateInstantiation);
			if (cache.TryGetValue(key, out bool applies))
				return applies;

			applies = orig(self, entity, lateInstantiation);
			cache[key] = applies;
			return applies;
		}
	}
}
//also btw i saw that one image in the tiles folder (if u know u know)