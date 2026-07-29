const app = document.querySelector("#app");
const toast = document.querySelector("#toast");
const roles = ["脚滑人","Doge","庸医","地鼠","兔子","铯郎","法猫","卡比","粉侠","爬行者","炮仙","实物","灰卡比","音魔","CTF","合虫","彩怪","贤松","江仙","myz","叶子"];
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
  pendingSkills: [], pendingDrafts: {}, pendingModifiers: {}, preSubmittedDrafts: {}, activePendingId: "", gameLog: null,
  timerDeadline: 0, timerApi: "", timerMode: "", feedPinned: true, feedScrollTop: 0, chatDraft: "", botIds: [], leaving: false,
  roomSettings: { ...defaultRoomSettings }, soundEnabled: localStorage.getItem("weremf.sound") !== "off"
};
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
function playSound(name) {
  const source = soundFiles[name];
  if (!source || !state.soundEnabled || state.reconnecting || !("Audio" in globalThis)) return false;
  const audio = new globalThis.Audio(source); audio.preload = "auto"; audio.volume = 0.8;
  soundPlayers.add(audio);
  const release = () => soundPlayers.delete(audio);
  audio.addEventListener?.("ended", release, { once: true }); audio.addEventListener?.("error", release, { once: true });
  const playback = audio.play(); playback?.catch?.(release);
  return true;
}
function unlockAudio() {
  if (!state.soundEnabled || !("Audio" in globalThis)) return;
  const audio = new globalThis.Audio(soundFiles.request); audio.muted = true;
  const release = () => { try { audio.pause(); audio.currentTime = 0; } catch {} };
  const playback = audio.play(); playback?.then?.(release).catch?.(release);
}
function stopSounds() { for (const audio of soundPlayers) { try { audio.pause(); } catch {} } soundPlayers.clear(); }
function playGameMessageSound(msg) { const sound = soundForGameMessage(msg); if (sound) playSound(sound); }
function soundToggleButton() { return `<button class="btn btn-ghost sound-toggle" data-toggle-sound title="${state.soundEnabled ? "关闭" : "开启"}游戏音效">${state.soundEnabled ? "🔊 音效" : "🔇 静音"}</button>`; }
const socketUrl = () => state.server.trim() || `${location.protocol === "https:" ? "wss:" : "ws:"}//${location.host}/ws`;
const notify = message => { toast.textContent = message; toast.classList.add("show"); clearTimeout(notify.timer); notify.timer = setTimeout(() => toast.classList.remove("show"), 2600); };
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

function stopTitleFlash() {
  if (titleFlashTimer) clearInterval(titleFlashTimer);
  titleFlashTimer = null;
  document.title = defaultTitle;
}
function flashTitle(message) {
  if (state.reconnecting) return;
  stopTitleFlash();
  let tick = 0;
  const update = () => {
    document.title = tick++ % 2 ? defaultTitle : `● ${message} · MF 杀`;
    if (tick >= 12) stopTitleFlash();
  };
  update();
  if (typeof setInterval !== "function") return;
  titleFlashTimer = setInterval(update, 650);
}
function requestBrowserNotifications() {
  if (!("Notification" in globalThis) || Notification.permission !== "default") return;
  Notification.requestPermission().catch(() => {});
}
function alertRequest(request) {
  playSound("request");
  flashTitle("轮到你行动");
  if (document.visibilityState === "visible" && !state.reconnecting)
    notify(`轮到你行动：${String(request.message_content || "请做出选择").replace(/\s+/g, " ").slice(0, 48)}`);
  if (!("Notification" in globalThis) || Notification.permission !== "granted" || state.reconnecting) return;
  const notification = new Notification("MF 杀 · 轮到你行动", {
    body: String(request.message_content || "请返回游戏做出选择"),
    icon: "/og.png",
    tag: `weremf-${state.roomCode}-${request.api || "request"}`,
    renotify: true
  });
  notification.onclick = () => globalThis.focus?.();
}
document.addEventListener?.("visibilitychange", () => { if (!document.hidden) stopTitleFlash(); });
globalThis.addEventListener?.("focus", stopTitleFlash);

