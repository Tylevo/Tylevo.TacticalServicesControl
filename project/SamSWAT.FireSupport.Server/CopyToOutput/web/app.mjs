import { createPreset, validatePreset, parsePreset, encodePreset, applyPreset } from "./presets.mjs";

const BASE_PATH = "/tsc";
const MAX_SAVED_PRESETS = 30;

const state = {
	schema: null,
	config: null,
	original: null,
	health: null,
	adminToken: "",
	adminTokenPanelOpen: false,
	busy: false,
	dirtyPaths: new Set(),
	builtInPresets: [],
	savedPresets: [],
	presetWarning: "",
	libraryWarning: "",
	pendingPreset: null
};

const elements = {
	nav: document.getElementById("sectionNav"),
	configurationView: document.getElementById("configurationView"),
	presetsView: document.getElementById("presetsView"),
	configurationLink: document.getElementById("configurationLink"),
	presetsLink: document.getElementById("presetsLink"),
	dashboardHeader: document.getElementById("dashboardHeader"),
	formRoot: document.getElementById("formRoot"),
	adminToken: document.getElementById("adminToken"),
	adminTokenPanel: document.getElementById("adminTokenPanel"),
	unlockAdminButton: document.getElementById("unlockAdminButton"),
	applyAdminTokenButton: document.getElementById("applyAdminTokenButton"),
	reloadButton: document.getElementById("reloadButton"),
	saveButton: document.getElementById("saveButton"),
	reloadDiskButton: document.getElementById("reloadDiskButton"),
	resetButton: document.getElementById("resetButton"),
	routeStatus: document.getElementById("routeStatus"),
	revisionStatus: document.getElementById("revisionStatus"),
	paymentStatus: document.getElementById("paymentStatus"),
	changeStatus: document.getElementById("changeStatus"),
	lastSavedStatus: document.getElementById("lastSavedStatus"),
	diagnosticsGrid: document.getElementById("diagnosticsGrid"),
	adminTokenHint: document.getElementById("adminTokenHint"),
	toast: document.getElementById("toast"),
	presetControls: document.getElementById("presetControls"),
	presetSelect: document.getElementById("presetSelect"),
	presetScope: document.getElementById("presetScope"),
	presetDescription: document.getElementById("presetDescription"),
	presetName: document.getElementById("presetName"),
	presetNotes: document.getElementById("presetNotes"),
	presetText: document.getElementById("presetText"),
	presetFile: document.getElementById("presetFile"),
	presetPreview: document.getElementById("presetPreview"),
	presetPreviewTitle: document.getElementById("presetPreviewTitle"),
	presetPreviewSummary: document.getElementById("presetPreviewSummary"),
	presetChanges: document.getElementById("presetChanges"),
	applyPresetButton: document.getElementById("applyPresetButton"),
	deletePresetButton: document.getElementById("deletePresetButton")
};

document.getElementById("previewPresetButton").addEventListener("click", () => run(() => previewPreset(selectedPreset())));
document.getElementById("savePresetButton").addEventListener("click", () => run(saveCurrentPreset));
document.getElementById("exportPresetButton").addEventListener("click", () => run(exportCurrentPreset));
document.getElementById("copyPresetButton").addEventListener("click", () => run(shareCurrentPreset));
document.getElementById("importPresetButton").addEventListener("click", () => run(() => previewPresetText(elements.presetText.value)));
document.getElementById("importPresetFileButton").addEventListener("click", () => elements.presetFile.click());
document.getElementById("cancelPresetButton").addEventListener("click", clearPresetPreview);
elements.applyPresetButton.addEventListener("click", () => run(applyPreviewedPreset));
elements.deletePresetButton.addEventListener("click", () => run(removeSavedPreset));
elements.presetSelect.addEventListener("change", () => { clearPresetPreview(); renderPresetDescription(); });
elements.presetScope.addEventListener("change", clearPresetPreview);
elements.presetFile.addEventListener("change", () => run(async () => {
	const file = elements.presetFile.files?.[0];
	try {
		if (!file) return;
		clearPresetPreview();
		if (file.size > 32768) throw new Error("Preset JSON files must be 32 KB or smaller.");
		previewPresetText(await file.text());
	} finally { elements.presetFile.value = ""; }
}));

