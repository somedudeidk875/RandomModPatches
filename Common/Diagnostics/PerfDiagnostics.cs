using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SOTSPerfPatch.Patches.Redemption;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Common.Diagnostics;

// in-game profiler, do not enable unless you know what ur doing
internal static class PerfDiagnostics
{
	public static bool Enabled { get; private set; }

	public static Report Latest { get; private set; }

	public static Report Baseline { get; private set; }

	public static bool SetBaselineToLatest()
	{
		if (Latest == null)
			return false;

		Baseline = Latest;
		return true;
	}

	public static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

	private enum Stage
	{
		UpdateTotal, UpdateNPCs, UpdateProjectiles, UpdateDust, UpdateGore, UpdatePlayers, UpdateModSystems, UpdateAudio,
		DrawTotal, DrawNPCLayer, DrawProjectileLayer, DrawDust, DrawGore, DrawPlayers, WaterLayers, DrawInterface,
		DrawTiles, DrawWalls, DrawWater, DrawBackground, DrawMap, DrawItems, DrawRain, DrawWires, ScreenShaders, Skies, CachedProjectiles, Lighting,
		Count,
	}

	private static readonly long[] stageTicks = new long[(int)Stage.Count];
	private static long[] npcUpdateTicks = Array.Empty<long>(), npcDrawTicks = Array.Empty<long>();
	private static long[] projUpdateTicks = Array.Empty<long>(), projDrawTicks = Array.Empty<long>();
	private static long[] itemDrawTicks = Array.Empty<long>();
	private static int updates, draws;

	private static readonly Dictionary<string, long> modSystemTicks = new();
	private static readonly Dictionary<string, long> modPlayerTicks = new();
	private static readonly Dictionary<string, long> modGlobalTicks = new();

	private static readonly Stopwatch window = new();

	// Main.GameUpdateCount rises once per real game tick, which is not the same as how often
	// Main.DoUpdate is called when a mod changes the frame pacing.
	private static uint gameTicksAtStart;
	private static long heapAtEnable;
	private static double peakHeapMb;
	private static long allocatedAtStart;
	private static readonly int[] gcAtStart = new int[3];
	private static TimeSpan gcPauseAtStart;

	public static void SetEnabled(bool enabled)
	{
		if (enabled == Enabled)
			return;
		Enabled = enabled;
		Baseline = null;
		if (enabled)
			RenderLoopProfiler.Wrap();
		else
			RenderLoopProfiler.Unwrap();

		if (enabled) {
			heapAtEnable = GC.GetTotalMemory(false);
			peakHeapMb = heapAtEnable / (1024.0 * 1024.0);
		}
		ResetWindow();
		if (!enabled)
			Latest = null;
	}

	public static void AllocateTypeArrays()
	{
		npcUpdateTicks = new long[NPCLoader.NPCCount];
		npcDrawTicks = new long[NPCLoader.NPCCount];
		projUpdateTicks = new long[ProjectileLoader.ProjectileCount];
		projDrawTicks = new long[ProjectileLoader.ProjectileCount];
		itemDrawTicks = new long[ItemLoader.ItemCount];
	}

