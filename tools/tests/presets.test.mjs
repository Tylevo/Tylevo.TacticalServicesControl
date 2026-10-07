import assert from "node:assert/strict";
import test from "node:test";
import { portableFields, createPreset, validatePreset, parsePreset, encodePreset, applyPreset }
	from "../../project/SamSWAT.FireSupport.Server/CopyToOutput/web/presets.mjs";

const services = ["A10", "DoublePass", "Uav", "FocusedSweep", "Extraction", "PriorityExfil"];
const currencies = ["RUB", "USD", "EUR", "GP", "BTC"];
const number = (path, min, max, step = 1) => ({ path, label: `Label ${path}`, type: "number", min, max, step });
const select = (path, options) => ({ path, label: `Label ${path}`, type: "select", options });
const toggle = (path) => ({ path, label: `Label ${path}`, type: "toggle" });
function schema() {
	return { sections: [{ id: "settings", fields: [
		select("paymentMode", ["PhoneAuthorizations", "DirectRadial", "Hybrid"]),
		select("paymentCurrency", currencies), number("requestCooldownSeconds", 0, 1800, 15),
		toggle("purchasePersistence.enabled"), number("purchasePersistence.maxStoredAuthorizationsPerService", 1, 25),
		number("purchasePersistence.pendingUseTimeoutSeconds", 155, 1800, 5),
		toggle("purchasePersistence.spendCreditsBeforeCash"), toggle("purchasePersistence.allowAutoPurchaseOnUse"),
		...services.flatMap((key) => [number(`prices.${key}`, 0, 10000000),
			select(`serviceCurrencies.${key}`, ["Inherit", ...currencies]), toggle(`enabled.${key}`)]),
		...["uav", "focusedSweep"].flatMap((key) => [number(`${key}.durationSeconds`, 5, 1800, 5),
			number(`${key}.rangeMeters`, 25, 1000, 25), number(`${key}.scanIntervalSeconds`, 0.1, 10, 0.1)]),
		...["extraction", "priorityExfil"].flatMap((key) => [number(`${key}.dispatchDelaySeconds`, 0, 120),
			number(`${key}.waitTimeSeconds`, 5, 300, 5), number(`${key}.speedMultiplier`, 0.5, 3, 0.05)]),
		number("extraction.extractTimeSeconds", 1, 60), number("priorityExfil.gridWidth", 0, 10),
		number("priorityExfil.gridHeight", 0, 30), number("doublePass.secondPassDelaySeconds", 6, 45),
		{ path: "revision", type: "readonly" }, toggle("adminDashboard.allowRemoteAccess"),
		number("priorityExfil.extractTimeSeconds", 1, 60), number("a10.secondPassDelaySeconds", 0, 45),
		{ path: "progressionPermit", type: "text" }, number("prices.UnknownService", 0, 10000000)
	] }] };
}
function config() {
	return {
		configSchemaVersion: 4, revision: 17, paymentMode: "PhoneAuthorizations", paymentSource: "StashRoubles",
		paymentCurrency: "RUB", requestCooldownSeconds: 300,
		prices: Object.fromEntries(services.map((key) => [key, 100])),
		serviceCurrencies: Object.fromEntries(services.map((key) => [key, "Inherit"])),
		enabled: Object.fromEntries(services.map((key) => [key, true])),
		purchasePersistence: { enabled: true, maxStoredAuthorizationsPerService: 2, pendingUseTimeoutSeconds: 180,
			spendCreditsBeforeCash: true, allowAutoPurchaseOnUse: true, mode: "PersistentAuthorizations",
			consumeOn: "AuthorizationAccepted", refundFailedDispatch: true },
		uav: { durationSeconds: 480, rangeMeters: 200, scanIntervalSeconds: 5 },
		focusedSweep: { durationSeconds: 90, rangeMeters: 100, scanIntervalSeconds: 0.75 },
		extraction: { dispatchDelaySeconds: 8, waitTimeSeconds: 30, extractTimeSeconds: 10, speedMultiplier: 1 },
		priorityExfil: { dispatchDelaySeconds: 3, waitTimeSeconds: 20, extractTimeSeconds: 10, speedMultiplier: 1.35,
			gridWidth: 0, gridHeight: 0 }, a10: { secondPassDelaySeconds: 0 }, doublePass: { secondPassDelaySeconds: 14 },
		adminDashboard: { enabled: true, allowRemoteAccess: false, requireTokenForLocalhost: false },
		adminToken: "secret-token", playerStateIncluded: true, profileId: "private-profile", progressionPermit: "secret-permit",
		stashCurrencyBalances: { RUB: 10000, GP: 5 }, authorizations: { Uav: 2 }, purchaseHistory: { receipts: [{ id: "private" }] }
	};
}
const partial = (settings, extra = {}) => ({ format: "tsc-preset", formatVersion: 1,
	name: "Custom", description: "", settings, ...extra });