const serviceMetaRules = [
	{ key: "a10", title: "A-10 Strafe", code: "CAS", summary: "Autocannon strike", pattern: /a-?10|strafe(?!.*double)/i },
	{ key: "double-pass", title: "Double Pass", code: "CAS+", summary: "Second A-10 pass", pattern: /double/i },
	{ key: "uav", title: "UAV Recon", code: "REC", summary: "Wide-area scan", pattern: /\buav\b/i },
	{ key: "focused", title: "Focused Sweep", code: "REC+", summary: "Tighter scan radius", pattern: /focused/i },
	{ key: "extraction", title: "UH-60 Extraction", code: "EXT", summary: "Combat pickup", pattern: /extraction(?!.*priority)|extract(?!.*priority)/i },
	// The persisted config path remains PriorityExfil for released-config
	// compatibility; the product occupying that slot is now Cargo Transfer.
	{ key: "priority", title: "UH-60 Cargo Transfer", code: "CGO", summary: "Mid-raid item delivery", pattern: /cargo|priority/i },
	{ key: "payment", title: "Payment", code: "PAY", summary: "Authorization source", pattern: /payment|source|mode/i },
	{ key: "cooldown", title: "Cooldown", code: "CD", summary: "Request pacing", pattern: /cooldown/i }
];

const sectionIntros = {
	main: "Host identity, revisioning, and request pacing.",
	payment: "Select which wallet supplies the configured payment currency.",
	services: "Enable or lock service packages before players can purchase them.",
	recon: "UAV and focused sweep scan timing, range, and refresh behavior.",
	extraction: "UH-60 extraction and cargo-transfer dispatch, wait, arrival, and cargo grid size. Larger items occupy multiple inventory cells.",
	fire: "A-10, double-pass, and fire support behavior.",
	diagnostics: "Live route and server state."
};

const fieldHelp = {
	"priorityExfil.gridWidth": "0 uses native width; 1-10 sets cargo columns. Applies when cargo next opens.",
	"priorityExfil.gridHeight": "0 uses native height; 1-30 sets cargo rows. Applies when cargo next opens."
};

elements.adminToken.value = state.adminToken;

elements.adminToken.addEventListener("input", () => {
	state.adminToken = elements.adminToken.value.trim();
	updateAdminControls();
});

elements.adminToken.addEventListener("keydown", (event) => {
	if (event.key === "Enter") {
		event.preventDefault();
		run(validateAdminToken);
	}
});

elements.unlockAdminButton.addEventListener("click", () => {
	state.adminTokenPanelOpen = !state.adminTokenPanelOpen;
	updateAdminControls();
	if (state.adminTokenPanelOpen) {
		elements.adminToken.focus();
	}
});

elements.applyAdminTokenButton.addEventListener("click", () => run(validateAdminToken));
elements.reloadButton.addEventListener("click", () => {
	if (confirmDiscard()) run(loadConfig);
});
elements.saveButton.addEventListener("click", () => run(saveConfig));
elements.reloadDiskButton.addEventListener("click", () => {
	if (confirmDiscard()) run(() => postAdmin("reload"));
});
elements.resetButton.addEventListener("click", () => {
	if (confirm("Reset TSC config to defaults?")) {
		run(() => postAdmin("reset"));
	}
});

window.addEventListener("hashchange", () => updateWorkspaceNavigation(true));
updateWorkspaceNavigation();
init().catch((error) => showToast(error.message, true));

window.addEventListener("beforeunload", (event) => {
	if (state.dirtyPaths.size === 0) return;
	event.preventDefault();
	event.returnValue = "";
});

function confirmDiscard() {
	return state.dirtyPaths.size === 0 || confirm("Discard your unsaved TSC changes and reload the settings?");
}

function updateWorkspaceNavigation(scrollToTarget = false) {
	let target;
	try { target = decodeURIComponent(window.location.hash.slice(1)); }
	catch { target = "configuration"; }
	target ||= "configuration";
	const presets = target === "presets";
	elements.configurationView.hidden = presets;
	elements.presetsView.hidden = !presets;
	setNavigationActive(elements.configurationLink, !presets, "page");
	setNavigationActive(elements.presetsLink, presets, "page");
	for (const link of elements.nav.children) {
		setNavigationActive(link, !presets && link.getAttribute("href") === `#${target}`, "location");
	}
	if (scrollToTarget) {
		const section = state.schema?.sections.some((entry) => entry.id === target);
		const destination = presets ? "presets" : section || target === "diagnosticsTitle" ? target : "configuration";
		const anchor = document.getElementById(destination);
		if (anchor) {
			anchor.style.scrollMarginTop = `${elements.dashboardHeader.offsetHeight + 20}px`;
			anchor.scrollIntoView({ block: "start" });
		}
	}
}

function setNavigationActive(link, active, current) {
	link.classList.toggle("is-active", active);
	if (active) link.setAttribute("aria-current", current);
	else link.removeAttribute("aria-current");
}

async function run(action) {
	if (state.busy) return;
	state.busy = true;
	updateBusyState();
	try {
		await action();
	} catch (error) {
		showToast(error.message, true);
	} finally {
		state.busy = false;
		updateBusyState();
	}
}