	//putting stuff to values
	public static void Install()
	{
		On_Main.DoUpdate += Main_DoUpdate;
		On_Main.UpdateAudio += Main_UpdateAudio;
		On_NPC.UpdateNPC += NPC_UpdateNPC;
		On_Projectile.Update += Projectile_Update;
		On_Dust.UpdateDust += Dust_UpdateDust;
		On_Gore.Update += Gore_Update;
		On_Main.DoDraw += Main_DoDraw;
		On_Main.DrawNPCs += Main_DrawNPCs;
		On_Main.DrawNPC += Main_DrawNPC;
		On_Main.DrawProjectiles += Main_DrawProjectiles;
		On_Main.DrawProj += Main_DrawProj;
		On_Main.DrawDust += Main_DrawDust;
		On_Main.DrawGore += Main_DrawGore;
		On_Main.DrawPlayers_AfterProjectiles += Main_DrawPlayers_AfterProjectiles;
		On_Main.DrawPlayers_BehindNPCs += Main_DrawPlayers_BehindNPCs;
		On_Main.CheckMonoliths += Main_CheckMonoliths;
		On_Main.DrawInterface += Main_DrawInterface;
		On_Main.RenderTiles += Main_RenderTiles;
		On_Main.RenderTiles2 += Main_RenderTiles2;
		On_Main.RenderWalls += Main_RenderWalls;
		On_Main.RenderWater += Main_RenderWater;
		On_Main.RenderBackground += Main_RenderBackground;
		On_Main.DrawWaters += Main_DrawWaters;
		On_Main.DrawItems += Main_DrawItems;
		On_Main.DrawRain += Main_DrawRain;
		On_Main.DrawWires += Main_DrawWires;
		On_Main.DrawMap += Main_DrawMap;
		On_Main.DrawCachedProjs += Main_DrawCachedProjs;
		On_Main.DrawItem += Main_DrawItem;
		Terraria.Graphics.Effects.On_FilterManager.EndCapture += FilterManager_EndCapture;
		Terraria.Graphics.Effects.On_SkyManager.Draw += SkyManager_Draw;
		On_Player.Update += Player_Update;
		Terraria.Graphics.Light.On_LightingEngine.ProcessArea += LightingEngine_ProcessArea;
	}

	//500 main methods

	//update hooks

	private static void Main_DoUpdate(On_Main.orig_DoUpdate orig, Main self, ref GameTime gameTime)
	{
		if (!Enabled) { orig(self, ref gameTime); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, ref gameTime);
		stageTicks[(int)Stage.UpdateTotal] += Stopwatch.GetTimestamp() - start;
		updates++;
	}

