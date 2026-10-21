using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace SOTSPerfPatch.Common.Configs;

//self-explanatory imo
public class PerfPatchConfig : ModConfig
{
	public override ConfigScope Mode => ConfigScope.ClientSide;

	public static PerfPatchConfig Instance => ModContent.GetInstance<PerfPatchConfig>();

	[Header("Profiler")]
	[DefaultValue(false)]
	[ReloadRequired]
	public bool EnableProfiler;

	[Header("SecretsOfTheShadows")]
	[DefaultValue(true)]
	public bool GateWaterLayer;

	[DefaultValue(true)]
	public bool SkipDuplicateNPCPass;

	[DefaultValue(true)]
	public bool FixRenderTargetLeaks;

	[DefaultValue(true)]
	public bool FixShadeTextureLeaks;

	[DefaultValue(true)]
	public bool ReduceTrailAllocations;

	[DefaultValue(true)]
	public bool SweepStuckTrails;

	[DefaultValue(true)]
	public bool DisposeTrailEffects;

	[DefaultValue(true)]
	public bool EndOrphanPolarisTrails;

	[DefaultValue(true)]
	public bool GateDebuffProjectileLoop;

	[DefaultValue(true)]
	public bool GateDebuffVineDraw;

	[DefaultValue(true)]
	public bool SkipEmptyDebuffCounters;

	[DefaultValue(true)]
	public bool MeleeHitFastPath;

	[DefaultValue(true)]
	public bool FastClosestPlayer;

	[DefaultValue(true)]
	public bool CleanupOnWorldExit;

	[DefaultValue(true)]
	public bool ThrottleSanctuarySteam;

	[DefaultValue(30)]
	[Range(0, 90)]
	public int SteamSamples;

	[DefaultValue(10)]
	[Range(0, 30)]
	public int SteamSamplesReduced;

	[DefaultValue(400)]
	[Range(50, 600)]
	public int SteamGoreCap;

	[Header("WrathOfTheGods")]
	[DefaultValue(true)]
	public bool SkipEmptyGenesisGrass;

	[DefaultValue(true)]
	public bool FasterGenesisGrassNearest;

	[DefaultValue(true)]
	public bool SkipIdleSnowScans;

	[Header("InfernalEclipse")]
	[DefaultValue(true)]
	public bool CacheItemBalanceLookups;

	[DefaultValue(true)]
	public bool GateInfernalNPCPreAI;

	[Header("Redemption")]
	[DefaultValue(true)]
	public bool FixRedemptionTargetLeak;

	[DefaultValue(true)]
	public bool SkipIdleRedemptionLayers;

	[Header("HomewardJourney")]
	[DefaultValue(true)]
	public bool CacheRitualChecks;

	[Header("InventoryVisualTweaks")]
	[DefaultValue(true)]
	public bool SkipIdleParticleLayers;

	[Header("VanillaCalamityMusic")]
	[DefaultValue(true)]
	public bool SkipAbsentNPCScans;

	[Header("TerrariaOverhaul")]
	[DefaultValue(true)]
	public bool FreeDistantChunkTextures;

	[DefaultValue(192)]
	[Range(128, 1024)]
	public int ChunkTextureBudget;

	[DefaultValue(true)]
	public bool SkipGorelessProjectileScans;
}
