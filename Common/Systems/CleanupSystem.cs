using System;
using SOTSPerfPatch.Patches.SOTS.Rendering;
using SOTSPerfPatch.Patches.Noxus;
using SOTSPerfPatch.Patches.SOTS.World;
using SOTSPerfPatch.Patches.TerrariaOverhaul;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Common.Systems;

//cleans up stuff
public class CleanupSystem : ModSystem
{
	public override void PreUpdateEntities()
	{
		Safe(FreeDarknessTexturesPatch.OnUpdate);
		Safe(FreeDistantChunkTexturesPatch.OnUpdate);
	}

	public override void OnWorldUnload() => Safe(ClearListsOnWorldExitPatch.Run);

	public override void OnWorldLoad() => Safe(NoxusWorldLoad);

	private static void NoxusWorldLoad()
	{
		if (ModLoader.HasMod("NoxusBoss"))
			SkipIdleSnowScansPatch.OnWorldLoad();
	}

	private void Safe(Action action)
	{
		try {
			action();
		}
		catch (Exception e) {
			Mod.Logger.Warn("A cleanup step failed and was skipped.", e);
		}
	}
}
