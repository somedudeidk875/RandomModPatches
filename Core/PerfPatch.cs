namespace SOTSPerfPatch.Core;

//ran out of "funny" things to say
public enum PatchState
{
	NotAttempted,
	Applied,
	SkippedOnServer,
	SkippedModMissing,
	Failed,
}

public abstract class PerfPatch
{
	public abstract string Name { get; }

	public abstract string TargetMod { get; }

	public virtual bool ClientOnly => true;

	public PatchState State { get; internal set; } = PatchState.NotAttempted;

	public string FailureReason { get; internal set; } = string.Empty;

	public abstract void Apply();
}
