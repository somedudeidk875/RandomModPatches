using System;
using System.Collections.Concurrent;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//InfernalItemBalanceChange.SetDefaults is a flat chain of about 6200 lines that runs for every single item ever created
//it uses its own GetItem and calls it over 700 times per item 
//with a lot of large content mods u can see the problem
//the cost lands on chest loading, shop rebuilds, item drops, Magic Storage and Recipe Browser passes, and once per frame for any rebalanced item whose tooltip is on screen

namespace SOTSPerfPatch.Patches.InfernalEclipse;

internal sealed class CacheItemLookupsPatch : PerfPatch
{
	public override string Name => "Infernal Eclipse: cache item name lookups";

	public override string TargetMod => "InfernalEclipseAPI";

	public override bool ClientOnly => false;

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string BalanceType = "InfernalEclipseAPI.Common.Globals.GlobalItems.InfernalItemBalanceChange";

		private delegate bool orig_GetItem(Mod mod, string name, Item item);
		private delegate int orig_FindItem(Mod mod, string name);

		private static readonly ConcurrentDictionary<(Mod, string), int> cache = new();

		//the three mods the original resolves leniently where a missing name means "no match"
		private static Mod bardHealer;
		private static Mod thorium;
		private static Mod thoriumRework;

		private static bool Enabled => PerfPatchConfig.Instance?.CacheItemBalanceLookups ?? false;

		public static void Install()
		{
			Type balance = HookHelper.ModType("InfernalEclipseAPI", BalanceType);

			var getItem = HookHelper.Method(balance, "GetItem", typeof(Mod), typeof(string), typeof(Item));
			var unsafeGetItem = HookHelper.Method(balance, "UnsafeGetItem", typeof(Mod), typeof(string), typeof(Item));
			var findItem = HookHelper.Method(balance, "FindItem", typeof(Mod), typeof(string));

			ModLoader.TryGetMod("CalamityBardHealer", out bardHealer);
			ModLoader.TryGetMod("ThoriumMod", out thorium);
			ModLoader.TryGetMod("ThoriumRework", out thoriumRework);

			MonoModHooks.Add(getItem, OnGetItem);
			MonoModHooks.Add(unsafeGetItem, OnUnsafeGetItem);
			MonoModHooks.Add(findItem, OnFindItem);
		}

		public static void Reset()
		{
			cache.Clear();
			bardHealer = null;
			thorium = null;
			thoriumRework = null;
		}

		private static bool OnGetItem(orig_GetItem orig, Mod mod, string name, Item item)
		{
			if (!Enabled)
				return orig(mod, name, item);

			int type = LookupLenient(mod, name);
			return type >= 0 && item.type == type;
		}

		private static bool OnUnsafeGetItem(orig_GetItem orig, Mod mod, string name, Item item)
		{
			if (!Enabled)
				return orig(mod, name, item);

			return item.type == LookupStrict(mod, name);
		}

		private static int OnFindItem(orig_FindItem orig, Mod mod, string name)
		{
			if (!Enabled)
				return orig(mod, name);

			return LookupStrict(mod, name);
		}

		private static int LookupLenient(Mod mod, string name)
		{
			if (cache.TryGetValue((mod, name), out int type))
				return type;

			if (mod == bardHealer || mod == thorium || mod == thoriumRework)
				type = mod.TryFind(name, out ModItem found) ? found.Type : -1;
			else
				type = mod.Find<ModItem>(name).Type;

			cache[(mod, name)] = type;
			return type;
		}

		private static int LookupStrict(Mod mod, string name)
		{
			if (cache.TryGetValue((mod, name), out int type) && type >= 0)
				return type;

			type = mod.Find<ModItem>(name).Type;
			cache[(mod, name)] = type;
			return type;
		}
	}
}