function updateBusyState() {
	elements.formRoot.inert = state.busy;
	elements.presetControls.inert = state.busy;
	for (const button of [elements.reloadButton, elements.reloadDiskButton, elements.resetButton,
		elements.unlockAdminButton, elements.applyAdminTokenButton]) {
		button.disabled = state.busy;
	}
	updateDirtyState();
}

async function init() {
	const [schema, config, health] = await Promise.all([
		requestJson(`${BASE_PATH}/schema`),
		requestJson(`${BASE_PATH}/config`),
		requestJson(`${BASE_PATH}/health`)
	]);
	state.schema = schema;
	state.config = config;
	state.original = cloneConfig(config);
	state.health = health;
	try {
		const catalog = await requestJson(`${BASE_PATH}/presets`);
		if (catalog.format !== "tsc-preset" || catalog.formatVersion !== 1 || !Array.isArray(catalog.presets)) throw new Error("Unsupported preset catalog.");
		state.builtInPresets = catalog.presets.map((preset) => validatePreset(preset, state.schema));
	} catch { state.presetWarning = "Built-in presets are unavailable. Update the server and dashboard together; JSON sharing remains available."; }
	await loadSavedPresets();
	render();
	updateWorkspaceNavigation(true);
	showToast("Config loaded");
}

async function loadConfig() {
	const [config, health] = await Promise.all([
		requestJson(`${BASE_PATH}/config`),
		requestJson(`${BASE_PATH}/health`)
	]);
	state.config = config;
	state.original = cloneConfig(config);
	state.health = health;
	state.dirtyPaths.clear();
	clearPresetPreview();
	await loadSavedPresets();
	render();
	showToast("Config reloaded");
}

async function saveConfig() {
	requireAdminToken();
	const body = cloneConfig(state.config);
	delete body.playerStateIncluded;
	delete body.stashCurrencyBalance;
	delete body.stashCurrencyBalances;
	delete body.stashRoubleBalance;
	delete body.stashCurrencyState;
	delete body.purchaseHistory;
	delete body.uplinkUnlocked;
	delete body.progressionPermit;
	delete body.authorizations;
	delete body.preparedPurchases;
	delete body.preparedPurchaseDetails;
	delete body.preparedPurchaseQuotes;
	let updated;
	try {
		updated = await requestJson(`${BASE_PATH}/config`, {
			method: "POST",
			headers: adminHeaders(),
			body: JSON.stringify(body)
		});
	} catch (error) {
		handleAdminFailure(error);
		if (error.status === 409) {
			throw new Error("Settings changed in SIC or another dashboard. Your edits are still here. Use Reload Config to get the latest settings before editing again.");
		}
		throw error;
	}
	state.config = updated;
	state.original = cloneConfig(updated);
	state.dirtyPaths.clear();
	state.health = await requestJson(`${BASE_PATH}/health`);
	clearPresetPreview();
	render();
	elements.lastSavedStatus.textContent = `Saved ${new Date().toLocaleTimeString()}`;
	showToast("Config saved");
}

async function postAdmin(route) {
	requireAdminToken();
	let updated;
	try {
		updated = await requestJson(`${BASE_PATH}/${route}`, {
			method: "POST",
			headers: adminHeaders()
		});
	} catch (error) {
		handleAdminFailure(error);
		throw error;
	}
	state.config = updated;
	state.original = cloneConfig(updated);
	state.dirtyPaths.clear();
	clearPresetPreview();
	state.health = await requestJson(`${BASE_PATH}/health`);
	render();
	showToast(route === "reset" ? "Defaults restored" : "Config reloaded from disk");
}

async function validateAdminToken() {
	requireAdminToken();
	let health;
	try {
		health = await requestJson(`${BASE_PATH}/admin/health`, {
			headers: adminHeaders()
		});
	} catch (error) {
		handleAdminFailure(error);
		throw error;
	}

	state.health = health;
	state.adminTokenPanelOpen = false;
	updateAdminControls();
	renderDiagnostics();
	await loadSavedPresets();
	renderPresetLibrary();
	showToast("Admin token accepted");
}

function handleAdminFailure(error) {
	if (error?.status !== 403) {
		return;
	}

	state.adminTokenPanelOpen = true;
	updateAdminControls();
	elements.adminToken.focus();
	showToast("Admin token missing or rejected. Paste the token from the SPT host config file, then Apply Token.", true);
}

function render() {
	renderStatus();
	renderTokenHint();
	updateAdminControls();
	renderNavigation();
	renderSections();
	renderDiagnostics();
	updateDirtyState();
	renderPresetLibrary();
	updateWorkspaceNavigation();
}

function renderStatus() {
	elements.routeStatus.textContent = state.health?.ok ? "Online" : "Unavailable";
	elements.routeStatus.classList.toggle("is-online", Boolean(state.health?.ok));
	elements.revisionStatus.textContent = `Revision ${state.config?.revision ?? "--"}`;
	elements.paymentStatus.textContent = state.config
		? `${getPaymentSourceName()} / ${getPaymentCurrency()}`
		: "Payment --";
}

