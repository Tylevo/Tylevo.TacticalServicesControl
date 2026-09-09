const FORMAT = "tsc-preset";
const FORMAT_VERSION = 1;
const CODE_PREFIX = "TSC1.";
const MAX_JSON_BYTES = 32 * 1024;
const MAX_CODE_LENGTH = 48 * 1024;
const CURRENCIES = Object.freeze(["RUB", "USD", "EUR", "GP", "BTC"]);
const SOURCES = Object.freeze(["CarriedRoubles", "StashRoubles", "PreferCarriedThenStash", "PreferStashThenCarried"]);
const FORBIDDEN_KEYS = new Set(["__proto__", "prototype", "constructor"]);
const SCOPES = new Set(["all", "pricing", "recon", "extraction", "fire"]);
const own = (value, key) => Object.prototype.hasOwnProperty.call(value, key);
const fail = (message) => { throw new Error(`Invalid TSC preset: ${message}`); };
const encoder = new TextEncoder();
const decoder = new TextDecoder("utf-8", { fatal: true });

// Paths are intentionally enumerated. A future server schema cannot turn an
// admin setting or a profile record into portable gameplay configuration.
const policies = {};
function field(path, type, scopes, { min, max, integer = false, options } = {}) {
	policies[path] = Object.freeze({ path, type, scopes: Object.freeze(scopes), min, max, integer,
		options: options ? Object.freeze([...options]) : undefined });
}
field("paymentMode", "select", [], { options: ["PhoneAuthorizations", "DirectRadial", "Hybrid"] });
field("paymentSource", "select", ["pricing"], { options: SOURCES });
field("paymentCurrency", "select", ["pricing"], { options: CURRENCIES });
field("requestCooldownSeconds", "number", [], { min: 0, max: 1800, integer: true });
field("purchasePersistence.enabled", "toggle", []);
field("purchasePersistence.maxStoredAuthorizationsPerService", "number", [], { min: 1, max: 25, integer: true });
field("purchasePersistence.pendingUseTimeoutSeconds", "number", [], { min: 155, max: 1800, integer: true });
field("purchasePersistence.spendCreditsBeforeCash", "toggle", []);
field("purchasePersistence.allowAutoPurchaseOnUse", "toggle", []);
for (const [service, scope] of [["A10", "fire"], ["DoublePass", "fire"], ["Uav", "recon"],
	["FocusedSweep", "recon"], ["Extraction", "extraction"], ["PriorityExfil", "extraction"]]) {
	field(`prices.${service}`, "number", ["pricing", scope], { min: 0, max: 10000000, integer: true });
	field(`serviceCurrencies.${service}`, "select", ["pricing", scope], { options: ["Inherit", ...CURRENCIES] });
	field(`enabled.${service}`, "toggle", [scope]);
}
for (const service of ["uav", "focusedSweep"]) {
	field(`${service}.durationSeconds`, "number", ["recon"], { min: 5, max: 1800, integer: true });
	field(`${service}.rangeMeters`, "number", ["recon"], { min: 25, max: 1000 });
	field(`${service}.scanIntervalSeconds`, "number", ["recon"], { min: 0.1, max: 10 });
}
for (const service of ["extraction", "priorityExfil"]) {
	field(`${service}.dispatchDelaySeconds`, "number", ["extraction"], { min: 0, max: 120 });
	field(`${service}.waitTimeSeconds`, "number", ["extraction"], { min: 5, max: 300, integer: true });
	field(`${service}.speedMultiplier`, "number", ["extraction"], { min: 0.5, max: 3 });
}
field("extraction.extractTimeSeconds", "number", ["extraction"], { min: 1, max: 60 });
field("priorityExfil.gridWidth", "number", ["extraction"], { min: 0, max: 10, integer: true });
field("priorityExfil.gridHeight", "number", ["extraction"], { min: 0, max: 30, integer: true });
field("doublePass.secondPassDelaySeconds", "number", ["fire"], { min: 6, max: 45 });
Object.freeze(policies);

function record(value, context) {
	if (value === null || typeof value !== "object" || Array.isArray(value)) fail(`${context} must be an object.`);
	const prototype = Object.getPrototypeOf(value);
	if (prototype !== null && Object.getPrototypeOf(prototype) !== null) fail(`${context} must be a plain object.`);
	for (const key of Reflect.ownKeys(value)) {
		if (typeof key !== "string" || FORBIDDEN_KEYS.has(key)) fail(`${context} contains a forbidden key.`);
		const descriptor = Object.getOwnPropertyDescriptor(value, key);
		if (!own(descriptor, "value") || !descriptor.enumerable) fail(`${context} must contain plain JSON values.`);
	}
	return value;
}

function getPath(config, path) {
	let current = config;
	for (const key of path.split(".")) {
		if (current === null || typeof current !== "object" || !own(current, key)) return undefined;
		const descriptor = Object.getOwnPropertyDescriptor(current, key);
		if (!own(descriptor, "value")) fail("configuration must contain plain JSON values.");
		current = descriptor.value;
	}
	return current;
}

