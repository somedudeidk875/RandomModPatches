using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Common.Diagnostics;
using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Common.Systems;

//installs the profiler hooks, produces reports and draws the overlay
public class DiagnosticsSystem : ModSystem
{
	public static bool OverlayVisible { get; set; }

	public static bool ProfilerInstalled { get; private set; }

	public override void PostSetupContent()
	{
		ProfilerInstalled = !Main.dedServ && (PerfPatchConfig.Instance?.EnableProfiler ?? false);
		if (!ProfilerInstalled)
			return;

		PerfDiagnostics.Install();
		PerfDiagnostics.AllocateTypeArrays();
		ModHookProfiler.Install();
		InterfaceLayerProfiler.Install();
		RenderLoopProfiler.Find();
	}

	public override void Unload()
	{
		PerfDiagnostics.SetEnabled(false);
		GraphicsResourceProbe.Reset();
		OverlayVisible = false;
	}

	public override void PostUpdateEverything()
	{
		if (ProfilerInstalled)
			PerfDiagnostics.Tick();
	}

	public override void PostDrawInterface(SpriteBatch spriteBatch)
	{
		if (!OverlayVisible)
			return;

		var position = new Vector2(20f, Main.screenHeight * 0.3f);
		Report report = PerfDiagnostics.Latest;
		if (report == null) {
			Utils.DrawBorderString(spriteBatch, "SOTS Perf: collecting...", position, Color.White, 0.8f);
			return;
		}

		foreach (string line in report.OverlayLines()) {
			Utils.DrawBorderString(spriteBatch, line, position, Color.White, 0.8f);
			position.Y += 20f;
		}
	}
}
