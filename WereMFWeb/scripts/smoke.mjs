const endpoint = process.argv[2] || "ws://127.0.0.1:5055/ws";
const clients = [];
const wait = ms => new Promise(r => setTimeout(r, ms));
function open(payload) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(endpoint); const seen = [];
    ws.onopen = () => ws.send(JSON.stringify(payload));
    ws.onerror = reject;
    ws.onmessage = event => { const msg = JSON.parse(event.data); seen.push(msg); if (msg.type === "welcome") resolve({ ws, welcome: msg, seen }); };
  });
}
const host = await open({ type: "create_room", playerName: "玩家1" }); clients.push(host);
for (let i = 2; i <= 7; i++) { clients.push(await open({ type: "join_room", roomCode: host.welcome.roomCode, playerName: `玩家${i}` })); }
for (const client of clients) client.ws.addEventListener("message", event => {
  const msg = JSON.parse(event.data); const api = msg.payload?.api;
  if (client === host && ["request_leaf_game", "request_anonymous_game", "request_reroll_player"].includes(api)) client.ws.send(JSON.stringify({ type: "game_input", value: "0" }));
});
host.ws.send(JSON.stringify({ type: "start_game" }));
for (let i = 0; i < 120; i++) {
  await wait(100);
  const roles = clients.map(c => c.seen.filter(x => x.payload?.api === "player_notify_chara").length);
  if (roles.every(x => x === 1)) {
    console.log(JSON.stringify({ ok: true, room: host.welcome.roomCode, seats: clients.length, privateRoleMessages: roles }));
    clients.forEach(c => c.ws.close()); process.exit(0);
  }
}
console.error(JSON.stringify({ ok: false, messages: clients.map(c => c.seen.map(x => x.payload?.api || x.type).slice(-12)) }));
clients.forEach(c => c.ws.close()); process.exit(1);
