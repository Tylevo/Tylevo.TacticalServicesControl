using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class A10StrikeRequest
{
	public string SupportRequestId { get; set; } = string.Empty;
	public ESupportType SupportType { get; set; } = ESupportType.Strafe;
	public Vector3 Position { get; set; }
	public Vector3 Direction { get; set; }
	public Vector3 Rotation { get; set; }
	public int VisualSeed { get; set; }
	public int PassIndex { get; set; }
	public string RequesterProfileId { get; set; } = string.Empty;
	public bool VisualOnly { get; set; }
	public A10AuthorityRole Role { get; set; } = A10AuthorityRole.Singleplayer;
}
