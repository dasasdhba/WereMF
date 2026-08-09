import { roles } from "../store.js";

export function selectionRule(context = {}, roleNames = roles) {
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
  return { min: 1, max: pendingMaximum || (roleNames.includes(type) ? 1 : 1) };
}

export function selectionCountValid(selected, context, roleNames = roles) {
  const rule = selectionRule(context, roleNames);
  return selected.length >= rule.min && selected.length <= rule.max;
}

export function requiredModifier(context = {}) {
  const api = context?.api || ""; const type = context?.type || "";
  return api ? ["request_jiaohua_dead_skill", "request_rabi_skill"].includes(api) : type === "兔子";
}

export function pendingModifierOptions(type) {
  return ({ "兔子": [["x", "鲜奶"], ["d", "毒奶"]] })[type] || [];
}

export function copyLeafOptions(request) {
  if (request?.api !== "request_hechong_copy_leaf") return [];
  return [...String(request.message_content || "").matchAll(/(\d+)\s*[：:]\s*([^；;]+)/g)]
    .map(match => ({ value: match[1], label: match[2].trim() }));
}

export function leafOptions(request, roleNames = roles) {
  if (request?.api !== "request_leaf_charas") return [];
  if (Array.isArray(request.data?.options)) return request.data.options;
  return roleNames.filter(value => !["粉侠", "彩怪", "叶子"].includes(value)).map(value => ({ value, camp: "" }));
}

export function leafSelectionValid(request, selected, roleNames = roles) {
  if (request?.api !== "request_leaf_charas") return true;
  const options = leafOptions(request, roleNames); const count = request.data?.choice_count || 4;
  if (selected.length !== count || selected.some(value => !options.some(option => option.value === value))) return false;
  const required = request.data?.required_camps || [];
  return required.every(camp => selected.some(value => options.find(option => option.value === value)?.camp === camp));
}

export function choosePlayerSelection(selected, id, context, { roleNames = roles } = {}) {
  const rule = selectionRule(context, roleNames);
  const myz = context?.api === "request_myz_skill" || (!context?.api && context?.type === "myz");
  if (myz) return selected.length >= rule.max ? [id] : [...selected, id];
  if (selected.includes(id)) return selected.filter(value => value !== id);
  if (rule.max === 1) return [id];
  if (selected.length >= rule.max) return null;
  return [...selected, id];
}

export function toggleRoleSelection(selected, role, context, roleNames = roles) {
  if (selected.includes(role)) return selected.filter(value => value !== role);
  const limit = selectionRule(context, roleNames).max;
  return selected.length >= limit ? null : [...selected, role];
}

export function formatSelection(api, selected, modifier) {
  const ids = selected.join(" ");
  if (api === "request_vote") return modifier === "b" ? "b" : String(selected[0] ?? "");
  return `${ids}${modifier ? ` ${modifier}` : ""}`.trim();
}

export function requestChoiceInvalid(msg, value, index) {
  if (typeof value !== "number") return false;
  const property = msg.api === "request_myz_skill" && index === 1 ? "invalid_target_choice" : "invalid_choice";
  const list = msg.data?.[property] || [];
  return list.some(item => (typeof item === "number" ? item : item.id) === value);
}

export function invalidIdsFor(request, playerId, selected = []) {
  const data = request?.data; let list;
  if (request?.api === "request_vote" && Array.isArray(data)) list = data.find(x => x.id === playerId)?.invalid_vote || [];
  else if (request?.api === "request_myz_skill" && selected.length === 1) list = data?.invalid_target_choice || [];
  else list = data?.invalid_choice || data?.invalid_vote || [];
  return new Set((list || []).map(x => typeof x === "number" ? x : x.id));
}
