using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SOTSPerfPatch.Common.Configs;
using SOTSPerfPatch.Core;
using Terraria;
using Terraria.ModLoader;

//T. Overhaul splits the world into 64x64 tile chunks and gives a chunk a render target the first time blood or a bullet hole lands in it 
//a chunk costs about 1 MB of video memory (a 512x512 decal target + a small lighting one)
//ChunkSystem never unloads them
//the sweep that would do it is commented out in the source (i found this really funny for some reason)
//   /*public override void PostUpdateEverything()
//   {
//       foreach(var chunk in EnumerateChunksInArea(Main.LocalPlayer, ChunkUpdateArea, false)) {
//
//also chunk.Dispose() is only ever reached from ChunkSystem.Unload which runs when the mod unloads and not when you leave a world
//there is no ClearWorld or OnWorldUnload anywhere in the mod so the chunks carry over from world to world too

namespace SOTSPerfPatch.Patches.TerrariaOverhaul;

internal sealed class FreeDistantChunkTexturesPatch : PerfPatch
{
	public override string Name => "Overhaul: free distant chunk textures";

	public override string TargetMod => "TerrariaOverhaul";

	public static int ChunksFreed { get; private set; }

	public override void Apply() => Hooks.Install();

	internal static void OnUpdate() => Hooks.Tick();

	internal static void Reset() => Hooks.Reset();

	private static class Hooks
	{
		private const string ModName = "TerrariaOverhaul";
		private const string ChunkSystemType = "TerrariaOverhaul.Core.Chunks.ChunkSystem";
		private const string ChunkType = "TerrariaOverhaul.Core.Chunks.Chunk";

		private const int SweepInterval = 120;

		private const int KeptRadiusInChunks = 6;

		private static FieldInfo chunksField;
		private static int chunkSize;
		private static bool installed;

		private static readonly List<(long Key, long DistanceSq)> candidates = new();

		private static bool Enabled => PerfPatchConfig.Instance?.FreeDistantChunkTextures ?? false;

		private static int Budget => PerfPatchConfig.Instance?.ChunkTextureBudget ?? 192;

		public static void Install()
		{
			Type chunkSystem = HookHelper.ModType(ModName, ChunkSystemType);
			Type chunk = HookHelper.ModType(ModName, ChunkType);

			chunksField = chunkSystem.GetField("chunks", BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingFieldException($"{ChunkSystemType}.chunks was not found.");

			//resolved rather than assumed so a different chunk size still lines up
			FieldInfo maxChunkSize = chunk.GetField("MaxChunkSize", BindingFlags.Static | BindingFlags.Public)
				?? throw new MissingFieldException($"{ChunkType}.MaxChunkSize was not found.");

			chunkSize = (int)maxChunkSize.GetRawConstantValue();
			if (chunkSize <= 0)
				throw new InvalidOperationException($"{ChunkType}.MaxChunkSize was {chunkSize}.");

			//chunk implements IDisposable so the chunks themselves need no reflection at all
			if (!typeof(IDisposable).IsAssignableFrom(chunk))
				throw new InvalidOperationException($"{ChunkType} no longer implements IDisposable.");

			installed = true;
		}

		public static void Reset()
		{
			chunksField = null;
			installed = false;
			candidates.Clear();
			ChunksFreed = 0;
		}

		public static void Tick()
		{
			if (!installed || !Enabled || Main.dedServ || Main.gameMenu)
				return;

			if (Main.GameUpdateCount % SweepInterval != 0)
				return;

			if (chunksField.GetValue(null) is not IDictionary chunks)
				return;

			int budget = Budget;
			if (chunks.Count <= budget)
				return;

			if (Main.LocalPlayer is not { active: true } player)
				return;

			//the player's position in chunk space 
			//worked out the same way ChunkSystem does it
			int playerChunkX = (int)(player.position.X / 16f) / chunkSize;
			int playerChunkY = (int)(player.position.Y / 16f) / chunkSize;

			candidates.Clear();

			foreach (DictionaryEntry entry in chunks) {
				long key = (long)entry.Key;

				//ChunkSystem.PackPosition is ((long)y << 32) | (uint)x.
				int x = (int)(key & 0xFFFFFFFFL);
				int y = (int)(key >> 32);

				long dx = x - playerChunkX;
				long dy = y - playerChunkY;

				if (dx > -KeptRadiusInChunks && dx < KeptRadiusInChunks && dy > -KeptRadiusInChunks && dy < KeptRadiusInChunks)
					continue;

				candidates.Add((key, dx * dx + dy * dy));
			}

			//furthest first.
			candidates.Sort(static (a, b) => b.DistanceSq.CompareTo(a.DistanceSq));

			int toFree = chunks.Count - budget;

			for (int i = 0; i < candidates.Count && toFree > 0; i++) {
				long key = candidates[i].Key;

				if (chunks[key] is IDisposable chunk) {
					//T. Overhaul's own teardown
					chunk.Dispose();
					ChunksFreed++;
				}

				chunks.Remove(key);
				toFree--;
			}

			candidates.Clear();
		}
	}
}
