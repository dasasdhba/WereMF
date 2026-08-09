export const soundFiles = Object.freeze({
  gameReady: "/sounds/game_ready.ogg", dayReady: "/sounds/day_ready.ogg", nightReady: "/sounds/night_ready.ogg",
  nightSummary: "/sounds/night_summary.ogg", voteSummary: "/sounds/vote_summary.ogg", request: "/sounds/request.wav",
  requestTimeout: "/sounds/request_timeout.wav", barWin: "/sounds/gameover_bar_win.ogg", bombWin: "/sounds/gameover_bomb_win.ogg",
  leafWin: "/sounds/gameover_leaf_win.ogg", allDead: "/sounds/gameover_all_dead.ogg"
});

export function soundForGameMessage(msg) {
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

export function createAudioEffects({ getState, globalObject = globalThis } = {}) {
  const players = new Set();
  function playSound(name) {
    const source = soundFiles[name]; const state = getState();
    if (!source || !state.soundEnabled || state.reconnecting || !("Audio" in globalObject)) return false;
    const audio = new globalObject.Audio(source); audio.preload = "auto"; audio.volume = 0.8;
    players.add(audio); const release = () => players.delete(audio);
    audio.addEventListener?.("ended", release, { once: true }); audio.addEventListener?.("error", release, { once: true });
    audio.play()?.catch?.(release); return true;
  }
  function unlockAudio() {
    const state = getState(); if (!state.soundEnabled || !("Audio" in globalObject)) return;
    const audio = new globalObject.Audio(soundFiles.request); audio.muted = true;
    const release = () => { try { audio.pause(); audio.currentTime = 0; } catch {} };
    audio.play()?.then?.(release).catch?.(release);
  }
  function stopSounds() { for (const audio of players) { try { audio.pause(); } catch {} } players.clear(); }
  return { playSound, unlockAudio, stopSounds, playGameMessageSound: msg => { const sound = soundForGameMessage(msg); if (sound) playSound(sound); } };
}
