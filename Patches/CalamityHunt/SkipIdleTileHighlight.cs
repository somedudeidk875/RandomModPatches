using System;
using System.Reflection;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//TileEdgeHighlight draws a glowing outline around solid tiles during one of Stellar Geliath's attacks
//the draw side of it is gated properly and DrawHighlight bails out while its fade value is still zero
//the pass that fills the target DrawHighlight reads is not gated at all
//CombineTileTargets detours pdateAtmosphereTransparencyToSkyColor so it runs every frame (<-dreaded three words) from the moment render targets exist to the moment they go away
//every one of those frames it does the stuff below:
//	SetRenderTarget(tileTarget); Clear(Transparent);      //full screen clear
//	Begin; Draw(Main.instance.tileTarget); End;           //full screen blit
//	Begin; foreach (Player p in Main.ActivePlayers) Main.PlayerRenderer.DrawPlayer(...); End;
//	Main.tileBatch.Begin();
//	... a sweep over every tile on screen ...
//	Main.tileBatch.End();
//the sweep is the expensive half 
//it walks the whole screen in tiles (about 124 by 72 at 1080p) and for each one calls WorldGen.InWorld, Lighting.Brightness and WorldGen.SolidTile
//that is roughly nine thousand solid tile checks a frame (on top of drawing every player a second time) all to fill a target
//that nothing looks at unless Geliath is mid attack
//the mod's own source has a TODO above the loop about it being "a little laggy" 
//(if i had a nickel everytime a mod author put a todo above a known laggy piece of code and this mod fixed it, id have two nickels)

namespace SOTSPerfPatch.Patches.CalamityHunt;

internal sealed class SkipIdleTileHighlight : PerfPatch
{
	public override string Name => "Hunt of the Old God: skip the idle tile outline pass";

	public override string TargetMod => "CalamityHunt";

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string ModName = "CalamityHunt";
		private const string SystemType = "CalamityHunt.Common.Systems.TileEdgeHighlight";

		private delegate void orig_Combine(object self, On_Main.orig_UpdateAtmosphereTransparencyToSkyColor orig);

		private static FieldInfo fade;

		private static bool Enabled => PerfPatchConfig.Instance?.SkipIdleTileHighlight ?? false;

		public static void Install()
		{
			Type system = HookHelper.ModType(ModName, SystemType);

			fade = system.GetField("_fade", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMemberException($"{SystemType}._fade was not found.");

			var combine = HookHelper.Method(system, "CombineTileTargets",
				typeof(On_Main.orig_UpdateAtmosphereTransparencyToSkyColor));

			MonoModHooks.Add(combine, OnCombine);
		}

		public static void Reset() => fade = null;

		//the same test DrawHighlight makes before it uses the target
		private static bool Visible(object self)
		{
			if (fade == null)
				return true;

			return fade.GetValue(self) is not float value || value > 0.01f;
		}

		private static void OnCombine(orig_Combine handler, object self, On_Main.orig_UpdateAtmosphereTransparencyToSkyColor orig)
		{
			if (!Enabled || Visible(self))
				handler(self, orig);
			else
				orig();
		}
	}
}
