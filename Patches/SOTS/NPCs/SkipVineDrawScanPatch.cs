using System;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using SOTS.Common.GlobalNPCs;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Common.Systems;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Patches.SOTS.NPCs;

//DebuffNPC.PreDraw loops over projectiles for every NPC every frame looking for FloweringBud and EvilGrowth vines
//it uses Main.maxNPCs (200) as the bound so it only ever sees the first 200 projectile slots which i think is a bug???

internal sealed class SkipVineDrawScanPatch : PerfPatch
{
	public override string Name => "DebuffNPC vine draw gate";

	public override string TargetMod => "SOTS";

	public override void Apply() => Hooks.Install();

	private static class Hooks
	{
		public static void Install()
		{
			var preDraw = HookHelper.Method(typeof(DebuffNPC), nameof(DebuffNPC.PreDraw),
				typeof(NPC), typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch),
				typeof(Microsoft.Xna.Framework.Vector2), typeof(Microsoft.Xna.Framework.Color));
			MonoModHooks.Modify(preDraw, EditPreDraw);
		}

		private static void EditPreDraw(ILContext il)
		{
			var c = new ILCursor(il);

			//the loop condition compares the index against Main.maxNPCs followed by either blt or clt
			int site = -1, count = 0;
			while (c.TryGotoNext(MoveType.After,
				i => i.MatchLdsfld<Main>(nameof(Main.maxNPCs)) || i.MatchLdcI4(200),
				i => i.OpCode == OpCodes.Blt || i.OpCode == OpCodes.Blt_S || i.OpCode == OpCodes.Clt)) {
				site = c.Index - 1; //between the constant and the comparison
				count++;
			}

			if (count != 1)
				throw new InvalidOperationException($"Expected exactly one loop bounded by Main.maxNPCs in DebuffNPC.PreDraw, found {count}. SOTS may have been updated.");

			c.Index = site;
			c.EmitDelegate<Func<int, int>>(GateLoopBound);
		}

		private static int GateLoopBound(int originalBound)
		{
			bool enabled = PerfPatchConfig.Instance?.GateDebuffVineDraw ?? false;
			if (!enabled)
				return originalBound;

			return SOTSEntityTracker.AnyVineProjectile ? Main.maxProjectiles : 0;
		}
	}
}
