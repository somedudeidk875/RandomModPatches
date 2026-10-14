using System;
using Microsoft.Xna.Framework.Graphics;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

// MoR's RenderTargetManager.UpdateRenderTargets replaces six screen-sized render targets without disposing the ones it is replacing (another cool explanation)
//   BasicLayer.Target = new RenderTarget2D(..., Main.screenWidth, Main.screenHeight);
//   BasicLayer.EffectTarget = new RenderTarget2D(...);
//   ... four more ...
//at 1920x1080 each of those is ~8 MB of video memory so every call orphans roughly 50 MB 
//the managed objects are tiny so this barely shows up in the heap while the video memory is gone for the rest of the session
//it is called on Main.OnResolutionChanged through a guard that cannot work
//   public static Vector2 lastViewSize;     <-never assigned anywhere
//   if (!Main.dedServ && lastViewSize != Main.ViewSize) UpdateRenderTargets();
//lastViewSize is declared and read but never written so it stays Vector2.Zero and the comparison is always true
//every resolution change, window resize and fullscreen toggle leaks another set

namespace SOTSPerfPatch.Patches.Redemption;

internal sealed class FreeReplacedTargetsPatch : PerfPatch
{
	public override string Name => "Redemption: free replaced render targets";

	public override string TargetMod => EffectLayerFields.ModName;

	public override void Apply() => Hooks.Install();

	private static class Hooks
	{
		//the instance is typed as object because this mod cant reference MoR's types
		private delegate void orig_UpdateRenderTargets(object self);

		//counted so the report can show the fix firing
		public static int TargetsFreed { get; private set; }

		private static bool Enabled => PerfPatchConfig.Instance?.FixRedemptionTargetLeak ?? false;

		public static void Install()
		{
			Type manager = HookHelper.ModType(EffectLayerFields.ModName, EffectLayerFields.ManagerType);
			EffectLayerFields.Resolve(manager);

			var update = HookHelper.Method(manager, "UpdateRenderTargets");
			MonoModHooks.Add(update, OnUpdateRenderTargets);
		}

		private static void OnUpdateRenderTargets(orig_UpdateRenderTargets orig, object self)
		{
			if (!Enabled) {
				orig(self);
				return;
			}

			RenderTarget2D[] replaced = EffectLayerFields.CurrentTargets(self);

			orig(self);

			Main.QueueMainThreadAction(() => Free(replaced));
		}

		private static void Free(RenderTarget2D[] targets)
		{
			foreach (RenderTarget2D target in targets) {
				if (target == null || target.IsDisposed)
					continue;

				target.Dispose();
				TargetsFreed++;
			}
		}
	}

	//for the diagnostics
	internal static int TargetsFreed => Hooks.TargetsFreed;
}
