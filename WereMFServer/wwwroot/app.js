const app = document.querySelector("#app");
const toast = document.querySelector("#toast");
const roles = ["脚滑人","Doge","庸医","地鼠","兔子","铯郎","法猫","卡比","粉侠","爬行者","炮仙","实物","灰卡比","音魔","CTF","合虫","彩怪","贤松","江仙","myz","叶子"];
const state = {
  view: "landing", socket: null, connected: false, server: new URLSearchParams(location.search).get("server") || "",
  roomCode: "", playerId: null, playerName: "", token: "", isHost: false, started: false,
  players: [], entities: [], votes: [], events: [], phase: "等待", round: 0, role: "身份尚未揭晓",
  roleVisible: true, request: null, selected: [], modifier: "", reconnecting: false,
  pendingSkills: [], pendingDrafts: {}, activePendingId: "", gameLog: null,
  timerDeadline: 0, timerApi: "", timerMode: ""
};
const e = (value = "") => String(value).replace(/[&<>'"]/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"}[c]));
const socketUrl = () => state.server.trim() || `${location.protocol === "https:" ? "wss:" : "ws:"}//${location.host}/ws`;
const notify = message => { toast.textContent = message; toast.classList.add("show"); clearTimeout(notify.timer); notify.timer = setTimeout(() => toast.classList.remove("show"), 2600); };
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
  flashTitle("轮到你行动");
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
  ws.addEventListener("close", () => { state.connected = false; render(); if (state.view !== "landing" && !state.reconnecting) notify("连接已断开，可刷新页面自动重连"); });
  ws.addEventListener("error", () => notify("无法连接游戏服务器"));
}
function send(data) { if (state.socket?.readyState === WebSocket.OPEN) state.socket.send(JSON.stringify(data)); else notify("尚未连接服务器"); }

function onMessage(message) {
  if (message.type === "error") { notify(message.message); return; }
  if (message.type === "welcome") {
    Object.assign(state, { view: "room", roomCode: message.roomCode, playerId: message.playerId, playerName: message.playerName, token: message.token, isHost: message.isHost });
    localStorage.setItem("weremf.session", JSON.stringify({ roomCode: state.roomCode, playerName: state.playerName, token: state.token, server: state.server }));
  }
  if (message.type === "player_remapped") state.playerId = message.playerId;
  if (message.type === "room_state") {
    if (!message.started || !state.players.length) state.players = [...message.players];
    state.started = message.started;
    if (message.started) state.view = "game";
  }
  if (message.type === "game_message") handleGameMessage(message.payload);
  if (message.type === "game_ended") { addEvent("SYSTEM", message.message, false); state.request = null; clearTimer(); }
  if (message.type === "server_notice") addEvent("SERVER", message.message, true);
  if (message.type === "request_timer") { state.timerDeadline = message.deadlineUtc || 0; state.timerApi = message.api || ""; state.timerMode = message.mode || "request"; }
  if (message.type === "request_timeout_resolved") { notify(message.message); addEvent("TIMEOUT", message.message, message.api !== "request_vote"); state.request = null; state.selected = []; state.modifier = ""; clearTimer(); }
  if (message.type === "game_log_available") { state.gameLog = message; addEvent("SYSTEM", "本局日志已生成，所有玩家均可下载", false); }
  if (message.type === "input_accepted") notify(message.remaining ? `已暂存，还可提交 ${message.remaining} 次` : "已提交，等待其他玩家");
  render();
}
function handleGameMessage(msg) {
  const api = msg.api || ""; const text = String(msg.message_content || "").trim(); const privateMessage = msg.message_type !== "public";
  if (api === "player_init" || api === "player_anonymous_init") state.players = (msg.data || []).map(p => ({ ...p, connected: true }));
  if (api === "player_notify_chara" || api === "player_notify_chara_reset") state.role = text || "未知身份";
  if (api === "leaf_notify_first_chara" || api === "leaf_notify_first_chara_reroll") state.role = `叶子 · ${text}`;
  if (api === "night_start_broadcast") { clearTimer(); state.phase = "夜晚"; state.round++; state.request = null; state.selected = []; state.modifier = ""; state.votes = []; state.pendingSkills = []; state.pendingDrafts = {}; state.activePendingId = ""; }
  if (api === "day_start_broadcast") { clearTimer(); state.phase = "白天"; state.request = null; state.selected = []; state.modifier = ""; state.pendingSkills = []; state.pendingDrafts = {}; state.activePendingId = ""; }
  if (api === "vote_start_broadcast") { clearTimer(); state.phase = "投票"; state.request = null; state.selected = []; state.modifier = ""; state.votes = []; }
  if (api === "vote_end_broadcast") clearTimer();
  if (api === "game_win_broadcast") { clearTimer(); state.phase = "终局"; }
  if (api === "game_update_night" || api === "game_update_day") { state.entities = Array.isArray(msg.data) ? msg.data : msg.data?.entities || []; state.players = state.entities.map(entity => ({ ...entity.player, connected: true })); }
  if (api === "game_update_vote") updateVotes(msg);
  if (api === "pending_skill_created") rememberPending(msg.data);
  if (api === "invalid_pending_skill_notify") removePending(pendingId(msg.data));
  if (api.startsWith("request_") && !api.endsWith("_parse_error")) activateRequest(msg);
  if (api.endsWith("_parse_error")) { notify(text || "这个选择无效，请重试"); }
  if (text && !api.startsWith("request_") && !api.startsWith("game_update_")) addEvent(api, text, privateMessage);
}
function clearTimer() { state.timerDeadline = 0; state.timerApi = ""; state.timerMode = ""; }
function timerText() {
  if (!state.timerDeadline) return "";
  const seconds = Math.max(0, Math.ceil((state.timerDeadline - Date.now()) / 1000));
  const minutes = Math.floor(seconds / 60); const rest = seconds % 60;
  return `${minutes}:${String(rest).padStart(2,"0")}`;
}
function timerBadge() { return state.timerDeadline ? `<span class="request-timer ${state.timerMode === "vote" ? "vote" : ""}" data-timer>${timerText()}</span>` : ""; }
function syncDraft(value = state.selected.join(" ")) {
  const skillId = state.request ? pendingId(state.request.data) : state.activePendingId;
  const api = state.request?.api || "";
  if (skillId || api) send({ type: "pending_draft", skillId, api, value: String(value).trim() });
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
  if (!state.activePendingId) { state.activePendingId = id; state.selected = [...(state.pendingDrafts[id] || [])]; }
}
function removePending(id) {
  if (!id) return; state.pendingSkills = state.pendingSkills.filter(x => x.id !== id); delete state.pendingDrafts[id];
  if (state.activePendingId === id) { state.activePendingId = state.pendingSkills[0]?.id || ""; state.selected = [...(state.pendingDrafts[state.activePendingId] || [])]; }
}
function activateRequest(msg) {
  state.request = msg; state.modifier = ""; const id = pendingId(msg.data); const draft = id ? [...(state.pendingDrafts[id] || [])] : [];
  const invalid = invalidIdsFor(msg); const removed = draft.filter(x => typeof x === "number" && invalid.has(x));
  state.selected = draft.filter(x => typeof x !== "number" || !invalid.has(x)); state.activePendingId = id || state.activePendingId;
  if (removed.length) { const names = removed.map(id => state.players.find(x => x.id === id)?.name || `${id} 号`).join("、"); syncDraft(); notify(`局面已变化，已取消无效选择：${names}`); }
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
}function addEvent(api, text, privateMessage) {
  state.events.push({ api, text, private: privateMessage, time: new Date().toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" }) });
  if (state.events.length > 180) state.events.shift();
  flashTitle("有新消息");
}

function landing() {
  return `<main class="shell"><header class="topbar"><div class="brand"><span class="brand-mark">MF</span> MF 杀 ONLINE</div><div class="status"><span class="status-dot ${state.connected ? "" : "off"}"></span>${state.connected ? "已连接" : "等待连接"}</div></header>
  <section class="landing"><div class="hero"><div class="eyebrow">A social deduction game · 7–16 players</div><h1>今夜，<span>谁在说谎？</span></h1><p class="hero-copy">身份藏在暗处，技能按优先级交错结算。每个玩家只会收到属于自己的信息——讨论、判断，然后在天亮前做出选择。</p><div class="features"><div><b>21</b>独特身份</div><div><b>2</b>对立阵营</div><div><b>1</b>不可信的夜晚</div></div></div>
  <form class="entry-card" id="entry-form"><h2>进入游戏</h2><p>创建房间，或凭 6 位房间号加入朋友。</p><div class="field"><label for="name">你的昵称</label><input class="input" id="name" maxlength="20" autocomplete="nickname" placeholder="今晚怎么称呼你" required /></div><div class="field"><label for="code">房间号 · 加入时填写</label><input class="input" id="code" inputmode="numeric" maxlength="6" placeholder="例如 042016" /></div><div class="entry-actions"><button class="btn btn-primary" name="action" value="create">创建房间</button><button class="btn btn-ghost" name="action" value="join">加入房间</button></div><details class="server-field"><summary>连接自己的服务器</summary><input class="input" id="server" value="${e(state.server)}" placeholder="wss://game.example.com/ws" /></details></form></section></main>`;
}
function room() {
  const seats = [...state.players]; while (seats.length < Math.max(7, state.players.length)) seats.push(null);
  return `<main class="shell"><header class="topbar"><div class="brand"><span class="brand-mark">MF</span> MF 杀 ONLINE</div><div class="status"><span class="status-dot ${state.connected ? "" : "off"}"></span>${state.connected ? "房间在线" : "连接中断"}</div></header>
  <section class="room-head"><div><div class="eyebrow">私人房间</div><div class="room-code">${e(state.roomCode)} <small>点击复制</small></div></div><button class="btn btn-ghost" id="copy-room">复制邀请信息</button></section>
  <section class="lobby"><div class="panel"><div class="panel-title"><h2>玩家席位</h2><span class="count">${state.players.length} / 16</span></div><div class="players-grid">${seats.map((p,i) => p ? `<div class="player-seat ${p.connected ? "" : "offline"}"><span class="seat-no">${p.id}</span><div>${e(p.name)}${p.isHost ? '<span class="host-tag">房主</span>' : ""}${p.id === state.playerId ? '<span class="you-tag">你</span>' : ""}</div></div>` : `<div class="player-seat empty-seat"><span class="seat-no">${i+1}</span><div>等待加入</div></div>`).join("")}</div></div>
  <aside class="panel"><div class="panel-title"><h3>开局之前</h3></div><p class="lobby-note">需要至少 <strong>7 名玩家</strong>。开始后，系统会私下发送身份与行动面板。建议所有人保持页面开启，并在语音或线下完成白天讨论。</p><div class="divider"></div><p class="lobby-note">你是 <strong>${state.isHost ? "房主" : `${state.playerId} 号玩家`}</strong>${state.isHost ? "，玩家到齐后由你开局。" : "，等待房主开局。"}</p><div class="start-wrap">${state.isHost ? `<button class="btn btn-primary" id="start-game" ${state.players.length < 7 ? "disabled" : ""}>${state.players.length < 7 ? `还差 ${7-state.players.length} 人` : "所有人准备好 · 开始"}</button>` : `<button class="btn" disabled>等待房主开始</button>`}</div></aside></section></main>`;
}
function entityFor(id) { return state.entities.find(x => (x.player?.id ?? x.player?.Id) === id); }
function playerCard(p) {
  const entity = entityFor(p.id); const st = entity?.state || {}; const dead = st.is_dead_public || st.is_dead; const invalid = invalidIds().has(p.id); const selected = state.selected.includes(p.id);
  const vote = state.votes.find(x => x.id === p.id); const voteText = vote?.target == null ? "" : vote.target === 0 ? "弃票" : `投给 ${voteTargetText(vote.target)}`;
  const tokens = [["☁",st.smog_count],["💊",st.capsule_count],["💧",st.potion_count],["🍪",st.xian_song_count],["🐞",st.bug_count]].filter(x => x[1]>0);
  return `<button class="board-player ${dead ? "dead" : ""} ${selected ? "selected" : ""}" data-player="${p.id}" ${invalid ? "disabled" : ""}><div class="board-name"><span class="seat-no" style="display:inline-grid;width:25px;height:25px;border-radius:8px;margin-right:7px">${p.id}</span>${e(p.name)}</div><div class="board-role">${(entity?.role?.summary_name || entity?.role?.role?.summary_name) ? e(entity.role.summary_name || entity.role.role.summary_name) : dead ? e(st.dead_showing_name || "身份未公开") : "身份隐藏"}</div>${voteText ? `<div class="vote-status ${vote.confirmed ? "confirmed" : ""}">${e(voteText)}${vote.confirmed ? " · 已确认" : " · 可改票"}</div>` : ""}${tokens.length ? `<div class="tokens">${tokens.map(x=>`<span class="token">${x[0]} × ${x[1]}</span>`).join("")}</div>` : ""}</button>`;
}
function invalidIdsFor(request) {
  const data = request?.data; let list;
  if (request?.api === "request_vote" && Array.isArray(data)) list = data.find(x => x.id === state.playerId)?.invalid_vote || [];
  else list = data?.invalid_choice || data?.invalid_vote || [];
  return new Set(list.map(x => typeof x === "number" ? x : x.id));
}
function invalidIds() { return invalidIdsFor(state.request); }
function actionPanel() {
  const r = state.request;
  if (!r && state.pendingSkills.length) {
    const active = state.pendingSkills.find(x => x.id === state.activePendingId) || state.pendingSkills[0];
    return `<section class="panel action-panel pending"><div class="action-kicker">提前准备 · 尚不能提交</div><div class="choice-row">${state.pendingSkills.map(x=>`<button class="choice ${x.id===active.id?"selected":""}" data-pending="${e(x.id)}">${e(x.type)} · 优先级 ${x.priority}</button>`).join("")}</div><h2>预选「${e(active.type)}」技能目标</h2><p class="lobby-note">你可以现在选择目标；真正轮到该技能时才会开放提交。如果局面变化导致目标失效，系统会自动取消并提醒。</p><div class="action-footer"><button class="btn btn-ghost" data-clear-draft>清除预选</button><button class="btn btn-primary" disabled>等待技能结算顺序</button></div></section>`;
  }
  if (!r) return `<section class="panel action-panel inactive"><div class="action-kicker">CURRENT ACTION ${timerBadge()}</div><h2>${state.timerMode === "vote" ? "投票进行中" : "等待其他玩家行动"}</h2><p class="lobby-note">${state.timerMode === "vote" ? "共享投票时间正在倒计时；每次有效投票都会扣减时间。" : "轮到你时，选择面板会自动出现。"}</p></section>`;
  const api = r.api; const boolRequest = /(?:reborn|drink_milk|give_mfa|red_ground|anonymous_game|leaf_game|leaf_chara_reroll|using_copy_skill|for_next_game|reroll_player)$/.test(api);
  const forceChoice = api.includes("force_threaten") && api !== "request_myz_skill_force_threaten"; const roleChoice = api === "request_leaf_charas" || api === "request_hechong_copy_leaf";
  let choices = "";
  if (boolRequest) choices = `<div class="choice-row"><button class="choice" data-value="1">确认 / 是</button><button class="choice" data-value="0">放弃 / 否</button></div>`;
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
  const showSubmit = !boolRequest && !forceChoice; const requiredModifier = ["request_jiaohua_dead_skill","request_rabi_skill"].includes(api); const canSubmit = state.selected.length && (!requiredModifier || state.modifier) && leafSelectionValid(r);
  return `<section class="panel action-panel"><div class="action-kicker">${r.web_concurrent ? `并发输入 · 剩余 ${r.web_remaining} 次` : "轮到你行动"} ${timerBadge()}</div><h2>${e(r.message_content || "请做出选择")}</h2>${choices}${showSubmit ? `<div class="action-footer">${api === "request_leaf_charas" ? "" : `<button class="btn btn-ghost" data-giveup>放弃</button>`}<button class="btn btn-primary" data-submit ${canSubmit ? "" : "disabled"}>确认选择${state.selected.length ? ` · ${state.selected.join("、")}` : ""}</button></div>` : ""}<details class="manual"><summary>高级：按 CLI 格式输入</summary><div class="manual-row"><input class="input" id="manual-input" placeholder="输入原始指令"/><button class="btn" data-manual>发送</button></div></details></section>`;
}
function game() {
  const players = state.players.length ? state.players : Array.from({length:7},(_,i)=>({id:i+1,name:`${i+1} 号玩家`}));
  return `<main class="shell"><header class="topbar"><div class="brand"><span class="brand-mark">MF</span> 房间 ${e(state.roomCode)}</div><div class="status"><span class="status-dot ${state.connected ? "" : "off"}"></span>${state.playerId} 号 · ${e(state.playerName)}</div></header>
  <div class="game-layout"><aside class="panel phase-panel"><div class="phase-orb ${state.phase === "夜晚" ? "night" : ""}"></div><div><div class="phase-name">${e(state.phase)}</div><div class="phase-sub">${state.round ? `第 ${state.round} 夜 · ` : ""}${state.phase === "夜晚" ? "请保持安静" : "信息公开"}</div></div><div class="identity-card ${state.roleVisible ? "" : "hidden"}"><div class="identity-main"><div><small>你的身份</small><strong>${e(state.role)}</strong></div><div class="game-seat"><small>本局编号</small><b>${state.playerId}</b><span>号</span></div></div><button class="identity-toggle" id="toggle-role">${state.roleVisible ? "隐藏身份" : "显示身份"}</button></div>${state.isHost ? `<div class="host-tools"><button class="btn" data-command="\\restart">重开</button></div>` : ""}${state.gameLog ? `<button class="btn log-download" id="download-log">下载本局日志</button>` : ""}</aside>
  <section class="panel board-panel"><div class="panel-title"><h2>在场玩家</h2><span class="count">点击玩家以选择目标</span></div><div class="board-list">${players.map(playerCard).join("")}</div></section>
  <aside class="right-stack">${actionPanel()}<section class="panel"><div class="panel-title"><h3>事件记录</h3><span class="count">仅你可见的消息已标红</span></div><div class="feed">${state.events.length ? state.events.slice().reverse().map(x=>`<article class="event ${x.private ? "private" : ""}"><div class="event-meta">${e(x.time)} · ${e(x.api.replaceAll("_"," "))}</div><div class="event-text">${e(x.text)}</div></article>`).join("") : '<div class="empty-state">夜幕尚未降临</div>'}</div></section></aside></div></main>`;
}
function render() {
  app.innerHTML = state.view === "landing" ? landing() : state.view === "room" ? room() : game();
  bind();
}
function bind() {
  document.querySelector("#entry-form")?.addEventListener("submit", event => {
    event.preventDefault(); const action = event.submitter?.value; const name = document.querySelector("#name").value.trim(); const roomCode = document.querySelector("#code").value.trim(); state.server = document.querySelector("#server").value.trim();
    if (!name) return notify("先填一个昵称"); if (action === "join" && !/^\d{6}$/.test(roomCode)) return notify("房间号是 6 位数字");
    requestBrowserNotifications();
    connect({ type: action === "create" ? "create_room" : "join_room", playerName: name, roomCode });
  });
  document.querySelector("#copy-room")?.addEventListener("click", async () => { await navigator.clipboard.writeText(`来玩 MF 杀：房间 ${state.roomCode}`); notify("邀请信息已复制"); });
  document.querySelector("#start-game")?.addEventListener("click", () => send({ type: "start_game" }));
  document.querySelector("#toggle-role")?.addEventListener("click", () => { state.roleVisible = !state.roleVisible; render(); });
  document.querySelector("#download-log")?.addEventListener("click", () => { const blob = new Blob([state.gameLog.content], { type: "text/plain;charset=utf-8" }); const url = URL.createObjectURL(blob); const link = document.createElement("a"); link.href = url; link.download = state.gameLog.fileName || "WereMF.log"; link.click(); URL.revokeObjectURL(url); });
  document.querySelectorAll("[data-player]").forEach(el => el.addEventListener("click", () => {
    if (!state.request && !state.activePendingId) return; const id = Number(el.dataset.player);
    if (state.request?.api === "request_vote") state.selected = state.selected.includes(id) ? [] : [id];
    else state.selected = state.selected.includes(id) ? state.selected.filter(x=>x!==id) : [...state.selected,id];
    if (!state.request && state.activePendingId) state.pendingDrafts[state.activePendingId] = [...state.selected];
    syncDraft(formatSelection()); render();
  }));
  document.querySelectorAll("[data-pending]").forEach(el => el.addEventListener("click", () => { state.pendingDrafts[state.activePendingId] = [...state.selected]; syncDraft(); state.activePendingId = el.dataset.pending; state.selected = [...(state.pendingDrafts[state.activePendingId] || [])]; render(); }));
  document.querySelector("[data-clear-draft]")?.addEventListener("click", () => { state.selected = []; state.pendingDrafts[state.activePendingId] = []; syncDraft(""); render(); });
  document.querySelectorAll("[data-value]").forEach(el => el.addEventListener("click", () => submit(el.dataset.value)));
  document.querySelectorAll("[data-role]").forEach(el => el.addEventListener("click", () => {
    const role = el.dataset.role;
    if (state.selected.includes(role)) state.selected = state.selected.filter(x=>x!==role);
    else {
      const limit = state.request?.api === "request_leaf_charas" ? (state.request.data?.choice_count || 4) : Infinity;
      if (state.selected.length >= limit) return notify(`最多选择 ${limit} 个角色`);
      state.selected = [...state.selected,role];
    }
    syncDraft(formatSelection()); render();
  }));
  document.querySelectorAll("[data-modifier]").forEach(el => el.addEventListener("click", () => { state.modifier = el.dataset.modifier; syncDraft(formatSelection()); render(); }));
  document.querySelector("[data-submit]")?.addEventListener("click", () => submit(formatSelection()));
  document.querySelector("[data-giveup]")?.addEventListener("click", () => submit("0"));
  document.querySelector("[data-manual]")?.addEventListener("click", () => submit(document.querySelector("#manual-input").value));
  document.querySelectorAll("[data-command]").forEach(el => el.addEventListener("click", () => send({ type: "command", value: el.dataset.command })));
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
if (saved?.roomCode && saved?.token) { state.server = saved.server || ""; state.reconnecting = true; connect({ type: "reconnect", ...saved }); setTimeout(()=>state.reconnecting=false,1500); }
if (typeof setInterval === "function") setInterval(() => {
  if (!state.timerDeadline) return;
  document.querySelectorAll("[data-timer]").forEach(node => node.textContent = timerText());
}, 250);
render();