function renderTokenHint() {
	const tokenPath = getAdminTokenPath();
	const hint = `Token file on SPT host: ${tokenPath}`;
	elements.adminToken.placeholder = "Paste token for this session";
	elements.adminToken.title = hint;
	if (elements.adminTokenHint) {
		elements.adminTokenHint.textContent = hint;
		elements.adminTokenHint.title = hint;
	}
}

function updateAdminControls() {
	const tokenRequired = isAdminTokenRequired();
	elements.unlockAdminButton.hidden = !tokenRequired;
	elements.adminTokenPanel.hidden = !tokenRequired || !state.adminTokenPanelOpen;
	elements.applyAdminTokenButton.hidden = !tokenRequired;
	elements.unlockAdminButton.textContent = state.adminToken ? "Admin Unlocked" : "Unlock Admin";
	elements.unlockAdminButton.classList.toggle("primary", Boolean(state.adminToken));
}

function renderNavigation() {
	elements.nav.innerHTML = "";
	for (const section of state.schema.sections) {
		const link = document.createElement("a");
		link.href = `#${section.id}`;
		link.textContent = section.label;
		link.dataset.section = section.id;
		elements.nav.appendChild(link);
	}
	const diagnostics = document.createElement("a");
	diagnostics.href = "#diagnosticsTitle";
	diagnostics.textContent = "Diagnostics";
	diagnostics.dataset.section = "diagnostics";
	elements.nav.appendChild(diagnostics);
}

function renderSections() {
	elements.formRoot.innerHTML = "";
	for (const section of state.schema.sections) {
		const panel = document.createElement("section");
		panel.className = `section-panel section-${section.id}`;
		panel.id = section.id;

		const heading = document.createElement("div");
		heading.className = "section-heading";

		const headingText = document.createElement("div");
		const kicker = document.createElement("span");
		kicker.className = "section-kicker";
		kicker.textContent = getSectionKicker(section);
		const title = document.createElement("h2");
		title.textContent = section.label;
		const intro = document.createElement("p");
		intro.className = "section-intro";
		intro.textContent = getSectionIntro(section);
		headingText.append(kicker, title, intro);
		heading.appendChild(headingText);
		panel.appendChild(heading);

		const grid = document.createElement("div");
		grid.className = shouldUseServiceDeck(section)
			? "field-grid service-deck"
			: "field-grid";
		const groupedPaths = new Set();
		for (const field of section.fields) {
			if (groupedPaths.has(field.path)) continue;
			if (section.id === "pricing" && field.path?.startsWith("prices.")) {
				const serviceKey = field.path.slice("prices.".length);
				const currencyField = section.fields.find((entry) => entry.path === `serviceCurrencies.${serviceKey}`);
				if (currencyField) {
					grid.appendChild(renderPricingCard(field, currencyField));
					groupedPaths.add(currencyField.path);
					continue;
				}
			}
			if (section.id === "pricing" && field.path?.startsWith("serviceCurrencies.") &&
				section.fields.some((entry) => entry.path === field.path.replace("serviceCurrencies.", "prices."))) continue;
			grid.appendChild(renderField(field, section));
		}
		panel.appendChild(grid);
		elements.formRoot.appendChild(panel);
	}
}

function shouldUseServiceDeck(section) {
	return section.id === "pricing" ||
		section.id === "services" ||
		section.id === "recon" ||
		section.id === "extraction" ||
		section.id === "fire";
}

function getSectionKicker(section) {
	if (section.id?.includes("pricing")) return "Per-service";
	if (section.id === "services") return "Availability";
	if (section.id?.includes("recon")) return "Recon";
	if (section.id?.includes("extraction")) return "Extraction";
	if (section.id?.includes("fire")) return "Fire Support";
	if (section.id?.includes("payment")) return "Wallet";
	return "Main";
}

function getSectionIntro(section) {
	if (section.id?.includes("pricing")) {
		return "Choose a currency and amount for each service. Use global follows the payment setting. GP coins and Bitcoin come from the stash. Changing currency keeps the amount; set the item count you want.";
	}
	if (section.id?.includes("payment")) {
		return `Default currency: ${getPaymentCurrencyName()}. Each service can override it below. The wallet setting applies to cash; GP coins and Bitcoin always use the stash.`;
	}
	return sectionIntros[section.id] || "Server-authoritative service configuration.";
}

function getPaymentCurrency(serviceKey) {
	let selected = state.config?.paymentCurrency ?? "RUB";
	if (serviceKey && Object.prototype.hasOwnProperty.call(state.config?.serviceCurrencies ?? {}, serviceKey)) {
		const override = state.config.serviceCurrencies[serviceKey];
		if (String(override).trim().toUpperCase() !== "INHERIT") selected = override;
	}
	const value = String(selected ?? "").trim().toUpperCase();
	return ["RUB", "USD", "EUR", "GP", "BTC"].includes(value)
		? value
		: "INVALID";
}

