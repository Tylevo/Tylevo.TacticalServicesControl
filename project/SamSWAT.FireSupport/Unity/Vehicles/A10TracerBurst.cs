using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class A10TracerBurst
{
	public int BurstId { get; }
	public int VisualSeed { get; }
	public int PassIndex { get; }
	public float FireStartNetworkTime { get; }
	public A10TracerSegment[] Segments { get; }

	public A10TracerBurst(
		int burstId,
		int visualSeed,
		int passIndex,
		float fireStartNetworkTime,
		A10TracerSegment[] segments)
	{
		BurstId = burstId;
		VisualSeed = visualSeed;
		PassIndex = passIndex;
		FireStartNetworkTime = fireStartNetworkTime;
		Segments = segments ?? Array.Empty<A10TracerSegment>();
	}
}
