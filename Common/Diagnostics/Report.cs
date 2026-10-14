using System.Collections.Generic;

namespace SOTSPerfPatch.Common.Diagnostics;

//one second of measurements, stage times are per update or per frame, in ms
internal sealed class Report
{
	public sealed class TypeCost
	{
		public string Name;
		public string Mod;
		public double UpdateMs;
		public double DrawMs;

		public override string ToString() => $"{Name} [{Mod}] {UpdateMs + DrawMs:0.00} ms (upd {UpdateMs:0.00}, draw {DrawMs:0.00})";
	}

	public sealed class ModCost
	{
		public string Name;
		public double SystemMs;
		public double PlayerMs;
		public double GlobalMs;

		public override string ToString() =>
			$"{Name} {SystemMs + PlayerMs + GlobalMs:0.00} ms (systems {SystemMs:0.00}, player {PlayerMs:0.00}, globals {GlobalMs:0.00})";
	}

	public double Fps, Ups, GameTicksPerSecond;
	public double UpdateMs, UpdateNPCsMs, UpdateProjectilesMs, UpdateDustMs, UpdateGoreMs, UpdatePlayersMs, UpdateModSystemsMs, UpdateAudioMs;
	public double DrawMs, DrawNPCLayerMs, DrawProjectileLayerMs, DrawDustMs, DrawGoreMs, DrawPlayersMs, WaterLayersMs, DrawInterfaceMs;
	public double DrawTilesMs, DrawWallsMs, DrawWaterMs, DrawBackgroundMs, DrawMapMs, DrawItemsMs, DrawRainMs, DrawWiresMs, LightingMs;
	public double ScreenShadersMs, SkiesMs, CachedProjectilesMs;
	public string EffectState = string.Empty;
	public int ActiveFilters, ActiveSkies, RenderTargetSubscribers;
	public bool UiLayersInstrumented;
	public int UiLayersDrawn;
	public double HeapMb, HeapGrowthMb, PeakHeapMb, AllocMbPerSec, Gen0PerSec, Gen1PerSec, Gen2PerSec, GcPauseMsPerSec;
	public int GraphicsResources;
	public double ProcessMb;
	public string GraphicsBreakdown = string.Empty;
	public int RedemptionTargetsFreed;
	public int InstrumentedModMethods;
	public bool DeepHooksInstalled;
	public List<ModCost> TopMods = new();
	public List<ModCost> TopInterfaceLayers = new();
	public List<ModCost> TopRenderTargets = new();
	public double RenderTargetsMs;
	public bool RenderLoopAvailable;

	public Report Baseline;

	//update time not covered by the measured stages
	public double UnaccountedUpdateMs =>
		UpdateMs - (UpdateNPCsMs + UpdateProjectilesMs + UpdateDustMs + UpdateGoreMs + UpdatePlayersMs + UpdateModSystemsMs + UpdateAudioMs);

	//draw time not covered by the measured stages
	public double UnaccountedDrawMs =>
		DrawMs - (DrawNPCLayerMs + DrawProjectileLayerMs + DrawDustMs + DrawGoreMs + DrawPlayersMs +
			WaterLayersMs + DrawInterfaceMs + DrawTilesMs + DrawWallsMs + DrawWaterMs + DrawBackgroundMs +
			DrawMapMs + DrawItemsMs + DrawRainMs + DrawWiresMs + ScreenShadersMs + SkiesMs + CachedProjectilesMs);
	public int ActiveNPCs, ActiveProjectiles, ActiveDust, ActiveGore, ActiveItems;
	public int PlayingMusicTracks;
	public string PlayingMusicNames;
	public List<TypeCost> TopNPCs = new();
	public List<TypeCost> TopProjectiles = new();
	public List<TypeCost> TopItems = new();
	public string SOTSState = string.Empty;

	//name of the first entry in a list
	private static string FirstOrDash<T>(List<T> list) => list.Count > 0 ? list[0].ToString() : "-";

