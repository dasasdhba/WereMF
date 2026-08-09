import { selectionCountValid, requiredModifier, requestChoiceInvalid, selectionRule } from "./selection.js";

export function pendingId(data) {
  const raw = data?.skill_id ?? data?.id ?? data;
  return typeof raw === "object" ? raw?.id || "" : String(raw || "");
}

export function activePending(state) {
  return state.pendingSkills.find(item => item.id === state.activePendingId) || state.pendingSkills[0] || null;
}

export function rememberPending(state, data) {
  const id = pendingId(data);
  if (!id || state.pendingSkills.some(item => item.id === id)) return false;
  state.pendingSkills.push({ ...data, id });
  state.pendingSkills.sort((a, b) => b.priority - a.priority);
  if (!state.activePendingId) {
    state.activePendingId = id;
    state.selected = [...(state.pendingDrafts[id] || [])];
    state.modifier = state.pendingModifiers[id] || "";
  }
  return true;
}

export function removePending(state, id) {
  if (!id) return false;
  state.pendingSkills = state.pendingSkills.filter(item => item.id !== id);
  delete state.pendingDrafts[id]; delete state.pendingModifiers[id]; delete state.preSubmittedDrafts[id];
  if (state.activePendingId === id) {
    state.activePendingId = state.pendingSkills[0]?.id || "";
    state.selected = [...(state.pendingDrafts[state.activePendingId] || [])];
    state.modifier = state.pendingModifiers[state.activePendingId] || "";
  }
  return true;
}

export function prepareRequest(state, msg, roleNames) {
  const id = pendingId(msg.data); const draft = id ? [...(state.pendingDrafts[id] || [])] : [];
  if (id && state.myzThreatenedSkills[id]) msg.web_myz_threaten = state.myzThreatenedSkills[id];
  state.request = msg; state.modifier = id ? state.pendingModifiers[id] || "" : state.modifier;
  const invalid = draft.filter((value, index) => requestChoiceInvalid(msg, value, index));
  const valid = draft.filter((value, index) => !requestChoiceInvalid(msg, value, index));
  const rule = selectionRule(msg, roleNames); const removed = [...invalid, ...valid.slice(rule.max)];
  state.selected = valid.slice(0, rule.max); state.activePendingId = id || state.activePendingId;
  const needsReview = Boolean(removed.length || !selectionCountValid(state.selected, msg, roleNames) || (requiredModifier(msg) && !state.modifier));
  if (id && needsReview) state.preSubmittedDrafts[id] = false;
  return { id, removed, needsReview };
}
