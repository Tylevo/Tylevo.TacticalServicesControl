using BepInEx.Bootstrap;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.UI;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Main-menu-only authorization storefront. It owns no player, hands controller,
/// inventory item, or raid runtime object.
/// </summary>
public sealed partial class MainMenuPurchaseController : MonoBehaviour
{
	private const string ButtonName = "TSC_MainMenuUplinkButton";
	private const string PageName = "TSC_MainMenuPurchasePage";
	private const string SeasonalModifiersPluginGuid = "com.tylevo.seasonalmodifiers";
	private const int LayoutScanPasses = 12;
	private const float LayoutScanIntervalSeconds = 0.5f;
	private const float LayoutDriftCheckIntervalSeconds = 1f;

	// Shared visual language with the native in-raid phone: dark panels,
	// ivory text, amber actions, and green service readiness.
	private static readonly Color s_background = new Color32(3, 6, 7, 250);
	private static readonly Color s_panel = new Color32(5, 7, 8, 255);
	private static readonly Color s_row = new Color32(18, 21, 21, 255);
	private static readonly Color s_line = new Color32(220, 216, 200, 41);
	private static readonly Color s_lineStrong = new Color32(232, 185, 103, 117);
	private static readonly Color s_text = new Color32(220, 216, 200, 255);
	private static readonly Color s_muted = new Color32(150, 146, 132, 255);
	private static readonly Color s_amberHigh = new Color32(232, 185, 103, 255);
	private static readonly Color s_green = new Color32(113, 157, 70, 255);
	private static readonly Color s_greenHigh = new Color32(145, 200, 90, 255);
	private static readonly Color s_red = new Color32(198, 72, 61, 255);

	private static readonly ServiceDescriptor[] s_services =
	[
		new(ESupportType.Strafe, "A10", "A-10 STRAFE"),
		new(ESupportType.DoubleStrafe, "DoublePass", "A-10 DOUBLE PASS"),
		new(ESupportType.Extract, "Extraction", "UH-60 EXTRACTION"),
		// PriorityExfil remains the persisted service key so released
		// authorizations carry forward one-for-one as Cargo Transfer credits.
		new(ESupportType.PriorityExfil, "PriorityExfil", "UH-60 CARGO TRANSFER"),
		new(ESupportType.Uav, "Uav", "UAV RECON"),
		new(ESupportType.FocusedSweep, "FocusedSweep", "UAV FOCUSED SWEEP")
	];

	private static MainMenuPurchaseController s_instance;
	private static string s_boundSessionKey;

	private readonly Dictionary<ESupportType, RowView> _rows = new();
	private MenuScreen _menuScreen;
	private GameObject _pageRoot;
	private Text _statusText;
	private Text _balanceText;
	private Button _refreshButton;
	private GameObject _purchaseConfirmationRoot;
	private Text _purchaseConfirmationTitle;
	private Text _purchaseConfirmationBody;
	private Button _purchaseConfirmationConfirmButton;
	private ESupportType _confirmationType = ESupportType.None;
	private int _confirmationPrice = -1;
	private PaymentCurrency _confirmationCurrency = PaymentCurrency.RUB;
	private int _confirmationRevision;
	private string _confirmationSessionKey = string.Empty;
	private string _confirmationProfileId = string.Empty;
	private string _confirmationRequestId = string.Empty;
	private RaidOpsFireSupportServerConfig _snapshot;
	private CancellationTokenSource _refreshCts;
	private string _profileId = string.Empty;
	private string _sessionKey = string.Empty;
	private string _ambiguousRequestId = string.Empty;
	private ESupportType _ambiguousType = ESupportType.None;
	private int _ambiguousPrice = -1;
	private PaymentCurrency _ambiguousCurrency = PaymentCurrency.RUB;
	private int _generation;
	private int _layoutScansRemaining;
	private float _nextLayoutScanAt;
	private bool _ready;
	private bool _refreshPending;
	private bool _purchasePending;
	private ESupportType _pendingType = ESupportType.None;
	private bool _seasonalClientActive;
	private bool _superseded;
	private bool _destroyed;

	private bool ShouldShowMenuButton =>
		PluginSettings.Enabled?.Value == true &&
		!_seasonalClientActive;

	public static void Attach(MenuScreen menuScreen, Profile profile)
	{
		if (menuScreen == null)
		{
			return;
		}

		MainMenuPurchaseController[] controllers =
			menuScreen.GetComponents<MainMenuPurchaseController>();
		MainMenuPurchaseController controller = null;
		foreach (MainMenuPurchaseController candidate in controllers)
		{
			if (candidate == null || candidate._superseded)
			{
				continue;
			}

			if (candidate == s_instance)
			{
				controller = candidate;
				break;
			}
			controller ??= candidate;
		}
		controller ??= menuScreen.gameObject.AddComponent<MainMenuPurchaseController>();

		foreach (MainMenuPurchaseController candidate in controllers)
		{
			if (candidate != null && candidate != controller)
			{
				candidate.DetachForReplacement();
			}
		}
		controller.Bind(menuScreen, profile);
	}

	public static void CloseForRaidStart()
	{
		s_instance?.ClosePage();
		s_instance?.SetTaskBarVisible(false);
	}

	private void Bind(MenuScreen menuScreen, Profile profile)
	{
		if (s_instance != null && s_instance != this)
		{
			s_instance.DetachForReplacement();
		}

		_superseded = false;
		s_instance = this;
		_menuScreen = menuScreen;
		_profileId = profile?.Id?.Trim() ?? string.Empty;
		string authenticatedKey = FireSupportServerConfigClient.GetAuthenticatedSessionKey();
		string nextBoundKey = $"{authenticatedKey}|menu-profile:{_profileId}";
		if (!string.Equals(s_boundSessionKey, nextBoundKey, StringComparison.Ordinal))
		{
			s_boundSessionKey = nextBoundKey;
			ResetPageState();
			FireSupportServerConfigClient.ClearPreRaidSessionState();
		}

		_sessionKey = authenticatedKey;
		_seasonalClientActive = IsSeasonalModifiersClientActive();
		if (_seasonalClientActive)
		{
			SuppressMenuForSeasonal();
			return;
		}

		enabled = true;
		EnsureMenuButton();
		_layoutScansRemaining = LayoutScanPasses;
		_nextLayoutScanAt = Time.unscaledTime + LayoutScanIntervalSeconds;
	}