	//short form for the on-screen overlay
	//dont look at this for took long or u might lose braincells
	public List<string> OverlayLines()
	{
		var lines = new List<string>();
		lines.Add($"SOTS Perf  {Fps:0} fps  {Ups:0} update calls/s  {GameTicksPerSecond:0} game ticks/s   update {UpdateMs:0.0} ms   draw {DrawMs:0.0} ms");
		lines.Add($"Update: NPCs {UpdateNPCsMs:0.00}  proj {UpdateProjectilesMs:0.00}  dust {UpdateDustMs:0.00}  gore {UpdateGoreMs:0.00}  " +
			$"players {UpdatePlayersMs:0.00}  mod systems {UpdateModSystemsMs:0.00}  audio {UpdateAudioMs:0.00}  other {UnaccountedUpdateMs:0.00}");

		double perTick = GameTicksPerSecond > 0 ? Ups / GameTicksPerSecond : 1.0;
		if (perTick > 1.05) {
			lines.Add($"Per game tick: NPCs {UpdateNPCsMs * perTick:0.00}  proj {UpdateProjectilesMs * perTick:0.00}  " +
				$"players {UpdatePlayersMs * perTick:0.00}  mod systems {UpdateModSystemsMs * perTick:0.00}  " +
				$"other {UnaccountedUpdateMs * perTick:0.00}  total {UpdateMs * perTick:0.0} of 16.7 ms");
		}
		lines.Add($"Draw: NPC layer {DrawNPCLayerMs:0.00}  proj layer {DrawProjectileLayerMs:0.00}  players {DrawPlayersMs:0.00}  " +
			$"dust {DrawDustMs:0.00}  gore {DrawGoreMs:0.00}  water {WaterLayersMs:0.00}  UI {DrawInterfaceMs:0.00}");
		lines.Add($"Draw: tiles {DrawTilesMs:0.00}  walls {DrawWallsMs:0.00}  water {DrawWaterMs:0.00}  background {DrawBackgroundMs:0.00}  " +
			$"items {DrawItemsMs:0.00}  map {DrawMapMs:0.00}  rain {DrawRainMs:0.00}  wires {DrawWiresMs:0.00}");
		lines.Add($"Draw: shaders {ScreenShadersMs:0.00}  skies {SkiesMs:0.00}  cached projs {CachedProjectilesMs:0.00}  " +
			$"other {UnaccountedDrawMs:0.00}   (lighting threads {LightingMs:0.00})");
		lines.Add($"Screen effects: {EffectState}");
		if (TopInterfaceLayers.Count > 0)
			lines.Add($"Top UI layer: {TopInterfaceLayers[0].Name} {TopInterfaceLayers[0].SystemMs:0.00} ms");
		else
			lines.Add($"Top UI layer: none measured (hook {(UiLayersInstrumented ? "on" : "off")}, {UiLayersDrawn} layers drawn)");
		lines.Add($"Top mod: {FirstOrDash(TopMods)}");
		lines.Add($"Top NPC: {FirstOrDash(TopNPCs)}");
		lines.Add($"Top proj: {FirstOrDash(TopProjectiles)}");
		lines.Add($"Active: NPCs {ActiveNPCs}  proj {ActiveProjectiles}  items {ActiveItems}  dust {ActiveDust}  gore {ActiveGore}   music playing {PlayingMusicTracks}");
		lines.Add($"Top dropped item: {FirstOrDash(TopItems)}");
		lines.Add($"Memory: heap {HeapMb:0} MB (peak {PeakHeapMb:0}, {HeapGrowthMb:+0;-0;0} MB since overlay on)  alloc {AllocMbPerSec:0.0} MB/s  " +
			$"GC {Gen0PerSec:0.0}/{Gen1PerSec:0.0}/{Gen2PerSec:0.0} per s  pause {GcPauseMsPerSec:0.0} ms/s");
		lines.Add($"Graphics: {GraphicsResources} live resources" +
			(GraphicsBreakdown.Length > 0 ? $" ({GraphicsBreakdown})" : "") +
			$"  process {ProcessMb:0} MB" +
			(RedemptionTargetsFreed > 0 ? $"  (Redemption targets freed {RedemptionTargetsFreed})" : ""));
		lines.Add($"SOTS: {SOTSState}");

		if (Baseline != null && Baseline != this) {
			lines.Add($"vs baseline: fps {Fps - Baseline.Fps:+0;-0;0}  update {UpdateMs - Baseline.UpdateMs:+0.0;-0.0;0}  " +
				$"draw {DrawMs - Baseline.DrawMs:+0.0;-0.0;0}  items {DrawItemsMs - Baseline.DrawItemsMs:+0.00;-0.00;0}  " +
				$"other {UnaccountedDrawMs - Baseline.UnaccountedDrawMs:+0.00;-0.00;0}  " +
				$"heap {HeapMb - Baseline.HeapMb:+0;-0;0} MB  world items {ActiveItems - Baseline.ActiveItems:+0;-0;0}  " +
				$"audio {UpdateAudioMs - Baseline.UpdateAudioMs:+0.00;-0.00;0}  music tracks {PlayingMusicTracks - Baseline.PlayingMusicTracks:+0;-0;0}");
			lines.Add($"vs baseline: filters {ActiveFilters - Baseline.ActiveFilters:+0;-0;0}  skies {ActiveSkies - Baseline.ActiveSkies:+0;-0;0}  " +
				$"render targets {RenderTargetsMs - Baseline.RenderTargetsMs:+0.00;-0.00;0} ms  UI {DrawInterfaceMs - Baseline.DrawInterfaceMs:+0.00;-0.00;0} ms  " +
				$"NPC updates {UpdateNPCsMs - Baseline.UpdateNPCsMs:+0.00;-0.00;0} ms  proj updates {UpdateProjectilesMs - Baseline.UpdateProjectilesMs:+0.00;-0.00;0} ms");
			lines.Add($"vs baseline: graphics resources {GraphicsResources - Baseline.GraphicsResources:+0;-0;0}  " +
				$"process {ProcessMb - Baseline.ProcessMb:+0;-0;0} MB");
		}

		return lines;
	}

