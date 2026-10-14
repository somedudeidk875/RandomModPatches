using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Terraria.ModLoader;

//this replaces each subscriber with a wrapper that times it and calls the real one so the report can name which mod's render-target work is costing time
namespace SOTSPerfPatch.Common.Diagnostics;

internal static class RenderLoopProfiler
{
	private const string ManagerTypeName = "Luminance.Core.Graphics.RenderTargetManager";
	private const string EventFieldName = "RenderTargetUpdateLoopEvent";

	private static readonly Dictionary<string, long> subscriberTicks = new();

	private static FieldInfo eventField;

	private static Delegate installedDelegate;

	//true when Luminance is present and its render loop could be found
	public static bool Available => eventField != null;

	public static void Find()
	{
		if (eventField != null)
			return;

		foreach (Mod mod in ModLoader.Mods) {
			Type manager = mod.Code?.GetType(ManagerTypeName);
			if (manager == null)
				continue;

			eventField = manager.GetField(EventFieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (eventField != null)
				return;
		}
	}

	//wraps every subscriber and re-wraps when the subscriber list changes
	public static void Wrap()
	{
		if (eventField == null)
			return;

		try {
			var current = eventField.GetValue(null) as Delegate;
			if (current == null || ReferenceEquals(current, installedDelegate))
				return;

			Delegate combined = null;
			foreach (Delegate subscriber in current.GetInvocationList()) {
				Delegate replacement = subscriber.Target is TimedSubscriber
					? subscriber
					: WrapOne(subscriber, current.GetType());

				combined = combined == null ? replacement : Delegate.Combine(combined, replacement);
			}

			eventField.SetValue(null, combined);
			installedDelegate = combined;
		}
		catch (Exception e) {
			installedDelegate = null;
			ModContent.GetInstance<SOTSPerfPatch>().Logger.Warn("Could not time the mod render target loop.", e);
		}
	}

	private static Delegate WrapOne(Delegate subscriber, Type delegateType)
	{
		var call = (Action)Delegate.CreateDelegate(typeof(Action), subscriber.Target, subscriber.Method);
		var timed = new TimedSubscriber(subscriber, call, DescribeOwner(subscriber.Method));
		return Delegate.CreateDelegate(delegateType, timed, nameof(TimedSubscriber.Invoke));
	}

	//puts the real subscribers back
	public static void Unwrap()
	{
		if (installedDelegate == null)
			return;

		try {
			var current = eventField.GetValue(null) as Delegate;
			Delegate restored = null;

			foreach (Delegate subscriber in current?.GetInvocationList() ?? Array.Empty<Delegate>()) {
				Delegate real = subscriber.Target is TimedSubscriber timed ? timed.Original : subscriber;
				restored = restored == null ? real : Delegate.Combine(restored, real);
			}

			eventField.SetValue(null, restored);
		}
		catch (Exception) {

		}

		installedDelegate = null;
		subscriberTicks.Clear();
	}

	public static void Reset() => subscriberTicks.Clear();

	//most expensive render-target subscribers this second in ms per frame.
	public static List<Report.ModCost> TopSubscribers(int draws)
	{
		var list = new List<Report.ModCost>();
		if (draws == 0)
			return list;

		foreach (KeyValuePair<string, long> pair in subscriberTicks)
			list.Add(new Report.ModCost { Name = pair.Key, SystemMs = pair.Value * PerfDiagnostics.MsPerTick / draws });

		list.Sort((a, b) => b.SystemMs.CompareTo(a.SystemMs));
		if (list.Count > 10)
			list.RemoveRange(10, list.Count - 10);
		return list;
	}

	public static double TotalMs(int draws)
	{
		if (draws == 0)
			return 0;

		long total = 0;
		foreach (KeyValuePair<string, long> pair in subscriberTicks)
			total += pair.Value;

		return total * PerfDiagnostics.MsPerTick / draws;
	}

	private static string DescribeOwner(MethodInfo method)
	{
		Type owner = method.DeclaringType;
		if (owner == null)
			return method.Name;

		string modName = "?";
		foreach (Mod mod in ModLoader.Mods) {
			if (mod.Code == owner.Assembly) {
				modName = mod.Name;
				break;
			}
		}
		return $"{modName}: {owner.Name}";
	}

	private static void Add(string name, long ticks)
	{
		subscriberTicks.TryGetValue(name, out long current);
		subscriberTicks[name] = current + ticks;
	}

	//calls one real subscriber and records how long it took
	private sealed class TimedSubscriber
	{
		private readonly Action inner;
		private readonly string name;

		//the delegate this replaced, kept so the original list can be restored
		public Delegate Original { get; }

		public TimedSubscriber(Delegate original, Action inner, string name)
		{
			Original = original;
			this.inner = inner;
			this.name = name;
		}

		public void Invoke()
		{
			if (!PerfDiagnostics.Enabled) {
				inner();
				return;
			}

			long start = Stopwatch.GetTimestamp();
			inner();
			Add(name, Stopwatch.GetTimestamp() - start);
		}
	}
}
