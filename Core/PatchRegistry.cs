using System;
using System.Collections.Generic;
using SOTSPerfPatch.Patches.ContinentOfJourney;
using SOTSPerfPatch.Patches.InfernalEclipse;
using SOTSPerfPatch.Patches.InventoryVisualTweaks;
using SOTSPerfPatch.Patches.Noxus;
using SOTSPerfPatch.Patches.SOTS.NPCs;
using SOTSPerfPatch.Patches.SOTS.Combat;
using SOTSPerfPatch.Patches.Redemption;
using SOTSPerfPatch.Patches.SOTS.Rendering;
using SOTSPerfPatch.Patches.SOTS.Tiles;
using SOTSPerfPatch.Patches.SOTS.Trails;
using SOTSPerfPatch.Patches.TerrariaOverhaul;
using SOTSPerfPatch.Patches.UnCalamityModMusic;
using SOTSPerfPatch.Patches.SOTS.World;
using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Core;

internal static class PatchRegistry
{
	private static readonly List<PerfPatch> patches = new();

	public static IReadOnlyList<PerfPatch> Patches => patches;

	//every patch in the mod in the order they are applied.
	private static PerfPatch[] CreatePatches() => new PerfPatch[] {
		//SOTS
		//rendering
		new SkipUnusedWaterLayersPatch(),
		new DrawNPCEffectsOncePatch(),
		new FreeResizedTargetsPatch(),
		new FreeDarknessTexturesPatch(),

		//trails
		new ReuseTrailBuffersPatch(),
		new FreeTrailEffectsPatch(),
		new EndOrphanedSaberTrailsPatch(),

		//NPCs
		new SkipDebuffProjectileScanPatch(),
		new SkipVineDrawScanPatch(),
		new SkipEmptyCountersPatch(),

		//combat
		new SkipMeleeHitWrapperPatch(),

		//world
		new ThrottleSanctuarySteamPatch(),
		new ClearListsOnWorldExitPatch(),

		//tiles
		new FasterClosestPlayerPatch(),

		//WOTG
		new SkipEmptyGenesisGrassPatch(),
		new FasterNearestGrassPatch(),
		new SkipIdleSnowScansPatch(),

		//IEOR
		new CacheItemLookupsPatch(),
		new SkipIdleNPCPreAIPatch(),

		//MoR
		new FreeReplacedTargetsPatch(),
		new SkipIdleEffectLayersPatch(),

		//HWJ
		new CacheRitualChecksPatch(),

		//inventory visual tweaks
		new SkipIdleParticleLayersPatch(),

		//VCMM
		new SkipAbsentNPCScansPatch(),

		//terraria overhaul
		new FreeDistantChunkTexturesPatch(),
		new SkipGorelessProjectileScansPatch(),
	};

	public static void ApplyAll(Mod mod)
	{
		patches.Clear();
		patches.AddRange(CreatePatches());

		int applied = 0;
		foreach (PerfPatch patch in patches) {
			if (!ModLoader.HasMod(patch.TargetMod)) {
				patch.State = PatchState.SkippedModMissing;
				continue;
			}

			if (patch.ClientOnly && Main.dedServ) {
				patch.State = PatchState.SkippedOnServer;
				continue;
			}

			try {
				patch.Apply();
				patch.State = PatchState.Applied;
				applied++;
				mod.Logger.Info($"Applied: {patch.Name}");
			}
			catch (Exception e) {
				patch.State = PatchState.Failed;
				patch.FailureReason = e.GetBaseException().Message;
				mod.Logger.Warn($"Could not apply '{patch.Name}'. It is disabled; everything else still works.", e);
			}
		}

		mod.Logger.Info($"{applied} of {patches.Count} patches applied.");
	}

	public static void Clear()
	{
		//drops the mod references the lookup cache hold so other mods' assemblies can unload
		CacheItemLookupsPatch.Reset();
		CacheRitualChecksPatch.Reset();
		SkipIdleParticleLayersPatch.Reset();
		SkipAbsentNPCScansPatch.Reset();
		FreeDistantChunkTexturesPatch.Reset();
		SkipGorelessProjectileScansPatch.Reset();
		patches.Clear();
	}
}
