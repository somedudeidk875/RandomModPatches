using System;
using System.Reflection;
using NoxusBoss.Core.World.GameScenes.RiftEclipse;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//WOTG checks the whole world for rift eclipse snow on a timer on a new background thread by walking down from the surface in every column
//u probably already knew where this was going, but it does this even when no eclipse is running and  even when it is running but not raining

namespace SOTSPerfPatch.Patches.Noxus;

internal sealed class SkipIdleSnowScansPatch : PerfPatch
{
	public override string Name => "Rift Eclipse: skip pointless world snow scans";

	public override string TargetMod => "NoxusBoss";

	//the scan runs on the server too since it edits tiles
	public override bool ClientOnly => false;

	public override void Apply() => Hooks.Install();

	internal static void OnWorldLoad() => Hooks.AllowCleanupPasses();

	[JITWhenModsEnabled("NoxusBoss")]
	private static class Hooks
	{
		private delegate void orig_PerformSnowCheck();

		private const int CleanupPasses = 3;

		private static int cleanupPassesLeft = CleanupPasses;
		private static FieldInfo scanInProgressField;

		private static bool Enabled => PerfPatchConfig.Instance?.SkipIdleSnowScans ?? false;

		public static void Install()
		{
			var performSnowCheck = HookHelper.Method(typeof(RiftEclipseSnowSystem), nameof(RiftEclipseSnowSystem.PerformSnowCheck));

			//set by the caller before starting the thread and cleared by the scan itself
			scanInProgressField = typeof(RiftEclipseSnowSystem).GetField("placingSnowOverWorld",
				BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
				?? throw new MissingFieldException("RiftEclipseSnowSystem.placingSnowOverWorld was not found. Wrath of the Gods may have been updated.");

			cleanupPassesLeft = CleanupPasses;
			MonoModHooks.Add(performSnowCheck, OnPerformSnowCheck);
		}

		public static void AllowCleanupPasses() => cleanupPassesLeft = CleanupPasses;

		private static void OnPerformSnowCheck(orig_PerformSnowCheck orig)
		{
			if (!Enabled) {
				orig();
				return;
			}

			bool eclipseRunning = RiftEclipseManagementSystem.RiftEclipseOngoing;
			bool placingSnow = eclipseRunning && Main.raining;

			if (placingSnow) {
				//snow is going down now so it will need clearing once the eclipse ends
				cleanupPassesLeft = CleanupPasses;
				orig();
				return;
			}

			if (!eclipseRunning && cleanupPassesLeft > 0) {
				cleanupPassesLeft--;
				orig();
				return;
			}

			scanInProgressField.SetValue(null, false);
		}
	}
}
