import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";
import { applyEntityStatePatch, applyFullEntitySnapshot, createState, entityId } from "../WereMFWeb/src/store.js";

const port = 5270;
const serverExe = fileURLToPath(new URL("../WereMFServer/bin/Release/net8.0/WereMFServer.exe", import.meta.url));
const gameExe = fileURLToPath(new URL("../WereMF/bin/Release/net8.0/WereMF.exe", import.meta.url));
const config = fileURLToPath(new URL("../WereMF/config.json", import.meta.url));
const env = { ...process.env, HTTP_PROXY: "", HTTPS_PROXY: "", ALL_PROXY: "", NO_PROXY: "*", SILICONFLOW_API_KEY: "", LLM_FALLBACK_BASE_URL: "" };
const server = spawn(serverExe, ["--path", gameExe, "--config", config, "--host", "127.0.0.1", "--port", String(port), "--seed", "1945294126", "--event-interval-ms", "0", "--disable-llm-bots"], { windowsHide: true, env });
let output = "";
server.stdout.on("data", value => output += value);
server.stderr.on("data", value => output += value);

async function waitServer() {
  for (let attempt = 0; attempt < 100; attempt++) {
    try { if ((await fetch(`http://127.0.0.1:${port}/api/health`)).ok) return; } catch {}
    await delay(100);
  }
  throw new Error(`server did not start\n${output}`);
}

class Client {
  constructor(name) { this.name = name; this.messages = []; }
  async open(first) {
    this.ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
    this.ws.addEventListener("message", event => {
      const message = JSON.parse(event.data);
      this.messages.push(message);
      const payload = message.type === "game_message" ? message.payload : null;
      if (payload?.api?.startsWith("request_") && !payload.api.endsWith("_parse_error") && payload.api !== "request_vote") {
        const value = payload.api === "request_huika_skill" ? "1" : "0";
        this.ws.send(JSON.stringify({ type: "game_input", value }));
      }
    });
    await new Promise((resolve, reject) => {
      this.ws.addEventListener("open", resolve, { once: true });
      this.ws.addEventListener("error", reject, { once: true });
    });
    this.ws.send(JSON.stringify(first));
    return this.wait(message => message.type === "welcome");
  }
  wait(predicate, timeout = 15000) {
    const existing = this.messages.findLast(predicate);
    if (existing) return Promise.resolve(existing);
    return new Promise((resolve, reject) => {
      const onMessage = event => {
        const message = JSON.parse(event.data);
        if (!predicate(message)) return;
        clearTimeout(timer);
        this.ws.removeEventListener("message", onMessage);
        resolve(message);
      };
      const timer = setTimeout(() => {
        this.ws.removeEventListener("message", onMessage);
        reject(new Error(`${this.name} timed out\n${output}\n${JSON.stringify(this.messages.slice(-10), null, 2)}`));
      }, timeout);
      this.ws.addEventListener("message", onMessage);
    });
  }
  close() { try { this.ws?.close(); } catch {} }
}

const names = ["LAKE", "NL", "TNT", "大爷", "116", "HJM", "冻双"];
const clients = [];
try {
  await waitServer();
  const host = new Client(names[0]);
  clients.push(host);
  const welcome = await host.open({ type: "create_room", playerName: names[0] });
  for (const name of names.slice(1)) {
    const client = new Client(name);
    clients.push(client);
    await client.open({ type: "join_room", roomCode: welcome.roomCode, playerName: name });
  }
  await delay(100);
  host.ws.send(JSON.stringify({ type: "start_game" }));

  const patchMessage = await host.wait(message => message.type === "game_message" && message.payload?.api === "game_update_night_patch", 30000);
  const patch = patchMessage.payload;
  assert.equal(patch.message_type, "public");
  assert.equal(patch.data.cause, "huika_smog");
  assert.deepEqual(patch.data.entities, [{ player_id: 1, state: { smog_count: 1 } }]);

  for (const client of clients) {
    await client.wait(message => message.type === "game_message" && message.payload?.api === "game_update_night_patch");
    const snapshots = client.messages.filter(message => message.type === "game_message" && message.payload?.api === "game_update_night");
    const patches = client.messages.filter(message => message.type === "game_message" && message.payload?.api === "game_update_night_patch");
    assert.ok(snapshots.length > 0, `${client.name} missing night snapshot`);
    assert.equal(patches.length, 1, `${client.name} patch count`);
    const state = createState();
    applyFullEntitySnapshot(state, snapshots.at(-1).payload.data);
    assert.equal(applyEntityStatePatch(state, patches[0].payload.data), true);
    assert.equal(state.entities.find(entity => entityId(entity) === 1).state.smog_count, 1);
  }
  console.log(JSON.stringify({ ok: true, clients: clients.length, patch: patch.data }));
} finally {
  for (const client of clients) client.close();
  server.kill();
  await delay(300);
}
