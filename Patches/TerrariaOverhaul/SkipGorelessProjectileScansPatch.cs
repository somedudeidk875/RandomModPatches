using System;
using System.Reflection;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//T. Overhaul's ProjectileGoreInteraction is a GlobalProjectile so its PreAI runs for every active projectile every tick
//it also walks all 600 gore slots looking for gore to push around
//   for (int i = 0; i < Main.maxGore; i++) {
//       var gore = Main.gore[i];
//       if (gore == null || !gore.active || Main.gore[i] is not OverhaulGore goreExt) {
//           continue;
//       }
//there is a dontHitGore flag but it only latches after a projectile has actually touched gore and it is cleared again every 3 ticks while the projectile is moving
//a projectile that never touches anything (which is most of them) pays the full walk every single tick

namespace SOTSPerfPatch.Patches.TerrariaOverhaul;

internal sealed class SkipGorelessProjectileScansPatch : PerfPatch
{
	public override string Name => "Overhaul: skip gore scans when there is no gore";

	public override string TargetMod => "TerrariaOverhaul";

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string ModName = "TerrariaOverhaul";
		private const string InteractionType = "TerrariaOverhaul.Common.Interaction.ProjectileGoreInteraction";
		private const string GoreType = "TerrariaOverhaul.Common.BloodAndGore.OverhaulGore";

		//the instance is typed as object because this mod cannot reference Overhaul's types.
		private delegate bool orig_PreAI(object self, Projectile projectile);

		private static FieldInfo dontHitGore;
		private static Type overhaulGore;

		private static uint scannedTick = uint.MaxValue;
		private static bool goreExists;

		private static bool Enabled => PerfPatchConfig.Instance?.SkipGorelessProjectileScans ?? false;

		public static void Install()
		{
			Type interaction = HookHelper.ModType(ModName, InteractionType);
			overhaulGore = HookHelper.ModType(ModName, GoreType);

			dontHitGore = interaction.GetField("dontHitGore", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingFieldException($"{InteractionType}.dontHitGore was not found.");

			if (dontHitGore.FieldType != typeof(bool))
				throw new InvalidOperationException($"{InteractionType}.dontHitGore is no longer a bool.");

			var preAI = HookHelper.Method(interaction, "PreAI", typeof(Projectile));
			MonoModHooks.Add(preAI, OnPreAI);
		}

		public static void Reset()
		{
			dontHitGore = null;
			overhaulGore = null;
			scannedTick = uint.MaxValue;
		}

		private static bool OnPreAI(orig_PreAI orig, object self, Projectile projectile)
		{
			if (!Enabled || AnyOverhaulGore())
				return orig(self, projectile);

			//nothing in the loop can run, so all that is left of the original is this. setting the flag
			//to false when it is already false is the same as leaving it alone, so the condition does
			//not need to read it first.
			if (projectile.position != projectile.oldPosition && projectile.timeLeft % 3 == 0)
				dontHitGore.SetValue(self, false);

			return true;
		}

		//one pass over the gore array per tick, shared by every projectile in it. a plain Gore only
		//becomes an OverhaulGore inside Gore.Update, so this answer cannot turn from false to true
		//while projectiles are updating.
		private static bool AnyOverhaulGore()
		{
			if (scannedTick == Main.GameUpdateCount)
				return goreExists;

			scannedTick = Main.GameUpdateCount;
			goreExists = false;

			Gore[] gores = Main.gore;

			for (int i = 0; i < Main.maxGore; i++) {
				Gore gore = gores[i];

				if (gore != null && gore.active && overhaulGore.IsInstanceOfType(gore)) {
					goreExists = true;
					break;
				}
			}

			return goreExists;
		}
	}
}
