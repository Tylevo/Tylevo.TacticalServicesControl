using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// One purchase-phone session may hand its exact, confirmed authorization to
/// the existing deployment flow once. This never purchases or consumes credit.
/// </summary>
public sealed class PhonePurchaseDeploymentTransition
{
	private bool _started;
	private bool _resultReceived;
	private bool _finished;
	private ESupportType _purchasedSupport = ESupportType.None;

	public ESupportType PendingSupport { get; private set; } = ESupportType.None;

	public void Begin(bool enabled, bool isPurchasePhone, ESupportType supportType)
	{
		if (_started || _finished)
		{
			return;
		}

		_started = true;
		if (enabled && isPurchasePhone && IsSupported(supportType))
		{
			_purchasedSupport = supportType;
		}
	}

	public bool RecordPurchaseResult(bool paid, FireSupportPurchaseResponse result)
	{
		if (!_started || _finished || _resultReceived)
		{
			return false;
		}

		_resultReceived = true;
		if (_purchasedSupport == ESupportType.None || !paid || result == null ||
		    !result.Ok || !result.AuthorizationGranted || result.AuthorizationConsumed ||
		    !string.Equals(result.SupportType, _purchasedSupport.ToString(), StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		PendingSupport = _purchasedSupport;
		return true;
	}

	public bool TryComplete(
		bool sessionSucceeded,
		bool prepaidMode,
		bool authorizationAvailable,
		bool controllerReady,
		out ESupportType supportType)
	{
		supportType = ESupportType.None;
		if (_finished)
		{
			return false;
		}

		_finished = true;
		ESupportType pending = PendingSupport;
		PendingSupport = ESupportType.None;
		if (!sessionSucceeded || !prepaidMode || !authorizationAvailable || !controllerReady ||
		    pending == ESupportType.None)
		{
			return false;
		}

		supportType = pending;
		return true;
	}

	public void Cancel()
	{
		_finished = true;
		PendingSupport = ESupportType.None;
	}

	public static bool UsesPrepaidAuthorization(PaymentMode paymentMode)
	{
		return paymentMode is PaymentMode.PhoneAuthorizations or PaymentMode.Hybrid;
	}

	private static bool IsSupported(ESupportType supportType)
	{
		return supportType is ESupportType.Strafe or ESupportType.DoubleStrafe or
			ESupportType.Extract or ESupportType.PriorityExfil or ESupportType.Uav or ESupportType.FocusedSweep;
	}
}
