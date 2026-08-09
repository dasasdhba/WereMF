export function game({ state, e, soundToggleButton, entityFor, chatAvailability, actionPanel, playerCard, roleStateItems }) {
  const players = state.players.length ? state.players : Array.from({length:7},(_,i)=>({id:i+1,name:`${i+1} 号玩家`}));
  const chat = chatAvailability();
  const timelineHtml = state.events.length ? state.events.map(x => {
    if (x.cliInput) {
      const api = String(x.api || "CLI").replaceAll("_", " ");
      return `<article class="event cli-input"><div class="event-meta">${e(x.time)} · CLI 输入 · 仅你可见 · ${e(api)}</div><div class="event-text">&gt; ${e(x.text)}</div></article>`;
    }
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