function connect(firstMessage) {
  if (state.socket) try { state.socket.close(); } catch {}
  let url = socketUrl(); if (!/^wss?:\/\//.test(url)) url = `ws://${url}`; if (!url.endsWith("/ws")) url = url.replace(/\/$/, "") + "/ws";
  const ws = new WebSocket(url); state.socket = ws; render();
  ws.addEventListener("open", () => { state.connected = true; ws.send(JSON.stringify(firstMessage)); render(); });
  ws.addEventListener("message", event => onMessage(JSON.parse(event.data)));
  ws.addEventListener("close", () => { state.connected = false; render(); if (state.view !== "landing" && !state.reconnecting && !state.leaving) notify("连接已断开，可刷新页面自动重连"); });
  ws.addEventListener("error", () => notify("无法连接游戏服务器"));
}
function send(data) { if (state.socket?.readyState === WebSocket.OPEN) state.socket.send(JSON.stringify(data)); else notify("尚未连接服务器"); }
function clearNewGamePresentation() {
  clearTimer();
  Object.assign(state, {
    entities: [], votes: [], events: [], phase: "准备", round: 0, role: "身份尚未揭晓", roleVisible: true,
    request: null, selected: [], modifier: "", pendingSkills: [], pendingDrafts: {}, pendingModifiers: {},
    preSubmittedDrafts: {}, activePendingId: "", gameLog: null, feedPinned: true, feedScrollTop: 0, chatDraft: ""
  });
}
function resetGameToLobby() {
  clearTimer();
  Object.assign(state, { view: "room", started: false, entities: [], votes: [], events: [], phase: "等待", round: 0,
    role: "身份尚未揭晓", roleVisible: true, request: null, selected: [], modifier: "", pendingSkills: [],
    pendingDrafts: {}, pendingModifiers: {}, preSubmittedDrafts: {}, activePendingId: "", gameLog: null, feedPinned: true, feedScrollTop: 0, chatDraft: "" });
}
function finishLeave() {
  localStorage.removeItem("weremf.session");
  const socket = state.socket;
  Object.assign(state, { view: "landing", socket: null, connected: false, roomCode: "", playerId: null,
    playerName: "", token: "", isHost: false, started: false, players: [], entities: [], votes: [], events: [],
    request: null, selected: [], pendingSkills: [], pendingDrafts: {}, pendingModifiers: {}, preSubmittedDrafts: {}, activePendingId: "", gameLog: null, chatDraft: "",
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
  if (playCue) playGameMessageSound(msg);
  const api = msg.api || ""; const text = String(msg.message_content || "").trim(); const privateMessage = msg.message_type !== "public";
  if (api === "player_init" || api === "player_anonymous_init") {
    clearNewGamePresentation();
    state.players = (msg.data || []).map(p => ({ ...p, connected: true, isBot: state.botIds.includes(p.id) }));
  }
  if (api === "player_notify_chara" || api === "player_notify_chara_reset") state.role = text || "未知身份";
  if (api === "leaf_notify_first_chara" || api === "leaf_notify_first_chara_reroll") state.role = `叶子 · ${text}`;
  if (api === "night_start_broadcast") { clearTimer(); state.phase = "夜晚"; state.round++; state.request = null; state.selected = []; state.modifier = ""; state.votes = []; state.pendingSkills = []; state.pendingDrafts = {}; state.pendingModifiers = {}; state.preSubmittedDrafts = {}; state.activePendingId = ""; }
  if (api === "day_start_broadcast") { clearTimer(); state.phase = "白天"; state.request = null; state.selected = []; state.modifier = ""; state.pendingSkills = []; state.pendingDrafts = {}; state.pendingModifiers = {}; state.preSubmittedDrafts = {}; state.activePendingId = ""; }
  if (api === "vote_start_broadcast") { clearTimer(); state.phase = "投票"; state.request = null; state.selected = []; state.modifier = ""; state.votes = []; }
  if (api === "vote_end_broadcast") clearTimer();
  if (api === "game_win_broadcast") { clearTimer(); state.phase = "终局"; }
  if (api === "game_update_night" || api === "game_update_day") { state.entities = Array.isArray(msg.data) ? msg.data : msg.data?.entities || []; state.players = state.entities.map(entity => ({ ...entity.player, connected: true, isBot: state.botIds.includes(entity.player.id) })); }
  if (api === "game_update_vote") updateVotes(msg);
  if (api === "pending_skill_created") rememberPending(msg.data);
  if (api === "invalid_pending_skill_notify") removePending(pendingId(msg.data));
  if (api === "skill_blocked_by_jiaohua_notify") removePending(pendingId(msg.data));
  if (api.startsWith("request_") && !api.endsWith("_parse_error")) activateRequest(msg);
  if (api.endsWith("_parse_error")) { notify(text || "这个选择无效，请重试"); }
  if (recordEvent && text && !api.startsWith("request_") && !api.startsWith("game_update_"))
    addEvent(api, text, privateMessage);
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
function activePending() { return state.pendingSkills.find(x => x.id === state.activePendingId) || state.pendingSkills[0] || null; }
function selectionRule(context = state.request || activePending()) {
  const api = context?.api || ""; const type = context?.type || ""; const data = context?.data;
  if (api === "request_leaf_charas") { const count = Number(data?.choice_count) || 4; return { min: count, max: count }; }
  if (api === "request_myz_skill" || (!api && type === "myz")) return { min: 2, max: 2 };
  const exact = Number(data?.choice_count);
  if (exact > 0) return { min: exact, max: exact };
  const minimum = Math.max(1, Number(data?.choice_min) || 1);
  const explicitMax = Number(data?.choice_max);
  if (explicitMax > 0) return { min: minimum, max: explicitMax };
  const match = String(context?.message_content || "").match(/最多\s*(\d+)\s*个/);
  if (match) return { min: 1, max: Number(match[1]) };
  const pendingMaximum = ({ "庸医": 3, "法猫": 2, "灰卡比": 2 })[type];
  return { min: 1, max: pendingMaximum || 1 };
}
function selectionCountValid(context = state.request || activePending()) {
  const rule = selectionRule(context);
  return state.selected.length >= rule.min && state.selected.length <= rule.max;
}
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
function pendingId(data) { const raw = data?.skill_id ?? data?.id ?? data; return typeof raw === "object" ? raw?.id || "" : String(raw || ""); }
function rememberPending(data) {
  const id = pendingId(data); if (!id || state.pendingSkills.some(x => x.id === id)) return;
  state.pendingSkills.push({ ...data, id }); state.pendingSkills.sort((a,b) => b.priority - a.priority);
  if (!state.activePendingId) { state.activePendingId = id; state.selected = [...(state.pendingDrafts[id] || [])]; state.modifier = state.pendingModifiers[id] || ""; }
}
function removePending(id) {
  if (!id) return; state.pendingSkills = state.pendingSkills.filter(x => x.id !== id); delete state.pendingDrafts[id]; delete state.pendingModifiers[id]; delete state.preSubmittedDrafts[id];
  if (state.activePendingId === id) { state.activePendingId = state.pendingSkills[0]?.id || ""; state.selected = [...(state.pendingDrafts[state.activePendingId] || [])]; state.modifier = state.pendingModifiers[state.activePendingId] || ""; }
}
function requestChoiceInvalid(msg, value, index) {
  if (typeof value !== "number") return false;
  const property = msg.api === "request_myz_skill" && index === 1 ? "invalid_target_choice" : "invalid_choice";
  const list = msg.data?.[property] || [];
  return list.some(item => (typeof item === "number" ? item : item.id) === value);
}
function activateRequest(msg) {
  const id = pendingId(msg.data); const draft = id ? [...(state.pendingDrafts[id] || [])] : [];
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
function chatAvailability() {
  if (!state.started || !["白天", "投票"].includes(state.phase)) return { allowed: false, reason: "仅白天可以发言" };
  const me = entityFor(state.playerId); const st = me?.state;
  if (!st) return { allowed: false, reason: "正在同步发言状态" };
  if (st.is_dead || st.is_dead_public) return { allowed: false, reason: "已出局玩家不能发言" };
  if (state.botIds.includes(state.playerId)) return { allowed: false, reason: "BOT 托管中，不能发言" };
  return { allowed: true, reason: "输入白天发言" };
}
function addEvent(api, text, privateMessage) { appendEvent(api, text, privateMessage); }

function landing() {
  return `<main class="shell"><header class="topbar"><div class="brand"><span class="brand-mark">MF</span> MF 杀 ONLINE</div><div class="status"><span class="status-dot ${state.connected ? "" : "off"}"></span>${state.connected ? "已连接" : "等待连接"}</div></header>
  <section class="landing"><div class="hero"><div class="eyebrow">A social deduction game · 7–16 players</div><h1>今夜，<span>谁在说谎？</span></h1><p class="hero-copy">身份藏在暗处，技能按优先级交错结算。每个玩家只会收到属于自己的信息——讨论、判断，然后在天亮前做出选择。</p><div class="features"><div><b>21</b>独特身份</div><div><b>2</b>对立阵营</div><div><b>1</b>不可信的夜晚</div></div></div>
  <form class="entry-card" id="entry-form"><h2>进入游戏</h2><p>创建房间，或凭 6 位房间号加入朋友。</p><div class="field"><label for="name">你的昵称</label><input class="input" id="name" maxlength="20" autocomplete="nickname" placeholder="今晚怎么称呼你" required /></div><div class="field"><label for="code">房间号 · 加入时填写</label><input class="input" id="code" inputmode="numeric" maxlength="6" placeholder="例如 042016" /></div><div class="entry-actions"><button class="btn btn-primary" name="action" value="create">创建房间</button><button class="btn btn-ghost" name="action" value="join">加入房间</button></div><details class="server-field"><summary>连接自己的服务器</summary><input class="input" id="server" value="${e(state.server)}" placeholder="wss://game.example.com/ws" /></details></form></section></main>`;
}
function room() {
  const seats = [...state.players]; while (seats.length < Math.max(7, state.players.length)) seats.push(null);
  const settings = state.roomSettings;
  const settingsPanel = state.isHost ? `<form class="room-settings" id="room-settings"><label>普通请求限时<input class="input" id="setting-request-timeout" type="number" min="5" max="600" value="${settings.requestTimeoutSeconds}"/><span>秒</span></label><label>每名可投票玩家时长<input class="input" id="setting-vote-per-alive" type="number" min="5" max="600" value="${settings.voteSecondsPerAlive}"/><span>秒</span></label><label>每次投票扣减<input class="input" id="setting-vote-penalty" type="number" min="0" max="600" value="${settings.votePenaltySeconds}"/><span>秒</span></label><label>连续消息展示间隔<input class="input" id="setting-event-interval" type="number" min="0" max="10" value="${settings.eventIntervalSeconds}"/><span>秒</span></label><button class="btn btn-ghost" type="submit">保存房间设置</button></form>` : `<div class="settings-summary">普通请求 ${settings.requestTimeoutSeconds}s · 投票每人 ${settings.voteSecondsPerAlive}s · 每票扣 ${settings.votePenaltySeconds}s · 消息间隔 ${settings.eventIntervalSeconds}s</div>`;
  return `<main class="shell"><header class="topbar"><div class="brand"><span class="brand-mark">MF</span> MF 杀 ONLINE</div><div class="top-actions">${soundToggleButton()}<div class="status"><span class="status-dot ${state.connected ? "" : "off"}"></span>${state.connected ? "房间在线" : "连接中断"}</div><button class="btn btn-ghost leave-room" data-leave-room>彻底退出</button></div></header>
  <section class="room-head"><div><div class="eyebrow">私人房间</div><div class="room-code" id="copy-room-code" role="button" tabindex="0">${e(state.roomCode)} <small>点击复制</small></div></div><button class="btn btn-ghost" id="copy-room">复制邀请信息</button></section>
  <section class="lobby"><div class="panel"><div class="panel-title"><h2>玩家席位</h2><span class="count">${state.players.length} / 16</span></div><div class="players-grid">${seats.map((p,i) => p ? `<div class="player-seat ${p.connected ? "" : "offline"}"><span class="seat-no">${p.id}</span><div>${e(p.name)}${p.isHost ? '<span class="host-tag">房主</span>' : ""}${p.isBot ? '<span class="bot-tag">BOT</span>' : ""}${p.id === state.playerId ? '<span class="you-tag">你</span>' : ""}</div></div>` : `<div class="player-seat empty-seat"><span class="seat-no">${i+1}</span><div>等待加入</div></div>`).join("")}</div></div>
  <aside class="panel"><div class="panel-title"><h3>开局之前</h3></div><p class="lobby-note">需要至少 <strong>7 名玩家</strong>。开始后，系统会私下发送身份与行动面板。建议所有人保持页面开启，并在语音或线下完成白天讨论。</p><div class="divider"></div><p class="lobby-note">你是 <strong>${state.isHost ? "房主" : `${state.playerId} 号玩家`}</strong>${state.isHost ? "，玩家到齐后由你开局。" : "，等待房主开局。"}</p>${settingsPanel}${state.isHost ? `<div class="bot-controls"><button class="btn btn-ghost" data-add-bot ${state.players.length >= 16 ? "disabled" : ""}>＋ 增加 Bot</button><button class="btn btn-ghost" data-remove-bot ${state.players.some(p=>p.isPermanentBot) ? "" : "disabled"}>－ 删除 Bot</button></div>` : ""}<div class="start-wrap">${state.isHost ? `<button class="btn btn-primary" id="start-game" ${state.players.length < 7 ? "disabled" : ""}>${state.players.length < 7 ? `还差 ${7-state.players.length} 人` : "所有人准备好 · 开始"}</button>` : `<button class="btn" disabled>等待房主开始</button>`}</div></aside></section></main>`;
}
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
function invalidIdsFor(request) {
  const data = request?.data; let list;
  if (request?.api === "request_vote" && Array.isArray(data)) list = data.find(x => x.id === state.playerId)?.invalid_vote || [];
  else if (request?.api === "request_myz_skill" && state.selected.length === 1) list = data?.invalid_target_choice || [];
  else list = data?.invalid_choice || data?.invalid_vote || [];
  return new Set(list.map(x => typeof x === "number" ? x : x.id));
}
function invalidIds() { return invalidIdsFor(state.request); }
function actionPanel() {
  const r = state.request;
  if (!r && state.pendingSkills.length) {
    const active = activePending(); const rule = selectionRule(active); const armed = Boolean(state.preSubmittedDrafts[active.id]);
    const modifierChoices = pendingModifierOptions(active.type);
    const modifierHtml = modifierChoices.length ? `<div class="choice-row" style="margin-top:10px">${modifierChoices.map(x=>`<button class="choice ${state.modifier===x[0]?"selected":""}" data-modifier="${x[0]}">${x[1]}</button>`).join("")}</div>` : "";
    const canPreSubmit = selectionCountValid(active) && (!requiredModifier(active) || state.modifier);
    const countHint = rule.min === rule.max ? `请选择 ${rule.max} 名玩家` : `可选择 ${rule.min}–${rule.max} 名玩家`;
    return `<section class="panel action-panel pending"><div class="action-kicker">提前准备 · ${armed ? "已预提交" : "尚不能正式提交"}</div><div class="choice-row">${state.pendingSkills.map(x=>`<button class="choice ${x.id===active.id?"selected":""}" data-pending="${e(x.id)}">${e(x.type)} · 优先级 ${x.priority}${state.preSubmittedDrafts[x.id] ? " · 已预提交" : ""}</button>`).join("")}</div><h2>预选「${e(active.type)}」技能目标</h2><p class="lobby-note">${countHint}。点击“预提交”后，真正轮到该技能时会按最新局面复核；合法则自动行动，失效则取消并提醒。</p>${modifierHtml}<div class="action-footer"><button class="btn btn-ghost" data-clear-draft>清除预选</button><button class="btn btn-primary" data-pre-submit ${armed || canPreSubmit ? "" : "disabled"}>${armed ? "取消预提交" : `预提交${state.selected.length ? ` · ${state.selected.join("、")}` : ""}`}</button></div></section>`;
  }
  if (!r) return `<section class="panel action-panel inactive"><div class="action-kicker">CURRENT ACTION ${timerBadge()}</div><h2>${state.timerMode === "vote" ? "投票进行中" : "等待其他玩家行动"}</h2><p class="lobby-note">${state.timerMode === "vote" ? "共享投票时间正在倒计时；每次有效投票都会扣减时间。" : "轮到你时，选择面板会自动出现。"}</p></section>`;
  const api = r.api; const boolRequest = /(?:reborn|drink_milk|give_mfa|red_ground|anonymous_game|leaf_game|leaf_chara_reroll|using_copy_skill|for_next_game|reroll_player)$/.test(api);
  const forceChoice = api.includes("force_threaten") && api !== "request_myz_skill_force_threaten"; const roleChoice = api === "request_leaf_charas"; const copyLeafChoice = api === "request_hechong_copy_leaf";
  let choices = "";
  if (boolRequest) choices = `<div class="choice-row"><button class="choice" data-value="1">确认 / 是</button><button class="choice" data-value="0">放弃 / 否</button></div>`;
  else if (copyLeafChoice) { const options = copyLeafOptions(r); choices = `<div class="choice-row">${options.map(option=>`<button class="choice" data-value="${e(option.value)}">${e(option.value)}：${e(option.label)}</button>`).join("")}</div>`; }
  else if (forceChoice) {
    const opts = api === "request_xiansong_skill_force_threaten" ? [["m","强制索要 MFA"],["x","丢咸松球"],["0","放弃"]] : [["1","选项 1"],["0","选项 0"]];
    choices = `<div class="choice-row">${opts.map(x=>`<button class="choice" data-value="${x[0]}">${x[1]}</button>`).join("")}</div>`;
  }
  else if (roleChoice) {
    const options = api === "request_leaf_charas" ? leafOptions(r) : roles.map(value => ({ value, camp: "" }));
    choices = `<div class="choice-row">${options.map(option=>`<button class="choice ${state.selected.includes(option.value)?"selected":""}" data-role="${e(option.value)}">${e(option.value)}${option.camp ? ` · ${e(option.camp)}` : ""}</button>`).join("")}</div>`;
    if (api === "request_leaf_charas") {
      const camps = [...new Set(state.selected.map(value => options.find(option => option.value === value)?.camp).filter(Boolean))];
      choices += `<p class="leaf-hint ${leafSelectionValid(r)?"valid":""}">已选 ${state.selected.length}/${r.data?.choice_count || 4} · ${camps.length ? `阵营：${camps.join("、")}` : "需同时包含吧方与爆方"} · 不可选择粉侠、彩怪、叶子</p>`;
    }
  }
  const modifierSets = {
    request_jiaohua_dead_skill: [["x","封住行动"],["p","保护玩家"]],
    request_rabi_skill: [["x","鲜奶"],["d","毒奶"]],
    request_doge_skill: [["","仅保护"],["b","保护后自爆"]],
    request_caimon_skill: [["","一根彩条"],["d","两根彩条"]],
    request_myz_skill_force_threaten: [["","普通威胁"],["f","自爆并强制"]],
    request_vote: (Array.isArray(r.data) && r.data.find(x=>x.id===state.playerId)?.can_suicide) ? [["","正常投票"],["b","脚滑人自爆"]] : [["","正常投票"]]
  };
  if (modifierSets[api]) choices += `<div class="choice-row" style="margin-top:10px">${modifierSets[api].map(x=>`<button class="choice ${state.modifier===x[0]?"selected":""}" data-modifier="${x[0]}">${x[1]}</button>`).join("")}</div>`;
  const showSubmit = !boolRequest && !forceChoice; const canSubmit = selectionCountValid(r) && (!requiredModifier(r) || state.modifier) && leafSelectionValid(r);
  return `<section class="panel action-panel"><div class="action-kicker">${r.web_concurrent ? `并发输入 · 剩余 ${r.web_remaining} 次` : "轮到你行动"} ${timerBadge()}</div><h2>${e(r.message_content || "请做出选择")}</h2>${choices}${showSubmit ? `<div class="action-footer">${api === "request_leaf_charas" ? "" : `<button class="btn btn-ghost" data-giveup>放弃</button>`}<button class="btn btn-primary" data-submit ${canSubmit ? "" : "disabled"}>确认选择${state.selected.length ? ` · ${state.selected.join("、")}` : ""}</button></div>` : ""}<details class="manual"><summary>高级：按 CLI 格式输入</summary><div class="manual-row"><input class="input" id="manual-input" placeholder="输入原始指令"/><button class="btn" data-manual>发送</button></div></details></section>`;
}
function game() {
  const players = state.players.length ? state.players : Array.from({length:7},(_,i)=>({id:i+1,name:`${i+1} 号玩家`}));
  const chat = chatAvailability();
  const timelineHtml = state.events.length ? state.events.map(x => {
    if (x.chat) {
      const sender = state.players.find(player => player.id === x.playerId);
      const senderText = `${x.playerId} 号 · ${sender?.name || "玩家"}`;
      return `<article class="event chat-message ${x.playerId === state.playerId ? "mine" : ""}"><div class="event-meta">${e(x.time)} · ${e(senderText)}</div><div class="event-text">${e(x.text)}</div></article>`;
    }
    return `<article class="event ${x.private ? "private" : ""}"><div class="event-meta">${e(x.time)} · ${e(x.api.replaceAll("_"," "))}</div><div class="event-text">${e(x.text)}</div></article>`;
  }).join("") : '<div class="empty-state">还没有聊天或事件</div>';
  const chatForm = `<form class="chat-form" id="chat-form"><input class="input" id="chat-input" maxlength="300" autocomplete="off" value="${e(state.chatDraft)}" placeholder="${e(chat.reason)}" ${chat.allowed ? "" : "disabled"}/><button class="btn btn-primary" ${chat.allowed ? "" : "disabled"}>发送</button></form>`;
  const me = entityFor(state.playerId); const myRole = me?.role;
  const personalStates = [
    me?.state?.is_bar_leader ? { text: "🍺 你是吧主", kind: "bar-leader" } : null,
    ...roleStateItems(myRole?.data ?? myRole?.role?.data)
  ].filter(Boolean);
  const personalStateHtml = personalStates.length ? `<div class="role-states personal-role-states">${personalStates.map(x => `<span class="role-state ${x.kind}">${e(x.text)}</span>`).join("")}</div>` : "";
  return `<main class="shell"><header class="topbar"><div class="brand"><span class="brand-mark">MF</span> 房间 ${e(state.roomCode)}</div><div class="top-actions">${soundToggleButton()}<div class="status"><span class="status-dot ${state.connected ? "" : "off"}"></span>${state.playerId} 号 · ${e(state.playerName)}</div><button class="btn btn-ghost leave-room" data-leave-room>彻底退出</button></div></header>
  <div class="game-layout"><aside class="panel phase-panel"><div class="phase-orb ${state.phase === "夜晚" ? "night" : ""}"></div><div><div class="phase-name">${e(state.phase)}</div><div class="phase-sub">${state.round ? `第 ${state.round} 夜 · ` : ""}${state.phase === "夜晚" ? "请保持安静" : "信息公开"}</div></div><div class="identity-card ${state.roleVisible ? "" : "hidden"}"><div class="identity-main"><div><small>你的身份</small><strong>${e(state.role)}</strong></div><div class="game-seat"><small>本局编号</small><b>${state.playerId}</b><span>号</span></div></div>${personalStateHtml}<button class="identity-toggle" id="toggle-role">${state.roleVisible ? "隐藏身份" : "显示身份"}</button></div>${state.isHost ? `<div class="host-tools"><button class="btn" data-restart-room>重开并返回大厅</button></div>` : ""}${state.gameLog ? `<button class="btn log-download" id="download-log">下载本局日志</button>` : ""}</aside>
  <section class="panel board-panel"><div class="panel-title"><h2>在场玩家</h2><span class="count">点击玩家以选择目标</span></div><div class="board-list">${players.map(playerCard).join("")}</div></section>
  <aside class="right-stack">${actionPanel()}<section class="panel chat-panel"><div class="panel-title"><h3>聊天与事件</h3><span class="count">白天存活玩家可以发言</span></div><div class="feed">${timelineHtml}</div>${chatForm}</section></aside></div></main>`;
}
function render() {
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
}
function choosePlayer(id) {
  if (!state.request && !state.activePendingId) return;
  const rule = selectionRule(); const myz = state.request?.api === "request_myz_skill" || (!state.request && activePending()?.type === "myz");
  if (myz) state.selected = state.selected.length >= rule.max ? [id] : [...state.selected, id];
  else if (state.selected.includes(id)) state.selected = state.selected.filter(x => x !== id);
  else if (rule.max === 1) state.selected = [id];
  else if (state.selected.length >= rule.max) return notify(`最多选择 ${rule.max} 名玩家`);
  else state.selected = [...state.selected, id];
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
  const api = state.request?.api || ""; const ids = state.selected.join(" ");
  if (api === "request_vote") return state.modifier === "b" ? "b" : String(state.selected[0] ?? "");
  return `${ids}${state.modifier ? ` ${state.modifier}` : ""}`.trim();
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