	//long form for the log
	public List<string> DetailedLines()
	{
		var lines = new List<string>(OverlayLines());
		foreach (string line in OverlayLines())
			lines.Add(line);

		lines.Add($"Music tracks playing: {(string.IsNullOrEmpty(PlayingMusicNames) ? "none" : PlayingMusicNames)}");
		lines.Add("Top NPC types (update + draw per frame):");
		foreach (TypeCost cost in TopNPCs)
			lines.Add("  " + cost);
		lines.Add("Top projectile types (update + draw per frame):");
		foreach (TypeCost cost in TopProjectiles)
			lines.Add("  " + cost);
		lines.Add("Top dropped item types (draw per frame):");
		foreach (TypeCost cost in TopItems)
			lines.Add("  " + cost);
		lines.Add($"Top mods by per-tick hook time ({InstrumentedModMethods} hooks measured" +
			(DeepHooksInstalled ? ", including global entity hooks" : "; run /sotsperf deep to include global entity hooks") + "):");
		foreach (ModCost cost in TopMods)
			lines.Add("  " + cost);
		if (RenderLoopAvailable) {
			lines.Add("Top mod render targets (per frame, part of draw \"other\"):");
			foreach (ModCost target in TopRenderTargets)
				lines.Add($"  {target.Name} {target.SystemMs:0.00} ms");
		}
		lines.Add("Top interface layers (per frame):");
		foreach (ModCost layer in TopInterfaceLayers)
			lines.Add($"  {layer.Name} {layer.SystemMs:0.00} ms");
		return lines;
	}
}
