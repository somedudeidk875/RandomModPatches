using System;
using Microsoft.Xna.Framework.Graphics;
using SOTS;
using SOTS.Projectiles.Minions;
using SOTS.Projectiles.Pyramid;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//CurseHelper.DrawPlayerFoam runs once per active player per frame out of SOTS's pre player draw with nothing in front of it to say whether that player has any foam at all
//what it does first is shown below:
//	List<int> slots = new List<int>();
//	for (int i = 0; i < Main.projectile.Length; i++) {
//		Projectile proj = Main.projectile[i];
//		bool validType = proj.type == ProjectileType<GasBlast>() || proj.type == ProjectileType<CursedBlade>() || proj.type == ProjectileType<BrachialLance>();
//		if (validType && proj.active && proj.owner == player.whoAmI)
//so a list is allocated every frame, all thousand slots are walked, and the three content lookups are done for every slot before anything checks whether the projectile is even active
//that is about three thousand lookups per player per frame
//then DrawFoam is called at least three more times and each of those opens with ModContent.Request on a string path before it looks at whether the list it was handed is empty
//all of it runs for everyone (whether or not they have ever touched the pyramid content) and runs forever

namespace SOTSPerfPatch.Patches.SOTS.Rendering;

internal sealed class SkipEmptyFoamDraws : PerfPatch
{
	public override string Name => "Skip empty curse foam draws";

	public override string TargetMod => "SOTS";

	public override void Apply() => Hooks.Install();

	internal static void Reset() => Hooks.Reset();

	[JITWhenModsEnabled("SOTS")]
	private static class Hooks
	{
		private delegate void orig_DrawPlayerFoam(SpriteBatch spriteBatch, Player player);

		private static readonly bool[] ownerHasFoamProjectile = new bool[Main.maxPlayers + 1];

		private static int gasBlast = -1;
		private static int cursedBlade = -1;
		private static int brachialLance = -1;

		private static uint scannedTick = uint.MaxValue;

		private static bool Enabled => PerfPatchConfig.Instance?.SkipEmptyFoamDraws ?? false;

		public static void Install()
		{
			var draw = HookHelper.Method(typeof(CurseHelper), nameof(CurseHelper.DrawPlayerFoam),
				typeof(SpriteBatch), typeof(Player));

			MonoModHooks.Add(draw, OnDrawPlayerFoam);
		}

		public static void Reset()
		{
			gasBlast = -1;
			cursedBlade = -1;
			brachialLance = -1;
			scannedTick = uint.MaxValue;
			Array.Clear(ownerHasFoamProjectile, 0, ownerHasFoamProjectile.Length);
		}

		private static void ResolveTypes()
		{
			if (gasBlast >= 0)
				return;

			gasBlast = ModContent.ProjectileType<GasBlast>();
			cursedBlade = ModContent.ProjectileType<CursedBlade>();
			brachialLance = ModContent.ProjectileType<BrachialLance>();
		}

		//one pass a tick for everyone
		private static void Rescan()
		{
			if (scannedTick == Main.GameUpdateCount)
				return;

			scannedTick = Main.GameUpdateCount;
			ResolveTypes();
			Array.Clear(ownerHasFoamProjectile, 0, ownerHasFoamProjectile.Length);

			int a = gasBlast, b = cursedBlade, d = brachialLance;
			Projectile[] projectiles = Main.projectile;
			for (int i = 0; i < Main.maxProjectiles; i++) {
				Projectile projectile = projectiles[i];
				if (!projectile.active)
					continue;

				int type = projectile.type;
				if (type != a && type != b && type != d)
					continue;

				int owner = projectile.owner;
				if ((uint)owner < (uint)ownerHasFoamProjectile.Length)
					ownerHasFoamProjectile[owner] = true;
			}
		}

		private static bool AnyFoam(Player player)
		{
			if (player == null || !player.TryGetModPlayer(out SOTSPlayer modPlayer))
				return true;

			if (modPlayer.foamParticleList1 == null || modPlayer.foamParticleList1.Count > 0)
				return true;

			Rescan();
			int who = player.whoAmI;
			return (uint)who >= (uint)ownerHasFoamProjectile.Length || ownerHasFoamProjectile[who];
		}

		private static void OnDrawPlayerFoam(orig_DrawPlayerFoam orig, SpriteBatch spriteBatch, Player player)
		{
			if (!Enabled || AnyFoam(player))
				orig(spriteBatch, player);
		}
	}
}
