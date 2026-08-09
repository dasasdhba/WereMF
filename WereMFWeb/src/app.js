import { applyEntityStatePatch as mergeEntityStatePatch } from "./store.js";
import { applyFullEntitySnapshot as setFullEntitySnapshot } from "./store.js";
import { reduceGameMessage } from "./reducers/game-message.js";
import { createSocketManager } from "./socket.js";
import { createAudioEffects } from "./effects/audio.js";
import { createNotificationEffects } from "./effects/notifications.js";
import { choosePlayerSelection, formatSelection as formatSelectionPure, invalidIdsFor as invalidIdsPure, selectionCountValid as selectionCountValidPure, selectionRule as selectionRulePure } from "./input/selection.js";
import { activePending as activePendingPure, pendingId as pendingIdPure, rememberPending as rememberPendingPure, removePending as removePendingPure } from "./input/pending-drafts.js";

const app = document.querySelector("#app");
const toast = document.querySelector("#toast");
const roles = ["脚滑人","Doge","庸医","地鼠","兔子","铯郎","法猫","卡比","粉侠","爬行者","炮仙","实物","灰卡比","音魔","CTF","合虫","彩怪","贤松","江仙","myz","叶子"];
const nightPatchStateKinds = Object.freeze({
  is_bar_leader: "boolean", is_dead: "boolean", is_dead_public: "boolean", dead_showing_name: "string",
  reversed: "boolean", smog_count: "number", capsule_count: "number", potion_count: "number",
  xian_song_count: "number", bug_count: "number", myz_threaten: "boolean", jiaohua_vote_blocked: "boolean",
  shiwu_kidnapped: "boolean", jiaohua_protected: "boolean", jiaohua_blocked: "number", leaf_protected: "boolean"
});
const defaultRoomSettings = { requestTimeoutSeconds: 30, voteSecondsPerAlive: 60, votePenaltySeconds: 30, eventIntervalSeconds: 2 };
const isReservedNickname = name => roles.some(role => role.toLocaleLowerCase() === String(name).trim().toLocaleLowerCase());
const soundFiles = Object.freeze({
  gameReady: "/sounds/game_ready.ogg", dayReady: "/sounds/day_ready.ogg", nightReady: "/sounds/night_ready.ogg",
  nightSummary: "/sounds/night_summary.ogg", voteSummary: "/sounds/vote_summary.ogg", request: "/sounds/request.wav",
  requestTimeout: "/sounds/request_timeout.wav", barWin: "/sounds/gameover_bar_win.ogg", bombWin: "/sounds/gameover_bomb_win.ogg",
  leafWin: "/sounds/gameover_leaf_win.ogg", allDead: "/sounds/gameover_all_dead.ogg"
});
const state = {
  view: "landing", socket: null, connected: false, server: new URLSearchParams(location.search).get("server") || "",
  roomCode: "", playerId: null, playerName: "", token: "", isHost: false, started: false,
  players: [], entities: [], votes: [], events: [], phase: "等待", round: 0, role: "身份尚未揭晓",
  roleVisible: true, request: null, selected: [], modifier: "", reconnecting: false,
  pendingSkills: [], pendingDrafts: {}, pendingModifiers: {}, preSubmittedDrafts: {}, myzThreatenedSkills: {}, activePendingId: "", gameLog: null,
  timerDeadline: 0, timerApi: "", timerMode: "", feedPinned: true, feedScrollTop: 0, chatDraft: "", botIds: [], leaving: false,
  roomSettings: { ...defaultRoomSettings }, soundEnabled: localStorage.getItem("weremf.sound") !== "off"
};
const audioEffects = createAudioEffects({ getState: () => state });
const { playSound, unlockAudio, stopSounds, playGameMessageSound } = audioEffects;
function soundToggleButton() { return `<button class="btn btn-ghost sound-toggle" data-toggle-sound title="${state.soundEnabled ? "关闭" : "开启"}游戏音效">${state.soundEnabled ? "🔊 音效" : "🔇 静音"}</button>`; }
const notificationEffects = createNotificationEffects({ getState: () => state, toastElement: toast, defaultTitle: document.title || "MF 杀 · 今夜谁在说谎" });
const { notify, stopTitleFlash, flashTitle, requestBrowserNotifications, alertRequest: notifyRequest } = notificationEffects;
let chatCompositionActive = false;
let renderPendingAfterComposition = false;
const e = (value = "") => String(value).replace(/[&<>'"]/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"}[c]));
const soundPlayers = new Set();
function soundForGameMessage(msg) {
  const api = msg?.api || ""; const text = String(msg?.message_content || "");
  if (api === "night_summary_broadcast") return "nightSummary";
  if (api === "day_start_broadcast") return "dayReady";
  if (api === "night_start_broadcast") return "nightReady";
  if (api === "vote_end_broadcast") return "voteSummary";
  if (api !== "game_win_broadcast") return "";
  if (text.includes("无人生还")) return "allDead";
  if (text.includes("吧方获胜")) return "barWin";
  if (text.includes("爆方获胜")) return "bombWin";
  if (text.includes("叶子获胜")) return "leafWin";
  return "";
}
const socketUrl = () => state.server.trim() || `${location.protocol === "https:" ? "wss:" : "ws:"}//${location.host}/ws`;
async function copyText(text) {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {}
  const input = document.createElement("textarea");
  input.value = text;
  input.style.position = "fixed";
  input.style.opacity = "0";
  document.body.appendChild(input);
  input.select();
  const copied = document.execCommand?.("copy") ?? false;
  input.remove();
  return copied;
}
const defaultTitle = document.title || "MF 杀 · 今夜谁在说谎";
let titleFlashTimer = null;

function alertRequest(request) { playSound("request"); notifyRequest(request); }
document.addEventListener?.("visibilitychange", () => { if (!document.hidden) stopTitleFlash(); });
globalThis.addEventListener?.("focus", stopTitleFlash);

const socketManager = createSocketManager({ state, render: () => render(), onMessage, notify });
const connect = socketManager.connect;
const send = socketManager.send;
function clearNewGamePresentation() {
  clearTimer();
  Object.assign(state, {
    entities: [], votes: [], events: [], phase: "准备", round: 0, role: "身份尚未揭晓", roleVisible: true,
    request: null, selected: [], modifier: "", pendingSkills: [], pendingDrafts: {}, pendingModifiers: {},
    preSubmittedDrafts: {}, myzThreatenedSkills: {}, activePendingId: "", gameLog: null, feedPinned: true, feedScrollTop: 0, chatDraft: ""
  });
}
function resetGameToLobby() {
  clearTimer();
  Object.assign(state, { view: "room", started: false, entities: [], votes: [], events: [], phase: "等待", round: 0,
    role: "身份尚未揭晓", roleVisible: true, request: null, selected: [], modifier: "", pendingSkills: [],
    pendingDrafts: {}, pendingModifiers: {}, preSubmittedDrafts: {}, myzThreatenedSkills: {}, activePendingId: "", gameLog: null, feedPinned: true, feedScrollTop: 0, chatDraft: "" });
}
function finishLeave() {
  localStorage.removeItem("weremf.session");
  const socket = state.socket;
  Object.assign(state, { view: "landing", socket: null, connected: false, roomCode: "", playerId: null,
    playerName: "", token: "", isHost: false, started: false, players: [], entities: [], votes: [], events: [],
    request: null, selected: [], pendingSkills: [], pendingDrafts: {}, pendingModifiers: {}, preSubmittedDrafts: {}, myzThreatenedSkills: {}, activePendingId: "", gameLog: null, chatDraft: "",
    botIds: [], leaving: false });
  try { socket?.close(); } catch {}
  stopTitleFlash();
  render();
}
function leaveRoom() {
  if (state.socket?.readyState !== WebSocket.OPEN) return notify("当前未连接，无法彻底退出；直接关闭页面仍可稍后重连");
  const warning = state.started
    ? "彻底退出后无法再重连到本局，你的席位会立即交给 Bot。确定退出吗？"
    : `确定彻底退出房间 ${state.roomCode} 并释放席位吗？`;
  if (globalThis.confirm?.(warning) === false) return;
  state.leaving = true;
  localStorage.removeItem("weremf.session");
  send({ type: "leave_room" });
}

function onMessage(message) {
  if (message.type === "error") {
    if (state.reconnecting && message.message?.includes("会话已失效")) finishLeave();
    notify(message.message); return;
  }
  if (message.type === "left_room") { finishLeave(); return; }
  if (message.type === "welcome") {
    Object.assign(state, { view: "room", roomCode: message.roomCode, playerId: message.playerId, playerName: message.playerName, token: message.token, isHost: message.isHost });
    localStorage.setItem("weremf.session", JSON.stringify({ roomCode: state.roomCode, playerName: state.playerName, token: state.token, server: state.server }));
  }
  if (message.type === "player_remapped") state.playerId = message.playerId;
  if (message.type === "session_state") Object.assign(state, { playerId: message.playerId, isHost: message.isHost });
  if (message.type === "room_restarted") { resetGameToLobby(); notify(message.message || "已返回等待大厅"); }
  if (message.type === "room_state") {
    const startingNewGame = message.started && !state.started; const replaying = state.reconnecting;
    state.botIds = [...(message.bots || [])];
    if (message.settings) state.roomSettings = { ...defaultRoomSettings, ...message.settings };
    if (!message.started || !state.players.length) state.players = [...message.players];
    if (!message.started) {
      const me = (message.players || []).find(p => !p.isPermanentBot && p.name === state.playerName);
      if (me) Object.assign(state, { playerId: me.id, isHost: me.isHost });
    }
    if (startingNewGame) { clearNewGamePresentation(); if (!replaying) playSound("gameReady"); }
    state.started = message.started;
    if (message.started) state.view = "game";
    else if (state.view !== "landing") state.view = "room";
    state.reconnecting = false;
  }
  if (message.type === "bot_takeover") { state.botIds = [...new Set([...state.botIds, message.playerId])]; notify(message.message); addEvent("BOT", message.message, false); }
  if (message.type === "game_message") handleGameMessage(message.payload);
  if (message.type === "chat_message") appendChat(message);
  if (message.type === "cli_input_recorded") appendCliInput(message);
  if (message.type === "game_ended") { addEvent("SYSTEM", message.message, false); state.request = null; clearTimer(); }
  if (message.type === "server_notice") addEvent("SERVER", message.message, true);
  if (message.type === "request_timer") { state.timerDeadline = message.deadlineUtc || 0; state.timerApi = message.api || ""; state.timerMode = message.mode || "request"; }
  if (message.type === "request_timeout_resolved") { playSound("requestTimeout"); notify(message.message); addEvent("TIMEOUT", message.message, message.api !== "request_vote"); state.request = null; state.selected = []; state.modifier = ""; clearTimer(); }
  if (message.type === "game_log_available") { state.gameLog = message; addEvent("SYSTEM", "本局日志已生成，所有玩家均可下载", false); }
  if (message.type === "input_accepted") notify(message.remaining ? `已暂存，还可提交 ${message.remaining} 次` : "已提交，等待其他玩家");
  if (message.type === "pre_submit_accepted") { const id = message.skillId || pendingId(state.request?.data); if (id) removePending(id); state.request = null; state.selected = []; state.modifier = ""; clearTimer(); notify(message.message || "预提交已自动发送"); }
  if (message.type === "pre_submit_rejected") { if (message.skillId) state.preSubmittedDrafts[message.skillId] = false; notify(message.message || "预提交已失效，请重新确认"); }
  render();
}
function handleGameMessage(msg, recordEvent = true, playCue = true) {
  reduceGameMessage(state, msg, { ...gameReducerDeps, recordEvents: recordEvent, playGameMessageSound: playCue ? playGameMessageSound : undefined });
}
const gameReducerDeps = {
  roles, clearNewGamePresentation, clearTimer, applyFullEntitySnapshot: setFullEntitySnapshot, applyEntityStatePatch,
  updateVotes, rememberPending: rememberPendingPure, removePending: removePendingPure, pendingId: pendingIdPure,
  syncDraft: value => syncDraft(value, false), notify, alertRequest, addEvent,
  playGameMessageSound
};
function applyEntityStatePatch(targetState, data) {
  return mergeEntityStatePatch(targetState, data);
}
function clearTimer() { state.timerDeadline = 0; state.timerApi = ""; state.timerMode = ""; }
function timerText() {
  if (!state.timerDeadline) return "";
  const seconds = Math.max(0, Math.ceil((state.timerDeadline - Date.now()) / 1000));
  const minutes = Math.floor(seconds / 60); const rest = seconds % 60;
  return `${minutes}:${String(rest).padStart(2,"0")}`;
}
function timerBadge() { return state.timerDeadline ? `<span class="request-timer ${state.timerMode === "vote" ? "vote" : ""}" data-timer>${timerText()}</span>` : ""; }
function syncDraft(value = state.selected.join(" "), preSubmit) {
  const skillId = state.request ? pendingId(state.request.data) : state.activePendingId;
  const api = state.request?.api || "";
  const armed = preSubmit ?? Boolean(skillId && state.preSubmittedDrafts[skillId]);
  if (skillId || api) send({ type: "pending_draft", skillId, api, value: String(value).trim(), preSubmit: armed });
}
function activePending() { return activePendingPure(state); }
function selectionRule(context = state.request || activePending()) { return selectionRulePure(context, roles); }
function selectionCountValid(context = state.request || activePending()) { return selectionCountValidPure(state.selected, context, roles); }
function requiredModifier(context = state.request || activePending()) {
  const api = context?.api || ""; const type = context?.type || "";
  return api ? ["request_jiaohua_dead_skill","request_rabi_skill"].includes(api) : type === "兔子";
}
function pendingModifierOptions(type) {
  return ({
    "兔子": [["x","鲜奶"],["d","毒奶"]]
  })[type] || [];
}
function copyLeafOptions(request = state.request) {
  if (request?.api !== "request_hechong_copy_leaf") return [];
  return [...String(request.message_content || "").matchAll(/(\d+)\s*[：:]\s*([^；;]+)/g)]
    .map(match => ({ value: match[1], label: match[2].trim() }));
}
function leafOptions(request = state.request) {
  if (request?.api !== "request_leaf_charas") return [];
  if (Array.isArray(request.data?.options)) return request.data.options;
  return roles.filter(value => !["粉侠","彩怪","叶子"].includes(value)).map(value => ({ value, camp: "" }));
}
function leafSelectionValid(request = state.request) {
  if (request?.api !== "request_leaf_charas") return true;
  const options = leafOptions(request); const selected = state.selected;
  const count = request.data?.choice_count || 4;
  if (selected.length !== count || selected.some(value => !options.some(option => option.value === value))) return false;
  const required = request.data?.required_camps || [];
  return required.every(camp => selected.some(value => options.find(option => option.value === value)?.camp === camp));
}
function pendingId(data) { return pendingIdPure(data); }
function rememberPending(data) {
  rememberPendingPure(state, data);
}
function removePending(id) {
  removePendingPure(state, id);
}
function requestChoiceInvalid(msg, value, index) {
  if (typeof value !== "number") return false;
  const property = msg.api === "request_myz_skill" && index === 1 ? "invalid_target_choice" : "invalid_choice";
  const list = msg.data?.[property] || [];
  return list.some(item => (typeof item === "number" ? item : item.id) === value);
}
function activateRequest(msg) {
  const id = pendingId(msg.data); const draft = id ? [...(state.pendingDrafts[id] || [])] : [];
  if (id && state.myzThreatenedSkills[id]) msg.web_myz_threaten = state.myzThreatenedSkills[id];
  state.request = msg; state.modifier = id ? state.pendingModifiers[id] || "" : "";
  const invalid = draft.filter((value, index) => requestChoiceInvalid(msg, value, index));
  const valid = draft.filter((value, index) => !requestChoiceInvalid(msg, value, index)); const rule = selectionRule(msg);
  const removed = [...invalid, ...valid.slice(rule.max)];
  state.selected = valid.slice(0, rule.max); state.activePendingId = id || state.activePendingId;
  if (removed.length || !selectionCountValid(msg) || (requiredModifier(msg) && !state.modifier)) {
    if (id) state.preSubmittedDrafts[id] = false;
    syncDraft(formatSelection(), false);
    if (removed.length) { const names = removed.map(value => state.players.find(x => x.id === value)?.name || `${value} 号`).join("、"); notify(`局面已变化，已取消无效选择：${names}`); }
  }
  alertRequest(msg);
}
function voteTargetText(target) {
  if (target === 0) return "弃票";
  const player = state.players.find(x => x.id === target);
  return player ? `${target} 号 · ${player.name}` : `${target} 号玩家`;
}
function updateVotes(msg) {
  const next = Array.isArray(msg.data) ? msg.data : msg.data?.votes || [];
  for (const vote of next) {
    const before = state.votes.find(x => x.id === vote.id); if (vote.target == null) continue;
    const voter = state.players.find(x => x.id === vote.id); const voterName = voter ? `${vote.id} 号 · ${voter.name}` : `${vote.id} 号玩家`;
    if (!before || before.target !== vote.target) {
      const action = before?.target == null ? "投给" : "改投";
      addEvent("公开投票", `${voterName} ${vote.target === 0 ? "选择弃票" : `${action} ${voteTargetText(vote.target)}`}`, false);
    } else if (!before.confirmed && vote.confirmed) {
      addEvent("公开投票", `${voterName} 确认${vote.target === 0 ? "弃票" : `投给 ${voteTargetText(vote.target)}`}`, false);
    }
  }
  state.votes = next;
}function isFeedAtBottom() {
  const feed = document.querySelector(".feed");
  return !feed || feed.scrollHeight - feed.scrollTop - feed.clientHeight <= 32;
}
function appendEvent(api, text, privateMessage) {
  const browsingHistory = !isFeedAtBottom();
  state.events.push({ api, text, private: privateMessage, time: new Date().toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" }) });
  if (state.events.length > 180) state.events.shift();
  flashTitle("有新消息");
  if (browsingHistory && !state.reconnecting)
    notify(`${privateMessage ? "私密消息" : "新消息"}：${String(text).replace(/\s+/g, " ").slice(0, 48)}`);
}
function appendChat(message) {
  const text = String(message.text || "").trim(); if (!text) return;
  const browsingHistory = !isFeedAtBottom();
  const sentAt = Number(message.sentAt); const date = Number.isFinite(sentAt) ? new Date(sentAt) : new Date();
  state.events.push({ api: "聊天", text, chat: true, playerId: Number(message.playerId), private: false, time: date.toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" }) });
  if (state.events.length > 180) state.events.shift();
  flashTitle("有新消息");
  if (browsingHistory && !state.reconnecting) {
    const sender = state.players.find(x => x.id === Number(message.playerId));
    notify(`${sender?.name || `${message.playerId} 号玩家`}：${text.replace(/\s+/g, " ").slice(0, 48)}`);
  }
}
function appendCliInput(message) {
  const value = String(message.value ?? "");
  const sentAt = Number(message.sentAt); const date = Number.isFinite(sentAt) ? new Date(sentAt) : new Date();
  state.events.push({
    api: message.api || "CLI",
    text: value,
    cliInput: true,
    private: true,
    time: date.toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" })
  });
  if (state.events.length > 180) state.events.shift();
}
function chatAvailability() {
  if (!state.started || !["白天", "投票"].includes(state.phase)) return { allowed: false, reason: "仅白天可以发言" };
  const me = entityFor(state.playerId); const st = me?.state;
  if (!st) return { allowed: false, reason: "正在同步发言状态" };
  if (st.is_dead || st.is_dead_public) return { allowed: false, reason: "已出局玩家不能发言" };
  if (state.botIds.includes(state.playerId)) return { allowed: false, reason: "BOT 托管中，不能发言" };
  return { allowed: true, reason: "输入白天发言" };
}
function addEvent(api, text, privateMessage) { appendEvent(api, text, privateMessage); }

function landing() { return renderLanding({ state, e }); }
function room() { return renderRoom({ state, e, soundToggleButton }); }
function entityFor(id) { return state.entities.find(x => (x.player?.id ?? x.player?.Id) === id); }
const roleStateLabels = {
  cai_count: ["彩量", "🎨"], reborn_list: ["复活名单", "↻"], bomb_count: ["炸弹", "💣"],
  placed_list: ["已放置", "💣"], bug_count: ["虫量", "🐞"], capsule: ["胶囊", "💊"],
  fen_count: ["粉量", "🌸"], ground_pool: ["地池", "🕳"], round: ["技能轮次", "◷"],
  mfa_list: ["MFA", "🔑"], disc_count: ["光盘", "💿"]
};
const roleBooleanLabels = {
  reborn: ["已触发复活", "尚未复活"], self_selected: ["已选择自己", null],
  first_round: ["首轮状态", "非首轮状态"], dead_voted: ["死后票已使用", "死后票可用"],
  fury: ["叶子二阶段", "叶子一阶段"], red_ground: ["红地状态", "普通地状态"],
  revealed: ["已自爆身份", "尚未自爆"], broadcasted: ["绑架已公开", null],
  can_reborn: ["可以复活", "暂不可复活"], can_force_choice: ["可以强制选择", null],
  disabled: ["角色技能被禁用", null]
};
function playerRef(id) {
  const player = state.players.find(x => x.id === Number(id));
  return player ? `${id}号·${player.name}` : `${id}号`;
}
function roleStateItems(data) {
  if (!data || typeof data !== "object") return [];
  const items = [];
  for (const [key, value] of Object.entries(data)) {
    if (value == null) continue;
    if (key === "copied_role" && typeof value === "object") {
      const name = value.chara_type || "未知角色";
      items.push({ text: `复制：${name}`, kind: "nested" }, ...roleStateItems(value.data));
      continue;
    }
    if ((key === "copied_roles" || key === "roles") && Array.isArray(value)) {
      for (const role of value) {
        const name = role?.chara_type || "未知角色";
        items.push({ text: `${key === "roles" ? "叶子" : "复制"}：${name}`, kind: "nested" }, ...roleStateItems(role?.data));
      }
      continue;
    }
    if (key === "last_selected" && typeof value === "object") {
      if (value.tonight?.length) items.push({ text: `今晚已选：${value.tonight.map(playerRef).join("、")}`, kind: "players" });
      if (value.last_night?.length) items.push({ text: `昨晚已选：${value.last_night.map(playerRef).join("、")}`, kind: "players" });
      continue;
    }
    if (Array.isArray(value)) {
      if (!value.length) continue;
      if (key === "ground_pool") {
        const groundNames = { 0: "花岗岩", 1: "土地", 2: "红土地" };
        const counts = value.reduce((result, ground) => {
          const name = groundNames[ground] || `未知土地(${ground})`;
          result[name] = (result[name] || 0) + 1;
          return result;
        }, {});
        items.push({ text: `🕳 土地池：${Object.entries(counts).map(([name, count]) => `${name}×${count}`).join("、")}`, kind: "value" });
        continue;
      }
      const [label, icon] = roleStateLabels[key] || [key.replaceAll("_", " "), "•"];
      items.push({ text: `${icon} ${label}：${value.map(x => typeof x === "number" ? playerRef(x) : String(x)).join("、")}`, kind: "value" });
      continue;
    }
    if (typeof value === "boolean") {
      const labels = roleBooleanLabels[key];
      if (labels) {
        const text = value ? labels[0] : labels[1];
        if (text) items.push({ text, kind: value ? "active" : "inactive" });
      } else items.push({ text: `${key.replaceAll("_", " ")}：${value ? "是" : "否"}`, kind: value ? "active" : "inactive" });
      continue;
    }
    if (typeof value === "object") { items.push(...roleStateItems(value)); continue; }
    const [label, icon] = roleStateLabels[key] || [key.replaceAll("_", " "), "•"];
    items.push({ text: `${icon} ${label}：${value}`, kind: "value" });
  }
  return items;
}
function playerCard(p) {
  const entity = entityFor(p.id); const st = entity?.state || {}; const dead = st.is_dead_public || st.is_dead; const invalid = invalidIds().has(p.id); const selected = state.selected.includes(p.id);
  const displayName = `${st.reversed ? "反·" : ""}${p.name}`;
  const vote = state.votes.find(x => x.id === p.id); const voteText = vote?.target == null ? "" : vote.target === 0 ? "弃票" : `投给 ${voteTargetText(vote.target)}`;
  const tokens = [["☁",st.smog_count],["🐞",st.bug_count],["🍪",st.xian_song_count],["💊",st.capsule_count],["💧",st.potion_count]].filter(x => x[1]>0);
  const effects = [
    state.botIds.includes(p.id) ? ["BOT 托管", "bot"] : null,
    st.jiaohua_vote_blocked ? ["✖ 禁票", "vote-blocked"] : null,
    st.jiaohua_protected ? ["🛡 脚滑保护", "protected"] : null,
    st.jiaohua_blocked > 0 ? ["❌ 技能被封", "skill-blocked"] : null,
    st.leaf_protected ? ["❎ 叶子保护", "leaf-protected"] : null,
    st.myz_threaten ? ["❌ 被威胁 · 白天无法行动", "threatened"] : null,
    st.shiwu_kidnapped ? ["被实物绑架", "kidnapped"] : null
  ].filter(Boolean);
  const visibleRole = entity?.role;
  const roleName = visibleRole?.summary_name || visibleRole?.role?.summary_name;
  const roleText = roleName ? `${roleName}${visibleRole?.public_reveal ? " · 已公开" : ""}` : dead ? (st.dead_showing_name || "身份未公开") : "身份隐藏";
  return `<button class="board-player ${dead ? "dead" : ""} ${selected ? "selected" : ""}" data-player="${p.id}" ${invalid ? "disabled" : ""}><div class="board-name"><span class="seat-no" style="display:inline-grid;width:25px;height:25px;border-radius:8px;margin-right:7px">${p.id}</span>${e(displayName)}</div><div class="board-role ${visibleRole?.public_reveal ? "public" : ""}">${e(roleText)}</div>${effects.length ? `<div class="state-badges">${effects.map(x => `<span class="state-badge ${x[1]}">${x[0]}</span>`).join("")}</div>` : ""}${voteText ? `<div class="vote-status ${vote.confirmed ? "confirmed" : ""}">${e(voteText)}${vote.confirmed ? " · 已确认" : " · 可改票"}</div>` : ""}${tokens.length ? `<div class="tokens">${tokens.map(x=>`<span class="token">${x[0]} × ${x[1]}</span>`).join("")}</div>` : ""}</button>`;
}
function invalidIdsFor(request) { return invalidIdsPure(request, state.playerId, state.selected); }
function invalidIds() { return invalidIdsFor(state.request); }
function actionPanel() { return renderActionPanel({ state, e, roles, timerBadge, activePending, selectionRule, selectionCountValid, requiredModifier, pendingModifierOptions, copyLeafOptions, leafOptions, leafSelectionValid }); }
function game() { return renderGame({ state, e, soundToggleButton, entityFor, chatAvailability, actionPanel, playerCard, roleStateItems }); }
function render() {
  if (chatCompositionActive) {
    renderPendingAfterComposition = true;
    return false;
  }
  renderPendingAfterComposition = false;
  const currentFeed = document.querySelector(".feed");
  if (currentFeed) {
    state.feedPinned = isFeedAtBottom();
    state.feedScrollTop = currentFeed.scrollTop;
  }
  const currentChatInput = document.querySelector("#chat-input");
  const restoreChatFocus = document.activeElement === currentChatInput;
  const selectionStart = currentChatInput?.selectionStart;
  const selectionEnd = currentChatInput?.selectionEnd;
  if (currentChatInput) state.chatDraft = currentChatInput.value;
  app.innerHTML = state.view === "landing" ? landing() : state.view === "room" ? room() : game();
  bind();
  const nextChatInput = document.querySelector("#chat-input");
  if (restoreChatFocus && nextChatInput && !nextChatInput.disabled) {
    nextChatInput.focus({ preventScroll: true });
    if (Number.isInteger(selectionStart) && Number.isInteger(selectionEnd))
      nextChatInput.setSelectionRange(selectionStart, selectionEnd);
  }
  return true;
}
function beginChatComposition() { chatCompositionActive = true; }
function endChatComposition(input) {
  chatCompositionActive = false;
  if (input) state.chatDraft = input.value;
  if (!renderPendingAfterComposition) return;
  renderPendingAfterComposition = false;
  setTimeout(() => render(), 0);
}
function choosePlayer(id) {
  if (!state.request && !state.activePendingId) return;
  const next = choosePlayerSelection(state.selected, id, state.request || activePending(), { roleNames: roles });
  if (next === null) return notify(`最多选择 ${selectionRule().max} 名玩家`);
  state.selected = next;
  if (!state.request && state.activePendingId) {
    state.pendingDrafts[state.activePendingId] = [...state.selected]; state.pendingModifiers[state.activePendingId] = state.modifier;
    state.preSubmittedDrafts[state.activePendingId] = false;
  }
  syncDraft(formatSelection(), false); render();
}
function bind() {
  document.querySelector("#entry-form")?.addEventListener("submit", event => {
    event.preventDefault(); const action = event.submitter?.value; const name = document.querySelector("#name").value.trim(); const roomCode = document.querySelector("#code").value.trim(); state.server = document.querySelector("#server").value.trim();
    if (!name) return notify("先填一个昵称"); if (isReservedNickname(name)) return notify("昵称不能与身份名相同"); if (action === "join" && !/^\d{6}$/.test(roomCode)) return notify("房间号是 6 位数字");
    unlockAudio(); requestBrowserNotifications();
    connect({ type: action === "create" ? "create_room" : "join_room", playerName: name, roomCode });
  });
  const copyRoomCode = async () => notify(await copyText(state.roomCode) ? "房间号已复制" : "复制失败，请手动选择房间号");
  document.querySelector("#copy-room-code")?.addEventListener("click", copyRoomCode);
  document.querySelector("#copy-room-code")?.addEventListener("keydown", event => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); copyRoomCode(); } });
  document.querySelector("#copy-room")?.addEventListener("click", async () => notify(await copyText(`来玩 MF 杀：房间 ${state.roomCode}`) ? "邀请信息已复制" : "复制失败，请手动复制房间号"));
  document.querySelector("#start-game")?.addEventListener("click", () => send({ type: "start_game" }));
  document.querySelector("#room-settings")?.addEventListener("submit", event => {
    event.preventDefault();
    const seconds = id => Number(document.querySelector(id).value);
    send({ type: "update_room_settings", requestTimeoutSeconds: seconds("#setting-request-timeout"), voteSecondsPerAlive: seconds("#setting-vote-per-alive"), votePenaltySeconds: seconds("#setting-vote-penalty"), eventIntervalSeconds: seconds("#setting-event-interval") });
  });
  document.querySelector("[data-toggle-sound]")?.addEventListener("click", () => { state.soundEnabled = !state.soundEnabled; localStorage.setItem("weremf.sound", state.soundEnabled ? "on" : "off"); if (state.soundEnabled) playSound("request"); else stopSounds(); notify(state.soundEnabled ? "音效已开启" : "音效已关闭"); render(); });
  document.querySelector("[data-leave-room]")?.addEventListener("click", leaveRoom);
  document.querySelector("[data-restart-room]")?.addEventListener("click", () => {
    if (globalThis.confirm?.("结束当前对局并让所有仍在房间的玩家返回等待大厅？") === false) return;
    send({ type: "restart_room" });
  });
  document.querySelector("[data-add-bot]")?.addEventListener("click", () => send({ type: "add_bot" }));
  document.querySelector("[data-remove-bot]")?.addEventListener("click", () => send({ type: "remove_bot" }));
  document.querySelector("#toggle-role")?.addEventListener("click", () => { state.roleVisible = !state.roleVisible; render(); });
  const chatInput = document.querySelector("#chat-input");
  chatInput?.addEventListener("input", () => { state.chatDraft = chatInput.value; });
  chatInput?.addEventListener("compositionstart", beginChatComposition);
  chatInput?.addEventListener("compositionend", () => endChatComposition(chatInput));
  document.querySelector("#chat-form")?.addEventListener("submit", event => {
    event.preventDefault(); const value = state.chatDraft.trim();
    if (!value) return; send({ type: "chat", value }); state.chatDraft = ""; chatInput.value = "";
  });
  document.querySelector("#download-log")?.addEventListener("click", () => { const blob = new Blob(["\uFEFF", state.gameLog.content], { type: "text/plain;charset=utf-8" }); const url = URL.createObjectURL(blob); const link = document.createElement("a"); link.href = url; link.download = state.gameLog.fileName || "WereMF.log"; link.click(); URL.revokeObjectURL(url); });
  document.querySelectorAll("[data-player]").forEach(el => el.addEventListener("click", () => choosePlayer(Number(el.dataset.player))));

  document.querySelectorAll("[data-pending]").forEach(el => el.addEventListener("click", () => {
    if (state.activePendingId) { state.pendingDrafts[state.activePendingId] = [...state.selected]; state.pendingModifiers[state.activePendingId] = state.modifier; syncDraft(formatSelection()); }
    state.activePendingId = el.dataset.pending; state.selected = [...(state.pendingDrafts[state.activePendingId] || [])]; state.modifier = state.pendingModifiers[state.activePendingId] || ""; render();
  }));
  document.querySelector("[data-clear-draft]")?.addEventListener("click", () => {
    state.selected = []; state.modifier = ""; state.pendingDrafts[state.activePendingId] = []; state.pendingModifiers[state.activePendingId] = ""; state.preSubmittedDrafts[state.activePendingId] = false; syncDraft("", false); render();
  });
  document.querySelector("[data-pre-submit]")?.addEventListener("click", () => {
    const id = state.activePendingId; if (!id) return;
    const armed = !state.preSubmittedDrafts[id]; state.preSubmittedDrafts[id] = armed;
    state.pendingDrafts[id] = [...state.selected]; state.pendingModifiers[id] = state.modifier;
    syncDraft(formatSelection(), armed); notify(armed ? "已预提交；轮到该技能时会自动复核并行动" : "已取消预提交"); render();
  });
  document.querySelectorAll("[data-value]").forEach(el => el.addEventListener("click", () => submit(el.dataset.value)));
  document.querySelectorAll("[data-role]").forEach(el => el.addEventListener("click", () => {
    const role = el.dataset.role;
    if (state.selected.includes(role)) state.selected = state.selected.filter(x=>x!==role);
    else {
      const limit = selectionRule().max;
      if (state.selected.length >= limit) return notify(`最多选择 ${limit} 个角色`);
      state.selected = [...state.selected,role];
    }
    syncDraft(formatSelection()); render();
  }));
  document.querySelectorAll("[data-modifier]").forEach(el => el.addEventListener("click", () => { state.modifier = el.dataset.modifier; if (!state.request && state.activePendingId) { state.pendingModifiers[state.activePendingId] = state.modifier; state.preSubmittedDrafts[state.activePendingId] = false; } syncDraft(formatSelection(), false); render(); }));
  document.querySelector("[data-submit]")?.addEventListener("click", () => submit(formatSelection()));
  document.querySelector("[data-giveup]")?.addEventListener("click", () => submit("0"));
  document.querySelector("[data-manual]")?.addEventListener("click", () => submit(document.querySelector("#manual-input").value));
  document.querySelectorAll("[data-command]").forEach(el => el.addEventListener("click", () => send({ type: "command", value: el.dataset.command })));
  const feed = document.querySelector(".feed");
  if (feed) {
    feed.scrollTop = state.feedPinned ? feed.scrollHeight : Math.min(state.feedScrollTop, Math.max(0, feed.scrollHeight - feed.clientHeight));
    feed.addEventListener("scroll", () => {
      state.feedPinned = isFeedAtBottom();
      state.feedScrollTop = feed.scrollTop;
    });
  }
}
function formatSelection() {
  return formatSelectionPure(state.request?.api || "", state.selected, state.modifier);
}
function submit(value) {
  if (value === "") return notify("请输入内容"); const request = state.request; send({ type: "game_input", value: String(value) });
  const id = pendingId(request?.data); if (id) removePending(id); state.request = null; state.selected = []; state.modifier = ""; if (state.timerMode !== "vote") clearTimer(); render();
}

const saved = JSON.parse(localStorage.getItem("weremf.session") || "null");
if (saved?.roomCode && saved?.token) { state.server = saved.server || ""; state.reconnecting = true; connect({ type: "reconnect", ...saved }); }
if (typeof setInterval === "function") setInterval(() => {
  if (!state.timerDeadline) return;
  document.querySelectorAll("[data-timer]").forEach(node => node.textContent = timerText());
}, 250);
render();

export { state, onMessage, render, game, actionPanel, selectionRule, selectionCountValid, formatSelection, choosePlayer, invalidIdsFor, roleStateItems, playerCard, chatAvailability, appendChat, appendCliInput, beginChatComposition, endChatComposition, isReservedNickname, defaultRoomSettings, soundForGameMessage, soundFiles, applyEntityStatePatch };
import { landing as renderLanding } from "./views/landing.js";
import { room as renderRoom } from "./views/room.js";
import { actionPanel as renderActionPanel } from "./views/action-panel.js";
import { game as renderGame } from "./views/game.js";
