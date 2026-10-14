using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Common.Systems;

//scans the projectile array once per tick and records whether any projectile of certain SOTS types is alive (this is better than whats in place beforehand)
public class SOTSEntityTracker : ModSystem
{
	private static readonly string[] DebuffLoopNames = {
		"FluxSlimeBall", "Doomhook", "PlatinumDart", "Rebar",
		"FloweringBud", "EvilGrowth", "HydroBubble", "AccretionSingularity",
	};

	private static readonly string[] VineNames = { "FloweringBud", "EvilGrowth" };

	private static readonly string[] WaterLayerNames = { "WaterShark", "HydroBubble", "HydroServant" };

	private static bool[] isDebuffLoopType;
	private static bool[] isVineType;
	private static bool[] isWaterLayerType;

	private static bool debuffNamesFound;
	private static bool vineNamesFound;
	private static bool waterLayerNamesFound;

	public static bool AnyDebuffLoopProjectile { get; private set; } = true;
	public static bool AnyVineProjectile { get; private set; } = true;
	public static bool AnyWaterLayerProjectile { get; private set; } = true;

	public override void PostSetupContent()
	{
		isDebuffLoopType = new bool[ProjectileLoader.ProjectileCount];
		isVineType = new bool[ProjectileLoader.ProjectileCount];
		isWaterLayerType = new bool[ProjectileLoader.ProjectileCount];

		debuffNamesFound = MarkTypes(DebuffLoopNames, isDebuffLoopType);
		vineNamesFound = MarkTypes(VineNames, isVineType);
		waterLayerNamesFound = MarkTypes(WaterLayerNames, isWaterLayerType);
	}

	//looks up each projectile by its internal name
	private bool MarkTypes(string[] names, bool[] lookup)
	{
		bool allFound = true;
		foreach (string name in names) {
			if (ModContent.TryFind("SOTS", name, out ModProjectile projectile)) {
				lookup[projectile.Type] = true;
			}
			else {
				allFound = false;
				Mod.Logger.Warn($"SOTS projectile '{name}' not found; patches that depend on it will fall back to original behavior.");
			}
		}
		return allFound;
	}

	public override void Unload()
	{
		isDebuffLoopType = isVineType = isWaterLayerType = null;
		ResetFlags();
	}

	public override void OnWorldUnload() => ResetFlags();

	public override void PreUpdateNPCs() => Scan();

	public override void PostUpdateEverything() => Scan();

	private static void ResetFlags()
	{
		AnyDebuffLoopProjectile = true;
		AnyVineProjectile = true;
		AnyWaterLayerProjectile = true;
	}

	private static void Scan()
	{
		if (isDebuffLoopType == null) {
			ResetFlags();
			return;
		}

		bool debuffLoop = false, vine = false, waterLayer = false;
		for (int i = 0; i < Main.maxProjectiles; i++) {
			Projectile projectile = Main.projectile[i];
			if (!projectile.active)
				continue;

			int type = projectile.type;
			if (type < 0 || type >= isDebuffLoopType.Length)
				continue;

			if (isDebuffLoopType[type])
				debuffLoop = true;
			if (isVineType[type])
				vine = true;
			if (isWaterLayerType[type])
				waterLayer = true;
		}

		AnyDebuffLoopProjectile = debuffLoop || !debuffNamesFound;
		AnyVineProjectile = vine || !vineNamesFound;
		AnyWaterLayerProjectile = waterLayer || !waterLayerNamesFound;
	}

}
