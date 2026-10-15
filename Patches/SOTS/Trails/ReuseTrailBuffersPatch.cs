using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SOTS.Prim;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Patches.SOTS.Trails;

//PrimTrailManager copies its trail list with ToArray, filters it with LINQ and builds new lists in five methods that run every frame 

internal sealed class ReuseTrailBuffersPatch : PerfPatch
{
	public override string Name => "Trail manager allocations";

	public override string TargetMod => "SOTS";

	public override void Apply() => Hooks.Install();

	[JITWhenModsEnabled("SOTS")]
	private static class Hooks
	{
		private delegate void orig_DrawTrails(PrimTrailManager self, SpriteBatch spriteBatch, GraphicsDevice graphicsDevice);

		private delegate void orig_DrawTarget(PrimTrailManager self, SpriteBatch spriteBatch);

		private delegate void orig_UpdateTrails(PrimTrailManager self);

		//reused every frame
		private static readonly List<PrimTrail> snapshot = new();
		private static readonly List<PrimTrail> pixelated = new();
		private static readonly List<PrimTrail> regular = new();
		private static readonly List<PrimTrail> updateSnapshot = new();

		private static PerfPatchConfig Config => PerfPatchConfig.Instance;

		private static bool Enabled => Config?.ReduceTrailAllocations ?? false;

		public static void Install()
		{
			var type = typeof(PrimTrailManager);
			var drawTrailsProj = HookHelper.Method(type, nameof(PrimTrailManager.DrawTrailsProj), typeof(SpriteBatch), typeof(GraphicsDevice));
			var drawTrailsNPC = HookHelper.Method(type, nameof(PrimTrailManager.DrawTrailsNPC), typeof(SpriteBatch), typeof(GraphicsDevice));
			var drawTargetProj = HookHelper.Method(type, nameof(PrimTrailManager.DrawTargetProj), typeof(SpriteBatch));
			var drawTargetNPC = HookHelper.Method(type, nameof(PrimTrailManager.DrawTargetNPC), typeof(SpriteBatch));
			var updateTrails = HookHelper.Method(type, nameof(PrimTrailManager.UpdateTrails));

			MonoModHooks.Add(drawTrailsProj, OnDrawTrailsProj);
			MonoModHooks.Add(drawTrailsNPC, OnDrawTrailsNPC);
			MonoModHooks.Add(drawTargetProj, OnDrawTargetProj);
			MonoModHooks.Add(drawTargetNPC, OnDrawTargetNPC);
			MonoModHooks.Add(updateTrails, OnUpdateTrails);
		}

		private static void OnDrawTrailsProj(orig_DrawTrails orig, PrimTrailManager self, SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
		{
			if (!Enabled) {
				orig(self, spriteBatch, graphicsDevice);
				return;
			}
			RenderPixelatedTrails(self, spriteBatch, graphicsDevice, PrimTrailManager.DrawProjectile, self.primTargetProjectile);
		}

		private static void OnDrawTrailsNPC(orig_DrawTrails orig, PrimTrailManager self, SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
		{
			if (!Enabled) {
				orig(self, spriteBatch, graphicsDevice);
				return;
			}
			RenderPixelatedTrails(self, spriteBatch, graphicsDevice, PrimTrailManager.DrawNPC, self.primTargetNPC);
		}

		private static void OnDrawTargetProj(orig_DrawTarget orig, PrimTrailManager self, SpriteBatch spriteBatch)
		{
			if (!Enabled) {
				orig(self, spriteBatch);
				return;
			}
			DrawTrailsToScreen(self, spriteBatch, PrimTrailManager.DrawProjectile, self.primTargetProjectile);
		}

		private static void OnDrawTargetNPC(orig_DrawTarget orig, PrimTrailManager self, SpriteBatch spriteBatch)
		{
			if (!Enabled) {
				orig(self, spriteBatch);
				return;
			}
			DrawTrailsToScreen(self, spriteBatch, PrimTrailManager.DrawNPC, self.primTargetNPC);
		}

		private static void OnUpdateTrails(orig_UpdateTrails orig, PrimTrailManager self)
		{
			PerfPatchConfig config = Config;
			bool reduceAllocations = config?.ReduceTrailAllocations ?? false;
			bool sweep = config?.SweepStuckTrails ?? false;

			if (!reduceAllocations && !sweep) {
				orig(self);
				return;
			}

			//iterate a copy
			updateSnapshot.Clear();
			updateSnapshot.AddRange(self._trails);
			try {
				foreach (PrimTrail trail in updateSnapshot) {
					trail.Update();

					if (sweep && trail.Destroyed && (trail.Points == null || trail.Points.Count == 0) && self._trails.Contains(trail))
						trail.Dispose();
				}
			}
			finally {
				updateSnapshot.Clear();
			}
		}

		private static void RenderPixelatedTrails(PrimTrailManager self, SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, int drawType, RenderTarget2D target)
		{
			try {
				Partition(self, drawType, collectRegular: false);
				if (pixelated.Count == 0 || target == null)
					return;

				RenderTargetBinding[] bindings = graphicsDevice.GetRenderTargets();
				graphicsDevice.SetRenderTarget(target);
				graphicsDevice.Clear(Color.Transparent);
				spriteBatch.Begin();
				foreach (PrimTrail trail in pixelated)
					trail.Draw();
				spriteBatch.End();
				graphicsDevice.SetRenderTargets(bindings);
			}
			finally {
				ClearBuffers();
			}
		}

		private static void DrawTrailsToScreen(PrimTrailManager self, SpriteBatch spriteBatch, int drawType, RenderTarget2D target)
		{
			try {
				Partition(self, drawType, collectRegular: true);

				if (target != null && pixelated.Count > 0) {
					spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
					Main.spriteBatch.Draw(target, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), Color.White);
					spriteBatch.End();
				}

				foreach (PrimTrail trail in regular)
					trail.Draw();
			}
			finally {
				ClearBuffers();
			}
		}

		private static void Partition(PrimTrailManager self, int drawType, bool collectRegular)
		{
			snapshot.Clear();
			snapshot.AddRange(self._trails);

			foreach (PrimTrail trail in snapshot) {
				if (trail.DrawType != drawType || trail.Disabled)
					continue;

				if (trail.Pixellated)
					pixelated.Add(trail);
				else if (collectRegular)
					regular.Add(trail);
			}
		}

		private static void ClearBuffers()
		{
			snapshot.Clear();
			pixelated.Clear();
			regular.Clear();
		}
	}
}