	private static void Main_UpdateAudio(On_Main.orig_UpdateAudio orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.UpdateAudio] += Stopwatch.GetTimestamp() - start;
	}

	private static void NPC_UpdateNPC(On_NPC.orig_UpdateNPC orig, NPC self, int i)
	{
		if (!Enabled) { orig(self, i); return; }
		bool active = self.active;
		int type = self.type;
		long start = Stopwatch.GetTimestamp();
		orig(self, i);
		long elapsed = Stopwatch.GetTimestamp() - start;
		stageTicks[(int)Stage.UpdateNPCs] += elapsed;
		if (active)
			AddTo(npcUpdateTicks, type, elapsed);
	}

	private static void Projectile_Update(On_Projectile.orig_Update orig, Projectile self, int i)
	{
		if (!Enabled) { orig(self, i); return; }
		bool active = self.active;
		int type = self.type;
		long start = Stopwatch.GetTimestamp();
		orig(self, i);
		long elapsed = Stopwatch.GetTimestamp() - start;
		stageTicks[(int)Stage.UpdateProjectiles] += elapsed;
		if (active)
			AddTo(projUpdateTicks, type, elapsed);
	}

	private static void Dust_UpdateDust(On_Dust.orig_UpdateDust orig)
	{
		if (!Enabled) { orig(); return; }
		long start = Stopwatch.GetTimestamp();
		orig();
		stageTicks[(int)Stage.UpdateDust] += Stopwatch.GetTimestamp() - start;
	}

	private static void Gore_Update(On_Gore.orig_Update orig, Gore self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.UpdateGore] += Stopwatch.GetTimestamp() - start;
	}

	//draw hooks

	private static void Main_DoDraw(On_Main.orig_DoDraw orig, Main self, GameTime gameTime)
	{
		if (!Enabled) { orig(self, gameTime); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, gameTime);
		stageTicks[(int)Stage.DrawTotal] += Stopwatch.GetTimestamp() - start;
		draws++;
	}

	private static void Main_DrawNPCs(On_Main.orig_DrawNPCs orig, Main self, bool behindTiles)
	{
		if (!Enabled) { orig(self, behindTiles); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, behindTiles);
		stageTicks[(int)Stage.DrawNPCLayer] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_DrawNPC(On_Main.orig_DrawNPC orig, Main self, int iNPCIndex, bool behindTiles)
	{
		if (!Enabled) { orig(self, iNPCIndex, behindTiles); return; }
		int type = Main.npc[iNPCIndex].type;
		long start = Stopwatch.GetTimestamp();
		orig(self, iNPCIndex, behindTiles);
		AddTo(npcDrawTicks, type, Stopwatch.GetTimestamp() - start);
	}

	private static void Main_DrawProjectiles(On_Main.orig_DrawProjectiles orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawProjectileLayer] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_DrawProj(On_Main.orig_DrawProj orig, Main self, int i)
	{
		if (!Enabled || i < 0 || i >= Main.maxProjectiles) { orig(self, i); return; }
		int type = Main.projectile[i].type;
		long start = Stopwatch.GetTimestamp();
		orig(self, i);
		AddTo(projDrawTicks, type, Stopwatch.GetTimestamp() - start);
	}

	private static void Main_DrawDust(On_Main.orig_DrawDust orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawDust] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_DrawGore(On_Main.orig_DrawGore orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawGore] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_DrawPlayers_AfterProjectiles(On_Main.orig_DrawPlayers_AfterProjectiles orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawPlayers] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_DrawPlayers_BehindNPCs(On_Main.orig_DrawPlayers_BehindNPCs orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawPlayers] += Stopwatch.GetTimestamp() - start;
	}

	//sots renders its magic water layers from here
	private static void Main_CheckMonoliths(On_Main.orig_CheckMonoliths orig)
	{
		if (!Enabled) { orig(); return; }
		long start = Stopwatch.GetTimestamp();
		orig();
		stageTicks[(int)Stage.WaterLayers] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_DrawInterface(On_Main.orig_DrawInterface orig, Main self, GameTime gameTime)
	{
		if (!Enabled) { orig(self, gameTime); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, gameTime);
		stageTicks[(int)Stage.DrawInterface] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_RenderTiles(On_Main.orig_RenderTiles orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawTiles] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_RenderTiles2(On_Main.orig_RenderTiles2 orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawTiles] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_RenderWalls(On_Main.orig_RenderWalls orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawWalls] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_RenderWater(On_Main.orig_RenderWater orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawWater] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_DrawWaters(On_Main.orig_DrawWaters orig, Main self, bool isBackground)
	{
		if (!Enabled) { orig(self, isBackground); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, isBackground);
		stageTicks[(int)Stage.DrawWater] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_RenderBackground(On_Main.orig_RenderBackground orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawBackground] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_DrawItems(On_Main.orig_DrawItems orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawItems] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_DrawRain(On_Main.orig_DrawRain orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawRain] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_DrawWires(On_Main.orig_DrawWires orig, Main self)
	{
		if (!Enabled) { orig(self); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self);
		stageTicks[(int)Stage.DrawWires] += Stopwatch.GetTimestamp() - start;
	}

	private static void Main_DrawMap(On_Main.orig_DrawMap orig, Main self, GameTime gameTime)
	{
		if (!Enabled) { orig(self, gameTime); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, gameTime);
		stageTicks[(int)Stage.DrawMap] += Stopwatch.GetTimestamp() - start;
	}

	//dropped items are drawn one at a time so attribute the cost to the item type
	private static void Main_DrawItem(On_Main.orig_DrawItem orig, Main self, Item item, int whoAmI)
	{
		if (!Enabled) { orig(self, item, whoAmI); return; }
		int type = item.type;
		long start = Stopwatch.GetTimestamp();
		orig(self, item, whoAmI);
		AddTo(itemDrawTicks, type, Stopwatch.GetTimestamp() - start);
	}

	private static void Main_DrawCachedProjs(On_Main.orig_DrawCachedProjs orig, Main self, List<int> projCache, bool startSpriteBatch)
	{
		if (!Enabled) { orig(self, projCache, startSpriteBatch); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, projCache, startSpriteBatch);
		stageTicks[(int)Stage.CachedProjectiles] += Stopwatch.GetTimestamp() - start;
	}

	private static void FilterManager_EndCapture(Terraria.Graphics.Effects.On_FilterManager.orig_EndCapture orig,
		Terraria.Graphics.Effects.FilterManager self, RenderTarget2D finalTexture, RenderTarget2D screenTarget1,
		RenderTarget2D screenTarget2, Color clearColor)
	{
		if (!Enabled) { orig(self, finalTexture, screenTarget1, screenTarget2, clearColor); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, finalTexture, screenTarget1, screenTarget2, clearColor);
		stageTicks[(int)Stage.ScreenShaders] += Stopwatch.GetTimestamp() - start;
	}

	private static void SkyManager_Draw(Terraria.Graphics.Effects.On_SkyManager.orig_Draw orig,
		Terraria.Graphics.Effects.SkyManager self, SpriteBatch spriteBatch)
	{
		if (!Enabled) { orig(self, spriteBatch); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, spriteBatch);
		stageTicks[(int)Stage.Skies] += Stopwatch.GetTimestamp() - start;
	}

	private static void Player_Update(On_Player.orig_Update orig, Player self, int i)
	{
		if (!Enabled) { orig(self, i); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, i);
		stageTicks[(int)Stage.UpdatePlayers] += Stopwatch.GetTimestamp() - start;
	}

	// Lighting runs on worker threads, so this counter is updated atomically.
	private static void LightingEngine_ProcessArea(Terraria.Graphics.Light.On_LightingEngine.orig_ProcessArea orig,
		Terraria.Graphics.Light.LightingEngine self, Rectangle area)
	{
		if (!Enabled) { orig(self, area); return; }
		long start = Stopwatch.GetTimestamp();
		orig(self, area);
		System.Threading.Interlocked.Add(ref stageTicks[(int)Stage.Lighting], Stopwatch.GetTimestamp() - start);
	}

	public static void AddModSystemTime(string modName, long elapsed)
	{
		stageTicks[(int)Stage.UpdateModSystems] += elapsed;
		Accumulate(modSystemTicks, modName, elapsed);
	}

	public static void AddModPlayerTime(string modName, long elapsed) => Accumulate(modPlayerTicks, modName, elapsed);

	//GlobalNPC/GlobalProjectile/GlobalItem hooks
	public static void AddModGlobalTime(string modName, long elapsed) => Accumulate(modGlobalTicks, modName, elapsed);

	private static void Accumulate(Dictionary<string, long> totals, string modName, long elapsed)
	{
		modName ??= "?";
		totals.TryGetValue(modName, out long current);
		totals[modName] = current + elapsed;
	}

	private static void AddTo(long[] perType, int type, long elapsed)
	{
		if ((uint)type < (uint)perType.Length)
			perType[type] += elapsed;
	}

	//reporting shenanigans

	//called once per update (yes ik the hypocrisy of criticizing mods for doing this then proceeding to do it. just dont turn this on if u want performance)
	public static void Tick()
	{
		if (!Enabled)
			return;
		if (!window.IsRunning)
			ResetWindow();
		if (window.Elapsed.TotalSeconds < 1.0 || updates == 0 || draws == 0)
			return;

		RenderLoopProfiler.Wrap();
		Latest = BuildReport();
		Baseline ??= Latest;
		Latest.Baseline = Baseline;
		ResetWindow();
	}

	private static void ResetWindow()
	{
		Array.Clear(stageTicks);
		Array.Clear(npcUpdateTicks);
		Array.Clear(npcDrawTicks);
		Array.Clear(projUpdateTicks);
		Array.Clear(projDrawTicks);
		Array.Clear(itemDrawTicks);
		modSystemTicks.Clear();
		modPlayerTicks.Clear();
		modGlobalTicks.Clear();
		InterfaceLayerProfiler.Reset();
		RenderLoopProfiler.Reset();
		updates = draws = 0;

		gameTicksAtStart = Main.GameUpdateCount;
		allocatedAtStart = GC.GetTotalAllocatedBytes(false);
		for (int gen = 0; gen < 3; gen++)
			gcAtStart[gen] = GC.CollectionCount(gen);
		gcPauseAtStart = GC.GetTotalPauseDuration();
		window.Restart();
	}

	private static Report BuildReport()
	{
		double seconds = window.Elapsed.TotalSeconds;
		double PerUpdate(Stage s) => PerUpdateMs(stageTicks[(int)s]);
		double PerDraw(Stage s) => PerDrawMs(stageTicks[(int)s]);

		var report = new Report {
			Fps = draws / seconds,
			Ups = updates / seconds,
			GameTicksPerSecond = (Main.GameUpdateCount - gameTicksAtStart) / seconds,
			UpdateMs = PerUpdate(Stage.UpdateTotal),
			UpdateNPCsMs = PerUpdate(Stage.UpdateNPCs),
			UpdateProjectilesMs = PerUpdate(Stage.UpdateProjectiles),
			UpdateDustMs = PerUpdate(Stage.UpdateDust),
			UpdateGoreMs = PerUpdate(Stage.UpdateGore),
			UpdatePlayersMs = PerUpdate(Stage.UpdatePlayers),
			UpdateModSystemsMs = PerUpdate(Stage.UpdateModSystems),
			UpdateAudioMs = PerUpdate(Stage.UpdateAudio),
			DrawMs = PerDraw(Stage.DrawTotal),
			DrawNPCLayerMs = PerDraw(Stage.DrawNPCLayer),
			DrawProjectileLayerMs = PerDraw(Stage.DrawProjectileLayer),
			DrawDustMs = PerDraw(Stage.DrawDust),
			DrawGoreMs = PerDraw(Stage.DrawGore),
			DrawPlayersMs = PerDraw(Stage.DrawPlayers),
			WaterLayersMs = PerDraw(Stage.WaterLayers),
			DrawInterfaceMs = PerDraw(Stage.DrawInterface),
			DrawTilesMs = PerDraw(Stage.DrawTiles),
			DrawWallsMs = PerDraw(Stage.DrawWalls),
			DrawWaterMs = PerDraw(Stage.DrawWater),
			DrawBackgroundMs = PerDraw(Stage.DrawBackground),
			DrawMapMs = PerDraw(Stage.DrawMap),
			DrawItemsMs = PerDraw(Stage.DrawItems),
			DrawRainMs = PerDraw(Stage.DrawRain),
			DrawWiresMs = PerDraw(Stage.DrawWires),
			ScreenShadersMs = PerDraw(Stage.ScreenShaders),
			SkiesMs = PerDraw(Stage.Skies),
			CachedProjectilesMs = PerDraw(Stage.CachedProjectiles),
			LightingMs = PerDraw(Stage.Lighting),

			HeapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
			HeapGrowthMb = (GC.GetTotalMemory(false) - heapAtEnable) / (1024.0 * 1024.0),
			AllocMbPerSec = (GC.GetTotalAllocatedBytes(false) - allocatedAtStart) / (1024.0 * 1024.0) / seconds,
			Gen0PerSec = (GC.CollectionCount(0) - gcAtStart[0]) / seconds,
			Gen1PerSec = (GC.CollectionCount(1) - gcAtStart[1]) / seconds,
			Gen2PerSec = (GC.CollectionCount(2) - gcAtStart[2]) / seconds,
			GcPauseMsPerSec = (GC.GetTotalPauseDuration() - gcPauseAtStart).TotalMilliseconds / seconds,

			ActiveNPCs = CountActiveNPCs(),
			ActiveProjectiles = CountActiveProjectiles(),
			ActiveDust = CountActiveDust(),
			ActiveGore = CountActiveGore(),
			ActiveItems = CountActiveItems(),
			PlayingMusicTracks = CountPlayingMusicTracks(out string playingNames),
			PlayingMusicNames = playingNames,
		};

		report.TopNPCs = TopNPCTypes();
		report.TopProjectiles = TopProjectileTypes();
		report.TopItems = TopItemTypes();
		report.PeakHeapMb = peakHeapMb = Math.Max(peakHeapMb, report.HeapMb);
		report.TopMods = TopMods();
		report.TopInterfaceLayers = InterfaceLayerProfiler.TopLayers(draws);
		report.TopRenderTargets = RenderLoopProfiler.TopSubscribers(draws);
		report.RenderTargetsMs = RenderLoopProfiler.TotalMs(draws);
		report.RenderLoopAvailable = RenderLoopProfiler.Available;
		report.InstrumentedModMethods = ModHookProfiler.InstrumentedMethods;
		report.DeepHooksInstalled = ModHookProfiler.DeepInstalled;
		report.SOTSState = SOTSStateProbe.Describe();
		GraphicsResourceProbe.Sample();
		report.GraphicsResources = GraphicsResourceProbe.Total;
		report.GraphicsBreakdown = GraphicsResourceProbe.Breakdown;
		report.ProcessMb = GraphicsResourceProbe.ProcessMb;
		report.RedemptionTargetsFreed = FreeReplacedTargetsPatch.TargetsFreed;

		report.EffectState = EffectStateProbe.Describe();
		report.ActiveFilters = EffectStateProbe.ActiveFilters;
		report.ActiveSkies = EffectStateProbe.ActiveSkies;
		report.RenderTargetSubscribers = report.TopRenderTargets.Count;
		report.UiLayersInstrumented = InterfaceLayerProfiler.Installed;
		report.UiLayersDrawn = InterfaceLayerProfiler.LayersDrawn;
		return report;
	}

	private static int CountActiveNPCs()
	{
		int count = 0;
		for (int i = 0; i < Main.maxNPCs; i++)
			if (Main.npc[i].active)
				count++;
		return count;
	}

	private static int CountActiveProjectiles()
	{
		int count = 0;
		for (int i = 0; i < Main.maxProjectiles; i++)
			if (Main.projectile[i].active)
				count++;
		return count;
	}

	private static int CountActiveDust()
	{
		int count = 0;
		for (int i = 0; i < Main.maxDust; i++)
			if (Main.dust[i].active)
				count++;
		return count;
	}

	//dropped items lying in the world
	private static int CountActiveItems()
	{
		int count = 0;
		for (int i = 0; i < Main.maxItems; i++)
			if (Main.item[i].active)
				count++;
		return count;
	}

	private static int CountActiveGore()
	{
		int count = 0;
		for (int i = 0; i < Main.maxGore; i++)
			if (Main.gore[i].active)
				count++;
		return count;
	}

	//15 most expensive npc types this second
	private static List<Report.TypeCost> TopNPCTypes()
	{
		var list = new List<Report.TypeCost>();
		for (int type = 0; type < npcUpdateTicks.Length; type++) {
			if (npcUpdateTicks[type] == 0 && npcDrawTicks[type] == 0)
				continue;

			list.Add(new Report.TypeCost {
				Name = Lang.GetNPCNameValue(type),
				Mod = NPCLoader.GetNPC(type)?.Mod.Name ?? "Terraria",
				UpdateMs = PerUpdateMs(npcUpdateTicks[type]),
				DrawMs = PerDrawMs(npcDrawTicks[type]),
			});
		}
		return SortAndTrim(list);
	}

	//15 most expensive projectile types this second
	private static List<Report.TypeCost> TopProjectileTypes()
	{
		var list = new List<Report.TypeCost>();
		for (int type = 0; type < projUpdateTicks.Length; type++) {
			if (projUpdateTicks[type] == 0 && projDrawTicks[type] == 0)
				continue;

			list.Add(new Report.TypeCost {
				Name = Lang.GetProjectileName(type).Value,
				Mod = ProjectileLoader.GetProjectile(type)?.Mod.Name ?? "Terraria",
				UpdateMs = PerUpdateMs(projUpdateTicks[type]),
				DrawMs = PerDrawMs(projDrawTicks[type]),
			});
		}
		return SortAndTrim(list);
	}

	//15 most expensive dropped item types to draw this second
	private static List<Report.TypeCost> TopItemTypes()
	{
		var list = new List<Report.TypeCost>();
		for (int type = 0; type < itemDrawTicks.Length; type++) {
			if (itemDrawTicks[type] == 0)
				continue;

			list.Add(new Report.TypeCost {
				Name = Lang.GetItemNameValue(type),
				Mod = ItemLoader.GetItem(type)?.Mod.Name ?? "Terraria",
				DrawMs = PerDrawMs(itemDrawTicks[type]),
			});
		}
		return SortAndTrim(list);
	}

	private static List<Report.TypeCost> SortAndTrim(List<Report.TypeCost> list)
	{
		list.Sort((a, b) => (b.UpdateMs + b.DrawMs).CompareTo(a.UpdateMs + a.DrawMs));
		if (list.Count > 15)
			list.RemoveRange(15, list.Count - 15);
		return list;
	}

	//15 mods spending the most time in their per-tick hooks
	private static List<Report.ModCost> TopMods()
	{
		var list = new List<Report.ModCost>();
		foreach (var pair in modSystemTicks)
			list.Add(new Report.ModCost { Name = pair.Key, SystemMs = PerUpdateMs(pair.Value) });

		foreach (var pair in modPlayerTicks) {
			Report.ModCost existing = list.Find(m => m.Name == pair.Key);
			if (existing != null)
				existing.PlayerMs = PerUpdateMs(pair.Value);
			else
				list.Add(new Report.ModCost { Name = pair.Key, PlayerMs = PerUpdateMs(pair.Value) });
		}

		foreach (var pair in modGlobalTicks) {
			Report.ModCost existing = list.Find(m => m.Name == pair.Key);
			if (existing != null)
				existing.GlobalMs = PerUpdateMs(pair.Value);
			else
				list.Add(new Report.ModCost { Name = pair.Key, GlobalMs = PerUpdateMs(pair.Value) });
		}

		list.Sort((a, b) => (b.SystemMs + b.PlayerMs + b.GlobalMs).CompareTo(a.SystemMs + a.PlayerMs + a.GlobalMs));
		if (list.Count > 15)
			list.RemoveRange(15, list.Count - 15);
		return list;
	}

	private static double PerUpdateMs(long ticks) => updates == 0 ? 0 : ticks * MsPerTick / updates;

	private static double PerDrawMs(long ticks) => draws == 0 ? 0 : ticks * MsPerTick / draws;

	private static int CountPlayingMusicTracks(out string names)
	{
		names = string.Empty;
		var audio = Main.audioSystem as LegacyAudioSystem;
		if (audio == null || audio.AudioTracks == null)
			return -1;

		var playing = new List<string>();
		for (int i = 0; i < audio.AudioTracks.Length; i++) {
			IAudioTrack track = audio.AudioTracks[i];
			if (track != null && track.IsPlaying)
				playing.Add(MusicName(audio, i));
		}
		names = string.Join(", ", playing);
		return playing.Count;
	}

	private static Dictionary<int, string> moddedMusicNames;

	private static string MusicName(LegacyAudioSystem audio, int slot)
	{
		if (moddedMusicNames == null) {
			moddedMusicNames = new Dictionary<int, string>();
			var field = typeof(MusicLoader).GetField("musicByPath", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			if (field?.GetValue(null) is Dictionary<string, int> byPath)
				foreach (var pair in byPath)
					moddedMusicNames[pair.Value] = pair.Key;
		}

		if (moddedMusicNames.TryGetValue(slot, out string path))
			return path;
		if (audio.TrackNamesByIndex != null && audio.TrackNamesByIndex.TryGetValue(slot, out string vanilla))
			return vanilla;
		return $"#{slot}";
	}
}