	private void Update()
	{
		if (_superseded || s_instance != this)
		{
			DetachForReplacement();
			return;
		}
		if (_seasonalClientActive)
		{
			SuppressMenuForSeasonal();
			return;
		}
		if (!CanUseTaskBar)
		{
			ClosePage();
			SetTaskBarVisible(false);
			return;
		}

		if (_pageRoot != null && _pageRoot.activeSelf) UpdateStorefrontScale();
		SetTaskBarVisible(true);
		if (Time.unscaledTime < _nextLayoutScanAt) return;
		float interval = _layoutScansRemaining > 0
			? LayoutScanIntervalSeconds : LayoutDriftCheckIntervalSeconds;
		if (_layoutScansRemaining > 0) _layoutScansRemaining--;
		_nextLayoutScanAt = Time.unscaledTime + interval;
		EnsureMenuButton();
	}

	private static bool IsSeasonalModifiersClientActive()
	{
		return Chainloader.PluginInfos.ContainsKey(SeasonalModifiersPluginGuid);
	}

	private void SuppressMenuForSeasonal()
	{
		ClosePage();
		RetireAllMenuButtons();
		_layoutScansRemaining = 0;
		_nextLayoutScanAt = float.PositiveInfinity;
		enabled = false;
	}

	private void OpenPage()
	{
		if (!CanUseTaskBar)
		{
			return;
		}

		if (Singleton<GameWorld>.Instance != null)
		{
			FireSupportPlugin.LogSource.LogWarning(
				"TSC pre-raid purchase page refused to open while GameWorld was active.");
			return;
		}

		BuildPage();
		if (_pageRoot == null)
		{
			return;
		}

		_pageRoot.SetActive(true);
		_pageRoot.transform.SetAsLastSibling();
		UpdateStorefrontScale();
		StartRefresh();
	}

	private void StartRefresh()
	{
		StartRefresh(afterMutation: false);
	}

	private void StartRefresh(bool afterMutation)
	{
		if (_refreshPending || _purchasePending || IsPurchaseConfirmationOpen)
		{
			return;
		}

		string authenticatedSessionKey =
			FireSupportServerConfigClient.GetAuthenticatedSessionKey();
		if (string.IsNullOrWhiteSpace(authenticatedSessionKey) ||
		    string.IsNullOrWhiteSpace(_profileId) ||
		    !FireSupportServerConfigClient.IsAuthenticatedProfile(_profileId))
		{
			FailClosedForSessionChange(
				authenticatedSessionKey,
				"Authenticated PMC session is not available. Reopen TSC UPLINK from the current main menu.");
			return;
		}
		if (!string.IsNullOrWhiteSpace(_sessionKey) &&
		    !string.Equals(_sessionKey, authenticatedSessionKey, StringComparison.Ordinal))
		{
			FailClosedForSessionChange(
				authenticatedSessionKey,
				"Authenticated PMC session changed. Reopen TSC UPLINK from the current main menu.");
			return;
		}

		_sessionKey = authenticatedSessionKey;

		_refreshCts?.Cancel();
		_refreshCts?.Dispose();
		_refreshCts = new CancellationTokenSource();
		// Keep the correlated purchase response visible while the required
		// post-mutation verification GET is in flight. A normal/manual refresh
		// still clears stale state until its authenticated snapshot arrives.
		if (!afterMutation)
		{
			_snapshot = null;
		}
		_ready = false;
		_refreshPending = true;
		int generation = ++_generation;
		SetStatus("Refreshing your stash and authorizations...", true);
		Redraw();
		RefreshAsync(generation, _sessionKey, afterMutation, _refreshCts.Token).Forget();
	}