function getPaymentCurrencyName(serviceKey) {
	return {
		RUB: "roubles (RUB)",
		USD: "US dollars (USD)",
		EUR: "euros (EUR)",
		GP: "GP coins (GP)",
		BTC: "Bitcoin (BTC)",
		INVALID: "an invalid payment currency"
	}[getPaymentCurrency(serviceKey)];
}

function getPaymentSourceName(value = state.config?.paymentSource) {
	return {
		CarriedRoubles: "Carried",
		StashRoubles: "Stash",
		PreferCarriedThenStash: "Carried, then stash",
		PreferStashThenCarried: "Stash, then carried"
	}[value] || value || "Payment --";
}

function getSelectOptionLabel(path, value) {
	if (path === "paymentSource") {
		return getPaymentSourceName(value);
	}
	if (path === "paymentCurrency" || path?.startsWith("serviceCurrencies.")) {
		return {
			Inherit: `Use global (${getPaymentCurrency()})`,
			RUB: "RUB — Roubles",
			USD: "USD — US Dollars",
			EUR: "EUR — Euros",
			GP: "GP — GP coins (stash)",
			BTC: "BTC — Bitcoin (stash)"
		}[value] || value;
	}
	return value;
}

function getFieldStep(field) {
	if (field.path?.startsWith("prices.") && getPaymentCurrency(field.path.slice("prices.".length)) !== "RUB") {
		return 1;
	}
	return field.step ?? 1;
}

function renderPricingCard(priceField, currencyField) {
	const card = document.createElement("article");
	card.className = "field-row service-card pricing-card";
	const meta = getServiceMeta(priceField);
	card.dataset.service = meta.key;
	const badge = document.createElement("span");
	badge.className = "service-code";
	badge.textContent = meta.code;
	const titleWrap = document.createElement("span");
	titleWrap.className = "service-title-wrap";
	const title = document.createElement("strong");
	title.className = "service-title";
	title.textContent = meta.title;
	const summary = document.createElement("span");
	summary.className = "service-summary";
	const currency = getPaymentCurrency(priceField.path.slice("prices.".length));
	const itemPayment = currency === "GP" || currency === "BTC";
	summary.textContent = itemPayment
		? `${getPaymentCurrencyName(priceField.path.slice("prices.".length))} from stash. Price is the item count.`
		: `${currency} · ${getPaymentSourceName()}`;
	titleWrap.append(title, summary);
	card.append(badge, titleWrap);
	const controls = { id: "pricing-controls" };
	card.append(
		renderField({ ...currencyField, label: "Payment currency" }, controls),
		renderField({ ...priceField, label: `Price (${currency})`, slider: itemPayment ? false : priceField.slider }, controls)
	);
	return card;
}

function renderField(field, section) {
	const row = document.createElement("label");
	row.className = `field-row field-${field.type}`;
	row.dataset.path = field.path;

	const meta = getServiceMeta(field);
	if (shouldUseServiceDeck(section)) {
		row.classList.add("service-card");
		row.dataset.service = meta.key;

		const badge = document.createElement("span");
		badge.className = "service-code";
		badge.textContent = meta.code;

		const titleWrap = document.createElement("span");
		titleWrap.className = "service-title-wrap";
		const serviceTitle = document.createElement("strong");
		serviceTitle.className = "service-title";
		serviceTitle.textContent = meta.title;
		const serviceSummary = document.createElement("span");
		serviceSummary.className = "service-summary";
		serviceSummary.textContent = fieldHelp[field.path] || meta.summary;
		titleWrap.append(serviceTitle, serviceSummary);

		row.append(badge, titleWrap);
	}

	const label = document.createElement("span");
	label.className = "field-label";
	label.textContent = field.label;
	row.appendChild(label);

	const controlWrap = document.createElement("span");
	controlWrap.className = "control-wrap";

	const value = getPath(state.config, field.path);
	if (field.type === "toggle") {
		const input = document.createElement("input");
		input.type = "checkbox";
		input.checked = Boolean(value);
		input.addEventListener("change", () => {
			setPath(state.config, field.path, input.checked);
			markDirty(field.path);
		});
		const toggle = document.createElement("span");
		toggle.className = "toggle-shell";
		toggle.appendChild(input);
		toggle.appendChild(document.createElement("span"));
		controlWrap.appendChild(toggle);
	} else if (field.type === "select") {
		const select = document.createElement("select");
		for (const option of field.options) {
			const optionEl = document.createElement("option");
			optionEl.value = option;
			optionEl.textContent = getSelectOptionLabel(field.path, option);
			select.appendChild(optionEl);
		}
		select.value = value ?? (field.path?.startsWith("serviceCurrencies.") ? "Inherit" : "");
		select.addEventListener("change", () => {
			setPath(state.config, field.path, select.value);
			markDirty(field.path);
			if (field.path === "paymentCurrency" || field.path?.startsWith("serviceCurrencies.") || field.path === "paymentSource") {
				renderStatus();
				renderSections();
				renderDiagnostics();
			}
		});
		controlWrap.appendChild(select);
	} else {
		const number = document.createElement("input");
		number.type = "number";
		number.value = value ?? 0;
		number.step = getFieldStep(field);
		if (field.min !== null && field.min !== undefined) number.min = field.min;
		if (field.max !== null && field.max !== undefined) number.max = field.max;
		number.readOnly = field.type === "readonly";
		number.addEventListener("input", () => {
			const numericValue = normalizeNumber(number.value, getFieldStep(field), field.min, field.max);
			setPath(state.config, field.path, numericValue);
			if (range) range.value = numericValue;
			markDirty(field.path);
		});

		let range = null;
		if (field.slider) {
			controlWrap.classList.add("has-range");
			range = document.createElement("input");
			range.type = "range";
			range.step = getFieldStep(field);
			range.min = field.min ?? 0;
			range.max = field.max ?? Math.max(Number(value ?? 0), 1);
			range.value = value ?? 0;
			range.addEventListener("input", () => {
				const numericValue = normalizeNumber(range.value, getFieldStep(field), field.min, field.max);
				number.value = numericValue;
				setPath(state.config, field.path, numericValue);
				markDirty(field.path);
			});
			controlWrap.appendChild(range);
		}
		controlWrap.appendChild(number);
	}

	row.appendChild(controlWrap);
	return row;
}

