using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using System.Diagnostics;
using System.Reflection;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Common.Diagnostics;

//times the per-tick hooks that every loaded mod implements, so the report can say which mod is spending the time 
internal static class ModHookProfiler
{
	//i controlled-f pre and post in the tmodloader docsd and just went with what looked right
	private static readonly string[] SystemHooks = {
		"PreUpdateEntities", "PreUpdateTime", "PreUpdateWorld", "PreUpdateInvasions", "PreUpdatePlayers",
		"PostUpdatePlayers", "PreUpdateNPCs", "PostUpdateNPCs", "PreUpdateGores", "PostUpdateGores",
		"PreUpdateProjectiles", "PostUpdateProjectiles", "PreUpdateItems", "PostUpdateItems",
		"PreUpdateDusts", "PostUpdateDusts", "PostUpdateTime", "PostUpdateWorld", "PostUpdateInvasions",
		"PostUpdateEverything", "PostUpdateInput", "PreSaveAndQuit", "UpdateUI", "ModifyScreenPosition",
	};

	//hooks, youll see more of these throughout this whole mod
	private static readonly string[] PlayerHooks = {
		"ResetEffects", "ResetInfoAccessories", "UpdateDead", "PreUpdate", "PostUpdate",
		"PreUpdateBuffs", "PostUpdateBuffs", "UpdateEquips", "PostUpdateEquips", "UpdateVisibleAccessories",
		"PostUpdateMiscEffects", "PostUpdateRunSpeeds", "PreUpdateMovement", "UpdateLifeRegen",
		"UpdateBadLifeRegen", "UpdateAutopause", "ModifyScreenPosition",
	};

	private delegate void orig_SystemHook(ModSystem self);

	private delegate void orig_NPCVoid(GlobalNPC self, NPC npc);

	private delegate bool orig_NPCBool(GlobalNPC self, NPC npc);

	private delegate void orig_NPCLifeRegen(GlobalNPC self, NPC npc, ref int damage);

	private delegate void orig_NPCDrawEffects(GlobalNPC self, NPC npc, ref Color drawColor);

	private delegate void orig_NPCPostDraw(GlobalNPC self, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor);

	private delegate bool orig_NPCPreDraw(GlobalNPC self, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor);

	private delegate void orig_ProjVoid(GlobalProjectile self, Projectile projectile);

	private delegate bool orig_ProjBool(GlobalProjectile self, Projectile projectile);

	private delegate void orig_ProjPostDraw(GlobalProjectile self, Projectile projectile, Color lightColor);

	private delegate bool orig_ProjPreDraw(GlobalProjectile self, Projectile projectile, ref Color lightColor);

	private delegate void orig_ItemPlayer(GlobalItem self, Item item, Player player);

	private delegate void orig_ItemVoid(GlobalItem self, Item item);

	private delegate void hook_NPCVoid(orig_NPCVoid orig, GlobalNPC self, NPC npc);

	private delegate bool hook_NPCBool(orig_NPCBool orig, GlobalNPC self, NPC npc);

	private delegate void hook_NPCLifeRegen(orig_NPCLifeRegen orig, GlobalNPC self, NPC npc, ref int damage);

	private delegate void hook_NPCDrawEffects(orig_NPCDrawEffects orig, GlobalNPC self, NPC npc, ref Color drawColor);

	private delegate void hook_NPCPostDraw(orig_NPCPostDraw orig, GlobalNPC self, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor);

	private delegate bool hook_NPCPreDraw(orig_NPCPreDraw orig, GlobalNPC self, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor);

	private delegate void hook_ProjVoid(orig_ProjVoid orig, GlobalProjectile self, Projectile projectile);

	private delegate bool hook_ProjBool(orig_ProjBool orig, GlobalProjectile self, Projectile projectile);

	private delegate void hook_ProjPostDraw(orig_ProjPostDraw orig, GlobalProjectile self, Projectile projectile, Color lightColor);

	private delegate bool hook_ProjPreDraw(orig_ProjPreDraw orig, GlobalProjectile self, Projectile projectile, ref Color lightColor);