const deepFreeze = (value) => {
	Object.freeze(value);
	for (const item of Object.values(value)) if (item && typeof item === "object") deepFreeze(item);
	return value;
};

test("legacy wallet presets discard the removed setting without changing prices or player state", () => {
	for (const source of ["CarriedRoubles", "StashRoubles", "PreferCarriedThenStash", "PreferStashThenCarried"]) {
		const legacy = partial({ paymentSource: source, paymentCurrency: "USD", "prices.A10": 73 });
		const parsed = parsePreset(JSON.stringify(legacy), schema());
		assert.equal("paymentSource" in parsed.settings, false);
		assert.equal(parsed.settings.paymentCurrency, "USD");
		assert.equal(parsed.settings["prices.A10"], 73);
		const target = config();
		const applied = applyPreset(target, parsed, schema());
		assert.equal(applied.config.paymentSource, "StashRoubles");
		assert.deepEqual(applied.config.authorizations, target.authorizations);
		assert.deepEqual(applied.config.purchaseHistory, target.purchaseHistory);
		assert.equal(applied.changes.some((change) => change.path === "paymentSource"), false);
		assert.equal("paymentSource" in createPreset({ ...config(), paymentSource: source }, schema(), "Legacy").settings, false);
		// Raw old share codes go through the same migration as old JSON files.
		const legacyCode = `TSC1.${Buffer.from(JSON.stringify(legacy)).toString("base64url")}`;
		assert.deepEqual(parsePreset(legacyCode, schema()), parsed);
		assert.deepEqual(parsePreset(encodePreset(parsed), schema()), parsed);
		assert.equal(legacy.settings.paymentSource, source);
	}
	assert.throws(() => validatePreset(partial({ paymentSource: "Wallet" }), schema()), /unsupported option/);
	assert.throws(() => validatePreset(partial({ paymentSource: 1, "prices.A10": 73 }), schema()), /unsupported option/);
	assert.throws(() => validatePreset(partial({ paymentSource: "carriedroubles", "prices.A10": 73 }), schema()), /unsupported option/);
});

test("Unicode custom and built-in metadata round-trip through JSON and canonical UTF-8 share codes", () => {
	const original = createPreset(config(), schema(), "  \u591c\u9593 \u0440\u0435\u0439\u0434 \u{1f6f0}\ufe0f  ", "  Friends: caf\u00e9 / \u20bd / \u20ac  ");
	assert.equal(original.name, "\u591c\u9593 \u0440\u0435\u0439\u0434 \u{1f6f0}\ufe0f");
	assert.equal(original.description, "Friends: caf\u00e9 / \u20bd / \u20ac");
	const builtIn = validatePreset({ ...original, id: "tactical-1" }, schema());
	const code = encodePreset(builtIn);
	assert.match(code, /^TSC1\.[A-Za-z0-9_-]+$/);
	assert.deepEqual(parsePreset(code, schema()), builtIn);
	assert.deepEqual(parsePreset(JSON.stringify(builtIn, null, 2), schema()), builtIn);
	assert.equal(encodePreset(parsePreset(code, schema())), code);
});

test("exports intersect explicit gameplay paths with actual schema and omit all private/admin/dormant fields", () => {
	const source = config();
	const before = structuredClone(source);
	const preset = createPreset(source, schema(), "My settings");
	const keys = Object.keys(preset.settings);
	assert.equal(keys.length, 42);
	assert.equal("paymentSource" in preset.settings, false);
	assert.equal(preset.settings["focusedSweep.scanIntervalSeconds"], 0.75);
	assert.equal(keys.some((key) => /^(admin|revision|profile|stash|authorizations|purchaseHistory|progressionPermit)/i.test(key)), false);
	assert.equal("priorityExfil.extractTimeSeconds" in preset.settings, false);
	assert.equal("a10.secondPassDelaySeconds" in preset.settings, false);
	assert.equal("purchasePersistence.consumeOn" in preset.settings, false);
	assert.equal(JSON.stringify(preset).includes("secret"), false);
	assert.deepEqual(source, before);
	const smaller = schema();
	smaller.sections[0].fields = smaller.sections[0].fields.filter((field) => field.path !== "prices.A10");
	assert.equal("prices.A10" in createPreset(source, smaller, "Partial").settings, false);
	assert.throws(() => validatePreset(partial({ "prices.A10": 1 }), smaller));
});

