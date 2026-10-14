using System.Collections.Generic;
using System.Diagnostics;
using Terraria.ModLoader;
using Terraria.UI;

namespace SOTSPerfPatch.Common.Diagnostics;

//times each interface layer so the UI figure can be broken down per layer
internal static class InterfaceLayerProfiler
{
	private static readonly Dictionary<string, long> layerTicks = new();

	public static bool Installed { get; private set; }

	public static int LayersDrawn { get; private set; }

	private static int layersThisWindow;

	private delegate bool orig_Draw(GameInterfaceLayer self);

	//does it actually work
	public static void Install()
	{
		if (Installed)
			return;

		var draw = typeof(GameInterfaceLayer).GetMethod("Draw",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
			null, System.Type.EmptyTypes, null);

		if (draw == null) {
			ModContent.GetInstance<SOTSPerfPatch>().Logger.Warn("GameInterfaceLayer.Draw not found; UI layer timing is off.");
			return;
		}

		MonoModHooks.Add(draw, OnDraw);
		Installed = true;
	}

	private static bool OnDraw(orig_Draw orig, GameInterfaceLayer self)
	{
		if (!PerfDiagnostics.Enabled)
			return orig(self);

		long start = Stopwatch.GetTimestamp();
		bool result = orig(self);
		long elapsed = Stopwatch.GetTimestamp() - start;

		string name = self.Name ?? "?";
		layerTicks.TryGetValue(name, out long current);
		layerTicks[name] = current + elapsed;
		layersThisWindow++;
		return result;
	}

	public static void Reset()
	{
		layerTicks.Clear();
		LayersDrawn = layersThisWindow;
		layersThisWindow = 0;
	}

	//the most expensive layers this second in ms per frame
	public static List<Report.ModCost> TopLayers(int draws)
	{
		var list = new List<Report.ModCost>();
		if (draws == 0)
			return list;

		foreach (KeyValuePair<string, long> pair in layerTicks)
			list.Add(new Report.ModCost { Name = pair.Key, SystemMs = pair.Value * PerfDiagnostics.MsPerTick / draws });

		list.Sort((a, b) => b.SystemMs.CompareTo(a.SystemMs));
		if (list.Count > 10)
			list.RemoveRange(10, list.Count - 10);
		return list;
	}
}