	private delegate void hook_ItemPlayer(orig_ItemPlayer orig, GlobalItem self, Item item, Player player);

	private delegate void hook_ItemVoid(orig_ItemVoid orig, GlobalItem self, Item item);

	private delegate void orig_PlayerHook(ModPlayer self);

	public static int InstrumentedMethods { get; private set; }

	public static bool DeepInstalled { get; private set; }

	private static bool installed;

	public static void Install()
	{
		if (installed)
			return;
		installed = true;

		var done = new HashSet<MethodInfo>();
		foreach (ModSystem system in ModContent.GetContent<ModSystem>())
			Instrument(system.GetType(), SystemHooks, done, OnSystemHook);
		foreach (ModPlayer player in ModContent.GetContent<ModPlayer>())
			Instrument(player.GetType(), PlayerHooks, done, OnPlayerHook);
	}

	public static void InstallDeep()
	{
		if (DeepInstalled)
			return;
		DeepInstalled = true;

		InstrumentGlobals(new HashSet<MethodInfo>());
	}

	//GlobalNPC, GlobalProjectile and GlobalItem hooks
	private static void InstrumentGlobals(HashSet<MethodInfo> done)
	{
		foreach (GlobalNPC global in ModContent.GetContent<GlobalNPC>()) {
			System.Type type = global.GetType();
			Instrument(type, new[] { "ResetEffects", "AI", "PostAI" }, done, (hook_NPCVoid)OnNPCVoid, typeof(NPC));
			Instrument(type, new[] { "PreAI" }, done, (hook_NPCBool)OnNPCBool, typeof(NPC));
			Instrument(type, new[] { "UpdateLifeRegen" }, done, (hook_NPCLifeRegen)OnNPCLifeRegen, typeof(NPC), typeof(int).MakeByRefType());
			Instrument(type, new[] { "DrawEffects" }, done, (hook_NPCDrawEffects)OnNPCDrawEffects, typeof(NPC), typeof(Color).MakeByRefType());
			Instrument(type, new[] { "PostDraw" }, done, (hook_NPCPostDraw)OnNPCPostDraw, typeof(NPC), typeof(SpriteBatch), typeof(Vector2), typeof(Color));
			Instrument(type, new[] { "PreDraw" }, done, (hook_NPCPreDraw)OnNPCPreDraw, typeof(NPC), typeof(SpriteBatch), typeof(Vector2), typeof(Color));
		}

		foreach (GlobalProjectile global in ModContent.GetContent<GlobalProjectile>()) {
			System.Type type = global.GetType();
			Instrument(type, new[] { "AI", "PostAI" }, done, (hook_ProjVoid)OnProjVoid, typeof(Projectile));
			Instrument(type, new[] { "PreAI" }, done, (hook_ProjBool)OnProjBool, typeof(Projectile));
			Instrument(type, new[] { "PostDraw" }, done, (hook_ProjPostDraw)OnProjPostDraw, typeof(Projectile), typeof(Color));
			Instrument(type, new[] { "PreDraw" }, done, (hook_ProjPreDraw)OnProjPreDraw, typeof(Projectile), typeof(Color).MakeByRefType());
		}

		foreach (GlobalItem global in ModContent.GetContent<GlobalItem>()) {
			System.Type type = global.GetType();
			Instrument(type, new[] { "UpdateInventory", "UpdateEquip" }, done, (hook_ItemPlayer)OnItemPlayer, typeof(Item), typeof(Player));
			Instrument(type, new[] { "PostUpdate", "Update" }, done, (hook_ItemVoid)OnItemVoid, typeof(Item));
		}
	}

	private static void Instrument(System.Type type, string[] hookNames, HashSet<MethodInfo> done, System.Delegate hook, params System.Type[] parameters)
	{
		foreach (string name in hookNames) {
			MethodInfo method = type.GetMethod(name,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
				binder: null, types: parameters, modifiers: null);

			if (method == null || method.IsAbstract || !method.IsVirtual || !done.Add(method))
				continue;

			try {
				MonoModHooks.Add(method, hook);
				InstrumentedMethods++;
			}
			catch (System.Exception) {
				//this should prevent crashes i think
			}
		}
	}