function getServiceMeta(field) {
	const haystack = `${field.label || ""} ${field.path || ""}`;
	for (const rule of serviceMetaRules) {
		if (rule.pattern.test(haystack)) return rule;
	}
	return {
		key: "generic",
		title: normalizeTitle(field.label || "Config"),
		code: "CFG",
		summary: field.path || "Server setting"
	};
}

function normalizeTitle(value) {
	return String(value || "Config")
		.replace(/\bprice\b/ig, "")
		.replace(/\benabled\b/ig, "")
		.replace(/\bduration\b/ig, "")
		.replace(/\brange\b/ig, "")
		.replace(/\bscan interval\b/ig, "")
		.trim() || "Config";
}

function renderDiagnostics() {
	const rows = [
		["Route Status", state.health?.ok ? "Online" : "Unavailable"],
		["Config Revision", state.config?.revision ?? "--"],
		["Payment Source", getPaymentSourceName()],
		["Payment Currency", getPaymentCurrency()],
		["Payment Mode", state.config?.paymentMode ?? "--"],
		["Request Cooldown", `${state.config?.requestCooldownSeconds ?? "--"} sec`],
		["Admin Token", state.health?.adminTokenConfigured ? "Configured" : "Missing"],
		["Admin Token File", getAdminTokenPath() || "config/tsc-admin-token.txt"],
		["Last Loaded", formatTime(state.health?.lastLoadedUtc)],
		["Last Saved", formatTime(state.health?.lastSavedUtc)]
	];
	elements.diagnosticsGrid.innerHTML = "";
	for (const [key, value] of rows) {
		const dt = document.createElement("dt");
		dt.textContent = key;
		const dd = document.createElement("dd");
		dd.textContent = value;
		elements.diagnosticsGrid.append(dt, dd);
	}
}

async function loadSavedPresets() {
	try {
		const library = await requestJson(`${BASE_PATH}/presets/saved`, { headers: adminHeaders() });
		const saved = library.presets;
		if (!Array.isArray(saved) || saved.length > MAX_SAVED_PRESETS) throw new Error("Invalid preset library.");
		state.savedPresets = saved.map((preset) => validatePreset(preset, state.schema));
		if (state.savedPresets.some((preset) => !preset.id)) throw new Error("A saved preset has no identifier.");
		state.libraryWarning = library.warning || "";
	} catch (error) {
		state.savedPresets = [];
		state.libraryWarning = error.status === 403 ? "Unlock Admin to load the host's saved presets." : "The host's saved presets could not be loaded. JSON import and export are still available.";
	}
}

function selectedPreset() {
	const [kind, id] = elements.presetSelect.value.split(":");
	const collection = kind === "custom" ? state.savedPresets : state.builtInPresets;
	const preset = collection.find((entry) => entry.id === id);
	if (!preset) throw new Error("Choose a preset first, or import one below.");
	return preset;
}

