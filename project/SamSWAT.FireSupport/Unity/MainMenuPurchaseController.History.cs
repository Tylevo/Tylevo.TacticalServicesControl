using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed partial class MainMenuPurchaseController
{
	private const int HistoryRowsPerPage = 8;
	private static readonly Color s_historySelected = new Color32(172, 167, 146, 255);
	private RectTransform _servicesContent;
	private Button _servicesTab;
	private Button _historyTab;
	private RectTransform _historyContent;
	private bool _historyActive;
	private int _historyPage;
	private FireSupportPurchaseHistory _displayedHistory;
	private readonly HistoryRowView[] _historyRows = new HistoryRowView[HistoryRowsPerPage];
	private Text _historySummary;
	private Text _historyEmpty;
	private Text _historyRange;
	private Text _historyPageLabel;
	private Button _historyPrevious;
	private Button _historyNext;

	private void BuildStoreHistory()
	{
		if (_storePanel == null || _historyContent != null) return;
		_historyContent = StoreBox(_storePanel, "Purchase history", 16, 152, 1548, 450, s_panel, s_line);
		StoreText(_historyContent, "History title", "PURCHASE HISTORY", 22, s_text,
			16, 8, 500, 29, true);
		_historySummary = StoreText(_historyContent, "History summary", "Recent completed authorization purchases.",
			13, s_muted, 580, 10, 952, 25, false, TextAnchor.MiddleRight);
		StoreText(_historyContent, "History time heading", "PURCHASED (LOCAL TIME)", 12, s_muted,
			32, 43, 280, 20, true);
		StoreText(_historyContent, "History service heading", "SERVICE", 12, s_muted,
			326, 43, 670, 20, true);
		StoreText(_historyContent, "History quantity heading", "QUANTITY", 12, s_muted,
			1014, 43, 120, 20, true, TextAnchor.MiddleRight);
		StoreText(_historyContent, "History cost heading", "TOTAL PAID", 12, s_muted,
			1154, 43, 362, 20, true, TextAnchor.MiddleRight);

		for (int index = 0; index < HistoryRowsPerPage; index++)
		{
			RectTransform row = StoreBox(_historyContent, $"Purchase history row {index + 1}",
				16, 70 + index * 40, 1516, 36, s_row, Color.clear);
			Text time = StoreText(row, "Purchased time", string.Empty, 17, s_muted, 16, 3, 280, 30);
			Text service = StoreText(row, "Purchased service", string.Empty, 18, s_text, 310, 3, 670, 30, true);
			Text quantity = StoreText(row, "Purchased quantity", string.Empty, 18, s_text,
				998, 3, 120, 30, false, TextAnchor.MiddleRight);
			Text price = StoreMoneyText(row, "Purchase total", string.Empty, 19, s_amberHigh,
				1138, 3, 362, 30, TextAnchor.MiddleRight);
			_historyRows[index] = new HistoryRowView(row.gameObject, time, service, quantity, price);
			row.gameObject.SetActive(false);
		}

		_historyEmpty = StoreText(_historyContent, "History empty state", string.Empty, 21, s_muted,
			80, 128, 1388, 200, false, TextAnchor.MiddleCenter);
		StoreBox(_historyContent, "History footer divider", 16, 404, 1516, 1, s_line, Color.clear);
		_historyRange = StoreText(_historyContent, "History range", string.Empty, 13, s_muted,
			16, 413, 1040, 28);
		_historyPrevious = StoreButton(_historyContent, "Previous history page", "PREVIOUS",
			1090, 413, 130, 28, () => ChangeStoreHistoryPage(-1), false);
		_historyPageLabel = StoreText(_historyContent, "History page", string.Empty, 14, s_muted,
			1230, 413, 152, 28, true, TextAnchor.MiddleCenter);
		_historyNext = StoreButton(_historyContent, "Next history page", "NEXT",
			1392, 413, 140, 28, () => ChangeStoreHistoryPage(1), false);
		RedrawStoreHistory();
	}

	private void SelectStoreTab(bool history)
	{
		if (!CanUseServices || IsPurchaseConfirmationOpen || _purchasePending) return;
		_historyActive = history;
		RedrawStoreHistory();
	}

	private void RedrawStoreHistory()
	{
		if (_servicesContent != null) _servicesContent.gameObject.SetActive(!_historyActive);
		if (_historyContent != null) _historyContent.gameObject.SetActive(_historyActive);
		bool canSwitch = !IsPurchaseConfirmationOpen && !_purchasePending;
		SetStoreTabVisual(_servicesTab, !_historyActive, canSwitch);
		SetStoreTabVisual(_historyTab, _historyActive, canSwitch);
		if (_historyContent == null) return;

		ClearStoreHistoryRows();
		_historyPrevious.interactable = false;
		_historyNext.interactable = false;
		_historyRange.text = string.Empty;
		_historyPageLabel.text = string.Empty;
		_historySummary.text = "Recent completed authorization purchases.";

		if (!TryGetStoreHistory(out FireSupportPurchaseHistory history))
		{
			_displayedHistory = null;
			_historyPage = 0;
			_historyEmpty.gameObject.SetActive(true);
			_historyEmpty.text = _refreshPending ? "Loading purchase history..."
				: _purchasePending ? "Updating purchase history..."
				: "Purchase history is unavailable.\nREFRESH to load your recent purchases.";
			return;
		}

		if (!ReferenceEquals(_displayedHistory, history))
		{
			_displayedHistory = history;
			_historyPage = 0;
		}

		// Never imply that this bounded snapshot is the profile's lifetime total.
		int count = Math.Min(history.Entries.Count, FireSupportPurchaseHistory.MaxEntries);
		bool hasOlder = history.HasMore || history.Entries.Count > count;
		_historySummary.text = hasOlder
			? $"Latest {count} purchases returned. Older purchases are not shown."
			: "Recent completed authorization purchases.";
		_historyEmpty.gameObject.SetActive(count == 0);
		_historyEmpty.text = "No recent purchases recorded.\nCompleted authorization purchases appear here.";
		if (count == 0)
		{
			_historyPage = 0;
			_historyRange.text = "0 RECENT PURCHASES";
			return;
		}

		int pageCount = (count + HistoryRowsPerPage - 1) / HistoryRowsPerPage;
		_historyPage = Mathf.Clamp(_historyPage, 0, pageCount - 1);
		int first = _historyPage * HistoryRowsPerPage;
		int last = Math.Min(first + HistoryRowsPerPage, count);
		for (int index = first; index < last; index++)
		{
			HistoryRowView row = _historyRows[index - first];
			FireSupportPurchaseHistoryEntry entry = history.Entries[index];
			row.Root.SetActive(true);
			row.Time.text = entry == null || entry.PurchasedUtc == default
				? "TIME UNAVAILABLE"
				: entry.PurchasedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
			row.Service.text = GetHistoryServiceName(entry?.Service);
			row.Quantity.text = entry != null && entry.Quantity > 0
				? entry.Quantity.ToString("N0", CultureInfo.InvariantCulture) : "--";
			bool validPrice = entry != null && entry.Price >= 0 &&
				PaymentCurrencyInfo.TryParse(entry.Currency, out _);
			row.Price.text = validPrice
				? PaymentCurrencyInfo.Format(entry.Price, PaymentCurrencyInfo.Parse(entry.Currency))
				: "UNAVAILABLE";
			row.Price.color = validPrice ? s_amberHigh : s_muted;
		}

		_historyRange.text = $"{first + 1}-{last} OF {count} RECENT PURCHASES";
		_historyPageLabel.text = $"PAGE {_historyPage + 1} / {pageCount}";
		_historyPrevious.interactable = canSwitch && _historyPage > 0;
		_historyNext.interactable = canSwitch && _historyPage + 1 < pageCount;
	}

	private bool TryGetStoreHistory(out FireSupportPurchaseHistory history)
	{
		history = null;
		if (!_ready || _refreshPending || _purchasePending || _snapshot == null ||
			!_snapshot.PlayerStateIncluded || string.IsNullOrWhiteSpace(_profileId) ||
			!FireSupportServerConfigClient.IsAuthenticatedProfile(_profileId) ||
			string.IsNullOrWhiteSpace(_sessionKey) ||
			!string.Equals(_sessionKey, FireSupportServerConfigClient.GetAuthenticatedSessionKey(), StringComparison.Ordinal))
			return false;

		FireSupportPurchaseHistory current = _snapshot.PurchaseHistory;
		if (current?.Entries == null ||
			!string.Equals(current.ProfileId, _profileId, StringComparison.Ordinal)) return false;
		history = current;
		return true;
	}

	private void ChangeStoreHistoryPage(int direction)
	{
		if (!CanUseServices || !_historyActive || IsPurchaseConfirmationOpen || _purchasePending ||
			!TryGetStoreHistory(out _)) return;
		_historyPage += direction < 0 ? -1 : 1;
		RedrawStoreHistory();
	}

	private void ResetStoreHistory()
	{
		_historyActive = false;
		_historyPage = 0;
		_displayedHistory = null;
		ClearStoreHistoryRows();
		if (_historyContent != null) _historyContent.gameObject.SetActive(false);
		if (_servicesContent != null) _servicesContent.gameObject.SetActive(true);
		if (_historySummary != null) _historySummary.text = string.Empty;
		if (_historyEmpty != null) _historyEmpty.text = string.Empty;
		if (_historyRange != null) _historyRange.text = string.Empty;
		if (_historyPageLabel != null) _historyPageLabel.text = string.Empty;
		if (_historyPrevious != null) _historyPrevious.interactable = false;
		if (_historyNext != null) _historyNext.interactable = false;
		SetStoreTabVisual(_servicesTab, true, false);
		SetStoreTabVisual(_historyTab, false, false);
	}

	private void ClearStoreHistoryRows()
	{
		foreach (HistoryRowView row in _historyRows)
		{
			if (row?.Root == null) continue;
			row.Root.SetActive(false);
			row.Time.text = string.Empty;
			row.Service.text = string.Empty;
			row.Quantity.text = string.Empty;
			row.Price.text = string.Empty;
		}
	}

	private static string GetHistoryServiceName(string serviceKey)
	{
		foreach (ServiceDescriptor service in s_services)
			if (string.Equals(service.ConfigKey, serviceKey, StringComparison.OrdinalIgnoreCase))
				return service.DisplayName;
		return "UNKNOWN SERVICE";
	}

	private static void SetStoreTabVisual(Button button, bool selected, bool interactable)
	{
		if (button == null) return;
		button.interactable = interactable;
		Image background = button.GetComponent<Image>();
		if (background != null) background.color = selected ? s_historySelected : new Color32(23, 26, 24, 180);
		PilotServicesBorder outline = button.GetComponent<PilotServicesBorder>();
		if (outline != null) outline.effectColor = selected ? s_historySelected : s_line;
		Text label = button.GetComponentInChildren<Text>();
		if (label != null) label.color = selected ? new Color32(23, 26, 24, 255) : s_muted;
	}

	private sealed class HistoryRowView
	{
		public HistoryRowView(GameObject root, Text time, Text service, Text quantity, Text price)
		{
			Root = root;
			Time = time;
			Service = service;
			Quantity = quantity;
			Price = price;
		}

		public GameObject Root { get; }
		public Text Time { get; }
		public Text Service { get; }
		public Text Quantity { get; }
		public Text Price { get; }
	}
}
