using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria.ModLoader;

//PrimitiveTrailCopy is what nearly every trail in Infernum is drawn with
//its own source puts the count at 254 references
//before it builds a trail it does this (shown below)
//	originalPositions = originalPositions.Where(p => p != Vector2.Zero);
//	List<Vector2> trailPoints = GetTrailPoints(originalPositions, generalOffset, totalTrailPoints);
//the Where is a lazy iterator
//it is not a collection and it is not a list so whatever the caller handed in what GetTrailPoints receives cannot be counted or indexed without walking it 
//and the loop in GetTrailPoints is the following
//	for (int i = 0; i < originalPositions.Count(); i++) {
//		if (originalPositions.ElementAt(i) == Vector2.Zero) continue;
//		float completionRatio = i / (float)originalPositions.Count();
//		...
//		controlPoints.Add(originalPositions.ElementAt(i) + offset);
//	}
//Count() is in the loop condition so the whole sequence is walked again on every pass
//ElementAt(i) walks from the front every time
//both of them appear twice
//that is four walks per pass each one running the Where test again so a trail of n points costs ~n times 2n plus 2i instead of n
//how much that hurts depends on the length

namespace SOTSPerfPatch.Patches.Infernum;

internal sealed class FasterTrailPoints : PerfPatch
{
	public override string Name => "Infernum: faster trail point building";

	public override string TargetMod => "InfernumMode";

	public override void Apply() => Hooks.Install();

	private static class Hooks
	{
		private const string ModName = "InfernumMode";
		private const string TrailType = "InfernumMode.Common.Graphics.Primitives.PrimitiveTrailCopy";

		//the instance is typed as object because this mod cannot reference Infernum's types.
		private delegate List<Vector2> orig_GetTrailPoints(object self, IEnumerable<Vector2> originalPositions, Vector2 generalOffset, int totalTrailPoints);

		private static bool Enabled => PerfPatchConfig.Instance?.FasterTrailPoints ?? false;

		public static void Install()
		{
			Type trail = HookHelper.ModType(ModName, TrailType);

			var points = HookHelper.Method(trail, "GetTrailPoints",
				typeof(IEnumerable<Vector2>), typeof(Vector2), typeof(int));

			MonoModHooks.Add(points, OnGetTrailPoints);
		}

		private static List<Vector2> OnGetTrailPoints(orig_GetTrailPoints orig, object self, IEnumerable<Vector2> originalPositions, Vector2 generalOffset, int totalTrailPoints)
		{
			//a caller that already passed a list gets the fast paths on its own, and a null would throw
			//inside the handler exactly as it does now.
			if (!Enabled || originalPositions is null || originalPositions is IList<Vector2>)
				return orig(self, originalPositions, generalOffset, totalTrailPoints);

			//a fresh list rather than a shared buffer, because this runs inside drawing code and the
			//handler is free to hold on to what it is given.
			return orig(self, new List<Vector2>(originalPositions), generalOffset, totalTrailPoints);
		}
	}
}