	//this part was just back and forth between docs and here lol
	private static void OnSystemHook(orig_SystemHook orig, ModSystem self)
	{
		if (!PerfDiagnostics.Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		PerfDiagnostics.AddModSystemTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
	}

	private static void OnPlayerHook(orig_PlayerHook orig, ModPlayer self)
	{
		if (!PerfDiagnostics.Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		PerfDiagnostics.AddModPlayerTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
	}

	private static void OnNPCVoid(orig_NPCVoid orig, GlobalNPC self, NPC npc)
	{
		if (!PerfDiagnostics.Enabled) { orig(self, npc); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, npc);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
	}

	private static bool OnNPCBool(orig_NPCBool orig, GlobalNPC self, NPC npc)
	{
		if (!PerfDiagnostics.Enabled)
			return orig(self, npc);
		long start = Stopwatch.GetTimestamp();
		bool result = orig(self, npc);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
		return result;
	}

	private static void OnNPCLifeRegen(orig_NPCLifeRegen orig, GlobalNPC self, NPC npc, ref int damage)
	{
		if (!PerfDiagnostics.Enabled) { orig(self, npc, ref damage); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, npc, ref damage);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
	}

	private static void OnNPCDrawEffects(orig_NPCDrawEffects orig, GlobalNPC self, NPC npc, ref Color drawColor)
	{
		if (!PerfDiagnostics.Enabled) { orig(self, npc, ref drawColor); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, npc, ref drawColor);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
	}

	private static void OnNPCPostDraw(orig_NPCPostDraw orig, GlobalNPC self, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
	{
		if (!PerfDiagnostics.Enabled) { orig(self, npc, spriteBatch, screenPos, drawColor); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, npc, spriteBatch, screenPos, drawColor);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
	}

	private static bool OnNPCPreDraw(orig_NPCPreDraw orig, GlobalNPC self, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
	{
		if (!PerfDiagnostics.Enabled)
			return orig(self, npc, spriteBatch, screenPos, drawColor);
		long start = Stopwatch.GetTimestamp();
		bool result = orig(self, npc, spriteBatch, screenPos, drawColor);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
		return result;
	}

	private static void OnProjVoid(orig_ProjVoid orig, GlobalProjectile self, Projectile projectile)
	{
		if (!PerfDiagnostics.Enabled) { orig(self, projectile); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, projectile);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
	}

	private static bool OnProjBool(orig_ProjBool orig, GlobalProjectile self, Projectile projectile)
	{
		if (!PerfDiagnostics.Enabled)
			return orig(self, projectile);
		long start = Stopwatch.GetTimestamp();
		bool result = orig(self, projectile);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
		return result;
	}

	private static void OnProjPostDraw(orig_ProjPostDraw orig, GlobalProjectile self, Projectile projectile, Color lightColor)
	{
		if (!PerfDiagnostics.Enabled) { orig(self, projectile, lightColor); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, projectile, lightColor);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
	}

	private static bool OnProjPreDraw(orig_ProjPreDraw orig, GlobalProjectile self, Projectile projectile, ref Color lightColor)
	{
		if (!PerfDiagnostics.Enabled)
			return orig(self, projectile, ref lightColor);
		long start = Stopwatch.GetTimestamp();
		bool result = orig(self, projectile, ref lightColor);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
		return result;
	}

	private static void OnItemPlayer(orig_ItemPlayer orig, GlobalItem self, Item item, Player player)
	{
		if (!PerfDiagnostics.Enabled) { orig(self, item, player); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, item, player);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
	}

	private static void OnItemVoid(orig_ItemVoid orig, GlobalItem self, Item item)
	{
		if (!PerfDiagnostics.Enabled) { orig(self, item); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, item);
		PerfDiagnostics.AddModGlobalTime(self.Mod?.Name, Stopwatch.GetTimestamp() - start);
	}
}
