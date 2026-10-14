using System;
using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;

namespace SOTSPerfPatch.Patches.Redemption;

//shared reflection for MoR RenderTargetManager used by both Redemption patches.

internal static class EffectLayerFields
{
	public const string ModName = "Redemption";
	public const string ManagerType = "Redemption.Effects.RenderTargets.RenderTargetManager";

	private static readonly string[] LayerNames = { "BasicLayer", "EmberLayer", "ShieldLayer" };

	private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

	private sealed class Layer
	{
		public FieldInfo OnManager;
		public FieldInfo Sprites;
		public FieldInfo Target;
		public FieldInfo EffectTarget;
	}

	private static Layer[] layers;

	public static void Resolve(Type managerType)
	{
		var found = new Layer[LayerNames.Length];

		for (int i = 0; i < LayerNames.Length; i++) {
			FieldInfo onManager = Field(managerType, LayerNames[i]);
			Type layerType = onManager.FieldType;

			found[i] = new Layer {
				OnManager = onManager,
				Sprites = Field(layerType, "Sprites"),
				Target = Field(layerType, "Target"),
				EffectTarget = Field(layerType, "EffectTarget"),
			};
		}

		layers = found;
	}

	private static FieldInfo Field(Type type, string name)
	{
		return type.GetField(name, PublicInstance)
			?? throw new MissingFieldException($"{type.FullName}.{name} was not found. Redemption may have been updated.");
	}

	//true when all three layers have nothing queued
	public static bool AllEmpty(object manager)
	{
		foreach (Layer layer in layers) {
			object instance = layer.OnManager.GetValue(manager);
			if (instance == null)
				continue;

			if (layer.Sprites.GetValue(instance) is ICollection sprites && sprites.Count > 0)
				return false;
		}

		return true;
	}

	//the six render targets currently held so they can be freed after they have been replaced
	public static RenderTarget2D[] CurrentTargets(object manager)
	{
		var targets = new RenderTarget2D[layers.Length * 2];

		for (int i = 0; i < layers.Length; i++) {
			object instance = layers[i].OnManager.GetValue(manager);
			if (instance == null)
				continue;

			targets[i * 2] = layers[i].Target.GetValue(instance) as RenderTarget2D;
			targets[i * 2 + 1] = layers[i].EffectTarget.GetValue(instance) as RenderTarget2D;
		}

		return targets;
	}
}
