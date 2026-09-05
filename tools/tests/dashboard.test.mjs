import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { runInNewContext } from "node:vm";

const webRoot = new URL("../../project/SamSWAT.FireSupport.Server/CopyToOutput/web/", import.meta.url);
const source = await readFile(new URL("app.mjs", webRoot), "utf8");
const html = await readFile(new URL("index.html", webRoot), "utf8");

// Execute the production script, including its startup and event handlers. This
// small DOM adapter tests request/state behavior without a browser dependency;
// it does not claim to validate layout or native browser inert behavior.
class Element {
	constructor(tagName = "div") {
		this.tagName = tagName;
		this.children = [];
		this.listeners = new Map();
		this.dataset = {};
		this.className = "";
		this.textContent = "";
		this.value = "";
		this.disabled = false;
		this.inert = false;
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
		if (!this.disabled) this.dispatchEvent({ type: "click" });
	}

	focus() {}
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

async function dashboard() {
	const elements = Object.fromEntries([...html.matchAll(/\bid="([^"]+)"/g)]
		.map((match) => [match[1], new Element()]));
	const window = new Element();
	const fixture = {
		elements,
		window,
		requests: [],
		confirmations: [],
		confirmResult: true,
		onRequest: null,
		serverConfig: { revision: 7, requestCooldownSeconds: 30, paymentCurrency: "RUB", paymentSource: "StashRoubles" }
	};
	const schema = { sections: [{ id: "main", label: "Main", fields: [
		{ path: "requestCooldownSeconds", label: "Request cooldown", type: "number", min: 0, max: 300 }
	] }] };
	const health = { ok: true, adminDashboard: { tokenRequired: false } };
	fixture.defaultResponse = ({ url, options }) => {
		if (url === "/tsc/schema") return response(schema);
		if (url === "/tsc/health" || url === "/tsc/admin/health") return response(health);
		if (url === "/tsc/config" && options.method !== "POST") return response(fixture.serverConfig);
		if (url === "/tsc/config" && options.method === "POST") {
			fixture.serverConfig = { ...JSON.parse(options.body), revision: fixture.serverConfig.revision + 1 };
			return response(fixture.serverConfig);
		}
		if (url === "/tsc/reload") return response(fixture.serverConfig);
		throw new Error(`Unexpected request: ${options.method || "GET"} ${url}`);
	};
	fixture.input = () => descendants(elements.formRoot).find((element) => element.type === "number");
	fixture.edit = (value) => {
		const input = fixture.input();
		assert.ok(input, "The real schema should render an editable input");
		input.value = String(value);
		input.dispatchEvent({ type: "input" });
	};
	runInNewContext(source, {
		document: {
			getElementById: (id) => elements[id] ?? null,
			createElement: (tagName) => new Element(tagName)
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
