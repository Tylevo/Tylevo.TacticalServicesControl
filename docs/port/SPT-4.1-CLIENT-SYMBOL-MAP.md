# TSC SPT 4.1 Client Symbol Map

Updated: 2026-08-12

Target evidence: SPT `4.1.2`, EFT `0.16.9.5.40743`, raw
`Assembly-CSharp.dll` SHA-256
`43A539F5AD00FCCD87EE54A084D8DBE1C5F63D12F8D855C8A392D68B3A1DEAF9`,
and compile-only `hollowed.dll` SHA-256
`E40F6E470CD3C09E827900EFE98BB490920E97CAE962880DCA23DDF2A78E501C`.

This map records the exact 4.1.2 contracts now used by TSC. "Compiled" means
the source built against the pinned publicized exact-build reference. It does
not mean that a Harmony patch registered, an injected field bound, or a
feature passed in game. Runtime/manual gates remain in
`SPT-4.1-PORT-LOG.md`.

## Harmony And ModulePatch Targets

| Source patch | 4.0-era contract | Exact 4.1.2 contract used by TSC | Port status |
| --- | --- | --- | --- |
| `InputManagerUtil` | `InputManager.Create` | Public static `InputManager Create(KeyGroup[], AxisGroup[], float, bool)` | Compiled; runtime input registration pending |
| `GameWorldDisposePatch` | `GameWorld.Dispose()` | Public instance `void Dispose()` | Compiled; two-raid cleanup pending |
| `GameWorldStartPatch` | `GameWorld.OnGameStarted()` | Public instance `void OnGameStarted()` | Compiled; raid-start registration pending |
| `GesturesMenuPatch` | `GInterface472` battle-screen controller | `EftGamePlayerOwner.InitBattleUIScreen()` with the exact `EFT.UI.IBattleUIScreenController` field contract; `GesturesQuickPanel.GesturesMenu` remains available | Compiled; injected controller and optional radial-panel behavior pending |
| `HelicopterItemTransferActionsPatch` | `GetActionsClass.GetAvailableActions` / `ActionsReturnClass` / `ActionsTypesClass` | `InteractionContextHelper.GetAvailableActions(GamePlayerOwner, IInteractive) -> AvailableInteractionState`; actions are `InteractionAction` | Compiled with an exact overload target; live selection pending |
| `HelicopterItemTransferInteractionStatePatch` | `GamePlayerOwner.InteractionsChangedHandler()` with old action aliases | `GamePlayerOwner.InteractionsChangedHandler()` plus `AvailableInteractionState` | Compiled; live priority/selection pending |
| `HelicopterItemTransferPurchaseObservedPatch` | `LocalPlayer.ProcessTraderServicePurchase(ETraderServiceType)` | Same public method and enum | Compiled; cargo settlement pending |
| `HelicopterItemTransferStashFeePurchasePatch` | Old abstract quest/controller aliases | `InventoryController.TryPurchaseTraderService(ETraderServiceType, EFT.Quests.QuestController, string) -> Task<bool>` | Compiled with the exact signature; carried/stash fee paths pending |
| `HelicopterItemTransferStashFeeButtonPatch` | `TransferItemsPanel.method_1` and old injected-field names | `TransferItemsPanel.UpdateCounters()` with `____inventoryController`, `____item` (`Stash`), `____transferItemsController`, and `____transferButton` | Compiled; Harmony field binding and button state pending |
| `MainMenuPurchasePatch` | `MatchmakerPlayerControllerClass` | `MenuScreen.Show(Profile, EFT.UI.Matchmaker.MatchmakerPlayersController, ESessionMode)` | Compiled with the exact overload; menu placement pending |
| `UavDeviceClientUsableItemControllerPatch` | `ClientUsableItemController.smethod_11(ClientPlayer, string)` | `ClientUsableItemController.CreateAsync(ClientPlayer, string)`; TSC returns `ClientUsableItemController.Create(ClientPlayer, Item)` for the Uplink | Compiled; remote/client equip pending |
| `UavDeviceHandsAnimationTypePatch` | `HandsControllerClass.method_49()` | `Player.GetWeaponAnimationType(Player.AbstractHandsController)`; inspect `__0.Item` and return `PlayerAnimator.EWeaponAnimationType.Pistol` for the Uplink | Compiled; first/third-person animation pending |
| `UavDeviceSetInHandsForQuickUsePatch` | Old quick-use callback aliases | `Player.SetInHandsForQuickUse(Item, Callback<IQuickUseItem>)`, forwarded to `SetInHandsUsableItem` with a typed callback bridge | Compiled; quick-use, cancel, and prior-weapon restore pending |
| `UavDeviceSetInHandsPatch` | Old usable-controller inheritance/factory aliases | `Player.SetInHandsUsableItem(Item, Callback<IUsableItemController>)`; `UavDeviceController` derives from `Player.UsableItemController`, and `Player.ItemHandsController.smethod_1<T>(Player, Item, Delegate8)` remains the setup path | Compiled; equip/death/teardown pending |
| `UavDeviceUsableInterfaceDispatchPatch` | `GClass2970.smethod_0(Item) -> GInterface323` | `EFT.NextObservedPlayer.ObservedPlayerUsableItemController.GetObservedUsableItem(Item) -> IObservedUsableItem`; TSC implements `Initialize(GameObject)`, `UpdateData(ObservedUsableItemUpdatedData)`, and `Disable()` | Compiled; observer networking pending |

