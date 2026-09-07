using EFT;
using EFT.Achievements;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.Trading;
using EFT.UI;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>The Pilot's native Services content; the plugin retains purchase recovery state.</summary>
public sealed class PilotServicesView : ServiceView
{
	internal const string PilotTraderId = "66f51f3a0000000000000a60";

	internal static bool IsPilot(Trader trader) => trader?.Id == PilotTraderId;

	public override bool CheckAvailable(Trader trader) =>
		IsPilot(trader) && MainMenuPurchaseController.ServicesEnabled;

	internal static PilotServicesView GetOrCreate(ServicesScreen screen)
	{
		PilotServicesView view = screen.GetComponentInChildren<PilotServicesView>(true);
		if (view != null) return view;
		GameObject root = new("TSC_PilotServices", typeof(RectTransform));
		root.SetActive(false);
		root.transform.SetParent(screen.RectTransform, false);
		RectTransform rect = (RectTransform)root.transform;
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = rect.offsetMax = Vector2.zero;
		return root.AddComponent<PilotServicesView>();
	}

	internal void Open(Profile profile, InventoryController inventoryController, IEftSession session)
	{
		ShowGameObject();
		MainMenuPurchaseController.OpenServices(RectTransform, profile, inventoryController, session);
	}

	public override void Show(Trader trader, Profile profile, InventoryController inventoryController,
		OfflineHealthController healthController, QuestBook quests, AchievementsBook achievements,
		ItemUiContext context, IEftSession session) => Open(profile, inventoryController, session);

	public override void Close()
	{
		MainMenuPurchaseController.CloseServices(RectTransform);
		base.Close();
	}

	private void OnDisable() => MainMenuPurchaseController.CloseServices(RectTransform);
	private void OnDestroy() => MainMenuPurchaseController.CloseServices(RectTransform);
}
