using EFT.InputSystem;
using HarmonyLib;
using JetBrains.Annotations;
using SPT.Reflection.Patching;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Utils;

[UsedImplicitly]
internal class InputManagerUtil : ModulePatch
{
	private static InputManager s_inputManager;

	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.DeclaredMethod(
			typeof(InputManager),
			nameof(InputManager.Create),
			[
				typeof(KeyGroup[]),
				typeof(AxisGroup[]),
				typeof(float),
				typeof(bool)
			]) ?? throw new System.MissingMethodException(
				"SPT InputManager.Create(KeyGroup[], AxisGroup[], float, bool) was not found.");
	}

	[PatchPostfix]
	private static void PatchPostfix(InputManager __result)
	{
		s_inputManager = __result;
	}

	internal static InputManager GetInputManager()
	{
		return s_inputManager;
	}
}
