using System.Collections.Generic;
using SOTS.Prim;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;

namespace SOTSPerfPatch.Patches.SOTS.World;

//some SOTS state is global and is never cleared when you leave a world so anything stuck in it carries over into the next world until tModLoader restarts
//this includes
//   the trail list (SOTS.primitives._trails) including the GPU effect each trail owns
//   the water particle list
//   the camera-shift projectile list which stores projectile slot numbers that mean nothing in another world

internal sealed class ClearListsOnWorldExitPatch : PerfPatch
{
	public override string Name => "World exit cleanup";

	public override string TargetMod => "SOTS";

	private static bool applied;

	public override void Apply()
	{
		Hooks.Validate();
		applied = true;
	}

	internal static void Run()
	{
		if (!applied || Main.dedServ || !(PerfPatchConfig.Instance?.CleanupOnWorldExit ?? false))
			return;

		Hooks.Clear();
	}

	private static class Hooks
	{
		//touch every member once at load so a missing one fails this patch instead of world unload
		public static void Validate()
		{
			_ = global::SOTS.SOTS.primitives;
			_ = global::SOTS.ParticleHelper.waterParticles;
			_ = global::SOTS.SOTSPlayer.CameraShiftProjectiles;
		}

		public static void Clear()
		{
			PrimTrailManager manager = global::SOTS.SOTS.primitives;
			if (manager?._trails != null) {
				//dispose removes the trail from the list so iterate a copy
				foreach (PrimTrail trail in new List<PrimTrail>(manager._trails))
					trail.Dispose();
				manager._trails.Clear();
			}

			global::SOTS.ParticleHelper.waterParticles?.Clear();
			global::SOTS.SOTSPlayer.CameraShiftProjectiles.Clear();
		}
	}
}
