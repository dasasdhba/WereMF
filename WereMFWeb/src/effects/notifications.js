export function createNotificationEffects({ getState, documentObject = globalThis.document, globalObject = globalThis, toastElement, defaultTitle = "MF 杀 · 今夜谁在说谎" } = {}) {
  let titleFlashTimer = null;
  function notify(message) {
    if (toastElement) { toastElement.textContent = message; toastElement.classList.add("show"); }
    clearTimeout(notify.timer); notify.timer = setTimeout(() => toastElement?.classList.remove("show"), 2600);
  }
  function stopTitleFlash() {
    if (titleFlashTimer) clearInterval(titleFlashTimer);
    titleFlashTimer = null; if (documentObject) documentObject.title = defaultTitle;
  }
  function flashTitle(message) {
    if (getState().reconnecting) return;
    stopTitleFlash(); let tick = 0;
    const update = () => { if (documentObject) documentObject.title = tick++ % 2 ? defaultTitle : `● ${message} · MF 杀`; if (tick >= 12) stopTitleFlash(); };
    update(); if (typeof globalObject.setInterval === "function") titleFlashTimer = globalObject.setInterval(update, 650);
  }
  function requestBrowserNotifications() {
    if (!("Notification" in globalObject) || globalObject.Notification.permission !== "default") return;
    globalObject.Notification.requestPermission().catch(() => {});
  }
  function alertRequest(request) {
    flashTitle("轮到你行动");
    if (documentObject?.visibilityState === "visible" && !getState().reconnecting)
      notify(`轮到你行动：${String(request.message_content || "请做出选择").replace(/\s+/g, " ").slice(0, 48)}`);
    if (!("Notification" in globalObject) || globalObject.Notification.permission !== "granted" || getState().reconnecting) return;
    const notification = new globalObject.Notification("MF 杀 · 轮到你行动", { body: String(request.message_content || "请返回游戏做出选择"), icon: "/og.png", tag: `weremf-${getState().roomCode}-${request.api || "request"}`, renotify: true });
    notification.onclick = () => globalObject.focus?.();
  }
  return { notify, stopTitleFlash, flashTitle, requestBrowserNotifications, alertRequest };
}