function renderPresetLibrary(preferred = elements.presetSelect.value) {
	elements.presetSelect.innerHTML = "";
	for (const [kind, label, presets] of [["builtin", "Built-in", state.builtInPresets], ["custom", "Saved on the SPT host", state.savedPresets]]) {
		if (!presets.length) continue;
		const group = document.createElement("optgroup");
		group.label = label;
		for (const preset of presets) {
			const option = document.createElement("option");
			option.value = `${kind}:${preset.id}`;
			option.textContent = preset.name;
			group.appendChild(option);
		}
		elements.presetSelect.appendChild(group);
	}
	const choices = [...state.builtInPresets.map((entry) => `builtin:${entry.id}`), ...state.savedPresets.map((entry) => `custom:${entry.id}`)];
	elements.presetSelect.value = choices.includes(preferred) ? preferred : choices[0] || "";
	renderPresetDescription();
}

function renderPresetDescription() {
	let description = "Import a JSON file or share code to get started.";
	try { description = selectedPreset().description || "Custom gameplay settings."; } catch {}
	elements.presetDescription.textContent = [description, state.presetWarning, state.libraryWarning].filter(Boolean).join(" ");
	elements.deletePresetButton.disabled = !elements.presetSelect.value.startsWith("custom:");
}

function currentPreset() {
	return createPreset(state.config, state.schema, elements.presetName.value, elements.presetNotes.value);
}

async function presetHostRequest(route, body) {
	requireAdminToken();
	try {
		return await requestJson(`${BASE_PATH}/presets/${route}`, { method: "POST", headers: adminHeaders(), body: JSON.stringify(body) });
	} catch (error) { handleAdminFailure(error); throw error; }
}

async function saveCurrentPreset() {
	const preset = currentPreset();
	const existing = state.savedPresets.find((entry) => entry.name.toLowerCase() === preset.name.toLowerCase());
	if (existing && !confirm(`Replace your saved preset "${existing.name}" with the current draft settings?`)) return;
	if (!existing && state.savedPresets.length >= MAX_SAVED_PRESETS) throw new Error(`You can save up to ${MAX_SAVED_PRESETS} presets. Export or remove one first.`);
	if (existing) preset.id = existing.id;
	const saved = validatePreset((await presetHostRequest("saved", preset)).preset, state.schema);
	if (!saved.id) throw new Error("The host did not return the saved preset identifier. Reload Config to check the library.");
	state.savedPresets = [...state.savedPresets.filter((entry) => entry.id !== saved.id), saved];
	state.libraryWarning = "";
	renderPresetLibrary(`custom:${saved.id}`);
	showToast("Preset saved on the SPT host");
}

async function removeSavedPreset() {
	if (!elements.presetSelect.value.startsWith("custom:")) return;
	const preset = selectedPreset();
	if (!confirm(`Remove "${preset.name}" from the host's preset library? Your current settings will stay as they are.`)) return;
	await presetHostRequest("remove", { id: preset.id });
	state.savedPresets = state.savedPresets.filter((entry) => entry.id !== preset.id);
	clearPresetPreview();
	renderPresetLibrary();
	showToast("Saved preset removed");
}

function exportCurrentPreset() {
	const preset = currentPreset();
	const url = URL.createObjectURL(new Blob([JSON.stringify(preset, null, 2) + "\n"], { type: "application/json" }));
	const link = document.createElement("a");
	link.href = url;
	link.download = `${preset.name.toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-|-$/g, "") || "tsc-preset"}.tsc-preset.json`;
	link.click();
	setTimeout(() => URL.revokeObjectURL(url), 1000);
	showToast("Preset JSON exported");
}

async function shareCurrentPreset() {
	const code = encodePreset(currentPreset());
	elements.presetText.value = code;
	try {
		await navigator.clipboard.writeText(code);
		showToast("Share code copied. It is also shown below.");
	} catch {
		elements.presetText.focus();
		elements.presetText.select();
		showToast("Share code ready below. Copy it to share your settings.");
	}
}

function clearPresetPreview() {
	state.pendingPreset = null;
	elements.presetPreview.hidden = true;
}

function previewPresetText(text) {
	clearPresetPreview();
	previewPreset(parsePreset(text, state.schema));
	// Imports live in a separate card, which stacks below the preview on small screens.
	elements.presetPreview.style.scrollMarginTop = `${(elements.dashboardHeader?.offsetHeight || 70) + 20}px`;
	elements.presetPreview.scrollIntoView({ block: "nearest" });
}

