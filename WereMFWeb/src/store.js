export const roles = Object.freeze(["脚滑人","Doge","庸医","地鼠","兔子","铯郎","法猫","卡比","粉侠","爬行者","炮仙","实物","灰卡比","音魔","CTF","合虫","彩怪","贤松","江仙","myz","叶子"]);

export const defaultRoomSettings = Object.freeze({ requestTimeoutSeconds: 30, voteSecondsPerAlive: 60, votePenaltySeconds: 30, eventIntervalSeconds: 2 });

export const nightPatchStateKinds = Object.freeze({
  is_bar_leader: "boolean", is_dead: "boolean", is_dead_public: "boolean", dead_showing_name: "string",
  reversed: "boolean", smog_count: "number", capsule_count: "number", potion_count: "number",
  xian_song_count: "number", bug_count: "number", myz_threaten: "boolean", jiaohua_vote_blocked: "boolean",
  shiwu_kidnapped: "boolean", jiaohua_protected: "boolean", jiaohua_blocked: "number", leaf_protected: "boolean"
});

export function createState({ server = "", soundEnabled = true } = {}) {
  return {
    view: "landing", socket: null, connected: false, server,
    roomCode: "", playerId: null, playerName: "", token: "", isHost: false, started: false,
    players: [], entities: [], votes: [], events: [], phase: "等待", round: 0, role: "身份尚未揭晓",
    roleVisible: true, request: null, selected: [], modifier: "", reconnecting: false,
    pendingSkills: [], pendingDrafts: {}, pendingModifiers: {}, preSubmittedDrafts: {}, myzThreatenedSkills: {}, activePendingId: "", gameLog: null,
    timerDeadline: 0, timerApi: "", timerMode: "", feedPinned: true, feedScrollTop: 0, chatDraft: "", botIds: [], leaving: false,
    roomSettings: { ...defaultRoomSettings }, soundEnabled
  };
}

export function entityId(entity) {
  return entity?.player?.id ?? entity?.player?.Id;
}

export function entityFor(state, id) {
  return state.entities.find(entity => entityId(entity) === id);
}

export function applyFullEntitySnapshot(state, data) {
  state.entities = Array.isArray(data) ? data : data?.entities || [];
  state.players = state.entities.map(entity => ({ ...entity.player, connected: true, isBot: state.botIds.includes(entityId(entity)) }));
  return state.entities;
}

export function applyEntityStatePatch(state, data, debug = globalThis.console?.debug?.bind(globalThis.console)) {
  if (data?.cause !== "huika_smog" || !Array.isArray(data.entities)) {
    debug?.("忽略非法灰卡比夜间增量", data);
    return false;
  }
  let changed = false;
  for (const item of data.entities) {
    const id = Number(item?.player_id);
    const entity = Number.isInteger(id) ? state.entities.find(candidate => Number(entityId(candidate)) === id) : null;
    if (!entity || !item?.state || typeof item.state !== "object" || Array.isArray(item.state)) {
      debug?.("忽略未知玩家或非法灰卡比实体增量", item);
      continue;
    }
    for (const [field, value] of Object.entries(item.state)) {
      const expected = nightPatchStateKinds[field];
      if (!expected || typeof value !== expected || (expected === "number" && !Number.isFinite(value))) {
        debug?.("忽略非法灰卡比状态字段", field, value);
        continue;
      }
      if (!entity.state || typeof entity.state !== "object" || Array.isArray(entity.state)) entity.state = {};
      if (entity.state[field] !== value) { entity.state[field] = value; changed = true; }
    }
  }
  return changed;
}
