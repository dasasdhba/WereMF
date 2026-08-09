import { spawn } from "node:child_process";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";

const port = 5197;
const serverExe = fileURLToPath(new URL("../WereMFServer/bin/Release/net8.0/WereMFServer.exe", import.meta.url));
const gameExe = fileURLToPath(new URL("../WereMF/bin/Release/net8.0/win-x64/publish/WereMF.exe", import.meta.url));
const server = spawn(serverExe, ["--path", gameExe, "--host", "127.0.0.1", "--port", String(port), "--seed", "260726", "--request-timeout-seconds", "3"], { windowsHide: true });
let serverOutput = "";
server.stdout.on("data", x => serverOutput += x);
server.stderr.on("data", x => serverOutput += x);
server.on("error", x => serverOutput += x.stack || String(x));

class Client {
  constructor(name) { this.name = name; this.messages = []; this.waiters = []; }
  async open(first) {
    this.ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
    this.ws.addEventListener("message", event => {
      const message = JSON.parse(event.data);
      this.messages.push(message);
      for (const waiter of [...this.waiters]) if (waiter.predicate(message)) { waiter.resolve(message); this.waiters.splice(this.waiters.indexOf(waiter), 1); }
    });
    await new Promise((resolve, reject) => { this.ws.addEventListener("open", resolve, { once: true }); this.ws.addEventListener("error", reject, { once: true }); });
    this.send(first);
    return this;
  }
  send(message) { this.ws.send(JSON.stringify(message)); }
  wait(predicate, timeout = 10000) {
    const found = this.messages.findLast(predicate);
    if (found) return Promise.resolve(found);
    return new Promise((resolve, reject) => {
      const waiter = { predicate, resolve };
      this.waiters.push(waiter);
      setTimeout(() => { const i = this.waiters.indexOf(waiter); if (i >= 0) this.waiters.splice(i, 1); reject(new Error(`${this.name} timed out`)); }, timeout);
    });
  }
  close() { try { this.ws?.close(); } catch {} }
}
const assert = (condition, message) => { if (!condition) throw new Error(message); };
async function waitServer() {
  for (let i = 0; i < 80; i++) { try { const response = await fetch(`http://127.0.0.1:${port}/api/health`); if (response.ok) return; } catch {} await delay(100); }
  throw new Error(`server did not start\n${serverOutput}`);
}
const clients = [];
try {
  await waitServer();
  const host = await new Client("host").open({ type: "create_room", playerName: "Host" }); clients.push(host);
  const hostWelcome = await host.wait(x => x.type === "welcome");
  const roomCode = hostWelcome.roomCode;
  const alice = await new Client("alice").open({ type: "join_room", roomCode, playerName: "Alice" }); clients.push(alice);
  const aliceWelcome = await alice.wait(x => x.type === "welcome");
  const bob = await new Client("bob").open({ type: "join_room", roomCode, playerName: "Bob" }); clients.push(bob);
  const bobWelcome = await bob.wait(x => x.type === "welcome");
  await host.wait(x => x.type === "room_state" && x.players.length === 3);

  host.send({ type: "leave_room" });
  await host.wait(x => x.type === "left_room");
  const transferred = await alice.wait(x => x.type === "room_state" && x.players.length === 2 && x.players.some(p => p.isHost));
  assert(transferred.players.filter(p => p.isHost).length === 1, "expected exactly one replacement host");
  assert(["Alice", "Bob"].includes(transferred.players.find(p => p.isHost).name), "replacement host must be an online human");
  const newHost = transferred.players.find(p => p.isHost).name === "Alice" ? alice : bob;
  const other = newHost === alice ? bob : alice;
  await newHost.wait(x => x.type === "session_state" && x.isHost === true);
  newHost.send({ type: "update_room_settings", requestTimeoutSeconds: 45, voteSecondsPerAlive: 50, votePenaltySeconds: 20, eventIntervalSeconds: 3 });
  const configured = await newHost.wait(x => x.type === "room_state" && x.settings?.eventIntervalSeconds === 3);
  assert(configured.settings.requestTimeoutSeconds === 45 && configured.settings.voteSecondsPerAlive === 50 && configured.settings.votePenaltySeconds === 20, "host room settings should be broadcast");
  other.send({ type: "update_room_settings", requestTimeoutSeconds: 99, voteSecondsPerAlive: 99, votePenaltySeconds: 99, eventIntervalSeconds: 9 });
  const settingsRejected = await other.wait(x => x.type === "error" && x.message.includes("只有房主"));
  assert(Boolean(settingsRejected), "non-host room settings update should be rejected");

  for (let total = 3; total <= 7; total++) {
    newHost.send({ type: "add_bot" });
    await newHost.wait(x => x.type === "room_state" && x.players.length === total);
  }
  newHost.send({ type: "start_game" });
  await newHost.wait(x => x.type === "room_state" && x.started === true, 15000);

  other.send({ type: "leave_room" });
  await other.wait(x => x.type === "left_room");
  const otherPlayerName = other === alice ? "Alice" : "Bob";
  const takeover = await newHost.wait(x => x.type === "bot_takeover" && x.playerName === otherPlayerName, 10000);
  assert(takeover.message.includes("彻底退出"), "active leave should announce explicit Bot takeover");
  const rejected = await new Client("reconnect-rejected").open({ type: "join_room", roomCode, playerName: otherPlayerName, token: other === alice ? aliceWelcome.token : bobWelcome.token }); clients.push(rejected);
  const rejection = await rejected.wait(x => x.type === "error");
  assert(rejection.message.includes("已经开始"), "abandoned token must not reconnect to active game");

  newHost.send({ type: "restart_room" });
  await newHost.wait(x => x.type === "room_restarted", 10000);
  const lobby = await newHost.wait(x => x.type === "room_state" && x.started === false && x.players.length === 6);
  assert(lobby.players.length === 6, `restart should remove abandoned seat, got ${lobby.players.length}`);
  assert(lobby.players.some(p => p.isHost && p.name === (newHost === alice ? "Alice" : "Bob")), "replacement host should remain host after restart");
  const restartedHost = lobby.players.find(p => p.isHost);
  const restartedSession = await newHost.wait(x => x.type === "session_state" && x.isHost === true && x.playerId === restartedHost.id);
  assert(restartedSession.playerId === restartedHost.id, `restart session playerId ${restartedSession.playerId} did not match host lobby seat ${restartedHost.id}`);
  assert(restartedSession?.isHost === true, "restart session should retain host authority");
  assert(lobby.settings?.eventIntervalSeconds === 3 && lobby.settings?.requestTimeoutSeconds === 45, "restart should retain room settings");
  const leakedPlayerList = clients.flatMap(x => x.messages).some(x => x.type === "game_message" && x.payload?.api === "request_player_list");
  assert(!leakedPlayerList, "internal request_player_list leaked to browser");
  const tempHost = await new Client("temp-host").open({ type: "create_room", playerName: "TempHost" }); clients.push(tempHost);
  const tempHostWelcome = await tempHost.wait(x => x.type === "welcome");
  const tempGuest = await new Client("temp-guest").open({ type: "join_room", roomCode: tempHostWelcome.roomCode, playerName: "TempGuest" }); clients.push(tempGuest);
  const tempGuestWelcome = await tempGuest.wait(x => x.type === "welcome");
  await tempHost.wait(x => x.type === "room_state" && x.players.length === 2);
  tempGuest.close();
  await tempHost.wait(x => x.type === "room_state" && x.players.length === 1 && !x.players.some(p => p.name === "TempGuest"));
  const expiredGuest = await new Client("expired-guest").open({ type: "reconnect", roomCode: tempHostWelcome.roomCode, playerName: "TempGuest", token: tempGuestWelcome.token }); clients.push(expiredGuest);
  const expiredError = await expiredGuest.wait(x => x.type === "error");
  assert(expiredError.message.includes("会话已失效"), "lobby disconnect should invalidate the old reconnect token");
  const freshGuest = await new Client("fresh-guest").open({ type: "join_room", roomCode: tempHostWelcome.roomCode, playerName: "TempGuest" }); clients.push(freshGuest);
  const freshWelcome = await freshGuest.wait(x => x.type === "welcome");
  assert(freshWelcome.token !== tempGuestWelcome.token, "manual rejoin should create a fresh session token");
  await tempHost.wait(x => x.type === "room_state" && x.players.length === 2);
  tempHost.close();
  const hostTransferred = await freshGuest.wait(x => x.type === "room_state" && x.players.length === 1 && x.players[0].isHost);
  assert(hostTransferred.players[0].name === "TempGuest", "lobby host disconnect should transfer host to an online human");
  await freshGuest.wait(x => x.type === "session_state" && x.isHost === true);
  freshGuest.close();
  await delay(250);
  const publicRooms = await (await fetch(`http://127.0.0.1:${port}/api/rooms`)).json();
  assert(!publicRooms.some(room => room.code === tempHostWelcome.roomCode), "room should be removed after the last lobby human disconnects");

  console.log(JSON.stringify({ ok: true, roomCode, replacementHost: newHost === alice ? "Alice" : "Bob", activeLeaveBot: otherPlayerName, lobbyPlayersAfterRestart: lobby.players.length, lobbyDisconnectRemoved: true, staleTokenRejected: true, emptyLobbyRemoved: true }, null, 2));
} finally {
  for (const client of clients) client.close();
  server.kill();
  await delay(300);
}
