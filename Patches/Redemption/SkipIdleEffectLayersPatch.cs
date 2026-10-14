using System;
using Microsoft.Xna.Framework.Graphics;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria.ModLoader;

//MoR composites three effect layers through render targets
//both halves of that run every frame whether or not any layer has anything queued and the layers are empty almost all the time
//cool explanation incoming:
//PreDrawLayers is subscribed to Main.OnPreDraw and does this every frame
//   RenderTargetBinding[] previousTargets = graphicsDevice.GetRenderTargets();  allocates an array
//   ... three "if (Layer?.Sprites.Count > 0)" checks ...
//   graphicsDevice.SetRenderTargets(previousTargets);                           GPU state change
//so the get and the set happen even when all three checks fail
//DrawLayers is worse since it is hooked onto On_Main.DrawNPCs which vanilla calls twice per frame and it opens with spriteBatch.End() and closes with spriteBatch.Begin()
//again regardless of whether any layer drew anything, that is two forced sprite batch flushes per frame for no drawing at all

namespace SOTSPerfPatch.Patches.Redemption;

internal sealed class SkipIdleEffectLayersPatch : PerfPatch
{
	public override string Name => "Redemption: skip idle effect layers";

	public override string TargetMod => EffectLayerFields.ModName;

	public override void Apply() => Hooks.Install();

	private static class Hooks
	{
		private delegate void orig_PreDrawLayers(object self, SpriteBatch spriteBatch, GraphicsDevice graphicsDevice);
		private delegate void orig_DrawLayers(object self, SpriteBatch spriteBatch);

		private static bool Enabled => PerfPatchConfig.Instance?.SkipIdleRedemptionLayers ?? false;

		public static void Install()
		{
			Type manager = HookHelper.ModType(EffectLayerFields.ModName, EffectLayerFields.ManagerType);
			EffectLayerFields.Resolve(manager);

			var preDraw = HookHelper.Method(manager, "PreDrawLayers", typeof(SpriteBatch), typeof(GraphicsDevice));
			var draw = HookHelper.Method(manager, "DrawLayers", typeof(SpriteBatch));

			MonoModHooks.Add(preDraw, OnPreDrawLayers);
			MonoModHooks.Add(draw, OnDrawLayers);
		}

		private static void OnPreDrawLayers(orig_PreDrawLayers orig, object self, SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
		{
			if (!Enabled || !EffectLayerFields.AllEmpty(self))
				orig(self, spriteBatch, graphicsDevice);
		}

		private static void OnDrawLayers(orig_DrawLayers orig, object self, SpriteBatch spriteBatch)
		{
			if (!Enabled || !EffectLayerFields.AllEmpty(self))
				orig(self, spriteBatch);
		}
	}
}
