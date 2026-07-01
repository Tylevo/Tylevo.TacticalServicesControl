const BASE_PATH = "/tsc";

const state = {
	schema: null,
	config: null,
	original: null,
	health: null,
	adminToken: "",
	adminTokenPanelOpen: false,
	dirtyPaths: new Set()
};

const elements = {
	nav: document.getElementById("sectionNav"),
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
	toast: document.getElementById("toast")
};

const serviceMetaRules = [
	{ key: "a10", title: "A-10 Strafe", code: "CAS", summary: "Autocannon strike", pattern: /a-?10|strafe(?!.*double)/i },
	{ key: "double-pass", title: "Double Pass", code: "CAS+", summary: "Second A-10 pass", pattern: /double/i },
	{ key: "uav", title: "UAV Recon", code: "REC", summary: "Wide-area scan", pattern: /\buav\b/i },
	{ key: "focused", title: "Focused Sweep", code: "REC+", summary: "Tighter scan radius", pattern: /focused/i },
	{ key: "extraction", title: "UH-60 Extraction", code: "EXT", summary: "Combat pickup", pattern: /extraction(?!.*priority)|extract(?!.*priority)/i },
	{ key: "priority", title: "Priority Exfil", code: "EXT+", summary: "Expedited pickup", pattern: /priority/i },
	{ key: "payment", title: "Payment", code: "PAY", summary: "Authorization source", pattern: /payment|source|mode/i },
	{ key: "cooldown", title: "Cooldown", code: "CD", summary: "Request pacing", pattern: /cooldown/i }
];

const sectionIntros = {
	main: "Host identity, revisioning, and request pacing.",
	payment: "Select how authorizations consume roubles.",
	"service-pricing": "Displayed phone prices and the authoritative cost used at payment time.",
	"service-toggles": "Enable or lock service packages before players can purchase them.",
	"recon-services": "UAV and focused sweep scan timing, range, and refresh behavior.",
	"extraction-services": "UH-60 dispatch, wait, arrival, and priority exfil tuning.",
	"fire-support": "A-10, double-pass, and fire support behavior.",
	diagnostics: "Live route and server state."
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
elements.reloadButton.addEventListener("click", () => run(loadConfig));
elements.saveButton.addEventListener("click", () => run(saveConfig));
elements.reloadDiskButton.addEventListener("click", () => run(() => postAdmin("reload")));
elements.resetButton.addEventListener("click", () => {
	if (confirm("Reset TSC config to defaults?")) {
		run(() => postAdmin("reset"));
	}
});

init().catch((error) => showToast(error.message, true));

function run(action) {
	action().catch((error) => showToast(error.message, true));
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
	render();
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
	render();
	showToast("Config reloaded");
}

async function saveConfig() {
	requireAdminToken();
	const body = cloneConfig(state.config);
	delete body.stashRoubleBalance;
	let updated;
	try {
		updated = await requestJson(`${BASE_PATH}/config`, {
			method: "POST",
			headers: adminHeaders(),
			body: JSON.stringify(body)
		});
	} catch (error) {
		handleAdminFailure(error);
		throw error;
	}
	state.config = updated;
	state.original = cloneConfig(updated);
	state.dirtyPaths.clear();
	state.health = await requestJson(`${BASE_PATH}/health`);
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
}

function renderStatus() {
	elements.routeStatus.textContent = state.health?.ok ? "Online" : "Unavailable";
	elements.routeStatus.classList.toggle("is-online", Boolean(state.health?.ok));
	elements.revisionStatus.textContent = `Revision ${state.config?.revision ?? "--"}`;
	elements.paymentStatus.textContent = state.config?.paymentSource || "Payment --";
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
		intro.textContent = sectionIntros[section.id] || "Server-authoritative service configuration.";
		headingText.append(kicker, title, intro);
		heading.appendChild(headingText);
		panel.appendChild(heading);

		const grid = document.createElement("div");
		grid.className = shouldUseServiceDeck(section)
			? "field-grid service-deck"
			: "field-grid";
		for (const field of section.fields) {
			grid.appendChild(renderField(field, section));
		}
		panel.appendChild(grid);
		elements.formRoot.appendChild(panel);
	}
}

function shouldUseServiceDeck(section) {
	return section.id === "service-pricing" ||
		section.id === "service-toggles" ||
		section.id === "recon-services" ||
		section.id === "extraction-services" ||
		section.id === "fire-support";
}

function getSectionKicker(section) {
	if (section.id?.includes("pricing")) return "Roubles";
	if (section.id?.includes("toggle")) return "Availability";
	if (section.id?.includes("recon")) return "Recon";
	if (section.id?.includes("extraction")) return "Extraction";
	if (section.id?.includes("fire")) return "Fire Support";
	if (section.id?.includes("payment")) return "Wallet";
	return "Main";
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
		serviceSummary.textContent = meta.summary;
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
			optionEl.textContent = option;
			select.appendChild(optionEl);
		}
		select.value = value ?? "";
		select.addEventListener("change", () => {
			setPath(state.config, field.path, select.value);
			markDirty(field.path);
		});
		controlWrap.appendChild(select);
	} else {
		const number = document.createElement("input");
		number.type = "number";
		number.value = value ?? 0;
		number.step = field.step ?? 1;
		if (field.min !== null && field.min !== undefined) number.min = field.min;
		if (field.max !== null && field.max !== undefined) number.max = field.max;
		number.readOnly = field.type === "readonly";
		number.addEventListener("input", () => {
			const numericValue = normalizeNumber(number.value, field.step, field.min, field.max);
			setPath(state.config, field.path, numericValue);
			if (range) range.value = numericValue;
			markDirty(field.path);
		});

		let range = null;
		if (field.slider) {
			controlWrap.classList.add("has-range");
			range = document.createElement("input");
			range.type = "range";
			range.value = value ?? 0;
			range.step = field.step ?? 1;
			range.min = field.min ?? 0;
			range.max = field.max ?? Math.max(Number(value ?? 0), 1);
			range.addEventListener("input", () => {
				const numericValue = normalizeNumber(range.value, field.step, field.min, field.max);
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
		["Payment Source", state.config?.paymentSource ?? "--"],
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
	elements.saveButton.disabled = count === 0;
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
