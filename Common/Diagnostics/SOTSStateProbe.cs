using System;
using SOTSPerfPatch.Patches.SOTS.Rendering;
using SOTSPerfPatch.Patches.SOTS.Trails;

namespace SOTSPerfPatch.Common.Diagnostics;

//sizes of SOTS global lists
internal static class SOTSStateProbe
{
	public static string Describe()
	{
		string fixes = $"shade textures freed {FreeDarknessTexturesPatch.TexturesFreed}, orphan trails ended {EndOrphanedSaberTrailsPatch.TrailsEnded}";
		try {
			return Read() + " | " + fixes;
		}
		catch (Exception) {
			return "state unavailable | " + fixes;
		}
	}

	private static string Read()
	{
		int trails = global::SOTS.SOTS.primitives?._trails?.Count ?? -1;
		int water = global::SOTS.ParticleHelper.waterParticles?.Count ?? -1;
		int camera = global::SOTS.SOTSPlayer.CameraShiftProjectiles?.Count ?? -1;
		return $"trails {trails}  water particles {water}  camera list {camera}";
	}
}
