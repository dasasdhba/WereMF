import { spawn } from "node:child_process";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";

const port = 5196;
const serverExe = fileURLToPath(new URL("../WereMFServer/bin/Release/net8.0/WereMFServer.exe", import.meta.url));
const gameExe = fileURLToPath(new URL("./chat-fake/bin/Release/net8.0/win-x64/publish/ChatFake.exe", import.meta.url));
const env = { ...process.env, HTTP_PROXY: "", HTTPS_PROXY: "", ALL_PROXY: "", NO_PROXY: "*", SILICONFLOW_API_KEY: "", LLM_FALLBACK_BASE_URL: "" };
const server = spawn(serverExe, ["--path", gameExe, "--host", "127.0.0.1", "--port", String(port)], { windowsHide: true, env });
let output = "";
server.stdout.on("data", value => output += value);
server.stderr.on("data", value => output += value);

const assert = (condition, message) => { if (!condition) throw new Error(message); };
const waitServer = async () => {
  for (let i = 0; i < 100; i++) {
    try { if ((await fetch(`http://127.0.0.1:${port}/api/health`)).ok) return; } catch {}
    await delay(100);
  }
  throw new Error(`server did not start\n${output}`);
};

class Client {
  constructor(name) { this.name = name; this.messages = []; this.waiters = []; }
  async open(first) {
    this.ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
    this.ws.addEventListener("message", event => {
      const message = JSON.parse(event.data);
      this.messages.push(message);
      for (const waiter of [...this.waiters]) {
        if (!waiter.predicate(message)) continue;
        this.waiters.splice(this.waiters.indexOf(waiter), 1);
        waiter.resolve(message);
      }
    });
    await new Promise((resolve, reject) => {
      this.ws.addEventListener("open", resolve, { once: true });
      this.ws.addEventListener("error", reject, { once: true });
    });
    this.ws.send(JSON.stringify(first));
    return this;
  }
  send(message) { this.ws.send(JSON.stringify(message)); }
  wait(predicate, timeout = 10000) {
    const found = this.messages.findLast(predicate);
    if (found) return Promise.resolve(found);
    return new Promise((resolve, reject) => {
      const waiter = { predicate, resolve };
      this.waiters.push(waiter);
      setTimeout(() => {
        const index = this.waiters.indexOf(waiter);
        if (index >= 0) this.waiters.splice(index, 1);
        reject(new Error(`${this.name} timed out\n${output}\n${JSON.stringify(this.messages.slice(-8), null, 2)}`));
      }, timeout);
    });
  }
  close() { try { this.ws?.close(); } catch {} }
}

const clients = [];
try {
  await waitServer();
  const host = await new Client("host").open({ type: "create_room", playerName: "Host" }); clients.push(host);
  const welcome = await host.wait(message => message.type === "welcome");
  const guest = await new Client("guest").open({ type: "join_room", roomCode: welcome.roomCode, playerName: "Second" }); clients.push(guest);
  const guestWelcome = await guest.wait(message => message.type === "welcome");

  for (let count = 3; count <= 7; count++) {
    host.send({ type: "add_bot" });
    await host.wait(message => message.type === "room_state" && message.players.length === count);
  }
  host.send({ type: "start_game" });
  await host.wait(message => message.type === "game_message" && message.payload?.api === "game_update_day");
  await guest.wait(message => message.type === "game_message" && message.payload?.api === "game_update_day");

  host.send({ type: "chat", value: "公开历史保留" });
  await guest.wait(message => message.type === "chat_message" && message.text === "公开历史保留");
  guest.send({ type: "chat", value: "出局玩家发言" });
  const denied = await guest.wait(message => message.type === "error" && message.message.includes("已出局"));
  assert(Boolean(denied), "dead player chat must be rejected by the server");

  guest.close();
  await delay(150);
  const reconnected = await new Client("reconnected-guest").open({ type: "reconnect", roomCode: welcome.roomCode, playerName: "Second", token: guestWelcome.token });
  clients.push(reconnected);
  const reconnectedWelcome = await reconnected.wait(message => message.type === "welcome");
  const replayedSnapshot = await reconnected.wait(message => message.type === "game_message" && message.payload?.api === "game_update_day");
  const replayedChat = await reconnected.wait(message => message.type === "chat_message" && message.text === "公开历史保留");
  assert(reconnectedWelcome.playerId === guestWelcome.playerId, "reconnect must preserve the player's seat");
  assert(replayedSnapshot.payload.message_type === "public", "reconnect snapshot must remain a public redacted envelope");
  assert(reconnected.messages.filter(message => message.type === "game_message" && message.payload?.api === "game_update_day").length === 1, "reconnect must replay one latest full snapshot");
  assert(Boolean(replayedChat), "reconnect must replay public chat history");

  console.log(JSON.stringify({ ok: true, routing: true, deadChatDenied: true, seatPreserved: true, redactedSnapshotReplayed: true, publicHistoryReplayed: true }, null, 2));
} finally {
  for (const client of clients) client.close();
  server.kill();
  await delay(300);
}