	private async UniTaskVoid RefreshAsync(
		int generation,
		string expectedSessionKey,
		bool afterMutation,
		CancellationToken cancellationToken)
	{
		try
		{
			RaidOpsFireSupportServerConfig snapshot =
				await FireSupportServerConfigClient.FetchPreRaidSnapshotOnceAsync(
					expectedSessionKey,
					cancellationToken);
			if (_destroyed || generation != _generation)
			{
				return;
			}

			if (!ValidateSnapshot(snapshot, out string reason))
			{
				_snapshot = snapshot;
				_ready = false;
				SetStatus(reason, false);
				return;
			}

			_snapshot = snapshot;
			_ready = true;
			bool recoveredPreparedPurchase = AdoptPreparedPurchase(snapshot);
			if (recoveredPreparedPurchase) _selectedService = _ambiguousType;
			if (!string.IsNullOrWhiteSpace(_ambiguousRequestId))
			{
				SetStatus(
					recoveredPreparedPurchase
						? "Interrupted purchase recovered. Review recovery to finish without a second charge."
						: "A purchase needs recovery. Select its service to continue.",
					false);
			}
			else
			{
				SetStatus(
					afterMutation
						? "Purchase confirmed. Your stash and authorizations are up to date."
						: "Services ready. Select an authorization to review.",
					true);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			if (!_destroyed && generation == _generation)
			{
				string authenticatedSessionKey =
					FireSupportServerConfigClient.GetAuthenticatedSessionKey();
				if (!string.Equals(
					    expectedSessionKey,
					    authenticatedSessionKey,
					    StringComparison.Ordinal) ||
				    !FireSupportServerConfigClient.IsAuthenticatedProfile(_profileId))
				{
					FailClosedForSessionChange(
						authenticatedSessionKey,
						"Authenticated PMC session changed during synchronization. Reopen TSC UPLINK.");
					return;
				}

				_ready = false;
				SetStatus($"Synchronization failed: {ex.Message}", false);
			}
		}
		finally
		{
			if (!_destroyed && generation == _generation)
			{
				_refreshPending = false;
				Redraw();
			}
		}
	}

	private void OpenDashboard()
	{
		string host = SPT.Common.Http.RequestHandler.Host;
		if (string.IsNullOrWhiteSpace(host) ||
		    !Uri.TryCreate(host, UriKind.Absolute, out Uri hostUri) ||
		    (hostUri.Scheme != Uri.UriSchemeHttp &&
		     hostUri.Scheme != Uri.UriSchemeHttps))
		{
			SetStatus(
				"The active SPT server address is unavailable. Start the server and refresh TSC UPLINK.",
				false);
			return;
		}

		UriBuilder dashboardUri = new(hostUri)
		{
			Path = "/tsc/admin",
			Query = string.Empty,
			Fragment = string.Empty
		};
		Application.OpenURL(dashboardUri.Uri.AbsoluteUri);
		SetStatus("Opening the TSC dashboard in your default browser...", true);
	}

	private bool IsPurchaseConfirmationOpen =>
		_purchaseConfirmationRoot != null &&
		_purchaseConfirmationRoot.activeSelf;

	private void ShowPurchaseConfirmation(ESupportType supportType)
	{
		if (IsPurchaseConfirmationOpen ||
		    !TryGetPurchaseContext(
			    supportType,
			    out ServiceDescriptor descriptor,
			    out bool retryAmbiguousPurchase))
		{
			return;
		}

		if (!TryResolvePurchaseTerms(
			    _snapshot,
			    descriptor,
			    retryAmbiguousPurchase ? _ambiguousRequestId : string.Empty,
			    out int price,
			    out PaymentCurrency currency,
			    out bool recoveredPreparedQuote))
		{
			SetStatus("Purchase terms are unavailable. REFRESH and try again.", false);
			return;
		}
		int? balance =
			FireSupportServerConfigClient.GetSnapshotStashBalance(_snapshot, currency);
		_confirmationType = supportType;
		_confirmationPrice = price;
		_confirmationCurrency = currency;
		_confirmationRevision = _snapshot.Revision;
		_confirmationSessionKey = _sessionKey;
		_confirmationProfileId = _profileId;
		_confirmationRequestId = retryAmbiguousPurchase
			? _ambiguousRequestId
			: string.Empty;

		SetConfirmationPresentation(descriptor, price, currency, balance, retryAmbiguousPurchase, recoveredPreparedQuote);
		_purchaseConfirmationRoot.SetActive(true);
		_purchaseConfirmationRoot.transform.SetAsLastSibling();
		Redraw();
	}

	private void ConfirmPurchase()
	{
		if (!IsPurchaseConfirmationOpen || _confirmationType == ESupportType.None)
		{
			return;
		}

		ESupportType supportType = _confirmationType;
		int expectedPrice = _confirmationPrice;
		PaymentCurrency expectedCurrency = _confirmationCurrency;
		int expectedRevision = _confirmationRevision;
		string expectedSessionKey = _confirmationSessionKey;
		string expectedProfileId = _confirmationProfileId;
		string expectedRequestId = _confirmationRequestId;
		HidePurchaseConfirmation(redraw: false);

		string authenticatedSessionKey =
			FireSupportServerConfigClient.GetAuthenticatedSessionKey();
		if (string.IsNullOrWhiteSpace(authenticatedSessionKey) ||
		    !string.Equals(expectedSessionKey, authenticatedSessionKey, StringComparison.Ordinal) ||
		    !string.Equals(expectedProfileId, _profileId, StringComparison.Ordinal) ||
		    !FireSupportServerConfigClient.IsAuthenticatedProfile(_profileId))
		{
			FailClosedForSessionChange(
				authenticatedSessionKey,
				"Authenticated PMC session changed. Reopen TSC UPLINK from the current main menu.");
			return;
		}

		ServiceDescriptor descriptor = GetDescriptor(supportType);
		bool retryPreparedPurchase = !string.IsNullOrWhiteSpace(expectedRequestId);
		bool matchingRetry =
			retryPreparedPurchase &&
			_ambiguousType == supportType &&
			string.Equals(
				_ambiguousRequestId,
				expectedRequestId,
				StringComparison.Ordinal);
		bool resolvedTerms =
			TryResolvePurchaseTerms(
				_snapshot,
				descriptor,
				retryPreparedPurchase ? expectedRequestId : string.Empty,
				out int currentPrice,
				out PaymentCurrency currentCurrency,
				out _);
		if (!_ready ||
		    _snapshot == null ||
		    _snapshot.Revision != expectedRevision ||
		    (retryPreparedPurchase && !matchingRetry) ||
		    !resolvedTerms ||
		    currentPrice != expectedPrice ||
		    currentCurrency != expectedCurrency)
		{
			SetStatus(
				"Service availability or pricing changed. REFRESH, then confirm the purchase again.",
				false);
			Redraw();
			return;
		}

		BeginPurchase(supportType, expectedPrice, expectedCurrency);
	}

	private void HidePurchaseConfirmation()
	{
		HidePurchaseConfirmation(redraw: true);
	}

	private void HidePurchaseConfirmation(bool redraw)
	{
		_confirmationType = ESupportType.None;
		_confirmationPrice = -1;
		_confirmationCurrency = PaymentCurrency.RUB;
		_confirmationRevision = 0;
		_confirmationSessionKey = string.Empty;
		_confirmationProfileId = string.Empty;
		_confirmationRequestId = string.Empty;
		if (_purchaseConfirmationRoot != null)
		{
			_purchaseConfirmationRoot.SetActive(false);
		}
		if (redraw)
		{
			Redraw();
		}
	}

	private static string FormatProjectedBalance(
		int balance,
		int price,
		PaymentCurrency currency)
	{
		long projected = (long)balance - price;
		return projected >= 0
			? PaymentCurrencyInfo.Format((int)Math.Min(int.MaxValue, projected), currency)
			: "INSUFFICIENT FUNDS";
	}

	private bool TryResolvePurchaseTerms(
		RaidOpsFireSupportServerConfig snapshot,
		ServiceDescriptor descriptor,
		string preparedRequestId,
		out int price,
		out PaymentCurrency currency,
		out bool recoveredPreparedQuote)
	{
		price = snapshot == null
			? -1
			: GetPrice(snapshot, descriptor.ConfigKey);
		currency = FireSupportServerConfigClient.GetSnapshotCurrency(snapshot);
		recoveredPreparedQuote = false;

		if (snapshot == null || string.IsNullOrWhiteSpace(preparedRequestId))
		{
			return price >= 0;
		}

		if (snapshot.PreparedPurchaseDetails == null)
		{
			// Legacy snapshots expose only the request ID. Preserve their
			// recovery behavior by retrying against the current list terms.
			return price >= 0;
		}

		if (snapshot.PreparedPurchaseDetails.TryGetValue(
			    descriptor.ConfigKey,
			    out FireSupportPreparedPurchaseQuote preparedQuote))
		{
			if (preparedQuote == null ||
			    !string.Equals(
				    preparedQuote.RequestId?.Trim(),
				    preparedRequestId,
				    StringComparison.Ordinal) ||
			    preparedQuote.Price < 0 ||
			    !PaymentCurrencyInfo.TryParse(
				    preparedQuote.Currency,
				    out PaymentCurrency preparedCurrency))
			{
				return false;
			}

			price = preparedQuote.Price;
			currency = preparedCurrency;
			recoveredPreparedQuote = true;
			return true;
		}

		// An accepted response can be lost after the server removes the
		// prepared row. Only the exact locally remembered idempotency key may
		// reuse the terms accepted by that click; an unrelated incomplete
		// details map is never interpreted using current prices.
		if (_ambiguousType == descriptor.Type &&
		    string.Equals(
			    _ambiguousRequestId,
			    preparedRequestId,
			    StringComparison.Ordinal) &&
		    _ambiguousPrice >= 0)
		{
			price = _ambiguousPrice;
			currency = _ambiguousCurrency;
			recoveredPreparedQuote = true;
			return true;
		}

		return false;
	}

	private void BeginPurchase(
		ESupportType supportType,
		int expectedCost,
		PaymentCurrency expectedCurrency)
	{
		if (!TryGetPurchaseContext(
			    supportType,
			    out ServiceDescriptor descriptor,
			    out bool retryAmbiguousPurchase))
		{
			return;
		}

		string requestId = retryAmbiguousPurchase
			? _ambiguousRequestId
			: Guid.NewGuid().ToString("N");
		_purchasePending = true;
		_pendingType = supportType;
		SetStatus(
			retryAmbiguousPurchase
				? $"Recovering the interrupted {descriptor.DisplayName} purchase..."
				: $"Submitting {descriptor.DisplayName} purchase...",
			true);
		Redraw();
		PurchaseAsync(
			supportType,
			requestId,
			expectedCost,
			expectedCurrency,
			_sessionKey,
			_generation).Forget();
	}

	private bool TryGetPurchaseContext(
		ESupportType supportType,
		out ServiceDescriptor descriptor,
		out bool retryAmbiguousPurchase)
	{
		descriptor = GetDescriptor(supportType);
		retryAmbiguousPurchase = false;
		if (!_ready || _snapshot == null || _refreshPending || _purchasePending)
		{
			return false;
		}

		if (!FireSupportServiceAvailability.IsLocalUseAllowed(supportType))
		{
			SetStatus(
				FireSupportServiceAvailability.GetLocalRestrictionReason(supportType),
				false);
			return false;
		}

		string authenticatedSessionKey =
			FireSupportServerConfigClient.GetAuthenticatedSessionKey();
		if (string.IsNullOrWhiteSpace(authenticatedSessionKey) ||
		    !string.Equals(_sessionKey, authenticatedSessionKey, StringComparison.Ordinal) ||
		    !FireSupportServerConfigClient.IsAuthenticatedProfile(_profileId))
		{
			FailClosedForSessionChange(
				authenticatedSessionKey,
				"Authenticated PMC session changed. Reopen TSC UPLINK from the current main menu.");
			return false;
		}

		bool hasAmbiguousPurchase = !string.IsNullOrWhiteSpace(_ambiguousRequestId);
		retryAmbiguousPurchase = hasAmbiguousPurchase && _ambiguousType == supportType;
		if ((hasAmbiguousPurchase && !retryAmbiguousPurchase) ||
		    (!retryAmbiguousPurchase &&
		     (!GetEnabled(_snapshot, descriptor.ConfigKey) ||
		      GetPrice(_snapshot, descriptor.ConfigKey) < 0 ||
		      GetOwned(_snapshot, descriptor.ConfigKey) >= GetMaximum(_snapshot))))
		{
			return false;
		}

		return true;
	}

	private async UniTaskVoid PurchaseAsync(
		ESupportType supportType,
		string requestId,
		int expectedCost,
		PaymentCurrency expectedCurrency,
		string expectedSessionKey,
		int generation)
	{
		bool refreshAfterMutation = false;
		try
		{
			FireSupportPurchaseResponse response =
				await FireSupportPayment.PurchasePersistentAuthorizationAsync(
					supportType,
					requestId,
					expectedCost,
					expectedCurrency,
					expectedSessionKey,
					_profileId);
			if (_destroyed || generation != _generation)
			{
				return;
			}
			string authenticatedSessionKey =
				FireSupportServerConfigClient.GetAuthenticatedSessionKey();
			if (!string.Equals(
				    expectedSessionKey,
				    authenticatedSessionKey,
				    StringComparison.Ordinal) ||
			    !FireSupportServerConfigClient.IsAuthenticatedProfile(_profileId))
			{
				FailClosedForSessionChange(
					authenticatedSessionKey,
					"Authenticated PMC session changed during purchase. Reopen TSC UPLINK.");
				return;
			}

			if (response == null ||
			    !string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
			{
				RememberAmbiguousPurchase(
					requestId,
					supportType,
					expectedCost,
					expectedCurrency);
				_ready = false;
				SetStatus(
					"The purchase result could not be verified. REFRESH, then review recovery for this service.",
					false);
				return;
			}

			ApplyResponseToView(supportType, response);
			if (response?.Ok == true)
			{
				ClearAmbiguousPurchase(requestId, supportType);
				SetStatus($"{GetDescriptor(supportType).DisplayName} authorization purchased.", true);
				refreshAfterMutation = true;
			}
			else
			{
				string reason = response?.Reason ?? "InvalidServerResponse";
				if (IsAmbiguousPurchaseFailure(reason))
				{
					// The server may have committed before the response was lost.
					// Retain the click's ID and require an authoritative refresh
					// before a new ID can be generated.
					RememberAmbiguousPurchase(
						requestId,
						supportType,
						expectedCost,
						expectedCurrency);
					_ready = false;
					SetStatus(
						"The purchase outcome is uncertain. REFRESH, then review recovery for this service.",
						false);
				}
				else
				{
					ClearAmbiguousPurchase(requestId, supportType);
					SetStatus(FormatPurchaseFailure(reason), false);
				}
			}
		}
		catch (Exception ex)
		{
			if (!_destroyed && generation == _generation)
			{
				RememberAmbiguousPurchase(
					requestId,
					supportType,
					expectedCost,
					expectedCurrency);
				_ready = false;
				SetStatus(
					$"The purchase outcome is uncertain ({ex.Message}). REFRESH, then review recovery for this service.",
					false);
			}
		}
		finally
		{
			if (!_destroyed && generation == _generation)
			{
				_purchasePending = false;
				_pendingType = ESupportType.None;
				Redraw();
				if (refreshAfterMutation && _pageRoot?.activeSelf == true)
				{
					StartRefresh(afterMutation: true);
				}
			}
		}
	}

	private void ApplyResponseToView(ESupportType supportType, FireSupportPurchaseResponse response)
	{
		if (_snapshot == null || response == null)
		{
			return;
		}

		PaymentCurrency snapshotCurrency =
			FireSupportServerConfigClient.GetSnapshotCurrency(_snapshot);
		bool responseCurrencyMatches =
			string.IsNullOrWhiteSpace(response.Currency)
				? snapshotCurrency == PaymentCurrency.RUB
				: PaymentCurrencyInfo.TryParse(
					  response.Currency,
					  out PaymentCurrency responseCurrency) &&
				  responseCurrency == snapshotCurrency;
		if (responseCurrencyMatches && response.NewBalance >= 0)
		{
			_snapshot.StashCurrencyBalance = response.NewBalance;
			if (snapshotCurrency == PaymentCurrency.RUB)
			{
				_snapshot.StashRoubleBalance = response.NewBalance;
			}
		}
		if (response.ServerRevision > 0)
		{
			_snapshot.Revision = response.ServerRevision;
		}
		if (response.AuthorizationsIncluded && response.Authorizations != null)
		{
			_snapshot.Authorizations =
				new Dictionary<string, int>(response.Authorizations, StringComparer.OrdinalIgnoreCase);
		}
		if (responseCurrencyMatches && response.Cost >= 0 && _snapshot.Prices != null)
		{
			_snapshot.Prices[GetDescriptor(supportType).ConfigKey] = response.Cost;
		}
	}

	private void Redraw()
	{
		if (_balanceText == null)
		{
			return;
		}

		PaymentCurrency currency =
			FireSupportServerConfigClient.GetSnapshotCurrency(_snapshot);
		int? stashBalance =
			FireSupportServerConfigClient.GetSnapshotStashBalance(_snapshot, currency);
		_balanceText.text = stashBalance is int balance
			? PaymentCurrencyInfo.Format(balance, currency)
			: "SYNC";
		if (_refreshButton != null)
		{
			_refreshButton.interactable =
				!_refreshPending &&
				!_purchasePending &&
				!IsPurchaseConfirmationOpen;
		}

		int maximum = GetMaximum(_snapshot);
		foreach (ServiceDescriptor service in s_services)
		{
			if (!_rows.TryGetValue(service.Type, out RowView row))
			{
				continue;
			}

			bool hasSnapshot = _snapshot != null;
			bool locallyAvailable =
				FireSupportServiceAvailability.IsLocalUseAllowed(service.Type);
			bool enabled =
				hasSnapshot &&
				locallyAvailable &&
				GetEnabled(_snapshot, service.ConfigKey);
			int owned = hasSnapshot ? GetOwned(_snapshot, service.ConfigKey) : 0;
			int price = hasSnapshot ? GetPrice(_snapshot, service.ConfigKey) : -1;
			bool atLimit = hasSnapshot && maximum > 0 && owned >= maximum;
			bool pending = _purchasePending && _pendingType == service.Type;
			bool hasAmbiguousPurchase = !string.IsNullOrWhiteSpace(_ambiguousRequestId);
			bool retryAmbiguousPurchase =
				locallyAvailable &&
				hasAmbiguousPurchase &&
				_ambiguousType == service.Type;
			string localRestrictionStatus =
				FireSupportServiceAvailability.GetLocalRestrictionStatus(service.Type);

			row.State.text = retryAmbiguousPurchase
				? "OUTCOME UNKNOWN"
				: !locallyAvailable
					? localRestrictionStatus
					: !hasSnapshot ? "SYNC" : !enabled ? "LOCKED" : atLimit ? "LIMIT REACHED" : "AVAILABLE";
			row.State.color = retryAmbiguousPurchase
				? s_amberHigh
				: enabled
				? s_greenHigh
				: s_red;
			row.Price.text = price >= 0
				? PaymentCurrencyInfo.Format(price, currency)
				: "--";
			row.Price.color = price >= 0 ? s_amberHigh : s_muted;
			row.Owned.text = hasSnapshot ? $"{owned} / {maximum}" : "-- / --";
			row.CanPurchase =
				_ready &&
				!_refreshPending &&
				!_purchasePending &&
				!IsPurchaseConfirmationOpen &&
				(retryAmbiguousPurchase ||
				 (!hasAmbiguousPurchase && enabled && !atLimit));
			row.Select.interactable = !IsPurchaseConfirmationOpen;
			row.ActionLabel =
				pending
					? "PROCESSING PURCHASE"
					: _refreshPending ? "SYNCING"
					: !_ready ? "REFRESH TO CONTINUE"
					: retryAmbiguousPurchase ? "REVIEW RECOVERY"
					: atLimit ? "LIMIT REACHED" : enabled ? "REVIEW PURCHASE" : "SERVICE LOCKED";
		}
		RedrawStoreDetail();
	}

	private static bool ValidateSnapshot(
		RaidOpsFireSupportServerConfig snapshot,
		out string reason)
	{
		if (snapshot == null || !snapshot.PlayerStateIncluded)
		{
			reason = "Server did not return authoritative player state.";
			return false;
		}
		if (snapshot.PreparedPurchases == null)
		{
			reason = "Server omitted the persistent-purchase recovery journal.";
			return false;
		}
		if (snapshot.PurchasePersistence == null)
		{
			reason = "Server omitted purchase-persistence settings.";
			return false;
		}

		bool hasPreparedPurchase = snapshot.PreparedPurchases.Count > 0;
		if (hasPreparedPurchase)
		{
			foreach (KeyValuePair<string, string> pending in snapshot.PreparedPurchases)
			{
				bool knownService = false;
				foreach (ServiceDescriptor service in s_services)
				{
					if (string.Equals(
						    service.ConfigKey,
						    pending.Key,
						    StringComparison.OrdinalIgnoreCase))
					{
						knownService = true;
						break;
					}
				}

				if (string.IsNullOrWhiteSpace(pending.Value) || !knownService)
				{
					reason =
						$"Server returned an invalid recovery record for {pending.Key}.";
					return false;
				}

				if (snapshot.PreparedPurchaseDetails != null &&
				    (!snapshot.PreparedPurchaseDetails.TryGetValue(
					     pending.Key,
					     out FireSupportPreparedPurchaseQuote quote) ||
				     quote == null ||
				     !string.Equals(
					     quote.RequestId?.Trim(),
					     pending.Value.Trim(),
					     StringComparison.Ordinal) ||
				     quote.Price < 0 ||
				     !PaymentCurrencyInfo.TryParse(quote.Currency, out _)))
				{
					reason =
						$"Server omitted valid recovery terms for {pending.Key}.";
					return false;
				}
			}
		}
		if (!snapshot.PurchasePersistence.Enabled && !hasPreparedPurchase)
		{
			reason = "Pre-raid buying requires Purchase Persistence on the TSC server.";
			return false;
		}
		if (!PaymentCurrencyInfo.TryParse(
			    snapshot.PaymentCurrency,
			    out PaymentCurrency currency))
		{
			reason = "Server omitted a valid payment currency.";
			return false;
		}
		if (!FireSupportServerConfigClient.GetSnapshotStashBalance(snapshot, currency).HasValue ||
		    snapshot.Authorizations == null)
		{
			reason = "Server omitted the authoritative stash balance or authorization ledger.";
			return false;
		}
		if ((!Enum.TryParse(snapshot.PaymentSource, true, out PaymentSource source) ||
		     source == PaymentSource.CarriedRoubles) &&
		    !hasPreparedPurchase)
		{
			reason = "Pre-raid buying requires a server-backed stash payment source.";
			return false;
		}
		if (snapshot.PurchasePersistence.MaxStoredAuthorizationsPerService <= 0 &&
		    !hasPreparedPurchase)
		{
			reason = "The server authorization limit is disabled.";
			return false;
		}
		foreach (ServiceDescriptor service in s_services)
		{
			if (snapshot.Prices == null ||
			    !snapshot.Prices.TryGetValue(service.ConfigKey, out int price) ||
			    price < 0)
			{
				reason = $"Server omitted a valid price for {service.DisplayName}.";
				return false;
			}
			if (snapshot.Enabled == null ||
			    !snapshot.Enabled.ContainsKey(service.ConfigKey))
			{
				reason = $"Server omitted availability for {service.DisplayName}.";
				return false;
			}
		}

		reason = string.Empty;
		return true;
	}

	private bool AdoptPreparedPurchase(RaidOpsFireSupportServerConfig snapshot)
	{
		if (!string.IsNullOrWhiteSpace(_ambiguousRequestId) ||
		    snapshot?.PreparedPurchases == null ||
		    snapshot.PreparedPurchases.Count == 0)
		{
			return false;
		}

		// The server emits entries from oldest to newest. Dictionary insertion
		// order is retained by the JSON client, so adopt the first valid record
		// and recover additional interrupted purchases on subsequent refreshes.
		foreach (KeyValuePair<string, string> pending in snapshot.PreparedPurchases)
		{
			string requestId = pending.Value?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(requestId))
			{
				continue;
			}

			foreach (ServiceDescriptor service in s_services)
			{
				if (!string.Equals(
					    service.ConfigKey,
					    pending.Key,
					    StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (snapshot.PreparedPurchaseDetails == null)
				{
					int currentPrice = GetPrice(snapshot, service.ConfigKey);
					if (currentPrice < 0)
					{
						return false;
					}

					RememberAmbiguousPurchase(
						requestId,
						service.Type,
						currentPrice,
						FireSupportServerConfigClient.GetSnapshotCurrency(snapshot));
					return true;
				}

				if (!snapshot.PreparedPurchaseDetails.TryGetValue(
					    service.ConfigKey,
					    out FireSupportPreparedPurchaseQuote preparedQuote) ||
				    preparedQuote == null ||
				    !string.Equals(
					    preparedQuote.RequestId?.Trim(),
					    requestId,
					    StringComparison.Ordinal) ||
				    preparedQuote.Price < 0 ||
				    !PaymentCurrencyInfo.TryParse(
					    preparedQuote.Currency,
					    out PaymentCurrency preparedCurrency))
				{
					return false;
				}

				RememberAmbiguousPurchase(
					requestId,
					service.Type,
					preparedQuote.Price,
					preparedCurrency);
				return true;
			}
		}

		return false;
	}

	private void SetStatus(string message, bool healthy)
	{
		if (_statusText == null)
		{
			return;
		}

		_statusText.text = message ?? string.Empty;
		_statusText.color = healthy
			? s_greenHigh
			: new Color32(255, 182, 173, 255);
	}

	private void ClosePage()
	{
		_refreshCts?.Cancel();
		HidePurchaseConfirmation(redraw: false);
		if (_pageRoot != null)
		{
			_pageRoot.SetActive(false);
		}
	}

	private void DetachForReplacement()
	{
		if (_superseded)
		{
			return;
		}

		_superseded = true;
		enabled = false;
		ClosePage();
		_destroyed = true;
		_generation++;
		_refreshCts?.Dispose();
		_refreshCts = null;

		RetireTaskBarButton();
		if (_pageRoot != null)
		{
			GameObject stalePage = _pageRoot;
			_pageRoot = null;
			Destroy(stalePage);
		}

		_menuScreen = null;
		Destroy(this);
	}

	private void ResetPageState()
	{
		_generation++;
		_refreshCts?.Cancel();
		_refreshCts?.Dispose();
		_refreshCts = null;
		_snapshot = null;
		_ready = false;
		_refreshPending = false;
		_purchasePending = false;
		_pendingType = ESupportType.None;
		_ambiguousRequestId = string.Empty;
		_ambiguousType = ESupportType.None;
		_ambiguousPrice = -1;
		_ambiguousCurrency = PaymentCurrency.RUB;
		HidePurchaseConfirmation(redraw: false);
		if (_pageRoot != null)
		{
			_pageRoot.SetActive(false);
		}
		Redraw();
	}

	private void FailClosedForSessionChange(string authenticatedSessionKey, string message)
	{
		ResetPageState();
		FireSupportServerConfigClient.ClearPreRaidSessionState();
		_sessionKey = authenticatedSessionKey ?? string.Empty;
		SetStatus(message, false);
	}

	private void OnDisable()
	{
		ClosePage();
		SetTaskBarVisible(false);
	}

	private void OnDestroy()
	{
		bool ownsCurrentUi = !_superseded && s_instance == this;
		_destroyed = true;
		_generation++;
		_refreshCts?.Cancel();
		_refreshCts?.Dispose();
		_refreshCts = null;
		if (ownsCurrentUi)
		{
			RetireTaskBarButton();
		}
		if (_pageRoot != null)
		{
			Destroy(_pageRoot);
		}
		if (s_instance == this)
		{
			s_instance = null;
		}
	}

	private static bool IsAmbiguousPurchaseFailure(string reason)
	{
		return reason is "RequestFailed" or "InvalidServerResponse" or "InternalServerError" or
			"AuthoritativeLedgerMissing" or "ResponseRequestIdMismatch" or
			"PersistentPurchasePending";
	}

	private void ClearAmbiguousPurchase(string requestId, ESupportType supportType)
	{
		if (_ambiguousType == supportType &&
		    string.Equals(_ambiguousRequestId, requestId, StringComparison.Ordinal))
		{
			_ambiguousRequestId = string.Empty;
			_ambiguousType = ESupportType.None;
			_ambiguousPrice = -1;
			_ambiguousCurrency = PaymentCurrency.RUB;
		}
	}

	private void RememberAmbiguousPurchase(
		string requestId,
		ESupportType supportType,
		int price,
		PaymentCurrency currency)
	{
		_ambiguousRequestId = requestId ?? string.Empty;
		_ambiguousType = supportType;
		_ambiguousPrice = price;
		_ambiguousCurrency = PaymentCurrencyInfo.Normalize(currency);
	}

	private static string FormatPurchaseFailure(string reason)
	{
		return reason switch
		{
			"AuthorizationLimitReached" => "Authorization limit reached for this service.",
			"InsufficientRoubles" or "InsufficientFunds" => "Insufficient stash funds.",
			"RateLimited" => "Purchase rate-limited. Wait briefly and refresh.",
			"ServiceUnavailable" => "This service is disabled by the server.",
			"PaymentSourceNotServerBacked" => "Server payment source is not stash-backed.",
			"PurchasePersistenceDisabled" => "Server purchase persistence is disabled.",
			"PurchaseQuoteChanged" => "Price changed on the server. Review the updated quote and confirm again.",
			"PurchaseCurrencyMismatch" => "Currency changed on the server. Refresh and confirm again.",
			"InvalidPaymentCurrency" => "Server currency is invalid. Select RUB, USD, or EUR in the dashboard.",
			"ProfileSessionChanged" => "Backend profile changed; reopen the page.",
			_ => $"Purchase denied: {reason}"
		};
	}

	private static ServiceDescriptor GetDescriptor(ESupportType type)
	{
		foreach (ServiceDescriptor descriptor in s_services)
		{
			if (descriptor.Type == type)
			{
				return descriptor;
			}
		}

		return s_services[0];
	}

	private static int GetPrice(RaidOpsFireSupportServerConfig snapshot, string key)
	{
		return snapshot?.Prices != null && snapshot.Prices.TryGetValue(key, out int value)
			? Math.Max(0, value)
			: -1;
	}

	private static bool GetEnabled(RaidOpsFireSupportServerConfig snapshot, string key)
	{
		return snapshot?.Enabled != null &&
		       snapshot.Enabled.TryGetValue(key, out bool enabled) &&
		       enabled;
	}

	private static int GetOwned(RaidOpsFireSupportServerConfig snapshot, string key)
	{
		return snapshot?.Authorizations != null &&
		       snapshot.Authorizations.TryGetValue(key, out int count)
			? Math.Max(0, count)
			: 0;
	}

	private static int GetMaximum(RaidOpsFireSupportServerConfig snapshot)
	{
		return Math.Max(0, snapshot?.PurchasePersistence?.MaxStoredAuthorizationsPerService ?? 0);
	}

	private static GameObject CreatePanel(Transform parent, string name, Color color)
	{
		GameObject panel = new(name, typeof(RectTransform), typeof(Image));
		panel.transform.SetParent(parent, false);
		Image image = panel.GetComponent<Image>();
		image.color = color;
		image.raycastTarget = false;
		return panel;
	}

	private static GameObject CreateBorderedPanel(
		Transform parent,
		string name,
		Color color,
		Color border)
	{
		GameObject panel = CreatePanel(parent, name, color);
		Outline outline = panel.AddComponent<Outline>();
		outline.effectColor = border;
		outline.effectDistance = new Vector2(1f, -1f);
		outline.useGraphicAlpha = false;
		return panel;
	}

	private static Text CreateText(
		Transform parent,
		string name,
		string value,
		int fontSize,
		FontStyle style,
		Color color,
		TextAnchor alignment,
		Vector2 anchor,
		Vector2 size,
		Vector2 position)
	{
		GameObject textObject = new(name, typeof(RectTransform), typeof(Text));
		textObject.transform.SetParent(parent, false);
		Text text = textObject.GetComponent<Text>();
		text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
		text.fontSize = fontSize;
		text.fontStyle = style;
		text.color = color;
		text.alignment = alignment;
		text.text = value;
		text.raycastTarget = false;
		SetRect(textObject.GetComponent<RectTransform>(), anchor, size, position);
		return text;
	}

	private static Button CreateButton(
		Transform parent,
		string name,
		string label,
		Vector2 anchor,
		Vector2 size,
		Vector2 position,
		Action onClick,
		ButtonVisual visual = ButtonVisual.Primary)
	{
		GameObject buttonObject = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
		buttonObject.transform.SetParent(parent, false);
		SetRect(buttonObject.GetComponent<RectTransform>(), anchor, size, position);
		Image image = buttonObject.GetComponent<Image>();
		Color border;
		Color labelColor;
		if (visual == ButtonVisual.Neutral)
		{
			image.color = new Color32(21, 23, 20, 255);
			border = s_line;
			labelColor = s_text;
		}
		else
		{
			image.color = new Color32(20, 43, 27, 255);
			border = new Color(s_green.r, s_green.g, s_green.b, 0.55f);
			labelColor = s_greenHigh;
		}
		Outline outline = buttonObject.AddComponent<Outline>();
		outline.effectColor = border;
		outline.effectDistance = new Vector2(1f, -1f);
		outline.useGraphicAlpha = false;
		Button button = buttonObject.GetComponent<Button>();
		button.targetGraphic = image;
		ColorBlock colors = button.colors;
		colors.normalColor = Color.white;
		colors.highlightedColor = visual == ButtonVisual.Primary
			? new Color(1.12f, 1.12f, 1.12f, 1f)
			: new Color(1f, 0.9f, 0.72f, 1f);
		colors.pressedColor = new Color(0.72f, 0.72f, 0.68f, 1f);
		colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 0.52f);
		button.colors = colors;
		button.onClick.AddListener(() => onClick?.Invoke());
		CreateText(buttonObject.transform, "Label", label, 17, FontStyle.Bold, labelColor,
			TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), size, Vector2.zero);
		return button;
	}

	private static void SetRect(
		RectTransform rect,
		Vector2 anchor,
		Vector2 size,
		Vector2 position)
	{
		rect.anchorMin = anchor;
		rect.anchorMax = anchor;
		rect.pivot = new Vector2(0.5f, 0.5f);
		rect.sizeDelta = size;
		rect.anchoredPosition = position;
	}

	private static void Stretch(RectTransform rect)
	{
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}

	private readonly struct ServiceDescriptor
	{
		public ServiceDescriptor(ESupportType type, string configKey, string displayName)
		{
			Type = type;
			ConfigKey = configKey;
			DisplayName = displayName;
		}

		public ESupportType Type { get; }
		public string ConfigKey { get; }
		public string DisplayName { get; }
	}

	private enum ButtonVisual
	{
		Primary,
		Neutral
	}

	private sealed class RowView
	{
		public RowView(Text name, Text state, Text price, Text owned, Button select, Image background, Outline border)
		{
			Name = name;
			State = state;
			Price = price;
			Owned = owned;
			Select = select;
			Background = background;
			Border = border;
		}

		public Text Name { get; }
		public Text State { get; }
		public Text Price { get; }
		public Text Owned { get; }
		public Button Select { get; }
		public Image Background { get; }
		public Outline Border { get; }
		public bool CanPurchase { get; set; }
		public string ActionLabel { get; set; }
	}
}
