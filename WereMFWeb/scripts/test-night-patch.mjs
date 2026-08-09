import assert from "node:assert/strict";
import test from "node:test";
import { applyEntityStatePatch, applyFullEntitySnapshot, createState } from "../src/store.js";

function gameState() {
  const state = createState({ soundEnabled: true });
  state.entities = [{ player: { id: 3, name: "玩家3" }, role: { chara_type: "myz", data: { secret: true } }, state: { smog_count: 0, is_dead: false, private_marker: "keep" } }];
  return state;
}

test("night patch merges public fields without replacing entities or private data", () => {
  const state = gameState(); const entities = state.entities;
  assert.equal(applyEntityStatePatch(state, { cause: "huika_smog", entities: [{ player_id: 3, state: { smog_count: 1 } }] }), true);
  assert.strictEqual(state.entities, entities);
  assert.equal(entities[0].state.smog_count, 1); assert.equal(entities[0].state.is_dead, false); assert.equal(entities[0].state.private_marker, "keep");
  assert.deepEqual(entities[0].role, { chara_type: "myz", data: { secret: true } });
});

test("night patch ignores unknown players, private fields, invalid types, and invalid causes", () => {
  const state = gameState();
  assert.equal(applyEntityStatePatch(state, { cause: "other", entities: [{ player_id: 3, state: { smog_count: 9 } }] }), false);
  assert.equal(applyEntityStatePatch(state, { cause: "huika_smog", entities: [{ player_id: 99, state: { smog_count: 9 } }, { player_id: 3, state: { role: "叶子", player: { id: 99 }, smog_count: "2", unknown: true } }] }), false);
  assert.deepEqual(state.entities[0].state, { smog_count: 0, is_dead: false, private_marker: "keep" });
  assert.deepEqual(state.entities[0].role, { chara_type: "myz", data: { secret: true } });
});

test("night patch applies multiple entities and only reports actual changes", () => {
  const state = gameState(); state.entities.push({ player: { id: 4 }, state: { is_dead: false, dead_showing_name: "" } });
  const patch = { cause: "huika_smog", entities: [{ player_id: 3, state: { smog_count: 1, is_dead: false } }, { player_id: 4, state: { is_dead: true, dead_showing_name: "玩家4" } }] };
  assert.equal(applyEntityStatePatch(state, patch), true);
  assert.equal(applyEntityStatePatch(state, patch), false);
  assert.equal(state.entities[1].state.is_dead, true); assert.equal(state.entities[1].state.dead_showing_name, "玩家4");
});

test("full snapshots replace entities and rebuild public players", () => {
  const state = gameState(); state.botIds = [8]; const oldEntities = state.entities;
  const next = [{ player: { id: 8, name: "新玩家" }, role: null, state: { smog_count: 2 } }];
  applyFullEntitySnapshot(state, next);
  assert.notStrictEqual(state.entities, oldEntities); assert.strictEqual(state.entities[0], next[0]);
  assert.deepEqual(state.players, [{ id: 8, name: "新玩家", connected: true, isBot: true }]);
});
