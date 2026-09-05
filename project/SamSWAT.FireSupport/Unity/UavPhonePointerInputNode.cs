using EFT;
using EFT.InputSystem;
using EFT.UI;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Phone-lifetime input consumer. EFT visits the most recently added child
/// before PlayerOwner translates its commands and axes. No global input/cursor override.
/// </summary>
internal sealed class UavPhonePointerInputNode : UIInputNode
{
	private UavDeviceController _controller;
	private GamePlayerOwner _inputOwner;
	private List<InputNode> _ownerChildren;

	internal bool IsRegistered => isActiveAndEnabled && _inputOwner != null &&
		ReferenceEquals(_inputOwner._children, _ownerChildren) && _ownerChildren?.Contains(this) == true;

	internal bool Attach(Player player, UavDeviceController controller)
	{
		GamePlayerOwner owner = player?.GetComponent<GamePlayerOwner>();
		if (owner == null)
		{
			return false;
		}

		_ownerChildren = owner._children;
		if (_ownerChildren == null)
		{
			return false;
		}
		_controller = controller;
		_inputOwner = owner;
		_ownerChildren.Add(this);
		return true;
	}

	internal void Detach()
	{
		_ownerChildren?.Remove(this);
		_ownerChildren = null;
		_inputOwner = null;
		_controller = null;
		enabled = false;
	}

	public override ETranslateResult TranslateCommand(ECommand command)
	{
		if (_controller == null)
		{
			return ETranslateResult.Ignore;
		}
		// Let the phone's existing Escape handler close it before EFT opens
		// the pause screen. The next press belongs to EFT after the phone ends.
		if (command == ECommand.Escape && _controller.OwnsPhoneCancelInput())
		{
			return ETranslateResult.Block;
		}
		_controller.GetPhonePointerInputCapture(out bool capture, out bool suppressMouse);
		if (!capture)
		{
			return suppressMouse && (command == ECommand.ToggleShooting || command == ECommand.ToggleAlternativeShooting)
				? ETranslateResult.Block : ETranslateResult.Ignore;
		}

		// Keep movement, menus and release commands flowing. In particular, do
		// not strand an existing aim, lean, breath or freelook state by eating its end.
		return command switch
		{
			ECommand.None or ECommand.ToggleSpeed or ECommand.DecreaseWalkSpeed or
			ECommand.ToggleDuck or ECommand.ToggleSprinting or ECommand.EndSprinting or
			ECommand.ToggleProne or ECommand.NextWalkPose or ECommand.PreviousWalkPose or
			ECommand.Jump or ECommand.Vaulting or ECommand.VaultingEnd or
			ECommand.ToggleWalk or ECommand.EndWalk or ECommand.RestorePose or
			ECommand.ToggleInventory or ECommand.Escape or ECommand.Enter or
			ECommand.ShowConsole or ECommand.MakeScreenshot or ECommand.F12 or
			ECommand.EndShooting or ECommand.EndAlternativeShooting or ECommand.EndBreathing or
			ECommand.EndInteracting or ECommand.EndSpecialInteracting or ECommand.ResetLookDirection or
			ECommand.ReturnFromLeftStep or ECommand.ReturnFromRightStep or
			ECommand.EndLeanLeft or ECommand.EndLeanRight or ECommand.EndAnimBlindFireAbove or
			ECommand.EndAnimBlindFireRight or ECommand.BlindShootEnd or
			ECommand.FinishHighThrow or ECommand.FinishLowThrow or
			ECommand.ToggleTalk or ECommand.StopTalk or ECommand.ToggleVoip
				=> ETranslateResult.Ignore,
			_ => ETranslateResult.Block
		};
	}

	public override void TranslateAxes(ref float[] axes)
	{
		if (_controller == null || axes == null)
		{
			return;
		}
		_controller.GetPhonePointerInputCapture(out bool capture, out _);
		if (!capture)
		{
			return;
		}
		for (int i = (int)EAxis.TurnX; i <= (int)EAxis.LeanX && i < axes.Length; i++)
		{
			axes[i] = 0f;
		}
	}

	public override ECursorResult ShouldLockCursor()
	{
		if (_controller == null)
		{
			return ECursorResult.Ignore;
		}
		_controller.GetPhonePointerInputCapture(out bool capture, out _);
		return capture ? ECursorResult.LockCursor : ECursorResult.Ignore;
	}

	private void OnDisable() => Detach();
	private void OnDestroy() => Detach();
}
