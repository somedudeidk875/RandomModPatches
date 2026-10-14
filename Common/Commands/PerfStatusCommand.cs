using Microsoft.Xna.Framework;
using SOTSPerfPatch.Common.Diagnostics;
using SOTSPerfPatch.Common.Systems;
using SOTSPerfPatch.Core;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Common.Commands;

// /sotsperf            lists every patch and whether it applied
// /sotsperf overlay    toggles the live performance overlay (and measurement)
// /sotsperf log        writes the latest detailed measurement to client.log
// /sotsperf baseline   makes the current state the comparison point for later measurements
// /sotsperf deep       also measures every mod's global entity hooks (costs RAM and frame rate)

//dev stuff, don't turn on this unless u know what ur doing
public class PerfStatusCommand : ModCommand
{
	public override CommandType Type => CommandType.Chat;

	public override string Command => "sotsperf";

	public override string Usage => "/sotsperf [overlay | log | baseline | deep]";

	public override string Description => "SOTS performance patch: patch status, live overlay, or log a measurement.";

	public override void Action(CommandCaller caller, string input, string[] args)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "";

		if (sub != "" && !DiagnosticsSystem.ProfilerInstalled) {
			caller.Reply("The profiler is switched off. Turn on \"Enable profiler\" in this mod's config and reload.", Color.OrangeRed);
			caller.Reply("It is off by default so it costs nothing while you play; the performance fixes work regardless.", Color.LightGray);
			return;
		}

		if (sub == "overlay")
			ToggleOverlay(caller);
		else if (sub == "log")
			WriteLog(caller);
		else if (sub == "baseline")
			SetBaseline(caller);
		else if (sub == "deep")
			EnableDeep(caller);
		else
			ListPatches(caller);
	}

	private static void ToggleOverlay(CommandCaller caller)
	{
		bool show = !DiagnosticsSystem.OverlayVisible;
		DiagnosticsSystem.OverlayVisible = show;
		PerfDiagnostics.SetEnabled(show);
		caller.Reply(show
			? "Performance overlay on. Numbers update every second. Run /sotsperf log to save a measurement."
			: "Performance overlay off.", Color.LightGreen);
	}

	private static void EnableDeep(CommandCaller caller)
	{
		if (ModHookProfiler.DeepInstalled) {
			caller.Reply("Global entity hooks are already being measured.", Color.LightGray);
			return;
		}

		ModHookProfiler.InstallDeep();

		if (!DiagnosticsSystem.OverlayVisible) {
			DiagnosticsSystem.OverlayVisible = true;
			PerfDiagnostics.SetEnabled(true);
			caller.Reply("Overlay turned on, so there is something to measure.", Color.LightGreen);
		}

		caller.Reply("Now measuring every mod's GlobalNPC, GlobalProjectile and GlobalItem hooks.", Color.LightGreen);
		caller.Reply("This costs RAM and some frame rate while it is on. Restart tModLoader to remove it.", Color.Orange);
	}

	private static void SetBaseline(CommandCaller caller)
	{
		if (PerfDiagnostics.SetBaselineToLatest())
			caller.Reply("Baseline set. Later measurements show how much has changed since now.", Color.LightGreen);
		else
			caller.Reply("No measurement yet. Turn the overlay on with /sotsperf overlay, wait a second, then try again.", Color.OrangeRed);
	}

	private void WriteLog(CommandCaller caller)
	{
		Report report = PerfDiagnostics.Latest;
		if (report == null) {
			caller.Reply("No measurement yet. Turn the overlay on with /sotsperf overlay, wait a second, then try again.", Color.OrangeRed);
			return;
		}

		Mod.Logger.Info("----- SOTS performance measurement -----");
		foreach (string line in report.DetailedLines())
			Mod.Logger.Info(line);
		Mod.Logger.Info("----------------------------------------");
		caller.Reply("Measurement written to client.log (search for \"SOTS performance measurement\").", Color.LightGreen);
	}

	private static void ListPatches(CommandCaller caller)
	{
		foreach (PerfPatch patch in PatchRegistry.Patches) {
			string text = "not attempted";
			Color color = Color.LightGray;

			if (patch.State == PatchState.Applied) {
				text = "applied";
				color = Color.LightGreen;
			}
			else if (patch.State == PatchState.SkippedModMissing) {
				text = "skipped (mod not installed)";
			}
			else if (patch.State == PatchState.SkippedOnServer) {
				text = "skipped (server)";
			}
			else if (patch.State == PatchState.Failed) {
				text = "FAILED: " + patch.FailureReason;
				color = Color.OrangeRed;
			}

			caller.Reply($"{patch.Name}: {text}", color);
		}
		if (!DiagnosticsSystem.ProfilerInstalled) {
			caller.Reply("Profiler: off. Turn on \"Enable profiler\" in the config and reload to measure performance.", Color.LightGray);
			return;
		}

		caller.Reply("Tip: /sotsperf overlay shows live timings, /sotsperf baseline marks a reference point, /sotsperf log saves a measurement, /sotsperf deep adds global entity hooks.", Color.LightGray);
	}
}
