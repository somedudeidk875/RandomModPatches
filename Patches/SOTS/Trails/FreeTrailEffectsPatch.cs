using Microsoft.Xna.Framework.Graphics;
using SOTS.Prim;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria.ModLoader;

namespace SOTSPerfPatch.Patches.SOTS.Trails;

//every PrimTrail creates its own BasicEffect (a GPU resource) and never disposes it so every sword swing or trail-leaving projectile leaks one 
//this patch disposes the effect when the trail is disposed
internal sealed class FreeTrailEffectsPatch : PerfPatch
{
	public override string Name => "Trail effect disposal";

	public override string TargetMod => "SOTS";

	public override void Apply() => Hooks.Install();

	[JITWhenModsEnabled("SOTS")]
	private static class Hooks
	{
		private delegate void orig_Dispose(PrimTrail self);

		private static bool Enabled => PerfPatchConfig.Instance?.DisposeTrailEffects ?? false;

		public static void Install()
		{
			var dispose = HookHelper.Method(typeof(PrimTrail), nameof(PrimTrail.Dispose));
			MonoModHooks.Add(dispose, OnDispose);
		}

		private static void OnDispose(orig_Dispose orig, PrimTrail self)
		{
			orig(self);

			if (!Enabled)
				return;

			BasicEffect effect = self.BasicEffect;
			if (effect != null && !effect.IsDisposed)
				effect.Dispose();
		}
	}
}