test("portable descriptors preserve actual bounds without mutating the input schema", () => {
	const input = schema();
	const before = structuredClone(input);
	const fields = portableFields(input);
	assert.equal(fields.length, 42);
	assert.equal(fields.some((field) => field.path === "paymentSource"), false);
	assert.equal(fields.find((field) => field.path === "requestCooldownSeconds").step, 15);
	assert.throws(() => fields[0].options.push("Injected"));
	assert.throws(() => { fields[0].path = "adminToken"; });
	assert.deepEqual(input, before);
	const legacySchema = schema();
	legacySchema.sections[0].fields.push(select("paymentSource", ["CarriedRoubles", "StashRoubles"]));
	assert.equal(portableFields(legacySchema).some((field) => field.path === "paymentSource"), false);
	assert.equal("paymentSource" in createPreset(config(), legacySchema, "Old schema").settings, false);
	assert.throws(() => portableFields({}));
	assert.throws(() => portableFields({ sections: [{ fields: null }] }));
	input.sections[0].fields.push({ ...input.sections[0].fields[0] });
	assert.throws(() => portableFields(input));
});

test("envelope validation rejects unknown formats, unsafe metadata, and empty/nested settings", () => {
	for (const invalid of [null, [], {}, partial({}), partial({ "prices.A10": 1 }, { formatVersion: 2 }),
		partial({ "prices.A10": 1 }, { formatVersion: "1" }), partial({ "prices.A10": 1 }, { format: "tsc-config" }),
		partial({ "prices.A10": 1 }, { name: " " }), partial({ "prices.A10": 1 }, { name: "x".repeat(81) }),
		partial({ "prices.A10": 1 }, { name: "before\u0085after" }),
		partial({ "prices.A10": 1 }, { id: "../outside" }), partial({ "prices.A10": 1 }, { profileId: "someone" }),
		partial({ prices: { A10: 1 } }), partial({ "prices.A10": [1] })]) {
		assert.throws(() => validatePreset(invalid, schema()));
	}
});

test("prototype keys and accessors cannot enter the transport or invoke code", () => {
	for (const json of [
		'{"format":"tsc-preset","formatVersion":1,"name":"x","settings":{"__proto__":{"polluted":true}}}',
		'{"format":"tsc-preset","formatVersion":1,"name":"x","settings":{"constructor":1}}',
		'{"format":"tsc-preset","formatVersion":1,"name":"x","settings":{"constructor.prototype.polluted":1}}',
		'{"format":"tsc-preset","formatVersion":1,"name":"x","settings":{"prices.A10":1},"__proto__":{}}'
	]) assert.throws(() => parsePreset(json, schema()));
	let reads = 0;
	const value = partial({ "prices.A10": 1 });
	Object.defineProperty(value, "name", { enumerable: true, get: () => { reads++; return "bad"; } });
	assert.throws(() => validatePreset(value, schema()));
	const prototype = Object.create(null);
	Object.defineProperty(prototype, "name", { get: () => { reads++; return "inherited"; } });
	const inherited = Object.assign(Object.create(prototype), {
		format: "tsc-preset", formatVersion: 1, settings: { "prices.A10": 1 }
	});
	assert.throws(() => validatePreset(inherited, schema()));
	assert.equal(reads, 0);
	assert.equal({}.polluted, undefined);
});

test("numbers enforce scalar types, integer counts and server bounds without requiring UI step multiples", () => {
	for (const [path, value] of [["prices.A10", "100"], ["prices.A10", 1.5], ["prices.A10", -1],
		["prices.A10", 10000001], ["prices.A10", Infinity], ["uav.rangeMeters", NaN],
		["priorityExfil.gridWidth", 1.2], ["priorityExfil.gridHeight", 31], ["uav.durationSeconds", 5.1],
		["purchasePersistence.maxStoredAuthorizationsPerService", 2.5], ["enabled.Uav", 1]]) {
		assert.throws(() => validatePreset(partial({ [path]: value }), schema()), `${path}: ${value}`);
	}
	const accepted = validatePreset(partial({ "focusedSweep.scanIntervalSeconds": 0.75,
		"extraction.dispatchDelaySeconds": 1.5, "doublePass.secondPassDelaySeconds": 6.5,
		"requestCooldownSeconds": 31, "prices.A10": 0 }), schema());
	assert.equal(accepted.settings["focusedSweep.scanIntervalSeconds"], 0.75);
	const tighter = schema();
	tighter.sections[0].fields.find((field) => field.path === "prices.A10").max = 50;
	assert.throws(() => validatePreset(partial({ "prices.A10": 51 }), tighter));
});

