using System;
using System.Collections.Generic;
using SOTSPerfPatch.Patches.CalamityHunt;
using SOTSPerfPatch.Patches.ContinentOfJourney;
using SOTSPerfPatch.Patches.InfernalEclipse;
using SOTSPerfPatch.Patches.Infernum;
using SOTSPerfPatch.Patches.Luminance;
using SOTSPerfPatch.Patches.InventoryVisualTweaks;
using SOTSPerfPatch.Patches.Macrocosm;
using SOTSPerfPatch.Patches.Noxus;
using SOTSPerfPatch.Patches.SOTS.NPCs;
using SOTSPerfPatch.Patches.SOTS.Combat;
using SOTSPerfPatch.Patches.Redemption;
using SOTSPerfPatch.Patches.SOTS.Rendering;
using SOTSPerfPatch.Patches.SOTS.Tiles;
using SOTSPerfPatch.Patches.SOTS.Trails;
using SOTSPerfPatch.Patches.TerrariaOverhaul;
using SOTSPerfPatch.Patches.ThoriumRework;
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
		new SkipUnusedWaterLayers(),
		new DrawNPCEffectsOnce(),
		new FreeResizedTargets(),
		new FreeDarknessTextures(),
		new SkipEmptyFoamDraws(),

		//trails
		new ReuseTrailBuffers(),
		new FreeTrailEffects(),
		new EndOrphanedTrails(),

		//NPCs
		new SkipDebuffScans(),
		new SkipVineScans(),
		new SkipEmptyCounters(),
		new CacheParticleMasks(),

		//combat
		new SkipMeleeHitScans(),

		//world
		new ThrottleSteam(),
		new ClearListsOnExit(),

		//tiles
		new FasterClosestPlayer(),
		new SkipOffscreenAuras(),

		//WOTG
		new SkipEmptyGrass(),
		new FasterNearestGrass(),
		new SkipIdleSnowScans(),

		//IEOR
		new CacheItemLookups(),
		new SkipIdleNPCs(),

		//MoR
		new FreeReplacedTargets(),
		new SkipIdleEffectLayers(),

		//HWJ
		new CacheRitualChecks(),

		//inventory visual tweaks
		new SkipIdleParticles(),

		//VCMM
		new SkipAbsentNPCScans(),

		//terraria overhaul
		new FreeDistantChunks(),
		new SkipGoreScans(),

		//macrocosm
		new SkipEmptyDraws(),

		//thorium rework
		new CacheTargetChecks(),

		//hunt of the old god
		new SkipIdleTileHighlight(),
		new SkipEmptyScreenPasses(),

		//infernum
		new CacheMetaballMasks(),
		new FasterTrailPoints(),

		//luminance
		new SkipEmptyPixelationPasses(),
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
		CacheItemLookups.Reset();
		CacheRitualChecks.Reset();
		SkipIdleParticles.Reset();
		SkipEmptyDraws.Reset();
		CacheTargetChecks.Reset();
		SkipAbsentNPCScans.Reset();
		FreeDistantChunks.Reset();
		SkipGoreScans.Reset();
		SkipIdleTileHighlight.Reset();
		SkipEmptyScreenPasses.Reset();
		CacheParticleMasks.Reset();
		SkipEmptyFoamDraws.Reset();
		CacheMetaballMasks.Reset();
		SkipEmptyPixelationPasses.Reset();
		patches.Clear();
	}
}
