using BepInEx.Configuration;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded;

internal enum UavRadarPalette
{
	SourceWhite,
	CyanRecon,
	MintGlass,
	WhiteDrone,
	SoftLime,
	IceBlue
}

internal static class PluginSettings
{
	private static ConfigDescription HiddenDescription(string description, AcceptableValueBase acceptableValues = null)
	{
		return new ConfigDescription(description, acceptableValues);
	}

	internal static ConfigEntry<bool> Enabled { get; private set; }
	internal static ConfigEntry<int> AmountOfStrafeRequests { get; private set; }
	internal static ConfigEntry<int> AmountOfExtractionRequests { get; private set; }
	internal static ConfigEntry<int> AmountOfUavRequests { get; private set; }
	internal static ConfigEntry<PaymentMode> PaymentMode { get; private set; }
	internal static ConfigEntry<bool> UseServerConfigUrl { get; private set; }
	internal static ConfigEntry<string> ServerConfigUrl { get; private set; }
	internal static ConfigEntry<string> ServerConfigAuthToken { get; private set; }
	internal static ConfigEntry<bool> RequireServerConfigInFika { get; private set; }
	internal static ConfigEntry<int> ServerConfigRefreshSeconds { get; private set; }
	internal static ConfigEntry<PaymentSource> PaymentSource { get; private set; }
	internal static ConfigEntry<int> StrafeRequestCostRoubles { get; private set; }
	internal static ConfigEntry<int> DoubleStrafeRequestCostRoubles { get; private set; }
	internal static ConfigEntry<int> ExtractionRequestCostRoubles { get; private set; }
	internal static ConfigEntry<int> PriorityExfilRequestCostRoubles { get; private set; }
	internal static ConfigEntry<int> UavRequestCostRoubles { get; private set; }
	internal static ConfigEntry<int> FocusedSweepRequestCostRoubles { get; private set; }
	internal static ConfigEntry<bool> EnablePriorityExfil { get; private set; }
	internal static ConfigEntry<bool> EnableDoublePass { get; private set; }
	internal static ConfigEntry<bool> EnableFocusedSweep { get; private set; }
	internal static ConfigEntry<float> DoubleStrafeSecondPassDelay { get; private set; }
	internal static ConfigEntry<A10HeadlessFikaMode> A10FikaHeadlessMode { get; private set; }
	internal static ConfigEntry<float> A10HeadlessDamageOriginDistance { get; private set; }
	internal static ConfigEntry<float> A10HeadlessDamageOriginAltitude { get; private set; }
	internal static ConfigEntry<A10ProjectileOwnerMode> A10HeadlessProjectileOwnerMode { get; private set; }
	internal static ConfigEntry<bool> EnableA10ClientVisualPrediction { get; private set; }
	internal static ConfigEntry<bool> EnableA10HeadlessDirectDamageFallback { get; private set; }
	internal static ConfigEntry<bool> A10HeadlessAllowRequesterSelfDamage { get; private set; }
	private static ConfigEntry<bool> A10HeadlessRequesterSelfDamageDefaultMigrated { get; set; }
	internal static ConfigEntry<int> UavDurationSeconds { get; private set; }
	internal static ConfigEntry<float> UavScanInterval { get; private set; }
	internal static ConfigEntry<float> UavRangeMeters { get; private set; }
	internal static ConfigEntry<int> FocusedSweepDurationSeconds { get; private set; }
	internal static ConfigEntry<float> FocusedSweepScanInterval { get; private set; }
	internal static ConfigEntry<float> FocusedSweepRangeMeters { get; private set; }
	internal static ConfigEntry<UavRadarPalette> UavRadarPalette { get; private set; }
	internal static ConfigEntry<KeyboardShortcut> OpenUplinkKey { get; private set; }
	internal static ConfigEntry<KeyboardShortcut> OpenDeployKey { get; private set; }
	internal static ConfigEntry<KeyboardShortcut> SpotterConfirmKey { get; private set; }
	internal static ConfigEntry<bool> PhoneAutoZoomEnabled { get; private set; }
	internal static ConfigEntry<float> PhoneZoomFov { get; private set; }
	internal static ConfigEntry<float> PhoneZoomVerticalFraming { get; private set; }
	internal static ConfigEntry<float> PhoneZoomHorizontalFraming { get; private set; }
	private static ConfigEntry<bool> PhoneFramingDefaultsMigrated { get; set; }
	private static ConfigEntry<bool> PhoneHorizontalDefaultMigrated { get; set; }
	private static ConfigEntry<bool> PhoneZoomFovDefaultMigrated { get; set; }
	internal static ConfigEntry<float> PhoneDeployPoseNormalizedTime { get; private set; }
	internal static ConfigEntry<bool> PhoneDeployHideRightHand { get; private set; }
	internal static ConfigEntry<bool> LegacyRadialEnabled { get; private set; }
	internal static ConfigEntry<bool> PhoneForceOpaqueLcdDebug { get; private set; }
	internal static ConfigEntry<float> PhoneLcdBackgroundCleanupStrength { get; private set; }
	internal static ConfigEntry<float> PhoneConfirmPortraitTextureDelaySeconds { get; private set; }
	internal static ConfigEntry<float> PhoneConfirmSwipeSpeedMultiplier { get; private set; }
	internal static ConfigEntry<float> PhoneConfirmSwipeStartNormalizedTime { get; private set; }
	internal static ConfigEntry<float> PhoneConfirmSwipeCommitNormalizedTime { get; private set; }
	internal static ConfigEntry<bool> PhoneConfirmPauseAtCommit { get; private set; }
	internal static ConfigEntry<float> PhoneConfirmOutroSpeedMultiplier { get; private set; }
	internal static ConfigEntry<float> PhoneAuthorizingDisplaySeconds { get; private set; }
	internal static ConfigEntry<float> PhoneAuthorizedDisplaySeconds { get; private set; }
	internal static ConfigEntry<float> PhoneDeniedDisplaySeconds { get; private set; }
	internal static ConfigEntry<float> PhoneRestoreAfterAuthorizedSeconds { get; private set; }
	internal static ConfigEntry<bool> UavActivationDeviceAnimation { get; private set; }
	internal static ConfigEntry<bool> UavWristPhoneVisual { get; private set; }
	internal static ConfigEntry<bool> UavWristPhoneArmPose { get; private set; }
	internal static ConfigEntry<bool> UavA10LoiterEnabled { get; private set; }
	internal static ConfigEntry<float> UavA10LoiterRadius { get; private set; }
	internal static ConfigEntry<float> UavA10LoiterAltitude { get; private set; }
	internal static ConfigEntry<float> UavA10LoiterOrbitPeriod { get; private set; }
	internal static ConfigEntry<float> UavA10LoiterIngressDuration { get; private set; }
	internal static ConfigEntry<float> UavA10LoiterIngressDistance { get; private set; }
	internal static ConfigEntry<float> UavA10LoiterEngineVolume { get; private set; }
	internal static ConfigEntry<float> UavA10LoiterModelPitchOffset { get; private set; }
	internal static ConfigEntry<float> UavA10LoiterModelYawOffset { get; private set; }
	internal static ConfigEntry<float> UavA10LoiterModelRollOffset { get; private set; }
	internal static ConfigEntry<int> HelicopterWaitTime { get; private set; }
	internal static ConfigEntry<int> PriorityExfilHelicopterWaitTime { get; private set; }
	internal static ConfigEntry<float> PriorityExfilDispatchDelay { get; private set; }
	internal static ConfigEntry<float> HelicopterExtractTime { get; private set; }
	internal static ConfigEntry<float> HelicopterSpeedMultiplier { get; private set; }
	internal static ConfigEntry<float> PriorityExfilHelicopterSpeedMultiplier { get; private set; }
	internal static ConfigEntry<int> RequestCooldown { get; private set; }
	internal static ConfigEntry<int> VoiceoverVolume { get; private set; }
	internal static ConfigEntry<bool> VerbosePhoneLogs { get; private set; }
	internal static ConfigEntry<bool> VerboseLcdLogs { get; private set; }
	internal static ConfigEntry<bool> VerboseFikaLogs { get; private set; }
	internal static ConfigEntry<bool> VerboseDashboardLogs { get; private set; }
	internal static ConfigEntry<bool> VerbosePaymentLogs { get; private set; }
	private static ConfigEntry<string> DashboardConfigInfo { get; set; }

