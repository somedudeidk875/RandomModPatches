using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//Luminance keeps four half resolution targets for pixelated primitives (one for each layer) and neither half of the system is gated
//the filling half detours CheckMonoliths and calls DrawPrimsToRenderTarget four times which opens with the following:
//	// Swap to the target regardless, in order to clear any leftover content from last frame.
//	renderTarget.SwapToRenderTarget();
//
//	if (pixelPrimitives.Any() || manualDrawActions.Any())
//	{ ... }
//the emptiness test is right there but it just lies after the swap
//SwapToRenderTarget is a SetRenderTarget and a Clear so that is four target binds and four clears a frame no matter wha
//the comment is correct about why the clear exists (the last frame does linger without it) but that only has to happen once after the content goes away rather than forever
//the drawing half has no test at all
//DrawTarget_NPCs and DrawTarget_Projectiles call DrawTargetScaled four times between them and each of those is a Begin, a full screen Draw of the target at twice scale, and an End
//four full screen alpha blended blits and eight batch restarts every frame whether or not a single pixelated primitive exists anywhere in the world
//Infernum's own older PixelationRenderTargetManager does gate exactly this with if (pixelPrimDrawersList.Count != 0) in front of both halves
//this rewrite dropped it
//(this performance bug is the reason why relatively simple fights like infernum crabulon feel laggy)

namespace SOTSPerfPatch.Patches.Luminance;

internal sealed class SkipEmptyPixelationPasses : PerfPatch
{
	public override string Name => "Luminance: skip empty pixelation passes";

	public override string TargetMod => "Luminance";

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string ModName = "Luminance";
		private const string SystemType = "Luminance.Core.Graphics.PrimitivePixelationSystem";
		private const string RendererType = "Luminance.Core.Graphics.IPixelatedPrimitiveRenderer";
		private const string TargetType = "Luminance.Core.Graphics.ManagedRenderTarget";

		//the layer targets in the order the enum declares them
		private static readonly string[] TargetFields = {
			"PixelationTarget_BeforeNPCs",
			"PixelationTarget_AfterNPCs",
			"PixelationTarget_BeforeProjectiles",
			"PixelationTarget_AfterProjectiles",
		};

		//the primitive list is Luminance's own type so it comes through as object
		private delegate void orig_Fill(RenderTarget2D renderTarget, object pixelPrimitives, Queue<Action> manualDrawActions);
		private delegate void orig_Blit(object target);

		private static FieldInfo[] targets;
		private static FieldInfo drawActions;

		private static readonly uint[] decidedTick = { uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue };
		private static readonly bool[] filled = new bool[4];

		private static bool Enabled => PerfPatchConfig.Instance?.SkipEmptyPixelationPasses ?? false;

		public static void Install()
		{
			Type system = HookHelper.ModType(ModName, SystemType);
			Type renderer = HookHelper.ModType(ModName, RendererType);
			Type managed = HookHelper.ModType(ModName, TargetType);

			targets = new FieldInfo[TargetFields.Length];
			for (int i = 0; i < TargetFields.Length; i++) {
				targets[i] = system.GetField(TargetFields[i], BindingFlags.Static | BindingFlags.NonPublic)
					?? throw new MissingMemberException($"{SystemType}.{TargetFields[i]} was not found.");
			}

			drawActions = system.GetField("CustomDrawActionsByLayer", BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMemberException($"{SystemType}.CustomDrawActionsByLayer was not found.");

			var fill = HookHelper.Method(system, "DrawPrimsToRenderTarget",
				typeof(RenderTarget2D), typeof(List<>).MakeGenericType(renderer), typeof(Queue<Action>));
			var blit = HookHelper.Method(system, "DrawTargetScaled", managed);

			MonoModHooks.Add(fill, OnFill);
			MonoModHooks.Add(blit, OnBlit);
		}

		public static void Reset()
		{
			targets = null;
			drawActions = null;
			for (int i = 0; i < decidedTick.Length; i++)
				decidedTick[i] = uint.MaxValue;
		}

		//the same test Luminance makes only asked before the clear instead of after it
		private static bool HasContent(object pixelPrimitives, Queue<Action> manualDrawActions)
		{
			if (pixelPrimitives is not ICollection primitives)
				return true;

			return primitives.Count > 0 || (manualDrawActions?.Count ?? 0) > 0;
		}

		//the queues is in CustomDrawActionsByLayer and one is handed to each layer's fill so the queue itself says which layer this call is for
		//the dictionary keys are the layer enum
		private static int LayerOf(Queue<Action> manualDrawActions)
		{
			if (manualDrawActions == null || drawActions == null)
				return -1;

			if (drawActions.GetValue(null) is not IDictionary byLayer)
				return -1;

			foreach (DictionaryEntry entry in byLayer) {
				if (ReferenceEquals(entry.Value, manualDrawActions))
					return Convert.ToInt32(entry.Key);
			}

			return -1;
		}

		//the blit is handed the wrapper instead of the raw target so this compares against the fields themselves
		private static int LayerOf(object target)
		{
			if (target == null || targets == null)
				return -1;

			for (int i = 0; i < targets.Length; i++) {
				if (ReferenceEquals(targets[i].GetValue(null), target))
					return i;
			}

			return -1;
		}

		private static void OnFill(orig_Fill orig, RenderTarget2D renderTarget, object pixelPrimitives, Queue<Action> manualDrawActions)
		{
			bool content = HasContent(pixelPrimitives, manualDrawActions);
			int layer = LayerOf(manualDrawActions);

			if ((uint)layer < (uint)filled.Length) {
				decidedTick[layer] = Main.GameUpdateCount;
				filled[layer] = content;
			}

			//a layer that could not be identified is left alone entirely so the blit runs too
			if (!Enabled || content || layer < 0)
				orig(renderTarget, pixelPrimitives, manualDrawActions);
		}

		private static void OnBlit(orig_Blit orig, object target)
		{
			if (!Enabled) {
				orig(target);
				return;
			}

			int layer = LayerOf(target);

			//anything this patch did not just decide about runs as it always did
			if ((uint)layer >= (uint)filled.Length || decidedTick[layer] != Main.GameUpdateCount || filled[layer])
				orig(target);
		}
	}
}