test("enum fields accept only supported exact options in the current schema", () => {
	for (const [path, value] of [["paymentCurrency", "JPY"], ["paymentCurrency", "rub"],
		["paymentCurrency", "Inherit"], ["serviceCurrencies.Uav", ""], ["paymentSource", "Stash"],
		["paymentMode", false]]) assert.throws(() => validatePreset(partial({ [path]: value }), schema()));
	const older = schema();
	older.sections[0].fields.find((field) => field.path === "paymentCurrency").options = ["RUB", "USD", "EUR"];
	assert.throws(() => validatePreset(partial({ paymentCurrency: "GP" }), older));
	assert.equal(validatePreset(partial({ "serviceCurrencies.Uav": "BTC" }), schema()).settings["serviceCurrencies.Uav"], "BTC");
});

test("all-scope apply returns a detached config and exact changes while retaining private/server identity", () => {
	const target = deepFreeze(config());
	const preset = deepFreeze(partial({ "prices.A10": 2, paymentCurrency: "GP", "enabled.Uav": false }));
	const result = applyPreset(target, preset, schema());
	assert.deepEqual(result.changes, [
		{ path: "paymentCurrency", label: "Label paymentCurrency", before: "RUB", after: "GP" },
		{ path: "prices.A10", label: "Label prices.A10", before: 100, after: 2 },
		{ path: "enabled.Uav", label: "Label enabled.Uav", before: true, after: false }
	]);
	assert.equal(result.config.revision, 17);
	assert.equal(result.config.adminToken, target.adminToken);
	assert.deepEqual(result.config.purchaseHistory, target.purchaseHistory);
	assert.notEqual(result.config.purchaseHistory, target.purchaseHistory);
	result.config.authorizations.Uav = 99;
	assert.equal(target.authorizations.Uav, 2);
	assert.equal(target.prices.A10, 100);
});

test("pricing scope changes only prices and currencies, preserving mode, enabled services and timing", () => {
	const target = config();
	const value = partial({ paymentCurrency: "USD", paymentSource: "StashRoubles", paymentMode: "Hybrid",
		"requestCooldownSeconds": 60, "prices.Uav": 50, "serviceCurrencies.Uav": "Inherit", "enabled.Uav": false,
		"uav.durationSeconds": 30, "purchasePersistence.enabled": false });
	const result = applyPreset(target, value, schema(), "pricing");
	assert.equal(result.config.paymentCurrency, "USD");
	assert.equal(result.config.paymentSource, "StashRoubles");
	assert.equal(result.config.prices.Uav, 50);
	assert.equal(result.config.paymentMode, target.paymentMode);
	assert.equal(result.config.requestCooldownSeconds, target.requestCooldownSeconds);
	assert.equal(result.config.enabled.Uav, true);
	assert.equal(result.config.uav.durationSeconds, 480);
	assert.equal(result.config.purchasePersistence.enabled, true);
});

test("recon scope pins preset inheritance so USD prices do not become target GP counts", () => {
	const target = config();
	target.paymentCurrency = "GP";
	const value = partial({ paymentCurrency: "USD", paymentSource: "StashRoubles", "prices.Uav": 100,
		"serviceCurrencies.Uav": "Inherit", "enabled.Uav": false, "uav.durationSeconds": 90,
		"prices.A10": 200, "prices.Extraction": 500, "extraction.waitTimeSeconds": 40 });
	const result = applyPreset(target, value, schema(), "recon");
	assert.equal(result.config.serviceCurrencies.Uav, "USD");
	assert.equal(result.config.paymentCurrency, "GP");
	assert.equal(result.config.paymentSource, "StashRoubles");
	assert.equal(result.config.enabled.Uav, false);
	assert.equal(result.config.uav.durationSeconds, 90);
	assert.equal(result.config.prices.A10, 100);
	assert.equal(result.config.prices.Extraction, 100);
	assert.equal(result.config.extraction.waitTimeSeconds, 30);
	assert.equal(result.changes.find((change) => change.path === "serviceCurrencies.Uav").after, "USD");
});

