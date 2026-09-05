using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Behavior-compatible replacement for SPT's BTR delivery callback. Only item
/// trees carrying a durable TSC marker are diverted to the UH-60 Pilot; every
/// other package continues through BtrDeliveryService unchanged.
/// </summary>
[Injectable(
	InjectionType.Singleton,
	TypePriority = OnUpdateOrder.BtrDeliveryCallbacks + 1)]
public sealed class FireSupportUh60DeliveryCallbacks(
	ISptLogger<FireSupportUh60DeliveryCallbacks> logger,
	FireSupportUh60DeliveryService uh60DeliveryService,
	BtrDeliveryService btrDeliveryService,
	TimeUtil timeUtil,
	BtrDeliveryConfig btrDeliveryConfig,
	SaveServer saveServer) : IOnUpdate
{
	public async Task<bool> OnUpdateAsync(
		long secondsSinceLastRun,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (secondsSinceLastRun < btrDeliveryConfig.RunIntervalSeconds)
		{
			return false;
		}

		await ProcessDeliveriesAsync(cancellationToken);
		return true;
	}

	private async Task ProcessDeliveriesAsync(
		CancellationToken cancellationToken)
	{
		foreach (var (sessionId, _) in saveServer.GetProfiles())
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (saveServer.IsProfileInvalidOrUnloadable(sessionId))
			{
				continue;
			}

			List<BtrDelivery>? deliveryList =
				saveServer.GetProfile(sessionId).BtrDeliveryList;
			if (deliveryList == null || deliveryList.Count == 0)
			{
				continue;
			}

			long currentTime = timeUtil.GetTimeStamp();
			foreach (BtrDelivery package in deliveryList
				         .Where(package =>
					         currentTime >= package.ScheduledTime)
				         .ToList())
			{
				await ProcessPackageAsync(
					sessionId,
					package,
					cancellationToken);
			}
		}
	}

	private async Task ProcessPackageAsync(
		MongoId sessionId,
		BtrDelivery package,
		CancellationToken cancellationToken)
	{
		if (package.Items == null || package.Items.Count == 0)
		{
			btrDeliveryService.RemoveBTRDeliveryPackageFromProfile(
				sessionId,
				package);
			await TrySaveProfileAsync(
				sessionId,
				"empty delivery package removal");
			return;
		}

		List<Item> originalItems = package.Items.ToList();
		bool hasMessengerItems;
		string profileId;
		List<Item> messengerItems;
		List<Item> stockItems;
		try
		{
			hasMessengerItems =
				uh60DeliveryService.TryPartitionMarkedItems(
					sessionId,
					originalItems,
					out profileId,
					out messengerItems,
					out stockItems);
		}
		catch (Exception exception)
		{
			logger.Error(
				"TSC UH-60 routing failed before delivery; the complete package will use stock BTR delivery.",
				exception);
			await SendStockPackageAsync(sessionId, package);
			return;
		}

		if (!hasMessengerItems)
		{
			await SendStockPackageAsync(sessionId, package);
			return;
		}

		// MailSendService silently filters unknown templates and the stock BTR
		// callback then removes the package. A marked package must instead remain
		// wholly queued until its supplying item mod is restored.
		if (!uh60DeliveryService.TryValidateItemTemplates(
			    originalItems,
			    out string missingTemplate))
		{
			logger.Error(
				$"TSC UH-60 package {package.Id} contains unavailable template {missingTemplate}; " +
				"the complete original package and marker remain queued for recovery.");
			return;
		}

		string packageId = package.Id.ToString();
		if (!uh60DeliveryService.TryPrepareDelivery(
			    sessionId,
			    profileId,
			    packageId,
			    messengerItems,
			    out FireSupportUh60DeliveryReceipt receipt,
			    out string prepareReason))
		{
			if (string.Equals(
				    prepareReason,
				    "DeliveryReceiptMismatch",
				    StringComparison.OrdinalIgnoreCase))
			{
				logger.Error(
					$"TSC UH-60 package {package.Id} conflicts with its durable receipt; " +
					"the complete package was left queued to avoid duplicate or lost cargo.");
				return;
			}

			logger.Warning(
				$"TSC UH-60 delivery receipt could not be prepared ({prepareReason}); " +
				"the complete package will use stock BTR delivery.");
			await SendStockPackageAsync(sessionId, package);
			return;
		}

		FireSupportDeliveryMailStatus pilotMailStatus =
			uh60DeliveryService.InspectMessengerDeliveryReceipt(
				sessionId,
				receipt.ReceiptToken,
				messengerItems);
		if (pilotMailStatus == FireSupportDeliveryMailStatus.Incomplete)
		{
			logger.Error(
				$"TSC UH-60 manifest {receipt.ReceiptToken} exists with incomplete attachments. " +
				"The original package remains queued and will not be resent automatically.");
			return;
		}

		try
		{
			if (pilotMailStatus == FireSupportDeliveryMailStatus.Missing)
			{
				messengerItems =
					messengerItems.AdoptOrphanedItems(new MongoId());
				uh60DeliveryService.SendMessengerDelivery(
					sessionId,
					messengerItems,
					receipt.ReceiptToken);
			}
		}
		catch (Exception exception)
		{
			logger.Error(
				"TSC UH-60 Pilot delivery raised an exception; checking the durable manifest before choosing a fallback.",
				exception);
		}

		pilotMailStatus =
			uh60DeliveryService.InspectMessengerDeliveryReceipt(
				sessionId,
				receipt.ReceiptToken,
				messengerItems);
		if (pilotMailStatus == FireSupportDeliveryMailStatus.Missing)
		{
			logger.Warning(
				$"TSC UH-60 manifest {receipt.ReceiptToken} was not inserted; " +
				"the complete package will use stock BTR delivery.");
			package.Items = originalItems;
			bool stockFallbackCompleted =
				await SendStockPackageAsync(sessionId, package);
			if (stockFallbackCompleted)
			{
				// The stock mail now owns the complete package. Retire the
				// prepared Pilot receipt and its marked IDs only after that mail,
				// package removal, and profile save were all confirmed.
				uh60DeliveryService.TryCompleteDelivery(
					sessionId,
					profileId,
					packageId,
					messengerItems);
			}
			return;
		}

		if (pilotMailStatus == FireSupportDeliveryMailStatus.Incomplete)
		{
			logger.Error(
				$"TSC UH-60 manifest {receipt.ReceiptToken} was inserted with incomplete attachments. " +
				"The original package remains queued and will not be resent automatically.");
			return;
		}

		uh60DeliveryService.TryRecordMailObserved(
			sessionId,
			profileId,
			packageId);

		// The receipt proves the custom mail owns the marked trees. Narrow the
		// authoritative package and save that mutation with the dialogue before
		// the native remainder is processed on a later callback pass.
		package.Items = stockItems;
		if (package.Items.Count == 0)
		{
			btrDeliveryService.RemoveBTRDeliveryPackageFromProfile(
				sessionId,
				package);
		}

		if (!await TrySaveProfileAsync(
			    sessionId,
			    $"UH-60 manifest {receipt.ReceiptToken}"))
		{
			return;
		}

		uh60DeliveryService.TryCompleteDelivery(
			sessionId,
			profileId,
			packageId,
			messengerItems);
	}

	private async Task<bool> SendStockPackageAsync(
		MongoId sessionId,
		BtrDelivery package)
	{
		if (package.Items == null || package.Items.Count == 0)
		{
			btrDeliveryService.RemoveBTRDeliveryPackageFromProfile(
				sessionId,
				package);
			return await TrySaveProfileAsync(
				sessionId,
				"empty stock BTR package removal");
		}

		if (!uh60DeliveryService.TryValidateItemTemplates(
			    package.Items,
			    out string missingTemplate))
		{
			logger.Error(
				$"Stock BTR package {package.Id} contains unavailable template {missingTemplate}; " +
				"it remains queued so restoring the supplying item mod can recover it.");
			return false;
		}

		List<Item> expectedItems = package.Items.ToList();
		if (!uh60DeliveryService.TryCaptureDeliveryMessageIds(
			    sessionId,
			    "656f0f98d80a697f855d34b1",
			    out HashSet<string> existingMessageIds))
		{
			logger.Error(
				$"TSC could not snapshot the stock BTR dialogue for package {package.Id}; it remains queued.");
			return false;
		}

		Exception? sendException = null;
		try
		{
			package.Items =
				expectedItems.AdoptOrphanedItems(new MongoId());
			btrDeliveryService.SendBTRDelivery(
				sessionId,
				package.Items);
		}
		catch (Exception exception)
		{
			sendException = exception;
		}

		FireSupportDeliveryMailStatus stockMailStatus =
			uh60DeliveryService.InspectNewDeliveryMessage(
				sessionId,
				"656f0f98d80a697f855d34b1",
				existingMessageIds,
				expectedItems);
		if (stockMailStatus != FireSupportDeliveryMailStatus.Complete)
		{
			string detail = stockMailStatus ==
			                FireSupportDeliveryMailStatus.Incomplete
				? "an incomplete stock mail was observed; automatic retry was stopped for this pass"
				: "no complete stock mail was observed";
			if (sendException != null)
			{
				logger.Error(
					$"TSC deferred to stock BTR delivery, but SPT raised an exception and {detail}; the package remains queued.",
					sendException);
			}
			else
			{
				logger.Error(
					$"TSC deferred to stock BTR delivery, but {detail}; the package remains queued.");
			}

			return false;
		}

		if (sendException != null)
		{
			logger.Warning(
				"TSC observed a complete stock BTR mail despite a later notification exception; " +
				"the source package will be removed to prevent duplicate delivery.");
		}

		btrDeliveryService.RemoveBTRDeliveryPackageFromProfile(
			sessionId,
			package);
		return await TrySaveProfileAsync(
			sessionId,
			$"stock BTR delivery package {package.Id}");
	}

	private async Task<bool> TrySaveProfileAsync(
		MongoId sessionId,
		string context)
	{
		try
		{
			await saveServer.SaveProfileAsync(sessionId);
			return true;
		}
		catch (Exception exception)
		{
			logger.Error(
				$"TSC could not save the profile after {context}; in-memory mail/package state was retained for the next safe save.",
				exception);
			return false;
		}
	}
}
