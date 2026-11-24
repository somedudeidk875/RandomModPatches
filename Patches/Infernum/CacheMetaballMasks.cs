using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria.ModLoader;

//Utilities.CreateMetaballsFromTexture spawns Infernum's lava and shadow metaballs along the lit pixels of a sprite
//to find those pixels it does this every call (shown below)
//	Color[] colorData = new Color[textureWidth * textureHeight];
//	texture.GetData(colorData);
//GetData is a readback
//it pulls the texture back off the graphics card into managed memory which means the processor has to stop and wait for the driver before it can carry on
//it is one of the worst things to put in a loop and three callers put it in AI not in a one off event:
//	AcceleratingMagicProfanedRock (Providence)
//	ProfanedRock and ProfanedCirclingRock (Profaned Guardians)
//all three are behind nothing but Main.rand.NextBool() so a fifty percent chance every tick for every rock
//the circling rock attack puts out rings of about twenty so those attacks are doing something ~ten readbacks a tick
//the other callers fire on one off events and are fine

namespace SOTSPerfPatch.Patches.Infernum;

internal sealed class CacheMetaballMasks : PerfPatch
{
	public override string Name => "Infernum: fix the metaball texture readback";

	public override string TargetMod => "InfernumMode";

	private static readonly Dictionary<Texture2D, Color[]> masks = new();

	public override void Apply() => Hooks.Install();

	internal static void Reset() => masks.Clear();

	private static bool Enabled => PerfPatchConfig.Instance?.CacheMetaballMasks ?? false;

	//replaces the new Color[width * height] with the pixels this texture was already read back for
	private static Color[] Mask(int length, Texture2D texture)
	{
		if (!Enabled || texture == null)
			return new Color[length];

		if (masks.TryGetValue(texture, out Color[] mask))
			return mask;

		mask = new Color[texture.Width * texture.Height];
		texture.GetData(mask);
		masks[texture] = mask;
		return mask;
	}

	//replaces the GetData call
	private static void Read(Texture2D texture, Color[] into)
	{
		if (texture == null || into == null)
			return;

		if (!Enabled || !masks.TryGetValue(texture, out Color[] mask) || !ReferenceEquals(mask, into))
			texture.GetData(into);
	}

	private static class Hooks
	{
		private const string ModName = "InfernumMode";
		private const string UtilitiesType = "InfernumMode.Utilities";

		public static void Install()
		{
			Type utilities = HookHelper.ModType(ModName, UtilitiesType);

			var create = HookHelper.Method(utilities, "CreateMetaballsFromTexture",
				typeof(Texture2D), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(int));

			//the edit pushes argument zero as the texture which is only right while this is static
			if (!create.IsStatic)
				throw new InvalidOperationException("CreateMetaballsFromTexture is no longer static. Infernum may have been updated.");

			MonoModHooks.Modify(create, EditCreate);
		}

		//two single instruction swaps both of which leave the stack exactly as it was
		private static void EditCreate(ILContext il)
		{
			var c = new ILCursor(il);

			int arrays = Count(c, i => i.MatchNewarr<Color>());
			if (arrays != 1)
				throw new InvalidOperationException(
					$"Expected exactly one Color array in CreateMetaballsFromTexture, found {arrays}. Infernum may have been updated.");

			c.Index = 0;
			c.GotoNext(MoveType.Before, i => i.MatchNewarr<Color>());
			c.Remove();
			//the texture is the first argument so it is still there to push
			c.Emit(OpCodes.Ldarg_0);
			c.EmitDelegate<Func<int, Texture2D, Color[]>>(Mask);

			int reads = Count(c, IsGetData);
			if (reads != 1)
				throw new InvalidOperationException(
					$"Expected exactly one GetData call in CreateMetaballsFromTexture, found {reads}. Infernum may have been updated.");

			c.Index = 0;
			c.GotoNext(MoveType.Before, IsGetData);
			c.Remove();
			c.EmitDelegate<Action<Texture2D, Color[]>>(Read);
		}

		private static int Count(ILCursor c, Func<Instruction, bool> match)
		{
			int found = 0;
			c.Index = 0;
			while (c.TryGotoNext(MoveType.Before, i => match(i))) {
				found++;
				c.Index++;
			}

			return found;
		}

		private static bool IsGetData(Instruction i) =>
			(i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call)
			&& i.Operand is MethodReference method
			&& method.Name == "GetData"
			&& method.Parameters.Count == 1;
	}
}