	internal static void Initialize(ConfigFile config)
	{
		Enabled = config.Bind(
			"",
			"Plugin state",
			true,
			HiddenDescription("Enables/disables plugin"));

		AmountOfStrafeRequests = config.Bind(
			"Main Settings",
			"Amount of autocannon strafe requests",
			2,
			HiddenDescription("",
				new AcceptableValueRange<int>(0, 10)));
		AmountOfExtractionRequests = config.Bind(
			"Main Settings",
			"Amount of helicopter extraction requests",
			1,
			HiddenDescription("",
				new AcceptableValueRange<int>(0, 10)));
		AmountOfUavRequests = config.Bind(
			"Main Settings",
			"Amount of UAV recon requests",
			1,
			HiddenDescription("",
				new AcceptableValueRange<int>(0, 10)));
		PaymentMode = config.Bind(
			"Main Settings",
			"Payment mode",
			global::SamSWAT.FireSupport.ArysReloaded.Unity.PaymentMode.PhoneAuthorizations,
			HiddenDescription("DirectRadial charges when deploying from the YY menu. PhoneAuthorizations requires prepaid TerraGroup phone authorizations. Hybrid consumes a phone authorization when available, otherwise charges from YY."));
		UseServerConfigUrl = config.Bind(
			"TSC Server Config",
			"Use server config URL",
			true,
			new ConfigDescription("Load TSC prices, payment source, stash balance, and service settings from the local TSC server config URL at runtime."));
		ServerConfigUrl = config.Bind(
			"TSC Server Config",
			"Server config URL",
			"https://127.0.0.1:6969/tsc",
			new ConfigDescription("Base HTTP or HTTPS URL for the TSC server config endpoint. Only use trusted local/LAN/VPN hosts; do not expose the dashboard or token route to the public internet."));
		ServerConfigAuthToken = config.Bind(
			"TSC Server Config",
			"Server config auth token",
			string.Empty,
			new ConfigDescription("Optional bearer token sent to TSC server config and purchase endpoints. This token is sent over the configured URL, so prefer localhost, LAN, VPN, or HTTPS."));
		RequireServerConfigInFika = config.Bind(
			"TSC Server Config",
			"Require server config in Fika",
			true,
			new ConfigDescription("Blocks TerraGroup phone purchases when server config URL is enabled but unavailable."));
		ServerConfigRefreshSeconds = config.Bind(
			"TSC Server Config",
			"Server config refresh seconds",
			60,
			new ConfigDescription("Seconds between TSC server config refresh attempts while in a raid. Higher values reduce server request logging.",
				new AcceptableValueRange<int>(0, 3600)));
		PaymentSource = config.Bind(
			"TerraGroup Payment",
			"Payment source",
			global::SamSWAT.FireSupport.ArysReloaded.Unity.PaymentSource.CarriedRoubles,
			new ConfigDescription("Rouble wallet used for TerraGroup phone purchases."));
		RequestCooldown = config.Bind(
			"Main Settings",
			"Cooldown between support requests",
			300,
			HiddenDescription("Seconds",
				new AcceptableValueRange<int>(60, 3600)));
		StrafeRequestCostRoubles = config.Bind(
			"Main Settings",
			"Autocannon strafe cost",
			250000,
			new ConfigDescription("Carried roubles required to request an A-10 autocannon strafe",
				new AcceptableValueRange<int>(0, 10000000)));
		DoubleStrafeRequestCostRoubles = config.Bind(
			"Main Settings",
			"A-10 double pass cost",
			450000,
			new ConfigDescription("Carried roubles required to request two A-10 autocannon passes on the same target",
				new AcceptableValueRange<int>(0, 10000000)));
		ExtractionRequestCostRoubles = config.Bind(
			"Main Settings",
			"Helicopter extraction cost",
			300000,
			new ConfigDescription("Carried roubles required to request a UH-60 extraction",
				new AcceptableValueRange<int>(0, 10000000)));
		PriorityExfilRequestCostRoubles = config.Bind(
			"Main Settings",
			"Priority exfil cost",
			450000,
			new ConfigDescription("Carried roubles required to request an expedited UH-60 extraction authorization",
				new AcceptableValueRange<int>(0, 10000000)));
		UavRequestCostRoubles = config.Bind(
			"Main Settings",
			"UAV recon cost",
			125000,
			new ConfigDescription("Carried roubles required to request a timed UAV recon scan",
				new AcceptableValueRange<int>(0, 10000000)));
		FocusedSweepRequestCostRoubles = config.Bind(
			"Main Settings",
			"Focused sweep cost",
			90000,
			new ConfigDescription("Carried roubles required to request a shorter, narrower, faster-refresh UAV sweep",
				new AcceptableValueRange<int>(0, 10000000)));
		EnablePriorityExfil = config.Bind(
			"Main Settings",
			"Enable Priority Exfil",
			true,
			HiddenDescription("Allows TerraGroup phone purchases for the expedited UH-60 extraction authorization"));
		EnableDoublePass = config.Bind(
			"Main Settings",
			"Enable A-10 Double Pass",
			true,
			HiddenDescription("Allows TerraGroup phone purchases for the two-pass A-10 authorization"));
		EnableFocusedSweep = config.Bind(
			"Main Settings",
			"Enable Focused Sweep",
			true,
			HiddenDescription("Allows TerraGroup phone purchases for the short-range fast-refresh UAV sweep authorization"));
		DoubleStrafeSecondPassDelay = config.Bind(
			"Main Settings",
			"A-10 double pass second pass delay",
			14f,
			HiddenDescription("Seconds between the first and second A-10 passes",
				new AcceptableValueRange<float>(6f, 45f)));
		A10FikaHeadlessMode = config.Bind(
			"A-10 Headless Authority",
			"A-10 Fika headless mode",
			A10HeadlessFikaMode.ExperimentalDamageOnly,
			HiddenDescription("Experimental Fika headless A-10 authority mode. Disabled rejects/refunds headless A-10 damage requests instead of attempting unsupported damage."));
		A10HeadlessDamageOriginDistance = config.Bind(
			"A-10 Headless Authority",
			"A-10 headless damage origin distance",
			425f,
			HiddenDescription("Meters from the marked target used for experimental headless authoritative GAU-8 projectile origin.",
				new AcceptableValueRange<float>(350f, 500f)));
		A10HeadlessDamageOriginAltitude = config.Bind(
			"A-10 Headless Authority",
			"A-10 headless damage origin altitude",
			150f,
			HiddenDescription("Meters above the marked target used for experimental headless authoritative GAU-8 projectile origin.",
				new AcceptableValueRange<float>(120f, 180f)));
		A10HeadlessProjectileOwnerMode = config.Bind(
			"A-10 Headless Authority",
			"A-10 headless projectile owner mode",
			A10ProjectileOwnerMode.RequesterProfile,
			HiddenDescription("Experimental. Separates requester/attribution from the projectile owner used by the headless damage executor."));
		EnableA10ClientVisualPrediction = config.Bind(
			"A-10 Headless Authority",
			"Enable A-10 client visual prediction",
			false,
			HiddenDescription("When false, Fika clients wait for the host/headless authority broadcast before playing A-10 visuals. This avoids seeing local flyovers when the host rejects or fails the strike."));
		EnableA10HeadlessDirectDamageFallback = config.Bind(
			"A-10 Headless Authority",
			"Enable A-10 headless direct damage fallback",
			true,
			HiddenDescription("Allows the Fika headless authority path to use a safe direct damage fallback if EFT exposes a supported API."));
		A10HeadlessAllowRequesterSelfDamage = config.Bind(
			"A-10 Headless Authority",
			"A-10 headless allow requester self damage",
			true,
			HiddenDescription("Allows the Fika-headless hit accounting/fallback path to include the requester as a damage candidate. Defaults to true to match original Arys/non-headless behavior where callers can die to their own strike."));
		A10HeadlessRequesterSelfDamageDefaultMigrated = config.Bind(
			"A-10 Headless Authority",
			"A-10 headless requester self damage default migrated",
			false,
			HiddenDescription("Internal migration flag for changing the experimental headless self-damage default to match original Arys behavior."));
		MigrateA10HeadlessRequesterSelfDamageDefault();

		UavDurationSeconds = config.Bind(
			"UAV Recon Settings",
			"UAV duration",
			45,
			HiddenDescription("How long the UAV radar overlay stays active (seconds)",
				new AcceptableValueRange<int>(5, 300)));
		UavScanInterval = config.Bind(
			"UAV Recon Settings",
			"UAV scan interval",
			1f,
			HiddenDescription("How often the UAV refreshes target positions (seconds)",
				new AcceptableValueRange<float>(0.1f, 10f)));
		UavRangeMeters = config.Bind(
			"UAV Recon Settings",
			"UAV range",
			200f,
			HiddenDescription("Maximum radar display range in meters",
				new AcceptableValueRange<float>(25f, 1000f)));
		FocusedSweepDurationSeconds = config.Bind(
			"UAV Recon Settings",
			"Focused sweep duration",
			30,
			HiddenDescription("How long the focused UAV sweep radar overlay stays active (seconds)",
				new AcceptableValueRange<int>(5, 300)));
		FocusedSweepScanInterval = config.Bind(
			"UAV Recon Settings",
			"Focused sweep scan interval",
			0.5f,
			HiddenDescription("How often the focused sweep refreshes target positions (seconds)",
				new AcceptableValueRange<float>(0.1f, 10f)));
		FocusedSweepRangeMeters = config.Bind(
			"UAV Recon Settings",
			"Focused sweep range",
			100f,
			HiddenDescription("Maximum focused sweep display range in meters",
				new AcceptableValueRange<float>(25f, 1000f)));
		UavRadarPalette = config.Bind(
			"UAV Recon Settings",
			"UAV radar palette",
			global::SamSWAT.FireSupport.ArysReloaded.UavRadarPalette.MintGlass,
			new ConfigDescription("Color palette used by the UAV radar overlay"));

		OpenUplinkKey = config.Bind(
			"TerraGroup Phone",
			"Open uplink key",
			new KeyboardShortcut(KeyCode.U),
			HiddenDescription("Equips the carried TerraGroup TSC Uplink to purchase TerraGroup support authorizations"));
		OpenDeployKey = config.Bind(
			"TerraGroup Phone",
			"Open deploy key",
			new KeyboardShortcut(KeyCode.K),
			HiddenDescription("Equips the carried TerraGroup TSC Uplink in deploy mode to use purchased support authorizations"));
		SpotterConfirmKey = config.Bind(
			"TerraGroup Phone",
			"Spotter confirm key",
			new KeyboardShortcut(KeyCode.Mouse2),
			HiddenDescription("Confirms spotter targeting steps (position, direction). LMB also confirms while the rangefinder is in hands, but with a weapon out it would fire, so this key is the safe confirm. Enter always works too."));
		PhoneAutoZoomEnabled = config.Bind(
			"TerraGroup Phone",
			"Automatic phone zoom",
			true,
			new ConfigDescription("Temporarily narrows the camera FOV while the local TerraGroup TSC Uplink is in your hands, then restores the previous FOV when it is stowed."));
		PhoneZoomFov = config.Bind(
			"TerraGroup Phone",
			"Phone zoom FOV",
			45f,
			new ConfigDescription(
				"Camera FOV used while the TerraGroup TSC Uplink is raised. Lower values make the phone appear larger. Applies the next time the phone is raised.",
				new AcceptableValueRange<float>(20f, 75f)));
		PhoneZoomVerticalFraming = config.Bind(
			"TerraGroup Phone",
			"Phone vertical framing",
			0.09f,
			new ConfigDescription(
				"Raises or lowers the first-person phone while automatic phone zoom is active. Positive values raise the phone toward screen center; negative values lower it.",
				new AcceptableValueRange<float>(-0.25f, 0.25f)));
		PhoneZoomHorizontalFraming = config.Bind(
			"TerraGroup Phone",
			"Phone horizontal framing",
			-0.004f,
			new ConfigDescription(
				"Moves the first-person phone horizontally while automatic phone zoom is active. Positive values move the phone right; negative values move it left.",
				new AcceptableValueRange<float>(-0.15f, 0.15f)));
		PhoneFramingDefaultsMigrated = config.Bind(
			"Internal",
			"Phone framing defaults migrated",
			false,
			HiddenDescription("Internal migration flag for the less aggressive phone framing defaults."));
		MigratePhoneFramingDefaults();
		PhoneHorizontalDefaultMigrated = config.Bind(
			"Internal",
			"Phone horizontal default migrated",
			false,
			HiddenDescription("Internal migration flag for the centered phone horizontal framing default."));
		MigratePhoneHorizontalDefault();
		PhoneZoomFovDefaultMigrated = config.Bind(
			"Internal",
			"Phone zoom FOV default migrated",
			false,
			HiddenDescription("Internal migration flag for the wider phone zoom FOV default."));
		MigratePhoneZoomFovDefault();
		// Intentionally left visible in the F12 config manager for live pose
		// tuning: adjust, reopen the deploy phone, and the new freeze frame
		// applies immediately.
		PhoneDeployPoseNormalizedTime = config.Bind(
			"TerraGroup Phone Animation",
			"Deploy pose normalized time",
			0.24f,
			new ConfigDescription(
				"Outro animation normalizedTime where the deploy phone freezes. Lower values freeze earlier (hand lower on the screen); the payment swipe pose is at 0.36.",
				new AcceptableValueRange<float>(0.05f, 0.8f)));
		PhoneDeployHideRightHand = config.Bind(
			"TerraGroup Phone Animation",
			"Deploy hide right hand",
			true,
			new ConfigDescription(
				"Hides the free right hand while the deploy selector is held. The swipe animation has no natural resting pose for it, so it otherwise hovers next to the phone."));
		LegacyRadialEnabled = config.Bind(
			"Legacy",
			"Enable legacy YY radial",
			false,
			HiddenDescription("Shows the old YY gesture-wheel fire support radial and its rangefinder flow. The TSC Uplink deploy phone is the primary flow."));
		PhoneForceOpaqueLcdDebug = config.Bind(
			"TerraGroup Phone",
			"Force Opaque LCD Debug",
			false,
			HiddenDescription("Forces the TerraGroup phone screen to use a flat opaque LCD material with no glass/reflection effects"));
		PhoneLcdBackgroundCleanupStrength = config.Bind(
			"TerraGroup Phone",
			"LCD background cleanup strength",
			0.9f,
			HiddenDescription("Darkens low-contrast TerraGroup phone background pixels so the LCD does not look transparent",
				new AcceptableValueRange<float>(0f, 1f)));
		PhoneConfirmPortraitTextureDelaySeconds = config.Bind(
			"TerraGroup Phone Animation",
			"Confirm portrait texture delay seconds",
			0.45f,
			HiddenDescription("Seconds after Enter before the portrait confirm texture can replace the review screen",
				new AcceptableValueRange<float>(0f, 3f)));
		PhoneConfirmSwipeSpeedMultiplier = config.Bind(
			"TerraGroup Phone Animation",
			"Confirm swipe speed multiplier",
			1.4f,
			HiddenDescription("Animator speed applied only during the vertical confirmation hand swipe",
				new AcceptableValueRange<float>(0.25f, 4f)));
		PhoneConfirmSwipeStartNormalizedTime = config.Bind(
			"TerraGroup Phone Animation",
			"Confirm swipe start normalized time",
			0.36f,
			HiddenDescription("Outro animation normalizedTime where the accelerated swipe section begins",
				new AcceptableValueRange<float>(0f, 1f)));
		PhoneConfirmSwipeCommitNormalizedTime = config.Bind(
			"TerraGroup Phone Animation",
			"Confirm swipe commit normalized time",
			0.78f,
			HiddenDescription("Outro animation normalizedTime where payment is attempted and the authorizing screen appears",
				new AcceptableValueRange<float>(0f, 1f)));
		PhoneConfirmPauseAtCommit = config.Bind(
			"TerraGroup Phone Animation",
			"Confirm pause at commit",
			true,
			HiddenDescription("Pauses the phone animator at the payment commit while authorizing/result screens are shown"));
		PhoneConfirmOutroSpeedMultiplier = config.Bind(
			"TerraGroup Phone Animation",
			"Confirm outro speed multiplier",
			1.6f,
			HiddenDescription("Animator speed used after the result screen when the phone finishes its outro",
				new AcceptableValueRange<float>(0.25f, 4f)));
		PhoneAuthorizingDisplaySeconds = config.Bind(
			"TerraGroup Phone Animation",
			"Authorizing display seconds",
			0.25f,
			HiddenDescription("Seconds to keep the authorizing screen visible after the payment commit",
				new AcceptableValueRange<float>(0.1f, 5f)));
		PhoneAuthorizedDisplaySeconds = config.Bind(
			"TerraGroup Phone Animation",
			"Authorized display seconds",
			0.4f,
			HiddenDescription("Seconds to keep the authorized result screen visible",
				new AcceptableValueRange<float>(0.1f, 5f)));
		PhoneDeniedDisplaySeconds = config.Bind(
			"TerraGroup Phone Animation",
			"Denied display seconds",
			0.85f,
			HiddenDescription("Seconds to keep the denied result screen visible",
				new AcceptableValueRange<float>(0.1f, 5f)));
		PhoneRestoreAfterAuthorizedSeconds = config.Bind(
			"TerraGroup Phone Animation",
			"Restore after authorized seconds",
			0f,
			HiddenDescription("Extra pause after the result screen before resuming the phone outro and restoring the previous weapon",
				new AcceptableValueRange<float>(0f, 3f)));

		UavActivationDeviceAnimation = config.Bind(
			"UAV Recon Settings",
			"UAV activation device animation",
			true,
			HiddenDescription("Briefly equips the TerraGroup TSC Uplink and taps it before the radar appears"));
		UavWristPhoneVisual = config.Bind(
			"UAV Recon Settings",
			"UAV wrist phone visual",
			true,
			HiddenDescription("Shows a local native Tarkov phone with fake tap animation when UAV recon starts"));
		UavWristPhoneArmPose = config.Bind(
			"UAV Recon Settings",
			"UAV wrist phone arm pose",
			true,
			HiddenDescription("Temporarily blends the left forearm/hand toward a watch-check pose while the UAV phone visual plays"));
		UavA10LoiterEnabled = config.Bind(
			"UAV Recon Settings",
			"UAV A-10 loiter visual",
			true,
			HiddenDescription("Shows a local cosmetic A-10 orbiting above the UAV scan area"));
		UavA10LoiterRadius = config.Bind(
			"UAV Recon Settings",
			"UAV A-10 loiter radius",
			420f,
			HiddenDescription("A-10 orbit radius in meters",
				new AcceptableValueRange<float>(100f, 2000f)));
		UavA10LoiterAltitude = config.Bind(
			"UAV Recon Settings",
			"UAV A-10 loiter altitude",
			360f,
			HiddenDescription("A-10 height above the UAV center in meters",
				new AcceptableValueRange<float>(100f, 1500f)));
		UavA10LoiterOrbitPeriod = config.Bind(
			"UAV Recon Settings",
			"UAV A-10 orbit period",
			34f,
			HiddenDescription("Seconds for one full A-10 orbit",
				new AcceptableValueRange<float>(10f, 180f)));
		UavA10LoiterIngressDuration = config.Bind(
			"UAV Recon Settings",
			"UAV aircraft ingress duration",
			7f,
			HiddenDescription("Seconds the UAV aircraft spends flying into the orbit before circling",
				new AcceptableValueRange<float>(0f, 30f)));
		UavA10LoiterIngressDistance = config.Bind(
			"UAV Recon Settings",
			"UAV aircraft ingress distance",
			900f,
			HiddenDescription("Meters behind the orbit entry point where the UAV aircraft spawns",
				new AcceptableValueRange<float>(0f, 3000f)));
		UavA10LoiterEngineVolume = config.Bind(
			"UAV Recon Settings",
			"UAV A-10 engine volume",
			0.45f,
			HiddenDescription("Local A-10 engine audio volume",
				new AcceptableValueRange<float>(0f, 1f)));
		UavA10LoiterModelPitchOffset = config.Bind(
			"UAV Recon Settings",
			"UAV A-10 model pitch offset",
			0f,
			HiddenDescription("Additional cosmetic A-10 model pitch offset in degrees",
				new AcceptableValueRange<float>(-180f, 180f)));
		UavA10LoiterModelYawOffset = config.Bind(
			"UAV Recon Settings",
			"UAV A-10 model yaw offset",
			0f,
			HiddenDescription("Additional cosmetic A-10 model yaw offset in degrees",
				new AcceptableValueRange<float>(-180f, 180f)));
		UavA10LoiterModelRollOffset = config.Bind(
			"UAV Recon Settings",
			"UAV A-10 model roll offset",
			0f,
			HiddenDescription("Additional cosmetic A-10 model roll offset in degrees",
				new AcceptableValueRange<float>(-180f, 180f)));

		HelicopterWaitTime = config.Bind(
			"Helicopter Extraction Settings",
			"Helicopter wait time",
			30,
			HiddenDescription("Helicopter wait time on extraction location (seconds)",
				new AcceptableValueRange<int>(10, 300)));
		PriorityExfilHelicopterWaitTime = config.Bind(
			"Helicopter Extraction Settings",
			"Priority exfil helicopter wait time",
			20,
			HiddenDescription("Priority exfil helicopter wait time on extraction location (seconds)",
				new AcceptableValueRange<int>(5, 300)));
		PriorityExfilDispatchDelay = config.Bind(
			"Helicopter Extraction Settings",
			"Priority exfil dispatch delay",
			3f,
			HiddenDescription("Seconds before the priority exfil helicopter is dispatched after target confirmation",
				new AcceptableValueRange<float>(0f, 30f)));
		HelicopterExtractTime = config.Bind(
			"Helicopter Extraction Settings",
			"Extraction time",
			10f,
			HiddenDescription("How long you will need to stay in the exfil zone before extraction (seconds)",
				new AcceptableValueRange<float>(1f, 30f)));
		HelicopterSpeedMultiplier = config.Bind(
			"Helicopter Extraction Settings",
			"Helicopter speed multiplier",
			1f,
			new ConfigDescription("How fast the helicopter arrival animation will be played",
				new AcceptableValueRange<float>(0.8f, 1.5f)));
		PriorityExfilHelicopterSpeedMultiplier = config.Bind(
			"Helicopter Extraction Settings",
			"Priority exfil helicopter speed multiplier",
			1.35f,
			new ConfigDescription("How fast the priority exfil helicopter arrival animation will be played",
				new AcceptableValueRange<float>(0.8f, 2f)));

		VoiceoverVolume = config.Bind(
			"Sound Settings",
			"Voiceover volume",
			90,
			HiddenDescription("",
				new AcceptableValueRange<int>(0, 100)));

		VerbosePhoneLogs = config.Bind(
			"Diagnostics",
			"Verbose phone logs",
			false,
			HiddenDescription("Enables detailed TSC Uplink phone session and animation diagnostics."));
		VerboseLcdLogs = config.Bind(
			"Diagnostics",
			"Verbose LCD logs",
			false,
			HiddenDescription("Enables detailed TSC Uplink LCD renderer, alpha, and asset diagnostics."));
		VerboseFikaLogs = config.Bind(
			"Diagnostics",
			"Verbose Fika logs",
			false,
			HiddenDescription("Enables detailed TSC Fika packet and host-authority diagnostics."));
		VerboseDashboardLogs = config.Bind(
			"Diagnostics",
			"Verbose dashboard logs",
			false,
			HiddenDescription("Enables detailed TSC dashboard/server-config diagnostics."));
		VerbosePaymentLogs = config.Bind(
			"Diagnostics",
			"Verbose payment logs",
			false,
			HiddenDescription("Enables detailed TSC payment/source/balance diagnostics."));

		DashboardConfigInfo = config.Bind(
			"TSC Dashboard",
			"Configure TSC at",
			"https://127.0.0.1:6969/tsc/admin",
			new ConfigDescription("TSC server, payment, UAV, helicopter, and support pricing settings are managed by the local web dashboard. Localhost-only by default; do not port-forward it."));

		HideFileOnlySettings(config);
		SubscribeEffectiveSettingChanges();
	}

