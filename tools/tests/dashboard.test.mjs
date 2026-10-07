import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { runInNewContext } from "node:vm";
import * as presetApi from "../../project/SamSWAT.FireSupport.Server/CopyToOutput/web/presets.mjs";

const webRoot = new URL("../../project/SamSWAT.FireSupport.Server/CopyToOutput/web/", import.meta.url);
const source = (await readFile(new URL("app.mjs", webRoot), "utf8")).replace(/^import .* from "\.\/presets\.mjs";\r?\n/, "");
const html = await readFile(new URL("index.html", webRoot), "utf8");

// Execute the production script, including its startup and event handlers. This
// small DOM adapter tests request/state behavior without a browser dependency;
// it does not claim to validate layout or native browser inert behavior.
class Element {
	constructor(tagName = "div") {
		this.tagName = tagName;
		this.children = [];
		this.listeners = new Map();
		this.attributes = new Map();
		this.dataset = {};
		this.className = "";
		this.textContent = "";
		this.value = "";
		this.disabled = false;
		this.inert = false;
		this.hidden = false;
		this.scrollCalls = [];
		this.style = {};
		this.offsetHeight = 70;
		this.classList = {
			add: (name) => this.classList.toggle(name, true),
			toggle: (name, enabled) => {
				const names = new Set(this.className.split(/\s+/).filter(Boolean));
				if (enabled) names.add(name);
				else names.delete(name);
				this.className = [...names].join(" ");
			}
		};
	}

	set innerHTML(value) {
		assert.equal(value, "", "The dashboard should build controls with DOM methods");
		this.children = [];
	}

	appendChild(child) {
		this.children.push(child);
		return child;
	}

	append(...children) {
		children.forEach((child) => this.appendChild(child));
	}

	addEventListener(type, listener) {
		const listeners = this.listeners.get(type) || [];
		listeners.push(listener);
		this.listeners.set(type, listeners);
	}

	dispatchEvent(event) {
		for (const listener of this.listeners.get(event.type) || []) listener(event);
	}