function setPath(config, path, value) {
	const parts = path.split(".");
	let current = config;
	for (const part of parts.slice(0, -1)) {
		if (!own(current, part) || current[part] === null) current[part] = {};
		record(current[part], part);
		current = current[part];
	}
	current[parts.at(-1)] = value;
}

function cloneJson(value, ancestors = new Set(), depth = 0) {
	if (value === null || typeof value === "string" || typeof value === "boolean") return value;
	if (typeof value === "number" && Number.isFinite(value)) return value;
	if (typeof value !== "object" || depth > 64 || ancestors.has(value)) fail("configuration is not a JSON document.");
	ancestors.add(value);
	let copy;
	if (Array.isArray(value)) {
		copy = Array.from(value, (item) => cloneJson(item, ancestors, depth + 1));
	} else {
		record(value, "configuration");
		copy = Object.fromEntries(Object.entries(value).map(([key, item]) => [key, cloneJson(item, ancestors, depth + 1)]));
	}
	ancestors.delete(value);
	return copy;
}

export function portableFields(schema) {
	record(schema, "schema");
	if (!Array.isArray(schema.sections)) fail("schema.sections must be an array.");
	const fields = [];
	const seen = new Set();
	for (const section of schema.sections) {
		record(section, "schema section");
		if (!Array.isArray(section.fields)) fail("schema section fields must be an array.");
		for (const descriptor of section.fields) {
			record(descriptor, "schema field");
			if (typeof descriptor.path !== "string") fail("schema field path is missing.");
			if (!own(policies, descriptor.path)) continue;
			const policy = policies[descriptor.path];
			if (seen.has(descriptor.path) || descriptor.type !== policy.type) fail(`unsupported schema for ${descriptor.path}.`);
			seen.add(descriptor.path);
			const copy = { ...descriptor };
			for (const bound of ["min", "max", "step"]) {
				if (copy[bound] != null && (typeof copy[bound] !== "number" || !Number.isFinite(copy[bound]))) fail(`invalid schema ${bound}.`);
			}
			if (copy.min != null && copy.max != null && copy.min > copy.max || copy.step != null && copy.step <= 0) fail("invalid schema bounds.");
			if (policy.type === "select") {
				if (!Array.isArray(copy.options) || copy.options.length === 0 || copy.options.some((option) => typeof option !== "string")) fail("invalid schema options.");
				copy.options = Object.freeze([...copy.options]);
			}
			fields.push(Object.freeze(copy));
		}
	}
	return Object.freeze(fields);
}

function validateValue(path, value, descriptor) {
	const policy = policies[path];
	if (policy.type === "toggle") {
		if (typeof value !== "boolean") fail(`${path} must be true or false.`);
	} else if (policy.type === "select") {
		if (typeof value !== "string" || !policy.options.includes(value) || !descriptor.options.includes(value)) fail(`${path} has an unsupported option.`);
	} else {
		if (typeof value !== "number" || !Number.isFinite(value) || policy.integer && !Number.isSafeInteger(value)) fail(`${path} must be a ${policy.integer ? "whole" : "finite"} number.`);
		const min = Math.max(policy.min, descriptor.min ?? policy.min);
		const max = Math.min(policy.max, descriptor.max ?? policy.max);
		if (value < min || value > max) fail(`${path} must be between ${min} and ${max}.`);
	}
	return Object.is(value, -0) ? 0 : value;
}

function metadata(value, key, maxLength, required = false) {
	if (!own(value, key)) {
		if (required) fail(`${key} is required.`);
		return "";
	}
	if (typeof value[key] !== "string") fail(`${key} must be text.`);
	const text = value[key].trim();
	if (required && !text || text.length > maxLength || key !== "description" && /[\u0000-\u001f\u007f-\u009f]/u.test(text)) fail(`${key} is empty or too long.`);
	return text;
}

function checkExtractionWindow(settings) {
	const wait = settings["extraction.waitTimeSeconds"];
	const countdown = settings["extraction.extractTimeSeconds"];
	if (typeof wait === "number" && typeof countdown === "number" && wait < countdown + 1) fail("extraction wait time must exceed its countdown by at least one second.");
}

function checkedJson(preset) {
	const json = JSON.stringify(preset);
	if (encoder.encode(json).length > MAX_JSON_BYTES) fail("JSON exceeds the 32 KB limit.");
	return json;
}

