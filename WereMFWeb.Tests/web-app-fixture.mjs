import { readFile } from "node:fs/promises";

export async function loadWebApp() {
  const appEl = { innerHTML: "" };
  const toastEl = { textContent: "", classList: { add() {}, remove() {} } };
  const scheduled = [];
  const playedSounds = [];
  let nextTimer = 1;
  const documentObject = {
    title: "MF 杀", hidden: false, visibilityState: "visible", activeElement: null,
    querySelector(selector) { return selector === "#app" ? appEl : selector === "#toast" ? toastEl : null; },
    querySelectorAll() { return []; }, addEventListener() {}
  };
  class FakeWebSocket { static OPEN = 1; constructor() { this.readyState = 0; } addEventListener() {} close() {} }
  class FakeAudio { constructor(src) { this.src = src; this.currentTime = 0; this.volume = 1; this.muted = false; } addEventListener() {} play() { playedSounds.push(this.src); return Promise.resolve(); } pause() {} }
  const storage = new Map();
  globalThis.document = documentObject;
  globalThis.location = { search: "", protocol: "http:", host: "127.0.0.1" };
  globalThis.localStorage = { getItem: key => storage.get(key) || null, setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) };
  Object.defineProperty(globalThis, "navigator", { configurable: true, value: { clipboard: { writeText: async () => {} } } });
  globalThis.WebSocket = FakeWebSocket; globalThis.Audio = FakeAudio;
  globalThis.setTimeout = (fn, delay) => { const id = nextTimer++; scheduled.push({ id, fn, delay }); return id; };
  globalThis.clearTimeout = id => { const index = scheduled.findIndex(item => item.id === id); if (index >= 0) scheduled.splice(index, 1); };
  globalThis.setInterval = () => 0; globalThis.clearInterval = () => {};
  globalThis.confirm = () => true;
  const source = await readFile(new URL("../WereMFWeb/src/app.js", import.meta.url), "utf8");
  const module = await import(new URL("../WereMFWeb/src/app.js?test-fixture", import.meta.url));
  return { ui: module, appEl, toastEl, scheduled, playedSounds, source };
}
