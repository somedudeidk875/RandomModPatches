using Microsoft.Xna.Framework.Graphics;
using SOTS;
using SOTS.Prim;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Patches.SOTS.Rendering;

//when the window is resized SOTS creates new screen-sized render targets without disposing the old ones (SOTSDetours.TargetProj and the two PrimTrailManager targets)
//this leaks video memory on every resize or fullscreen toggle
internal sealed class FreeResizedTargetsPatch : PerfPatch
{
	public override string Name => "Render target leak fix";

	public override string TargetMod => "SOTS";

	public override void Apply() => Hooks.Install();

	[JITWhenModsEnabled("SOTS")]
	private static class Hooks
	{
		private delegate void orig_ResizeTargets();

		private delegate void orig_LoadContent(PrimTrailManager self, GraphicsDevice graphicsDevice);

		private static bool Enabled => PerfPatchConfig.Instance?.FixRenderTargetLeaks ?? false;

		public static void Install()
		{
			var resize = HookHelper.Method(typeof(SOTSDetours), nameof(SOTSDetours.ResizeTargets));
			var loadContent = HookHelper.Method(typeof(PrimTrailManager), nameof(PrimTrailManager.LoadContent), typeof(GraphicsDevice));

			MonoModHooks.Add(resize, OnResizeTargets);
			MonoModHooks.Add(loadContent, OnLoadContent);
		}

		private static void OnResizeTargets(orig_ResizeTargets orig)
		{
			RenderTarget2D old = SOTSDetours.TargetProj;
			orig();

			if (old == null || !Enabled)
				return;

			Main.QueueMainThreadAction(() => DisposeIfReplacedByCurrent(old));
		}

		private static void OnLoadContent(orig_LoadContent orig, PrimTrailManager self, GraphicsDevice graphicsDevice)
		{
			RenderTarget2D oldNpc = self.primTargetNPC;
			RenderTarget2D oldProjectile = self.primTargetProjectile;
			orig(self, graphicsDevice);

			if (!Enabled)
				return;

			DisposeIfReplaced(oldNpc, self.primTargetNPC);
			DisposeIfReplaced(oldProjectile, self.primTargetProjectile);
		}

		//the SOTS lookup sits here instead of in the lambda above because a lambda compiles into its own
		//hidden class that the attribute on hooks does not reach
		private static void DisposeIfReplacedByCurrent(RenderTarget2D old)
			=> DisposeIfReplaced(old, SOTSDetours.TargetProj);

		private static void DisposeIfReplaced(RenderTarget2D old, RenderTarget2D current)
		{
			if (old != null && !ReferenceEquals(old, current) && !old.IsDisposed)
				old.Dispose();
		}
	}
}
