import { isFullSnapshotApi, isGameRequestApi, isGameUpdateApi } from "../protocol.js";
import { prepareRequest, pendingId } from "../input/pending-drafts.js";
import { requiredModifier, selectionCountValid, formatSelection } from "../input/selection.js";

export function reduceGameMessage(state, msg, deps) {
  if (!msg || typeof msg !== "object") return false;
  deps.playGameMessageSound?.(msg);
  const api = msg.api || ""; const text = String(msg.message_content || "").trim(); const privateMessage = msg.message_type !== "public";
  if (api === "player_init" || api === "player_anonymous_init") { deps.clearNewGamePresentation(); state.players = (msg.data || []).map(player => ({ ...player, connected: true, isBot: state.botIds.includes(player.id) })); }
  if (api === "player_notify_chara" || api === "player_notify_chara_reset") state.role = text || "未知身份";
  if (api === "leaf_notify_first_chara" || api === "leaf_notify_first_chara_reroll") state.role = `叶子 · ${text}`;
  if (api === "night_start_broadcast") { deps.clearTimer(); Object.assign(state, { phase: "夜晚", round: state.round + 1, request: null, selected: [], modifier: "", votes: [], pendingSkills: [], pendingDrafts: {}, pendingModifiers: {}, preSubmittedDrafts: {}, myzThreatenedSkills: {}, activePendingId: "" }); }
  if (api === "day_start_broadcast") { deps.clearTimer(); Object.assign(state, { phase: "白天", request: null, selected: [], modifier: "", pendingSkills: [], pendingDrafts: {}, pendingModifiers: {}, preSubmittedDrafts: {}, myzThreatenedSkills: {}, activePendingId: "" }); }
  if (api === "vote_start_broadcast") { deps.clearTimer(); Object.assign(state, { phase: "投票", request: null, selected: [], modifier: "", votes: [] }); }
  if (api === "vote_end_broadcast" || api === "game_win_broadcast") deps.clearTimer();
  if (api === "game_win_broadcast") state.phase = "终局";
  if (isFullSnapshotApi(api)) deps.applyFullEntitySnapshot(state, msg.data);
  if (api === "game_update_night_patch") deps.applyEntityStatePatch(state, msg.data);
  if (api === "game_update_vote") deps.updateVotes(msg);
  if (api === "pending_skill_created") deps.rememberPending(state, msg.data);
  if (api === "invalid_pending_skill_notify" || api === "skill_blocked_by_jiaohua_notify") deps.removePending(state, pendingId(msg.data));
  if (api === "myz_threaten_notify" || api === "myz_threaten_force_notify") {
    const id = pendingId(msg.data);
    if (id) { delete state.pendingDrafts[id]; delete state.pendingModifiers[id]; state.preSubmittedDrafts[id] = false; state.myzThreatenedSkills[id] = { force: api === "myz_threaten_force_notify", target: msg.data?.skill_id?.threaten?.target }; if (state.activePendingId === id) { state.selected = []; state.modifier = ""; } if (api === "myz_threaten_force_notify") deps.removePending(state, id); }
  }
  if (isGameRequestApi(api)) {
    const result = prepareRequest(state, msg, deps.roles);
    if (result.needsReview) { deps.syncDraft(formatSelection(state.request?.api || "", state.selected, state.modifier), false); if (result.removed.length) { const names = result.removed.map(value => state.players.find(player => player.id === value)?.name || `${value} 号`).join("、"); deps.notify(`局面已变化，已取消无效选择：${names}`); } }
    deps.alertRequest(msg);
  }
  if (api.endsWith("_parse_error")) deps.notify(text || "这个选择无效，请重试");
  if (deps.recordEvents !== false && text && !isGameRequestApi(api) && !isGameUpdateApi(api)) deps.addEvent(api, text, privateMessage);
  return true;
}
