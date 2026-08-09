import assert from "node:assert/strict";
import test from "node:test";
import { normalizeGameMessage } from "../src/protocol.js";
import { createState } from "../src/store.js";
import { choosePlayerSelection, formatSelection, leafSelectionValid, selectionRule, toggleRoleSelection } from "../src/input/selection.js";
import { activePending, prepareRequest, rememberPending, removePending } from "../src/input/pending-drafts.js";

test("protocol normalization keeps payload messages distinct from direct game messages", () => {
  const payload = { api: "game_update_night_patch", data: { cause: "huika_smog", entities: [] } };
  assert.deepEqual(normalizeGameMessage({ type: "game_message", payload }), payload);
  assert.deepEqual(normalizeGameMessage(payload), payload);
  assert.equal(normalizeGameMessage(null), null);
});

test("selection rules cover exact, textual maximum, myz and leaf choices", () => {
  assert.deepEqual(selectionRule({ api: "request_doctor_skill", message_content: "最多 3 个" }), { min: 1, max: 3 });
  assert.deepEqual(selectionRule({ api: "request_myz_skill", data: {} }), { min: 2, max: 2 });
  assert.deepEqual(selectionRule({ api: "request_leaf_charas", data: { choice_count: 4 } }), { min: 4, max: 4 });
  assert.deepEqual(selectionRule({ type: "叶子" }), { min: 1, max: 1 });
  const leaf = { api: "request_leaf_charas", data: { choice_count: 2, options: [{ value: "脚滑人", camp: "吧" }, { value: "炮仙", camp: "爆" }], required_camps: ["吧", "爆"] } };
  assert.equal(leafSelectionValid(leaf, ["脚滑人", "炮仙"]), true);
  assert.equal(leafSelectionValid(leaf, ["脚滑人"]), false);
});

test("selection and formatting preserve myz replacement and vote modifiers", () => {
  assert.deepEqual(choosePlayerSelection([], 2, { api: "request_myz_skill", data: {} }), [2]);
  assert.deepEqual(choosePlayerSelection([2], 3, { api: "request_myz_skill", data: {} }), [2, 3]);
  assert.deepEqual(choosePlayerSelection([2, 3], 4, { api: "request_myz_skill", data: {} }), [4]);
  assert.deepEqual(choosePlayerSelection([2], 3, { api: "request_doctor_skill", data: { choice_max: 1 } }), [3]);
  assert.equal(choosePlayerSelection([2, 3], 4, { api: "request_doctor_skill", data: { choice_max: 2 } }), null);
  assert.equal(formatSelection("request_vote", [3], ""), "3");
  assert.equal(formatSelection("request_vote", [3], "b"), "b");
  assert.equal(formatSelection("request_doctor_skill", [2, 3], "x"), "2 3 x");
  assert.deepEqual(toggleRoleSelection([], "炮仙", { api: "request_leaf_charas", data: { choice_count: 2 } }), ["炮仙"]);
});

test("pending drafts are ordered, selected, invalidated, and removed as a unit", () => {
  const state = createState();
  rememberPending(state, { id: "low", type: "炮仙", priority: 1 });
  rememberPending(state, { id: "high", type: "庸医", priority: 8 });
  assert.equal(activePending(state).id, "low");
  state.pendingDrafts.low = [4, 5]; state.pendingModifiers.low = "x";
  const result = prepareRequest(state, { api: "request_paoxian_skill", data: { skill_id: "low", invalid_choice: [{ id: 4 }] } }, ["炮仙"]);
  assert.equal(result.needsReview, true); assert.deepEqual(state.selected, [5]);
  removePending(state, "low");
  assert.equal(state.pendingSkills.some(item => item.id === "low"), false);
  assert.equal(state.pendingDrafts.low, undefined); assert.equal(state.preSubmittedDrafts.low, undefined);
});