## Transfer, Service, And UI Member Migrations

| 4.0-era member | Exact 4.1.2 member used by TSC | Evidence status |
| --- | --- | --- |
| `TransferItemsController.List_0` | `TransferItemsController._transferContainers : List<Stash>` | Compiled; cargo runtime pending |
| `Profile.TraderInfo.AlreadyPurchasedServices.Contains(serviceType)` | `Profile.TraderInfo.IsServiceAlreadyPurchased(serviceType)` | Compiled; service restore after purchase/cancel pending |
| `BackendConfigSettingsClass` / nested `ServiceData` | `GlobalConfiguration` / `GlobalConfiguration.ServiceData`; `ServicesData` remains keyed by `ETraderServiceType` | Compiled; live fee quote pending |
| `InsuranceCompanyClass` | `EFT.UI.Insurance.InsuranceCompany` | Compiled |
| `StashGridClass` | `EFT.InventoryLogic.Grid` for persistent-grid enumeration | Compiled |
| `TransferItemsInRaidScreen.GClass3893` | `TransferItemsInRaidScreen.TransferItemsInRaidScreenController(Profile, InventoryController, QuestController, InsuranceCompany, TransferItemsController)` | Compiled; screen open/close pending |
| Old transfer-screen `Show` types | `Show(InventoryController, QuestController, Profile, InsuranceCompany, TransferItemsController)` | Exact static signature recorded; TSC uses the screen-controller flow |

## Private Fields And Reflection Contracts

| Source lookup | Exact 4.1.2 condition | Runtime gate |
| --- | --- | --- |
| `InputNode._children` through Harmony traversal | Protected `List<InputNode>` remains on the base input node | Confirm insertion order and removal over two raids |
| `GesturesMenu._gesturesBindPanel` | Exact private field and `GesturesBindPanel` type remain in the pinned reference | Exercise only when the optional radial workflow is enabled |
| `TransferItemsPanel` injected fields | `_inventoryController`, `_item`, `_transferItemsController`, and `_transferButton` are the exact publicized 4.1.2 names | Confirm all four Harmony injections bind during client startup |
| `TransferItemsController._transferContainers` | Exact publicized member used directly by cargo staging/verification | Confirm staging and persistent-grid movement in a live transfer |
| `Effects.dictionary_1` fallback probe | The reflection string is retained only as a best-effort named-effect probe; TSC falls back through `EffectsArray` and its built-in impact path when absent | Confirm `big_smoky_explosion` and fallback visuals; this lookup is not a compile blocker |
| Profile ID fallback names `AccountId`, `Aid`, `AID`, `Id` | Bounded, null-safe runtime reflection | Verify authenticated identity in solo and Fika roles |
| Main-menu `_playerButton` fallback | Private UI lookup used only for placement fallback | Confirm placement below Records or Character at the main menu |
| Fika/player-wrapper reflection in A-10 routing | Compiled against pinned `Fika.Core.dll` 2.4.1 | Exercise human host/client and dedicated-headless ownership paths |

## Compile-Time Alias Migrations

These are named 4.1.2 contracts used by the passing build. Internal
decompiler-only `_E...`/`_F...` names are intentionally not referenced.

