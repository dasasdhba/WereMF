export function socketUrl(state, locationObject = globalThis.location) {
  return state.server.trim() || `${locationObject.protocol === "https:" ? "wss:" : "ws:"}//${locationObject.host}/ws`;
}

export function createSocketManager({ state, render, onMessage, notify, WebSocketImpl = globalThis.WebSocket, locationObject = globalThis.location } = {}) {
  function connect(firstMessage) {
    if (state.socket) try { state.socket.close(); } catch {}
    let url = socketUrl(state, locationObject); if (!/^wss?:\/\//.test(url)) url = `ws://${url}`; if (!url.endsWith("/ws")) url = url.replace(/\/$/, "") + "/ws";
    const ws = new WebSocketImpl(url); state.socket = ws; render();
    ws.addEventListener("open", () => { state.connected = true; ws.send(JSON.stringify(firstMessage)); render(); });
    ws.addEventListener("message", event => onMessage(JSON.parse(event.data)));
    ws.addEventListener("close", () => { state.connected = false; render(); if (state.view !== "landing" && !state.reconnecting && !state.leaving) notify("连接已断开，可刷新页面自动重连"); });
    ws.addEventListener("error", () => notify("无法连接游戏服务器"));
    return ws;
  }
  function send(data) {
    if (state.socket?.readyState === WebSocketImpl.OPEN) state.socket.send(JSON.stringify(data)); else notify("尚未连接服务器");
  }
  return { connect, send };
}
