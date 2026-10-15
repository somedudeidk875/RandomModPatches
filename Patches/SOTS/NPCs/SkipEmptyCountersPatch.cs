using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SOTS.Common.GlobalNPCs;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Patches.SOTS.NPCs;

//DebuffNPC.PostDraw requests nine textures by path for every NPC every frame to draw permanent debuff counters even though almost every NPC has none
internal sealed class SkipEmptyCountersPatch : PerfPatch
{
	public override string Name => "DebuffNPC counter draw skip";

	public override string TargetMod => "SOTS";

	public override void Apply() => Hooks.Install();

	[JITWhenModsEnabled("SOTS")]
	private static class Hooks
	{
		private delegate void orig_PostDraw(DebuffNPC self, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor);

		private static bool Enabled => PerfPatchConfig.Instance?.SkipEmptyDebuffCounters ?? false;

		public static void Install()
		{
			var postDraw = HookHelper.Method(typeof(DebuffNPC), nameof(DebuffNPC.PostDraw),
				typeof(NPC), typeof(SpriteBatch), typeof(Vector2), typeof(Color));
			MonoModHooks.Add(postDraw, OnPostDraw);
		}

		private static void OnPostDraw(orig_PostDraw orig, DebuffNPC self, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
		{
			if (Enabled && !HasAnyCounter(self))
				return;

			orig(self, npc, spriteBatch, screenPos, drawColor);
		}

		private static bool HasAnyCounter(DebuffNPC d) =>
			d.PlatinumCurse > 0 || d.HarvestCurse > 0 || d.DestableCurse > 0 ||
			d.BleedingCurse > 0 || d.BlazingCurse > 0 || d.AnomalyCurse > 0 ||
			d.BlightCurse > 0 || d.CrystalCurse > 0 || d.DamageCurse > 0;
	}
}