test("partial category inheritance uses current global; explicit currencies remain exact and missing defaults fail", () => {
	const target = config();
	target.paymentCurrency = "EUR";
	assert.equal(applyPreset(target, partial({ "serviceCurrencies.Uav": "Inherit" }), schema(), "recon")
		.config.serviceCurrencies.Uav, "EUR");
	assert.equal(applyPreset(target, partial({ "serviceCurrencies.Uav": "BTC" }), schema(), "recon")
		.config.serviceCurrencies.Uav, "BTC");
	delete target.paymentCurrency;
	assert.throws(() => applyPreset(target, partial({ "serviceCurrencies.Uav": "Inherit" }), schema(), "recon"));
});

test("extraction and fire category scopes include their own prices/toggles/timing without global effects", () => {
	const value = partial({ paymentCurrency: "RUB", "prices.A10": 10, "serviceCurrencies.A10": "Inherit",
		"enabled.A10": false, "doublePass.secondPassDelaySeconds": 7, "prices.Extraction": 20,
		"prices.PriorityExfil": 30, "serviceCurrencies.PriorityExfil": "GP", "enabled.PriorityExfil": false,
		"priorityExfil.gridWidth": 5, "extraction.dispatchDelaySeconds": 12, "uav.durationSeconds": 30 });
	const fire = applyPreset(config(), value, schema(), "fire").config;
	assert.equal(fire.prices.A10, 10);
	assert.equal(fire.enabled.A10, false);
	assert.equal(fire.doublePass.secondPassDelaySeconds, 7);
	assert.equal(fire.prices.Extraction, 100);
	const extraction = applyPreset(config(), value, schema(), "extraction").config;
	assert.equal(extraction.prices.Extraction, 20);
	assert.equal(extraction.prices.PriorityExfil, 30);
	assert.equal(extraction.priorityExfil.gridWidth, 5);
	assert.equal(extraction.extraction.dispatchDelaySeconds, 12);
	assert.equal(extraction.enabled.PriorityExfil, false);
	assert.equal(extraction.prices.A10, 100);
	assert.equal(extraction.uav.durationSeconds, 480);
});

test("validation precedes all changes even for invalid fields outside selected scope", () => {
	const target = config();
	const before = structuredClone(target);
	assert.throws(() => applyPreset(target, partial({ "prices.Uav": 5, "priorityExfil.gridWidth": 500 }), schema(), "recon"));
	assert.throws(() => applyPreset(target, partial({ "prices.Uav": 5, "adminDashboard.enabled": false }), schema()));
	assert.throws(() => applyPreset(target, partial({ "prices.Uav": 5 }), schema(), "unknown"));
	assert.deepEqual(target, before);
});

test("partial extraction changes respect the resulting wait/countdown relationship", () => {
	assert.throws(() => validatePreset(partial({ "extraction.waitTimeSeconds": 10, "extraction.extractTimeSeconds": 10 }), schema()));
	const target = config();
	const before = structuredClone(target);
	const value = validatePreset(partial({ "extraction.waitTimeSeconds": 5 }), schema());
	assert.throws(() => applyPreset(target, value, schema(), "extraction"));
	assert.deepEqual(target, before);
	assert.equal(applyPreset(target, partial({ "extraction.waitTimeSeconds": 11 }), schema()).config.extraction.waitTimeSeconds, 11);
});

test("parser rejects malformed/noncanonical codes, invalid UTF-8 and byte-sized document limits", () => {
	for (const text of ["", "null", "{}", "TSC2.abc", "TSC1.", "TSC1.a", "TSC1.@@", "TSC1.e30=",
		"TSC1._w", "TSC1.ey", "TSC1." + "A".repeat(48 * 1024), " ".repeat(33 * 1024) + "{}",
		JSON.stringify(partial({ "prices.A10": 1 }, { description: "\u{1f6f0}\ufe0f".repeat(5000) }))]) {
		assert.throws(() => parsePreset(text, schema()), text.slice(0, 50));
	}
	const oversizedEncoded = "TSC1." + btoa(" ".repeat(32769)).replace(/=+$/, "");
	assert.throws(() => parsePreset(oversizedEncoded, schema()), /32 KB/);
	assert.throws(() => encodePreset(partial({ "prices.A10": Infinity })));
});

test("scope with no matching fields returns a detached unchanged draft and no changes", () => {
	const target = config();
	const result = applyPreset(target, partial({ "prices.Uav": 1 }), schema(), "fire");
	assert.deepEqual(result.config, target);
	assert.notEqual(result.config, target);
	assert.deepEqual(result.changes, []);
});