| Old source alias | Exact 4.1.2 source contract |
| --- | --- |
| `TransferItemsControllerAbstractClass` | `EFT.TransferItemsController` |
| `StashItemClass` | `EFT.InventoryLogic.Stash` |
| `AbstractQuestControllerClass` | `EFT.Quests.QuestController` |
| `ActionsReturnClass` | `EFT.UI.AvailableInteractionState` |
| `ActionsTypesClass` | `EFT.UI.InteractionAction` |
| `GetActionsClass` | `EFT.InteractionContextHelper` |
| `TransferItemsInRaidScreen.GClass3893` | `EFT.UI.TransferItemsInRaidScreen.TransferItemsInRaidScreenController` |
| `GInterface177` interaction marker | `EFT.IInteractive`; `HeliCargoTransferPoint` also retains `IPhysicsTrigger` |
| `GInterface472` battle UI controller | `EFT.UI.IBattleUIScreenController` |
| `GInterface146` exit-scenario stopper | `CommonAssets.Scripts.Game.EndByExitTrigerScenario.IGame` |
| `DamageInfoStruct` | `EFT.Ballistics.DamageInfo` |
| `AmmoItemClass` | `EFT.InventoryLogic.Ammo` |
| `EftBulletClass` | `EFT.Ballistics.Shot` |
| `ItemFactoryClass` | `EFT.InventoryLogic.ItemFactory` |
| `SpecItemTemplateClass` / `SpecItemItemClass` | WTT 3.0.3 `SpecItemTemplate` / `SpecItem` |
| `MatchmakerPlayerControllerClass` | `EFT.UI.Matchmaker.MatchmakerPlayersController` |
| `NotificationManagerClass` | `EFT.Communications.NotificationManager` |
| `LayerMaskClass` | global `LayersMaskController` |
| `PoolManagerClass` | `EFT.ObjectsFactory` |
| `CameraClass` | `EFT.CameraControl.CameraManager` |

`VehicleWeapon` now creates `Ammo` through `ItemFactory` and receives
`EFT.Ballistics.Shot` from `BallisticsCalculator.CreateShot`. Direct A-10
damage construction uses `EFT.Ballistics.DamageInfo`. These paths compile but
still require solo/Fika hit and ownership acceptance tests.

## Non-Patch API Changes

- `FireSupportController.TranslateCommand`, `TranslateAxes`, and
  `ShouldLockCursor` now use the protected accessibility required by their 4.1
  base methods.
- `UavDeviceController` now uses the named 4.1 `Player.UsableItemController`
  hierarchy and setup contracts; the obsolete 4.0 controller aliases are gone.
- `UavDeviceItem` derives from WTT 3.0.3 `SpecItem` and uses
  `SpecItemTemplate`. The complete WTT install, including
  `FixPluginTypesSerialization.dll`, is required to prove that EFT creates the
  custom type at runtime.
- Two calls to `InventoryController.GetReachableItemsOfType<T>` remain
  obsolete-API warnings (`UavDeviceInventory.cs` and `FireSupportPayment.cs`).
  They do not block the port build but should be migrated separately to the
  non-alloc API.

## Build Result And Required Runtime Proof

The final clean five-project `SPT-4.1 Release` solution builds with 0 errors and
four non-blocking warnings against the pinned reference/dependency root: two
obsolete Core inventory-API calls and two regression-harness nullability
warnings. A focused standalone Core build also has 0 errors and reports 28
warnings because it includes Unity serialized-field diagnostics. The
integrated zero-dependency regression suite passes 101/101.

Before marking runtime rows complete:

1. Capture client startup logs and require every patch target to resolve
   exactly once with no type-load or Harmony field-injection errors.
2. Reach the main menu without Fika first, then complete a solo raid and a
   second consecutive raid.
3. For cargo, verify interaction selection, carried and stash fees, cancel,
   insufficient funds, successful delivery, departure, and non-extraction.
4. For phone hands, verify custom item typing, special-slot pickup, quick use,
   inventory open, equip cancellation, prior-weapon restoration, death, and
   raid teardown.
5. Exercise A-10 damage, ownership, tracers, and effect fallback in solo,
   human-host/client Fika, and dedicated-headless roles.
6. Record results and matching log hashes in `SPT-4.1-PORT-LOG.md` without
   committing private logs or profile identifiers.
