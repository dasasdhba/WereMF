import assert from "node:assert/strict";
import { loadWebApp } from "./web-app-fixture.mjs";

const { ui, appEl } = await loadWebApp();
const players = Array.from({ length: 7 }, (_, index) => ({ id: index + 1, name: `玩家${index + 1}` }));
const entities = players.map(player => ({ player, role: null, state: { smog_count: 0, is_dead: false } }));

ui.onMessage({ type: "welcome", roomCode: "123456", playerId: 1, playerName: "玩家1", token: "test", isHost: true });
ui.onMessage({ type: "room_state", roomCode: "123456", started: true, players, bots: [], settings: {} });
ui.onMessage({ type: "game_message", payload: { api: "night_start_broadcast", message_type: "public", message_content: "晚上开始" } });
ui.onMessage({ type: "game_message", payload: { api: "game_update_night", message_type: "public", data: entities } });
ui.onMessage({
  type: "game_message",
  payload: {
    api: "game_update_night_patch",
    message_type: "public",
    data: { cause: "huika_smog", entities: [{ player_id: 5, state: { smog_count: 1 } }] }
  }
});

assert.equal(ui.state.entities.find(entity => entity.player.id === 5).state.smog_count, 1);
assert.match(appEl.innerHTML, /data-player="5"[\s\S]*?☁ × 1[\s\S]*?<\/button>/);
console.log(JSON.stringify({ ok: true, phase: ui.state.phase, player: 5, rendered: "☁ × 1" }));