	private static void HideFileOnlySettings(ConfigFile config)
	{
		RemoveFromConfigManager(config, Enabled);
		RemoveFromConfigManager(config, AmountOfStrafeRequests);
		RemoveFromConfigManager(config, AmountOfExtractionRequests);
		RemoveFromConfigManager(config, AmountOfUavRequests);
		RemoveFromConfigManager(config, PaymentMode);
		RemoveFromConfigManager(config, UseServerConfigUrl);
		RemoveFromConfigManager(config, ServerConfigUrl);
		RemoveFromConfigManager(config, ServerConfigAuthToken);
		RemoveFromConfigManager(config, RequireServerConfigInFika);
		RemoveFromConfigManager(config, ServerConfigRefreshSeconds);
		RemoveFromConfigManager(config, PaymentSource);
		RemoveFromConfigManager(config, RequestCooldown);
		RemoveFromConfigManager(config, StrafeRequestCostRoubles);
		RemoveFromConfigManager(config, DoubleStrafeRequestCostRoubles);
		RemoveFromConfigManager(config, ExtractionRequestCostRoubles);
		RemoveFromConfigManager(config, PriorityExfilRequestCostRoubles);
		RemoveFromConfigManager(config, UavRequestCostRoubles);
		RemoveFromConfigManager(config, FocusedSweepRequestCostRoubles);
		RemoveFromConfigManager(config, EnablePriorityExfil);
		RemoveFromConfigManager(config, EnableDoublePass);
		RemoveFromConfigManager(config, EnableFocusedSweep);
		RemoveFromConfigManager(config, DoubleStrafeSecondPassDelay);
		RemoveFromConfigManager(config, A10FikaHeadlessMode);
		RemoveFromConfigManager(config, A10HeadlessDamageOriginDistance);
		RemoveFromConfigManager(config, A10HeadlessDamageOriginAltitude);
		RemoveFromConfigManager(config, A10HeadlessProjectileOwnerMode);
		RemoveFromConfigManager(config, A10HeadlessAllowRequesterSelfDamage);
		RemoveFromConfigManager(config, A10HeadlessRequesterSelfDamageDefaultMigrated);
		RemoveFromConfigManager(config, EnableA10HeadlessDirectDamageFallback);
		RemoveFromConfigManager(config, EnableA10ClientVisualPrediction);
		RemoveFromConfigManager(config, UavDurationSeconds);
		RemoveFromConfigManager(config, UavScanInterval);
		RemoveFromConfigManager(config, UavRangeMeters);
		RemoveFromConfigManager(config, FocusedSweepDurationSeconds);
		RemoveFromConfigManager(config, FocusedSweepScanInterval);
		RemoveFromConfigManager(config, FocusedSweepRangeMeters);
		RemoveFromConfigManager(config, UavRadarPalette);
		RemoveFromConfigManager(config, PhoneFramingDefaultsMigrated);
		RemoveFromConfigManager(config, PhoneHorizontalDefaultMigrated);
		RemoveFromConfigManager(config, PhoneZoomFovDefaultMigrated);
		RemoveFromConfigManager(config, LegacyRadialEnabled);
		RemoveFromConfigManager(config, PhoneForceOpaqueLcdDebug);
		RemoveFromConfigManager(config, PhoneLcdBackgroundCleanupStrength);
		RemoveFromConfigManager(config, PhoneConfirmPortraitTextureDelaySeconds);
		RemoveFromConfigManager(config, PhoneConfirmSwipeSpeedMultiplier);
		RemoveFromConfigManager(config, PhoneConfirmSwipeStartNormalizedTime);
		RemoveFromConfigManager(config, PhoneConfirmSwipeCommitNormalizedTime);
		RemoveFromConfigManager(config, PhoneConfirmPauseAtCommit);
		RemoveFromConfigManager(config, PhoneConfirmOutroSpeedMultiplier);
		RemoveFromConfigManager(config, PhoneAuthorizingDisplaySeconds);
		RemoveFromConfigManager(config, PhoneAuthorizedDisplaySeconds);
		RemoveFromConfigManager(config, PhoneDeniedDisplaySeconds);
		RemoveFromConfigManager(config, PhoneRestoreAfterAuthorizedSeconds);
		RemoveFromConfigManager(config, UavActivationDeviceAnimation);
		RemoveFromConfigManager(config, UavWristPhoneVisual);
		RemoveFromConfigManager(config, UavWristPhoneArmPose);
		RemoveFromConfigManager(config, UavA10LoiterEnabled);
		RemoveFromConfigManager(config, UavA10LoiterRadius);
		RemoveFromConfigManager(config, UavA10LoiterAltitude);
		RemoveFromConfigManager(config, UavA10LoiterOrbitPeriod);
		RemoveFromConfigManager(config, UavA10LoiterIngressDuration);
		RemoveFromConfigManager(config, UavA10LoiterIngressDistance);
		RemoveFromConfigManager(config, UavA10LoiterEngineVolume);
		RemoveFromConfigManager(config, UavA10LoiterModelPitchOffset);
		RemoveFromConfigManager(config, UavA10LoiterModelYawOffset);
		RemoveFromConfigManager(config, UavA10LoiterModelRollOffset);
		RemoveFromConfigManager(config, HelicopterWaitTime);
		RemoveFromConfigManager(config, PriorityExfilHelicopterWaitTime);
		RemoveFromConfigManager(config, PriorityExfilDispatchDelay);
		RemoveFromConfigManager(config, HelicopterExtractTime);
		RemoveFromConfigManager(config, HelicopterSpeedMultiplier);
		RemoveFromConfigManager(config, PriorityExfilHelicopterSpeedMultiplier);
		RemoveFromConfigManager(config, VoiceoverVolume);
		RemoveFromConfigManager(config, VerbosePhoneLogs);
		RemoveFromConfigManager(config, VerboseLcdLogs);
		RemoveFromConfigManager(config, VerboseFikaLogs);
		RemoveFromConfigManager(config, VerboseDashboardLogs);
		RemoveFromConfigManager(config, VerbosePaymentLogs);
	}

