using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace SOTSPerfPatch.Common.Diagnostics;

//counts the graphics resources the device is still holding and the whole process' memory
//other probes cant see some of this

internal static class GraphicsResourceProbe
{
	public static int Total { get; private set; }

	public static double ProcessMb { get; private set; }

	public static string Breakdown { get; private set; } = string.Empty;

	private static IList resources;
	private static bool searched;
	private static Process process;

	private static readonly Dictionary<string, int> counts = new();

	//reads the device's private resource list
	public static void Sample()
	{
		ReadProcessMemory();

		try {
			if (!searched) {
				searched = true;
				resources = FindResourceList();
			}

			if (resources == null) {
				Breakdown = string.Empty;
				Total = 0;
				return;
			}

			counts.Clear();
			int total = 0;

			for (int i = 0; i < resources.Count; i++) {
				object entry = resources[i];
				object target = (entry as WeakReference)?.Target;
				if (target == null)
					continue;

				total++;
				string name = target.GetType().Name;
				counts.TryGetValue(name, out int seen);
				counts[name] = seen + 1;
			}

			Total = total;
			Breakdown = TopThree();
		}
		catch (Exception) {
			Breakdown = string.Empty;
			Total = 0;
		}
	}

	private static void ReadProcessMemory()
	{
		try {
			process ??= Process.GetCurrentProcess();
			process.Refresh();
			ProcessMb = process.WorkingSet64 / (1024.0 * 1024.0);
		}
		catch (Exception) {
			ProcessMb = 0;
		}
	}

	//found by field type rather than by name so a rename in the graphics library does not break it 
	private static IList FindResourceList()
	{
		GraphicsDevice device = Main.instance?.GraphicsDevice; //graphicsdevices were not fun to learn about btw
		if (device == null)
			return null;

		const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		for (Type type = device.GetType(); type != null; type = type.BaseType) {
			foreach (FieldInfo field in type.GetFields(any)) {
				if (field.FieldType == typeof(List<WeakReference>))
					return field.GetValue(device) as IList;
			}
		}

		return null;
	}

	private static string TopThree()
	{
		string result = string.Empty;

		for (int picked = 0; picked < 3; picked++) {
			string best = null;
			int bestCount = 0;

			foreach (KeyValuePair<string, int> pair in counts) {
				if (pair.Value > bestCount) {
					best = pair.Key;
					bestCount = pair.Value;
				}
			}

			if (best == null)
				break;

			counts.Remove(best);
			result = result.Length == 0 ? $"{best} {bestCount}" : $"{result}, {best} {bestCount}";
		}

		return result;
	}

	public static void Reset()
	{
		resources = null;
		searched = false;
		process = null;
		counts.Clear();
		Total = 0;
		Breakdown = string.Empty;
		ProcessMb = 0;
	}
}