function sanitize(value, fields) {
	record(value, "preset");
	const allowedMetadata = new Set(["format", "formatVersion", "id", "name", "description", "settings"]);
	if (Object.keys(value).some((key) => !allowedMetadata.has(key))) fail("unknown metadata field.");
	if (!own(value, "format") || !own(value, "formatVersion") || value.format !== FORMAT || value.formatVersion !== FORMAT_VERSION) fail("unsupported format or format version.");
	if (!own(value, "settings")) fail("settings are required.");
	const preset = { format: FORMAT, formatVersion: FORMAT_VERSION };
	if (own(value, "id")) {
		preset.id = metadata(value, "id", 64, true);
		if (!/^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/u.test(preset.id)) fail("id must be a simple preset identifier.");
	}
	preset.name = metadata(value, "name", 80, true);
	preset.description = metadata(value, "description", 500);
	record(value.settings, "settings");
	const descriptors = new Map(fields.map((descriptor) => [descriptor.path, descriptor]));
	preset.settings = {};
	for (const [path, setting] of Object.entries(value.settings)) {
		if (!descriptors.has(path) || !own(policies, path)) fail(`unknown or unavailable setting ${path}.`);
		preset.settings[path] = validateValue(path, setting, descriptors.get(path));
	}
	if (Object.keys(preset.settings).length === 0) fail("at least one supported setting is required.");
	checkExtractionWindow(preset.settings);
	checkedJson(preset);
	return preset;
}

export function validatePreset(value, schema) {
	return sanitize(value, portableFields(schema));
}

export function createPreset(config, schema, name, description = "") {
	record(config, "configuration");
	const fields = portableFields(schema);
	const settings = {};
	for (const descriptor of fields) {
		const value = getPath(config, descriptor.path);
		if (value !== undefined) settings[descriptor.path] = value;
	}
	return sanitize({ format: FORMAT, formatVersion: FORMAT_VERSION, name, description, settings }, fields);
}

function base64Url(bytes) {
	let binary = "";
	for (const byte of bytes) binary += String.fromCharCode(byte);
	return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}

export function encodePreset(preset) {
	const sanitized = sanitize(preset, Object.values(policies));
	return CODE_PREFIX + base64Url(encoder.encode(checkedJson(sanitized)));
}

export function parsePreset(text, schema) {
	if (typeof text !== "string") fail("paste JSON or a TSC1 share code.");
	if (text.length > MAX_CODE_LENGTH) fail("input exceeds the 48 KB limit.");
	let json = text.trim();
	if (!json.startsWith(CODE_PREFIX) && encoder.encode(text).length > MAX_JSON_BYTES) fail("JSON exceeds the 32 KB limit.");
	if (json.startsWith(CODE_PREFIX)) {
		const encoded = json.slice(CODE_PREFIX.length);
		if (!/^[A-Za-z0-9_-]+$/u.test(encoded) || encoded.length % 4 === 1) fail("malformed share code.");
		let bytes;
		try {
			const binary = atob(encoded.replaceAll("-", "+").replaceAll("_", "/") + "=".repeat((4 - encoded.length % 4) % 4));
			bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
		} catch { fail("share code is not valid base64url."); }
		if (base64Url(bytes) !== encoded) fail("noncanonical share code.");
		if (bytes.length > MAX_JSON_BYTES) fail("JSON exceeds the 32 KB limit.");
		try { json = decoder.decode(bytes); }
		catch { fail("share code is not valid UTF-8."); }
	}
	if (encoder.encode(json).length > MAX_JSON_BYTES) fail("JSON exceeds the 32 KB limit.");
	let value;
	try { value = JSON.parse(json); }
	catch { fail("content is not valid JSON."); }
	return validatePreset(value, schema);
}

export function applyPreset(config, preset, schema, scope = "all") {
	if (!SCOPES.has(scope)) fail("unknown apply scope.");
	const fields = portableFields(schema);
	const sanitized = sanitize(preset, fields); // Validate every field, including those outside the selected scope.
	record(config, "configuration");
	const selected = {};
	for (const descriptor of fields) {
		const path = descriptor.path;
		if (own(sanitized.settings, path) && (scope === "all" || policies[path].scopes.includes(scope))) selected[path] = sanitized.settings[path];
	}
	if (["recon", "extraction", "fire"].includes(scope)) {
		for (const path of Object.keys(selected)) {
			if (!path.startsWith("serviceCurrencies.") || selected[path] !== "Inherit") continue;
			const currency = sanitized.settings.paymentCurrency ?? getPath(config, "paymentCurrency");
			if (!CURRENCIES.includes(currency)) fail("an inherited service currency needs a valid default currency.");
			selected[path] = validateValue(path, currency, fields.find((descriptor) => descriptor.path === path));
		}
	}
	const copy = cloneJson(config);
	const changes = [];
	for (const descriptor of fields) {
		if (!own(selected, descriptor.path)) continue;
		const before = getPath(config, descriptor.path);
		const after = selected[descriptor.path];
		if (Object.is(before, after)) continue;
		setPath(copy, descriptor.path, after);
		changes.push({ path: descriptor.path, label: descriptor.label || descriptor.path, before, after });
	}
	checkExtractionWindow({
		"extraction.waitTimeSeconds": getPath(copy, "extraction.waitTimeSeconds"),
		"extraction.extractTimeSeconds": getPath(copy, "extraction.extractTimeSeconds")
	});
	return { config: copy, changes };
}