	private static void MigrateA10HeadlessRequesterSelfDamageDefault()
	{
		if (A10HeadlessRequesterSelfDamageDefaultMigrated?.Value == true)
		{
			return;
		}

		if (A10HeadlessAllowRequesterSelfDamage != null && !A10HeadlessAllowRequesterSelfDamage.Value)
		{
			A10HeadlessAllowRequesterSelfDamage.Value = true;
			FireSupportPlugin.LogSource?.LogInfo("TSC migrated A-10 headless requester self-damage default to enabled to match original Arys behavior.");
		}

		if (A10HeadlessRequesterSelfDamageDefaultMigrated != null)
		{
			A10HeadlessRequesterSelfDamageDefaultMigrated.Value = true;
		}
	}

	private static void MigratePhoneFramingDefaults()
	{
		if (PhoneFramingDefaultsMigrated?.Value == true)
		{
			return;
		}

		if (PhoneZoomFov != null && Mathf.Approximately(PhoneZoomFov.Value, 38f))
		{
			PhoneZoomFov.Value = 42f;
		}

		if (PhoneZoomVerticalFraming != null &&
		    Mathf.Approximately(PhoneZoomVerticalFraming.Value, 0.12f))
		{
			PhoneZoomVerticalFraming.Value = 0.09f;
		}

		if (PhoneFramingDefaultsMigrated != null)
		{
			PhoneFramingDefaultsMigrated.Value = true;
		}
	}