	click() {
		if (this.disabled) return;
		const event = { type: "click", defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
		this.dispatchEvent(event);
		if (!event.defaultPrevented && this.href?.startsWith("#")) this.navigate?.(this.href);
	}

	setAttribute(name, value) { this.attributes.set(name, String(value)); }
	getAttribute(name) { return this.attributes.get(name) ?? (name === "href" ? this.href ?? null : null); }
	removeAttribute(name) { this.attributes.delete(name); }
	scrollIntoView(options) { this.scrollCalls.push(options); }

	focus() {}
	select() {}
}

function response(data, status = 200) {
	return { ok: status >= 200 && status < 300, status, text: async () => JSON.stringify(data) };
}

function deferred() {
	let resolve;
	const promise = new Promise((done) => { resolve = done; });
	return { promise, resolve };
}

function descendants(element) {
	return element.children.flatMap((child) => [child, ...descendants(child)]);
}

const settle = () => new Promise((resolve) => setImmediate(resolve));

async function dashboard(configOverrides = {}, initialHash = "") {
	const elements = Object.fromEntries([...html.matchAll(/<([a-z][\w-]*)\b([^>]*\bid="([^"]+)"[^>]*)>/gi)]
		.map((match) => {
			const element = new Element(match[1]);
			element.id = match[3];
			element.href = match[2].match(/\bhref="([^"]+)"/)?.[1];
			element.hidden = /\bhidden(?:\s|=|$)/.test(match[2]);
			return [match[3], element];
		}));
	const window = new Element();
	const history = [initialHash];
	let historyIndex = 0;
	window.location = {
		get hash() { return history[historyIndex]; },
		set hash(value) {
			if (value === history[historyIndex]) return;
			history.splice(historyIndex + 1);
			history.push(value);
			historyIndex++;
			window.dispatchEvent({ type: "hashchange" });
		}
	};
	window.history = {
		back() { if (historyIndex > 0) { historyIndex--; window.dispatchEvent({ type: "hashchange" }); } },
		forward() { if (historyIndex < history.length - 1) { historyIndex++; window.dispatchEvent({ type: "hashchange" }); } }
	};
	const fixture = {
		elements,
		window,
		navigate: (hash) => { window.location.hash = hash; },
		requests: [],
		confirmations: [],
		confirmResult: true,
		savedPresets: [],
		copiedText: "",
		onRequest: null,
		serverConfig: {
			revision: 7, requestCooldownSeconds: 30, paymentCurrency: "RUB", paymentSource: "StashRoubles",
			prices: { A10: 100000, DoublePass: 150000, Uav: 10000, FocusedSweep: 15000, Extraction: 50000, PriorityExfil: 30000 },
			serviceCurrencies: { A10: "Inherit", DoublePass: "Inherit", Uav: "Inherit", FocusedSweep: "Inherit", Extraction: "Inherit", PriorityExfil: "Inherit" },
			priorityExfil: { gridWidth: 0, gridHeight: 0, waitTimeSeconds: 60 },
			...configOverrides
		}
	};
	for (const element of Object.values(elements)) element.navigate = fixture.navigate;
	fixture.sectionLink = (hash) => elements.sectionNav.children.find((link) => link.href === hash);
	fixture.elementById = (id) => elements[id] ?? Object.values(elements).flatMap(descendants).find((element) => element.id === id) ?? null;
	const schema = { sections: [
		{ id: "main", label: "Main", fields: [
			{ path: "requestCooldownSeconds", label: "Request cooldown", type: "number", min: 0, max: 300 }
		] },
		{ id: "payment", label: "Payment", fields: [
			{ path: "paymentCurrency", label: "Payment Currency", type: "select", options: ["RUB", "USD", "EUR", "GP", "BTC"] }
		] },
		{ id: "pricing", label: "Service Pricing", fields: ["A10", "DoublePass", "Uav", "FocusedSweep", "Extraction", "PriorityExfil"].flatMap((key) => [
			{ path: `prices.${key}`, label: `${key} Price`, type: "number", min: 0, max: 10000000, step: 1, slider: true },
			{ path: `serviceCurrencies.${key}`, label: `${key} Currency`, type: "select", options: ["Inherit", "RUB", "USD", "EUR", "GP", "BTC"] }
		]) },
		{ id: "extraction", label: "UH-60 Services", fields: [
			{ path: "priorityExfil.gridWidth", label: "Cargo Grid Columns", type: "number", min: 0, max: 10, step: 1 },
			{ path: "priorityExfil.gridHeight", label: "Cargo Grid Rows", type: "number", min: 0, max: 30, step: 1 }
		] }
	] };
	const health = { ok: true, adminDashboard: { tokenRequired: false } };
	fixture.defaultResponse = ({ url, options }) => {
		if (url === "/tsc/schema") return response(schema);
		if (url === "/tsc/presets") return response({ format: "tsc-preset", formatVersion: 1, presets: [
			{ ...presetApi.createPreset({ ...fixture.serverConfig, requestCooldownSeconds: 60, prices: { ...fixture.serverConfig.prices, A10: 75000 } }, schema, "Balanced", "Test built-in"), id: "balanced" }
		] });
		if (url === "/tsc/presets/saved" && options.method !== "POST") return response({ presets: fixture.savedPresets });
		if (url === "/tsc/presets/saved") {
			const preset = JSON.parse(options.body);
			preset.id ||= `custom-${String(fixture.savedPresets.length + 1).padStart(32, "0")}`;
			fixture.savedPresets = [...fixture.savedPresets.filter((entry) => entry.id !== preset.id), preset];
			return response({ preset });
		}
		if (url === "/tsc/presets/remove") {
			fixture.savedPresets = fixture.savedPresets.filter((entry) => entry.id !== JSON.parse(options.body).id);
			return response({ ok: true });
		}
		if (url === "/tsc/health" || url === "/tsc/admin/health") return response(health);
		if (url === "/tsc/config" && options.method !== "POST") return response(fixture.serverConfig);
		if (url === "/tsc/config" && options.method === "POST") {
			fixture.serverConfig = { ...JSON.parse(options.body), revision: fixture.serverConfig.revision + 1 };
			return response(fixture.serverConfig);
		}
		if (url === "/tsc/reload") return response(fixture.serverConfig);
		throw new Error(`Unexpected request: ${options.method || "GET"} ${url}`);
	};
	fixture.field = (path) => descendants(elements.formRoot).find((element) => element.dataset.path === path);
	fixture.input = (path = "requestCooldownSeconds") => descendants(fixture.field(path))
		.find((element) => element.type === "number");
	fixture.edit = (value, path) => {
		const input = fixture.input(path);
		assert.ok(input, "The real schema should render an editable input");
		input.value = String(value);
		input.dispatchEvent({ type: "input" });
	};
	fixture.select = (path) => descendants(fixture.field(path)).find((element) => element.tagName === "select");
	fixture.choose = (value, path) => {
		const select = fixture.select(path);
		assert.ok(select);
		select.value = value;
		select.dispatchEvent({ type: "change" });
	};
	runInNewContext(source, {
		...presetApi,
		Blob,
		URL,
		navigator: { clipboard: { writeText: async (text) => { fixture.copiedText = text; } } },
		document: {
			getElementById: fixture.elementById,
			createElement: (tagName) => {
				const element = new Element(tagName);
				element.navigate = fixture.navigate;
				return element;
			}
		},
		window,
		fetch: async (url, options = {}) => {
			const request = { url, options };
			fixture.requests.push(request);
			return fixture.onRequest ? fixture.onRequest(request) : fixture.defaultResponse(request);
		},
		confirm: (message) => {
			fixture.confirmations.push(message);
			return fixture.confirmResult;
		},
		setTimeout: () => 0,
		clearTimeout: () => {}
	}, { filename: new URL("app.mjs", webRoot).pathname });
	await settle();
	assert.equal(elements.toast.textContent, "Config loaded", "Dashboard startup must finish successfully");
	fixture.requests.length = 0;
	return fixture;
}

test("dashboard opens Configuration by default with presets in a separate hidden area", async () => {
	const app = await dashboard();
	assert.equal(app.elements.configurationView.hidden, false);
	assert.equal(app.elements.presetsView.hidden, true);
	assert.equal(app.elements.configurationLink.getAttribute("aria-current"), "page");
	assert.equal(app.elements.presetsLink.getAttribute("aria-current"), null);
	assert.ok(app.elements.configurationLink.className.includes("is-active"));
	assert.equal(app.sectionLink("#presets"), undefined, "Preset area must not be duplicated among config sections");
	assert.ok(app.sectionLink("#diagnosticsTitle"));
	assert.equal(app.requests.length, 0);
});

test("payment source has no dashboard field and legacy imports cannot restore carried payment", async () => {
	const app = await dashboard();
	assert.equal(app.field("paymentSource"), undefined);
	assert.equal(descendants(app.elements.diagnosticsGrid).some((element) => element.textContent === "Payment Source"), false);
	app.elements.presetText.value = JSON.stringify({ format: "tsc-preset", formatVersion: 1,
		name: "Old wallet", settings: { paymentSource: "CarriedRoubles", "prices.A10": 73 } });
	app.elements.importPresetButton.click();
	await settle();
	app.elements.applyPresetButton.click();
	await settle();
	app.elements.saveButton.click();
	await settle();
	assert.equal(app.serverConfig.paymentSource, "StashRoubles");
	assert.equal(app.serverConfig.prices.A10, 73);
	assert.equal(app.field("paymentSource"), undefined);
});

test("direct preset and configuration section hashes reveal and scroll to the right area", async () => {
	for (const hash of ["#presets", "#pricing", "#diagnosticsTitle", "#configuration"]) {
		const app = await dashboard({}, hash);
		const presets = hash === "#presets";
		assert.equal(app.elements.configurationView.hidden, presets, hash);
		assert.equal(app.elements.presetsView.hidden, !presets, hash);
		assert.equal(app.elements[presets ? "presetsLink" : "configurationLink"].getAttribute("aria-current"), "page");
		assert.equal(app.elementById(hash.slice(1)).scrollCalls.length, 1, "Direct anchors must scroll after content becomes available");
		if (hash === "#pricing" || hash === "#diagnosticsTitle") {
			assert.ok(app.sectionLink(hash).className.includes("is-active"));
			assert.equal(app.sectionLink(hash).getAttribute("aria-current"), "location");
		}
	}
});

test("navigation measures the expanded header before scrolling each destination", async () => {
	const app = await dashboard({}, "#presets");
	app.elements.adminTokenPanel.hidden = false;
	app.elements.dashboardHeader.offsetHeight = 280;
	app.sectionLink("#pricing").click();
	assert.equal(app.elementById("pricing").style.scrollMarginTop, "300px");
	assert.equal(app.elementById("pricing").scrollCalls.length, 1);
	app.elements.adminTokenPanel.hidden = true;
	app.elements.dashboardHeader.offsetHeight = 96;
	app.elements.presetsLink.click();
	assert.equal(app.elements.presets.style.scrollMarginTop, "116px", "Returning to another area uses the current header height");
	assert.equal(app.requests.length, 0);
});

test("saving and reloading a deep-linked configuration do not scroll the workspace again", async () => {
	const app = await dashboard({}, "#pricing");
	assert.equal(app.elementById("pricing").scrollCalls.length, 1);
	app.edit(90);
	app.elements.saveButton.click();
	await settle();
	assert.equal(app.elementById("pricing").scrollCalls.length, 0, "Saving renders the current area without scrolling");
	assert.ok(app.sectionLink("#pricing").className.includes("is-active"));
	app.elements.reloadButton.click();
	await settle();
	assert.equal(app.elementById("pricing").scrollCalls.length, 0, "Reloading must also preserve the current scroll position");
});

test("area navigation preserves the unsaved form and staged preset preview without requests", async () => {
	const app = await dashboard();
	app.edit(45);
	const unsavedInput = app.input();
	app.elements.presetsLink.click();
	app.elements.previewPresetButton.click();
	await settle();
	const previewRows = [...app.elements.presetChanges.children];
	const previewTitle = app.elements.presetPreviewTitle.textContent;
	app.elements.presetName.value = "Keep my name";
	app.elements.presetNotes.value = "Keep my notes";
	app.elements.presetText.value = "Keep this draft share code";
	app.elements.configurationLink.click();
	assert.equal(app.elements.configurationView.hidden, false);
	assert.equal(app.elements.presetsView.hidden, true);
	assert.equal(app.input(), unsavedInput, "Switching areas must not rebuild the form");
	assert.equal(app.input().value, "45");
	assert.equal(app.elements.changeStatus.textContent, "1 unsaved change");
	app.elements.presetsLink.click();
	assert.equal(app.elements.presetPreview.hidden, false);
	assert.deepEqual(app.elements.presetChanges.children, previewRows);
	assert.equal(app.elements.presetPreviewTitle.textContent, previewTitle);
	assert.equal(app.elements.presetName.value, "Keep my name");
	assert.equal(app.elements.presetNotes.value, "Keep my notes");
	assert.equal(app.elements.presetText.value, "Keep this draft share code");
	assert.equal(app.requests.length, 0);
	assert.equal(app.confirmations.length, 0);
	app.elements.applyPresetButton.click();
	await settle();
	assert.equal(Number(app.input().value), 60, "The preserved preview remains applicable");
	assert.equal(app.elements.presetsView.hidden, false, "Applying must keep the current area");
	assert.equal(app.elements.saveButton.disabled, false, "Header Save Config remains available from presets");
});

test("section links and browser back and forward switch areas without resetting drafts", async () => {
	const app = await dashboard({}, "#presets");
	app.edit(80);
	app.sectionLink("#pricing").click();
	assert.equal(app.window.location.hash, "#pricing");
	assert.equal(app.elements.presetsView.hidden, true);
	assert.ok(app.sectionLink("#pricing").className.includes("is-active"));
	assert.equal(app.elementById("pricing").scrollCalls.length, 1);
	app.window.history.back();
	assert.equal(app.window.location.hash, "#presets");
	assert.equal(app.elements.presetsView.hidden, false);
	assert.equal(app.sectionLink("#pricing").getAttribute("aria-current"), null);
	app.window.history.forward();
	assert.equal(app.elements.configurationView.hidden, false);
	assert.equal(app.elements.configurationLink.getAttribute("aria-current"), "page");
	assert.equal(app.input().value, "80");
	assert.equal(app.elements.changeStatus.textContent, "1 unsaved change");
	assert.equal(app.requests.length, 0);
	assert.equal(app.confirmations.length, 0);
});

test("unknown or malformed hashes fall back to Configuration without losing edits", async () => {
	const app = await dashboard({}, "#presets");
	app.edit(90);
	for (const hash of ["#unknown-area", "#%E0%A4%A"]) {
		app.navigate(hash);
		assert.equal(app.elements.configurationView.hidden, false);
		assert.equal(app.elements.presetsView.hidden, true);
		assert.equal(app.input().value, "90");
	}
	assert.equal(app.requests.length, 0);
});

test("a pending save locks editing/actions and rejects overlapping requests", async () => {
	const app = await dashboard();
	const pendingSave = deferred();
	app.onRequest = (request) => request.options.method === "POST"
		? pendingSave.promise : app.defaultResponse(request);
	app.edit(45);
	app.elements.saveButton.click();

	assert.equal(app.elements.formRoot.inert, true);
	for (const id of ["saveButton", "reloadButton", "reloadDiskButton", "resetButton", "unlockAdminButton", "applyAdminTokenButton"]) {
		assert.equal(app.elements[id].disabled, true, `${id} must be locked while saving`);
	}
	// Synthetic dispatch and the token field's Enter shortcut reach run() even
	// when the buttons are disabled, exercising the operation guard itself.
	app.elements.saveButton.dispatchEvent({ type: "click" });
	app.elements.reloadButton.dispatchEvent({ type: "click" });
	app.elements.adminToken.dispatchEvent({ type: "keydown", key: "Enter", preventDefault() {} });
	assert.equal(app.requests.length, 1);
	assert.equal(app.requests[0].url, "/tsc/config");
	assert.equal(JSON.parse(app.requests[0].options.body).requestCooldownSeconds, 45);

	pendingSave.resolve(response({ ...app.serverConfig, revision: 8, requestCooldownSeconds: 45 }));
	await settle();
	assert.equal(app.elements.formRoot.inert, false);
	assert.equal(app.elements.reloadButton.disabled, false);
	assert.equal(app.elements.saveButton.disabled, true, "A saved draft has no remaining changes");
	assert.equal(app.elements.changeStatus.textContent, "0 unsaved changes");
	assert.equal(app.elements.revisionStatus.textContent, "Revision 8");
});

test("a failed save releases the operation lock and keeps the draft for retry", async () => {
	const app = await dashboard();
	app.edit(45);
	app.onRequest = () => response({ error: "Server unavailable" }, 503);
	app.elements.saveButton.click();
	await settle();
	assert.equal(app.elements.toast.textContent, "Server unavailable");
	assert.equal(app.elements.formRoot.inert, false);
	assert.equal(app.elements.saveButton.disabled, false);
	assert.equal(app.elements.reloadDiskButton.disabled, false);
	assert.equal(app.elements.changeStatus.textContent, "1 unsaved change");
	assert.equal(Number(app.input().value), 45);

	app.onRequest = null;
	app.elements.saveButton.click();
	await settle();
	assert.equal(app.elements.toast.textContent, "Config saved");
	assert.equal(app.elements.changeStatus.textContent, "0 unsaved changes");
});

for (const button of ["reloadButton", "reloadDiskButton"]) {
	test(`cancelling ${button} retains unsaved inputs and sends no request`, async () => {
		const app = await dashboard();
		app.edit(45);
		app.confirmResult = false;
		app.elements[button].click();
		await settle();
		assert.equal(app.confirmations.length, 1);
		assert.match(app.confirmations[0], /unsaved/i);
		assert.equal(app.requests.length, 0);
		assert.equal(Number(app.input().value), 45);
		assert.equal(app.elements.changeStatus.textContent, "1 unsaved change");
		assert.equal(app.elements.saveButton.disabled, false);
	});
}

test("leaving warns only while the form has changes", async () => {
	const app = await dashboard();
	function navigationEvent() {
		const event = { type: "beforeunload", defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
		app.window.dispatchEvent(event);
		return event;
	}
	assert.equal(navigationEvent().defaultPrevented, false);
	app.edit(45);
	const dirtyEvent = navigationEvent();
	assert.equal(dirtyEvent.defaultPrevented, true);
	assert.equal(dirtyEvent.returnValue, "");
	app.edit(30);
	assert.equal(navigationEvent().defaultPrevented, false, "Returning a field to its saved value clears the warning");
});

test("a 409 keeps the draft and revision until the user confirms a reload", async () => {
	const app = await dashboard();
	app.edit(45);
	app.onRequest = () => response({ error: "Revision conflict" }, 409);
	app.elements.saveButton.click();
	await settle();
	assert.equal(app.requests.length, 1, "A conflict must not automatically reload over the draft");
	assert.match(app.elements.toast.textContent, /Settings changed.*Your edits are still here.*Reload Config/);
	assert.match(app.elements.toast.className, /is-error/);
	assert.equal(Number(app.input().value), 45);
	assert.equal(app.elements.revisionStatus.textContent, "Revision 7");
	assert.equal(app.elements.changeStatus.textContent, "1 unsaved change");
	assert.equal(app.elements.formRoot.inert, false);
	assert.equal(app.elements.saveButton.disabled, false);

	app.confirmResult = false;
	app.elements.reloadButton.click();
	await settle();
	assert.equal(app.requests.length, 1);
	assert.equal(Number(app.input().value), 45);

	app.onRequest = null;
	app.serverConfig = { ...app.serverConfig, revision: 9, requestCooldownSeconds: 60 };
	app.confirmResult = true;
	app.elements.reloadButton.click();
	await settle();
	assert.equal(Number(app.input().value), 60);
	assert.equal(app.elements.revisionStatus.textContent, "Revision 9");
	assert.equal(app.elements.changeStatus.textContent, "0 unsaved changes");
	assert.equal(app.elements.saveButton.disabled, true);
});

test("a failed confirmed reload keeps the existing draft", async () => {
	const app = await dashboard();
	app.edit(45);
	app.onRequest = ({ url }) => url === "/tsc/health"
		? response({ error: "Health request failed" }, 503)
		: response({ ...app.serverConfig, revision: 8, requestCooldownSeconds: 60 });
	app.elements.reloadButton.click();
	await settle();
	assert.equal(app.confirmations.length, 1);
	assert.equal(Number(app.input().value), 45);
	assert.equal(app.elements.revisionStatus.textContent, "Revision 7");
	assert.equal(app.elements.changeStatus.textContent, "1 unsaved change");
	assert.equal(app.elements.formRoot.inert, false);
});

test("cargo dimensions render native defaults and explain each zero value", async () => {
	const app = await dashboard();
	for (const [path, axis, max] of [["priorityExfil.gridWidth", "width", 10], ["priorityExfil.gridHeight", "height", 30]]) {
		const input = app.input(path);
		assert.equal(Number(input.value), 0);
		assert.equal(Number(input.min), 0);
		assert.equal(Number(input.max), max);
		assert.equal(Number(input.step), 1);
		const summary = descendants(app.field(path)).find((element) => element.className === "service-summary");
		assert.match(summary.textContent, new RegExp(`0 uses native ${axis}`));
		assert.match(summary.textContent, /when cargo next opens/);
	}
	assert.equal(app.elements.saveButton.disabled, true);
});

test("cargo dimensions save independently, preserve siblings, and round trip explicit zero", async () => {
	const app = await dashboard({ priorityExfil: { gridWidth: 6, gridHeight: 8, waitTimeSeconds: 60 } });
	app.edit(0, "priorityExfil.gridWidth");
	assert.equal(app.elements.changeStatus.textContent, "1 unsaved change");
	app.elements.saveButton.click();
	await settle();
	const firstSave = JSON.parse(app.requests.find((request) => request.options.method === "POST").options.body);
	assert.deepEqual(firstSave.priorityExfil, { gridWidth: 0, gridHeight: 8, waitTimeSeconds: 60 });
	assert.equal(firstSave.requestCooldownSeconds, 30);
	assert.equal(Number(app.input("priorityExfil.gridWidth").value), 0);
	assert.equal(Number(app.input("priorityExfil.gridHeight").value), 8);
	assert.equal(app.elements.changeStatus.textContent, "0 unsaved changes");

	app.edit(10, "priorityExfil.gridWidth");
	app.edit(30, "priorityExfil.gridHeight");
	assert.equal(app.elements.changeStatus.textContent, "2 unsaved changes");
	app.elements.saveButton.click();
	await settle();
	assert.deepEqual(app.serverConfig.priorityExfil, { gridWidth: 10, gridHeight: 30, waitTimeSeconds: 60 });
	app.edit(0, "priorityExfil.gridWidth");
	app.edit(0, "priorityExfil.gridHeight");
	app.elements.saveButton.click();
	await settle();
	app.elements.reloadButton.click();
	await settle();
	assert.deepEqual(app.serverConfig.priorityExfil, { gridWidth: 0, gridHeight: 0, waitTimeSeconds: 60 });
	assert.equal(Number(app.input("priorityExfil.gridWidth").value), 0);
	assert.equal(Number(app.input("priorityExfil.gridHeight").value), 0);
	assert.equal(app.elements.saveButton.disabled, true);
});

test("cargo dimension edits save only whole values within the schema bounds", async () => {
	const app = await dashboard();
	for (const [width, height, expectedWidth, expectedHeight] of [
		[25, -2, 10, 0],
		[-1, 99, 0, 30],
		[4.4, 8.7, 4, 9]
	]) {
		app.edit(width, "priorityExfil.gridWidth");
		app.edit(height, "priorityExfil.gridHeight");
		app.elements.saveButton.click();
		await settle();
		assert.equal(app.serverConfig.priorityExfil.gridWidth, expectedWidth);
		assert.equal(app.serverConfig.priorityExfil.gridHeight, expectedHeight);
		assert.equal(Number(app.input("priorityExfil.gridWidth").value), expectedWidth);
		assert.equal(Number(app.input("priorityExfil.gridHeight").value), expectedHeight);
	}
});

test("mixed service currencies show paired controls and coin quantities without cash sliders", async () => {
	const app = await dashboard({ serviceCurrencies: { A10: "GP", Extraction: "BTC", Uav: "RUB" } });
	const cards = descendants(app.elements.formRoot).filter((element) => element.className.includes("pricing-card"));
	assert.equal(cards.length, 6);
	for (const [key, currency] of [["A10", "GP"], ["Extraction", "BTC"], ["Uav", "RUB"]]) {
		const price = app.field(`prices.${key}`);
		assert.equal(descendants(price).find((element) => element.className === "field-label").textContent, `Price (${currency})`);
		assert.equal(app.select(`serviceCurrencies.${key}`).value, currency);
		assert.equal(descendants(price).some((element) => element.type === "range"), currency === "RUB");
		const card = cards.find((entry) => descendants(entry).includes(price));
		assert.ok(descendants(card).includes(app.field(`serviceCurrencies.${key}`)));
		if (currency !== "RUB") assert.match(descendants(card).find((entry) => entry.className === "service-summary").textContent, /from stash.*item count/);
	}
	assert.equal(app.select("serviceCurrencies.DoublePass").value, "Inherit");
});

test("changing the global currency updates inherited labels while preserving service overrides and amounts", async () => {
	const app = await dashboard({ serviceCurrencies: { A10: "GP", Extraction: "BTC", Uav: "Inherit" } });
	app.edit(1, "prices.A10");
	app.choose("USD", "paymentCurrency");
	assert.equal(Number(app.input("prices.A10").value), 1);
	assert.match(descendants(app.field("prices.A10")).find((entry) => entry.className === "field-label").textContent, /GP/);
	assert.match(descendants(app.field("prices.Uav")).find((entry) => entry.className === "field-label").textContent, /USD/);
	assert.match(app.select("serviceCurrencies.Uav").children[0].textContent, /Use global \(USD\)/);
	app.choose("RUB", "serviceCurrencies.Extraction");
	assert.equal(Number(app.input("prices.Extraction").value), 50000, "Changing assets must never silently convert or reset the authored amount");
	app.elements.saveButton.click();
	await settle();
	assert.equal(app.serverConfig.paymentCurrency, "USD");
	assert.equal(app.serverConfig.serviceCurrencies.A10, "GP");
	assert.equal(app.serverConfig.serviceCurrencies.Extraction, "RUB");
	assert.equal(app.serverConfig.prices.A10, 1);
	assert.equal(app.elements.changeStatus.textContent, "0 unsaved changes");
});

test("item payment choices save whole counts and never send authenticated inventory state as config", async () => {
	const app = await dashboard({ stashCurrencyBalances: { GP: 20, BTC: 3 }, stashCurrencyState: { items: [] },
		purchaseHistory: { entries: [] }, playerStateIncluded: true, uplinkUnlocked: true, progressionPermit: "profile-scoped" });
	app.choose("GP", "serviceCurrencies.A10");
	app.edit(1.4, "prices.A10");
	app.choose("BTC", "serviceCurrencies.Extraction");
	app.edit(2, "prices.Extraction");
	app.elements.saveButton.click();
	await settle();
	const saved = JSON.parse(app.requests.find((entry) => entry.options.method === "POST").options.body);
	assert.equal(saved.prices.A10, 1);
	assert.equal(saved.prices.Extraction, 2);
	assert.equal(saved.serviceCurrencies.A10, "GP");
	assert.equal(saved.serviceCurrencies.Extraction, "BTC");
	for (const field of ["stashCurrencyBalances", "stashCurrencyState", "purchaseHistory", "playerStateIncluded", "uplinkUnlocked", "progressionPermit"])
		assert.equal(Object.hasOwn(saved, field), false, `${field} must remain profile-scoped`);
});


test("preset preview and apply only stage changes until Save Config", async () => {
 const app = await dashboard();
 app.elements.previewPresetButton.click();
 await settle();
 assert.equal(app.elements.presetPreview.hidden, false);
 assert.ok(app.elements.presetChanges.children.length >= 1);
 assert.equal(app.requests.length, 0);
 assert.equal(app.input("prices.A10").value, 100000);
 app.elements.applyPresetButton.click();
 await settle();
 assert.equal(app.input("prices.A10").value, 75000);
 assert.equal(app.serverConfig.prices.A10, 100000);
 assert.equal(app.requests.length, 0);
 app.elements.saveButton.click();
 await settle();
 assert.equal(app.serverConfig.prices.A10, 75000);
 assert.equal(app.requests.filter(r => r.options.method === "POST").length, 1);
});

test("preset preview refreshes if the draft changes before applying", async () => {
 const app = await dashboard();
 app.elements.previewPresetButton.click();
 await settle();
 app.edit(120);
 app.elements.applyPresetButton.click();
 await settle();
 assert.equal(app.input().value, "120");
 assert.match(app.elements.toast.textContent, /draft changed/i);
 app.elements.applyPresetButton.click();
 await settle();
 assert.equal(app.input().value, 60);
 assert.equal(app.requests.length, 0);
});

test("invalid imported preset never mutates the draft or contacts the server", async () => {
 const app = await dashboard();
 app.elements.presetText.value = JSON.stringify({format:"tsc-preset",formatVersion:1,name:"Bad",settings:{"prices.A10":1,"adminDashboard.allowRemoteAccess":true}});
 app.elements.importPresetButton.click();
 await settle();
 assert.equal(app.input("prices.A10").value, 100000);
 assert.equal(app.requests.length, 0);
 assert.equal(app.elements.presetChanges.children.length, 0);
 assert.ok(app.elements.toast.className.includes("is-error"));
});

test("host preset save persists a filtered copy without changing live config", async () => {
 const app = await dashboard({authorizations:{A10:5},progressionPermit:"secret",adminDashboard:{allowRemoteAccess:true}});
 app.elements.presetName.value = "My preset";
 app.elements.presetNotes.value = "Shared with friends";
 app.edit(90);
 app.elements.savePresetButton.click();
 await settle();
 assert.equal(app.savedPresets.length, 1);
 assert.equal(app.savedPresets[0].settings.requestCooldownSeconds, 90);
 assert.equal(app.serverConfig.requestCooldownSeconds, 30);
 const payload = app.requests.find(r=>r.url==="/tsc/presets/saved" && r.options.method==="POST").options.body;
 assert.ok(!payload.includes("secret") && !payload.includes("authorizations") && !payload.includes("adminDashboard"));
 app.elements.reloadButton.click();
 await settle();
 assert.equal(app.savedPresets.length, 1);
 assert.match(app.elements.presetSelect.value, /^custom:/);
});

test("share code round-trips Unicode metadata and excludes private fields", async () => {
 const app = await dashboard({progressionPermit:"private-permit",stashCurrencyBalances:{GP:99}});
 app.elements.presetName.value = "Friend\u2019s \u914d\u7f6e";
 app.elements.presetNotes.value = "Easy evenings";
 app.elements.copyPresetButton.click();
 await settle();
 assert.match(app.copiedText, /^TSC1\./);
 assert.equal(app.elements.presetText.value, app.copiedText);
 assert.equal(app.requests.length, 0);
 app.elements.importPresetButton.click();
 await settle();
 assert.equal(app.elements.presetPreviewTitle.textContent, "Friend\u2019s \u914d\u7f6e");
 assert.equal(app.elements.applyPresetButton.disabled, true);
});

test("host save rejection preserves draft and does not invent a saved preset", async () => {
 const app = await dashboard();
 app.elements.presetName.value = "My preset";
 app.onRequest = r=>r.url==="/tsc/presets/saved" ? response({error:"No admin access"},403) : app.defaultResponse(r);
 app.edit(90);
 app.elements.savePresetButton.click();
 await settle();
 assert.equal(app.savedPresets.length, 0);
 assert.equal(app.input().value, "90");
 assert.equal(app.elements.presetControls.inert, false);
 assert.ok(app.elements.toast.className.includes("is-error"));
});