function previewPreset(preset) {
	const scope = elements.presetScope.value || "all";
	const checked = validatePreset(preset, state.schema);
	const result = applyPreset(state.config, checked, state.schema, scope);
	state.pendingPreset = { preset: checked, scope, base: JSON.stringify(state.config), result };
	elements.presetName.value = checked.name;
	elements.presetNotes.value = checked.description || "";
	elements.presetPreviewTitle.textContent = checked.name;
	elements.presetPreviewSummary.textContent = result.changes.length
		? `${result.changes.length} settings will change in your draft. Save Config applies the draft to the server.`
		: "These settings already match your current draft.";
	elements.presetChanges.innerHTML = "";
	for (const change of result.changes) {
		const row = document.createElement("tr");
		for (const value of [change.label || change.path, change.before, change.after]) {
			const cell = document.createElement("td");
			cell.textContent = value === undefined ? "Default" : typeof value === "boolean" ? (value ? "On" : "Off") : String(value);
			row.appendChild(cell);
		}
		elements.presetChanges.appendChild(row);
	}
	elements.applyPresetButton.disabled = !result.changes.length;
	elements.presetPreview.hidden = false;
}

function applyPreviewedPreset() {
	const pending = state.pendingPreset;
	if (!pending) return;
	if (pending.base !== JSON.stringify(state.config) || pending.scope !== (elements.presetScope.value || "all")) {
		previewPreset(pending.preset);
		showToast("Your draft changed. Review the refreshed preview before applying.");
		return;
	}
	state.config = pending.result.config;
	for (const change of pending.result.changes) markDirty(change.path);
	clearPresetPreview();
	render();
	showToast("Preset applied to draft. Select Save Config to use these settings.");
}

function markDirty(path) {
	const current = getPath(state.config, path);
	const original = getPath(state.original, path);
	if (JSON.stringify(current) === JSON.stringify(original)) {
		state.dirtyPaths.delete(path);
	} else {
		state.dirtyPaths.add(path);
	}
	updateDirtyState();
}

function updateDirtyState() {
	const count = state.dirtyPaths.size;
	elements.changeStatus.textContent = `${count} unsaved ${count === 1 ? "change" : "changes"}`;
	elements.saveButton.disabled = state.busy || count === 0;
}

async function requestJson(url, options = {}) {
	const response = await fetch(url, {
		...options,
		headers: {
			Accept: "application/json",
			...(options.headers || {})
		}
	});
	const text = await response.text();
	let data = {};
	if (text) {
		try {
			data = JSON.parse(text);
		} catch {
			data = { error: text };
		}
	}
	if (!response.ok) {
		const error = new Error(data.error || `HTTP ${response.status}`);
		error.status = response.status;
		throw error;
	}
	return data;
}

function adminHeaders() {
	const headers = { "Content-Type": "application/json" };
	if (state.adminToken) {
		headers["X-TSC-Admin-Token"] = state.adminToken;
	}
	return headers;
}

function requireAdminToken() {
	if (!isAdminTokenRequired()) {
		return;
	}

	if (!state.adminToken) {
		const tokenPath = getAdminTokenPath();
		state.adminTokenPanelOpen = true;
		updateAdminControls();
		elements.adminToken.focus();
		throw new Error(tokenPath ? `Admin token required: ${tokenPath}` : "Admin token required");
	}
}

function isAdminTokenRequired() {
	return Boolean(state.health?.adminDashboard?.tokenRequired);
}

function getAdminTokenPath() {
	return "config/tsc-admin-token.txt";
}

function cloneConfig(value) {
	return JSON.parse(JSON.stringify(value));
}

function getPath(target, path) {
	const keys = path.split(".");
	if (!keys.every(isSafePathSegment)) return undefined;
	return keys.reduce((value, key) => value?.[key], target);
}

function setPath(target, path, value) {
	const keys = path.split(".");
	if (!keys.every(isSafePathSegment)) {
		throw new Error("Unsafe config path");
	}

	let cursor = target;
	while (keys.length > 1) {
		const key = keys.shift();
		if (cursor[key] === null || typeof cursor[key] !== "object") {
			cursor[key] = {};
		}
		cursor = cursor[key];
	}
	cursor[keys[0]] = value;
}

function isSafePathSegment(segment) {
	return segment &&
		segment !== "__proto__" &&
		segment !== "prototype" &&
		segment !== "constructor";
}

function normalizeNumber(value, step, min = null, max = null) {
	const parsed = Number(value);
	if (!Number.isFinite(parsed)) return 0;
	const stepText = String(step ?? 1);
	let normalized = stepText.includes(".") ? parsed : Math.round(parsed);
	if (min !== null && min !== undefined) normalized = Math.max(Number(min), normalized);
	if (max !== null && max !== undefined) normalized = Math.min(Number(max), normalized);
	return normalized;
}

function formatTime(value) {
	if (!value || value.startsWith("0001-")) return "--";
	return new Date(value).toLocaleString();
}

let toastTimer = null;
function showToast(message, isError = false) {
	clearTimeout(toastTimer);
	elements.toast.textContent = message;
	elements.toast.className = `toast is-visible${isError ? " is-error" : ""}`;
	toastTimer = setTimeout(() => {
		elements.toast.className = "toast";
	}, 2600);
}
