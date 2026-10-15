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

//DebuffNPC.PostAI runs for every NPC every tick and loops over all 1000 projectile slots
//this was like the third thing i did and i realized that there would prob be a lot more per tick/frame checks in SOTS lol
internal sealed class SkipDebuffProjectileScanPatch : PerfPatch
{
	public override string Name => "DebuffNPC projectile loop gate";

	public override string TargetMod => "SOTS";

	public override bool ClientOnly => false;

	public override void Apply() => Hooks.Install();

	[JITWhenModsEnabled("SOTS")]
	private static class Hooks
	{
		public static void Install()
		{
			var postAI = HookHelper.Method(typeof(DebuffNPC), nameof(DebuffNPC.PostAI), typeof(NPC));
			MonoModHooks.Modify(postAI, EditPostAI);
		}

		private static void EditPostAI(ILContext il)
		{
			var c = new ILCursor(il);

			int site = -1, count = 0;
			while (c.TryGotoNext(MoveType.After,
				i => i.MatchLdsfld<Main>(nameof(Main.projectile)),
				i => i.MatchLdlen(),
				i => i.MatchConvI4())) {
				site = c.Index;
				count++;
			}

			if (count != 1)
				throw new InvalidOperationException($"Expected exactly one Main.projectile.Length in DebuffNPC.PostAI, found {count}. SOTS may have been updated.");

			c.Index = site;
			c.EmitDelegate<Func<int, int>>(GateLoopBound);
		}

		private static int GateLoopBound(int length)
		{
			bool enabled = PerfPatchConfig.Instance?.GateDebuffProjectileLoop ?? false;
			return !enabled || SOTSEntityTracker.AnyDebuffLoopProjectile ? length : 0;
		}
	}
}
