using EFT;
using EFT.Ballistics;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class A10HeadlessDamageCommand
{
	public string SupportRequestId { get; set; } = string.Empty;
	public string TargetProfileId { get; set; } = string.Empty;
	public int TargetNetId { get; set; }
	public DamageInfoStruct DamageInfo { get; set; }
	public EBodyPart BodyPart { get; set; }
	public EBodyPartColliderType ColliderType { get; set; }
	public EArmorPlateCollider ArmorPlateCollider { get; set; }
	public MaterialType MaterialType { get; set; } = MaterialType.Body;
	public float Absorbed { get; set; }
}

public delegate bool A10HeadlessDamageCommandHandler(A10HeadlessDamageCommand command, out string reason);

public static class A10HeadlessDamageCommandDispatcher
{
	public static A10HeadlessDamageCommandHandler Handler { get; set; }

	public static bool TryDispatch(A10HeadlessDamageCommand command, out string reason)
	{
		A10HeadlessDamageCommandHandler handler = Handler;
		if (handler == null)
		{
			reason = "NoFikaDamageCommandHandler";
			return false;
		}

		try
		{
			return handler(command, out reason);
		}
		catch (System.Exception ex)
		{
			reason = $"{ex.GetType().Name}:{ex.Message}";
			return false;
		}
	}
}
