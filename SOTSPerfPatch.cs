using SOTSPerfPatch.Core;
using Terraria.ModLoader;

namespace SOTSPerfPatch;

public class SOTSPerfPatch : Mod
{
	internal static SOTSPerfPatch Instance { get; private set; }

	public override void Load()
	{
		Instance = this;
		PatchRegistry.ApplyAll(this);
	}

	public override void Unload()
	{
		PatchRegistry.Clear();
		Instance = null;
	}
}
