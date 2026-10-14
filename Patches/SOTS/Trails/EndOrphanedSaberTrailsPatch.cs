using SOTS.NPCs.Boss.Polaris.NewPolaris;
using SOTS.Prim;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Patches.SOTS.Trails;

//a Polaris saber trail only ends when its owner (a PolarisWeaponData) becomes inactive but PolarisWeaponData marks itself active on every update and nothing ever marks it inactive
//if Polaris dies or despawns with the saber out the trail keeps updating and drawing forever and because SOTS's trail list is global it even survives leaving the world

internal sealed class EndOrphanedSaberTrailsPatch : PerfPatch
{
	public override string Name => "Polaris orphaned trail fix";

	public override string TargetMod => "SOTS";

	public static int TrailsEnded { get; private set; }

	public override void Apply() => Hooks.Install();

	private static class Hooks
	{
		private const string SaberTrailTypeName = "SOTS.Prim.Trails.PolarisSaberTrail";

		private delegate void orig_OnUpdate(PrimTrail self);

		private static bool Enabled => PerfPatchConfig.Instance?.EndOrphanPolarisTrails ?? false;

		public static void Install()
		{
			System.Type saberTrail = typeof(PrimTrail).Assembly.GetType(SaberTrailTypeName)
				?? throw new System.TypeLoadException($"{SaberTrailTypeName} was not found. SOTS may have been updated.");
			if (!typeof(PrimTrail).IsAssignableFrom(saberTrail))
				throw new System.TypeLoadException($"{SaberTrailTypeName} no longer derives from PrimTrail. SOTS may have been updated.");

			var onUpdate = HookHelper.Method(saberTrail, "OnUpdate");
			//resolved up front so a renamed field fails this patch at load instead of mid-game
			_ = typeof(NewPolaris).GetField(nameof(NewPolaris.polarisWeaponData))
				?? throw new System.MissingFieldException("NewPolaris.polarisWeaponData was not found. SOTS may have been updated.");

			TrailsEnded = 0;
			MonoModHooks.Add(onUpdate, OnUpdate);
		}

		private static void OnUpdate(orig_OnUpdate orig, PrimTrail self)
		{
			var weapon = self.Entity as PolarisWeaponData;
			if (Enabled && !self.Destroyed && weapon != null && !IsOwnedByLivingPolaris(weapon)) {
				self.OnDestroy();
				TrailsEnded++;
				return;
			}

			orig(self);
		}

		private static bool IsOwnedByLivingPolaris(PolarisWeaponData weapon)
		{
			for (int i = 0; i < Main.maxNPCs; i++) {
				NPC npc = Main.npc[i];
				if (!npc.active)
					continue;

				var polaris = npc.ModNPC as NewPolaris;
				if (polaris == null || polaris.polarisWeaponData == null)
					continue;

				foreach (PolarisWeaponData owned in polaris.polarisWeaponData) {
					if (ReferenceEquals(owned, weapon))
						return true;
				}
			}
			return false;
		}
	}
}
