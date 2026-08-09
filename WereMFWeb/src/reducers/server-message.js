import { reduceGameMessage } from "./game-message.js";
import { defaultRoomSettings } from "../store.js";

export function reduceServerMessage(state, message, deps) {
  if (!message || typeof message !== "object") return false;
  if (message.type === "error") { if (state.reconnecting && message.message?.includes("会话已失效")) deps.finishLeave(); deps.notify(message.message); return true; }
  if (message.type === "left_room") { deps.finishLeave(); return true; }
  if (message.type === "welcome") { Object.assign(state, { view: "room", roomCode: message.roomCode, playerId: message.playerId, playerName: message.playerName, token: message.token, isHost: message.isHost }); deps.persistSession({ roomCode: state.roomCode, playerName: state.playerName, token: state.token, server: state.server }); }
  if (message.type === "player_remapped") state.playerId = message.playerId;
  if (message.type === "session_state") Object.assign(state, { playerId: message.playerId, isHost: message.isHost });
  if (message.type === "room_restarted") { deps.resetGameToLobby(); deps.notify(message.message || "已返回等待大厅"); }
  if (message.type === "room_state") {
    const startingNewGame = message.started && !state.started; const replaying = state.reconnecting;
    state.botIds = [...(message.bots || [])]; if (message.settings) state.roomSettings = { ...defaultRoomSettings, ...message.settings }; if (!message.started || !state.players.length) state.players = [...(message.players || [])];
    if (!message.started) { const me = (message.players || []).find(player => !player.isPermanentBot && player.name === state.playerName); if (me) Object.assign(state, { playerId: me.id, isHost: me.isHost }); }
    if (startingNewGame) { deps.clearNewGamePresentation(); if (!replaying) deps.playSound("gameReady"); }
    state.started = message.started; if (message.started) state.view = "game"; else if (state.view !== "landing") state.view = "room"; state.reconnecting = false;
  }
  if (message.type === "bot_takeover") { state.botIds = [...new Set([...state.botIds, message.playerId])]; deps.notify(message.message); deps.addEvent("BOT", message.message, false); }
  if (message.type === "game_message") reduceGameMessage(state, message.payload, deps);
  if (message.type === "chat_message") deps.appendChat(message);
  if (message.type === "cli_input_recorded") deps.appendCliInput(message);
  if (message.type === "game_ended") { deps.addEvent("SYSTEM", message.message, false); state.request = null; deps.clearTimer(); }
  if (message.type === "server_notice") deps.addEvent("SERVER", message.message, true);
  if (message.type === "request_timer") Object.assign(state, { timerDeadline: message.deadlineUtc || 0, timerApi: message.api || "", timerMode: message.mode || "request" });
  if (message.type === "request_timeout_resolved") { deps.playSound("requestTimeout"); deps.notify(message.message); deps.addEvent("TIMEOUT", message.message, message.api !== "request_vote"); Object.assign(state, { request: null, selected: [], modifier: "" }); deps.clearTimer(); }
  if (message.type === "game_log_available") { state.gameLog = message; deps.addEvent("SYSTEM", "本局日志已生成，所有玩家均可下载", false); }
  if (message.type === "input_accepted") deps.notify(message.remaining ? `已暂存，还可提交 ${message.remaining} 次` : "已提交，等待其他玩家");
  if (message.type === "pre_submit_accepted") { const id = message.skillId || deps.pendingId(state.request?.data); if (id) deps.removePending(state, id); Object.assign(state, { request: null, selected: [], modifier: "" }); deps.clearTimer(); deps.notify(message.message || "预提交已自动发送"); }
  if (message.type === "pre_submit_rejected") { if (message.skillId) state.preSubmittedDrafts[message.skillId] = false; deps.notify(message.message || "预提交已失效，请重新确认"); }
  return true;
}