	private static void MigratePhoneHorizontalDefault()
	{
		if (PhoneHorizontalDefaultMigrated?.Value == true)
		{
			return;
		}

		if (PhoneZoomHorizontalFraming != null &&
		    Mathf.Approximately(PhoneZoomHorizontalFraming.Value, 0.035f))
		{
			PhoneZoomHorizontalFraming.Value = -0.004f;
		}

		if (PhoneHorizontalDefaultMigrated != null)
		{
			PhoneHorizontalDefaultMigrated.Value = true;
		}
	}

	private static void MigratePhoneZoomFovDefault()
	{
		if (PhoneZoomFovDefaultMigrated?.Value == true)
		{
			return;
		}

		if (PhoneZoomFov != null && Mathf.Approximately(PhoneZoomFov.Value, 42f))
		{
			PhoneZoomFov.Value = 45f;
		}

		if (PhoneZoomFovDefaultMigrated != null)
		{
			PhoneZoomFovDefaultMigrated.Value = true;
		}
	}

	private static void RemoveFromConfigManager(ConfigFile config, ConfigEntryBase entry)
	{
		if (config != null && entry != null)
		{
			config.Remove(entry.Definition);
		}
	}

	private static void SubscribeEffectiveSettingChanges()
	{
		TrackEffectiveSetting(PaymentMode);
		TrackEffectiveSetting(UseServerConfigUrl);
		TrackEffectiveSetting(ServerConfigUrl);
		TrackEffectiveSetting(ServerConfigAuthToken);
		TrackEffectiveSetting(RequireServerConfigInFika);
		TrackEffectiveSetting(ServerConfigRefreshSeconds);
		TrackEffectiveSetting(PaymentSource);
		TrackEffectiveSetting(StrafeRequestCostRoubles);
		TrackEffectiveSetting(DoubleStrafeRequestCostRoubles);
		TrackEffectiveSetting(ExtractionRequestCostRoubles);
		TrackEffectiveSetting(PriorityExfilRequestCostRoubles);
		TrackEffectiveSetting(UavRequestCostRoubles);
		TrackEffectiveSetting(FocusedSweepRequestCostRoubles);
		TrackEffectiveSetting(EnablePriorityExfil);
		TrackEffectiveSetting(EnableDoublePass);
		TrackEffectiveSetting(EnableFocusedSweep);
		TrackEffectiveSetting(DoubleStrafeSecondPassDelay);
		TrackEffectiveSetting(A10FikaHeadlessMode);
		TrackEffectiveSetting(A10HeadlessDamageOriginDistance);
		TrackEffectiveSetting(A10HeadlessDamageOriginAltitude);
		TrackEffectiveSetting(A10HeadlessProjectileOwnerMode);
		TrackEffectiveSetting(A10HeadlessAllowRequesterSelfDamage);
		TrackEffectiveSetting(EnableA10HeadlessDirectDamageFallback);
		TrackEffectiveSetting(EnableA10ClientVisualPrediction);
		TrackEffectiveSetting(UavDurationSeconds);
		TrackEffectiveSetting(UavScanInterval);
		TrackEffectiveSetting(UavRangeMeters);
		TrackEffectiveSetting(FocusedSweepDurationSeconds);
		TrackEffectiveSetting(FocusedSweepScanInterval);
		TrackEffectiveSetting(FocusedSweepRangeMeters);
		TrackEffectiveSetting(HelicopterWaitTime);
		TrackEffectiveSetting(PriorityExfilHelicopterWaitTime);
		TrackEffectiveSetting(PriorityExfilDispatchDelay);
		TrackEffectiveSetting(HelicopterExtractTime);
		TrackEffectiveSetting(HelicopterSpeedMultiplier);
		TrackEffectiveSetting(PriorityExfilHelicopterSpeedMultiplier);
		TrackEffectiveSetting(RequestCooldown);
	}

	private static void TrackEffectiveSetting<T>(ConfigEntry<T> entry)
	{
		if (entry != null)
		{
			entry.SettingChanged += OnEffectiveSettingChanged;
		}
	}

	private static void OnEffectiveSettingChanged(object sender, System.EventArgs args)
	{
		FireSupportPayment.NotifySettingsChanged(sender);
	}
}
