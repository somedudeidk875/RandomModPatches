using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Terraria.Graphics.Effects;

namespace SOTSPerfPatch.Common.Diagnostics;


//reports screen shaders, filters, overlays, etc currently active
//if i remember like filter related stuff is only ever an issue with sots
//every filter cost a fullscreen shader pass every frame and sometimes are never removed (cough cough sots)
internal static class EffectStateProbe
{
	private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

	public static int ActiveFilters { get; private set; }
	public static int ActiveSkies { get; private set; }

	//reads the managers' private state
	public static string Describe()
	{
		try {
			List<string> filters = ActiveNames(Filters.Scene, "_effects");
			List<string> skies = ActiveNames(SkyManager.Instance, "_effects");
			ActiveFilters = filters.Count;
			ActiveSkies = skies.Count;

			return $"filters {filters.Count} [{Join(filters)}]  skies {skies.Count} [{Join(skies)}]  overlays {CountOverlays()}";
		}
		catch (System.Exception) {
			return "unavailable";
		}
	}

	//names of the effects in a manager's dictionary that report themselves as active
	private static List<string> ActiveNames(object manager, string dictionaryField)
	{
		var names = new List<string>();
		if (manager == null)
			return names;

		FieldInfo field = manager.GetType().GetField(dictionaryField, Hidden)
			?? manager.GetType().BaseType?.GetField(dictionaryField, Hidden);
		if (field?.GetValue(manager) is not IDictionary effects)
			return names;

		foreach (DictionaryEntry entry in effects) {
			if (IsActive(entry.Value))
				names.Add(entry.Key as string ?? "?");
		}
		names.Sort();
		return names;
	}

	private static bool IsActive(object effect)
	{
		if (effect == null)
			return false;

		MethodInfo isActive = effect.GetType().GetMethod("IsActive", BindingFlags.Instance | BindingFlags.Public);
		if (isActive != null && isActive.GetParameters().Length == 0)
			return isActive.Invoke(effect, null) is true;

		return false;
	}

	private static int CountOverlays()
	{
		FieldInfo field = typeof(OverlayManager).GetField("_activeOverlays", Hidden);
		if (field?.GetValue(Overlays.Scene) is not IEnumerable lists)
			return -1;

		int count = 0;
		foreach (object list in lists) {
			if (list is IEnumerable overlays) {
				foreach (object unused in overlays)
					//we iterating with this one
					count++;
			}
		}
		return count;
	}

	private static string Join(List<string> names)
	{
		if (names.Count == 0)
			return "none";
		if (names.Count <= 6)
			return string.Join(", ", names);

		return string.Join(", ", names.GetRange(0, 6)) + $", +{names.Count - 6} more";
	}
}
