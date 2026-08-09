import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import vm from "node:vm";

async function loadApp() {
  const source = await readFile(new URL("../src/app.js", import.meta.url), "utf8");
  const appElement = { innerHTML: "" };
  const context = {
    console,
    URL,
    URLSearchParams,
    WebSocket: class {},
    document: { querySelector: selector => selector === "#app" ? appElement : null, querySelectorAll: () => [], addEventListener: () => {}, activeElement: null },
    localStorage: { getItem: () => null, setItem: () => {}, removeItem: () => {} },
    location: { search: "", protocol: "http:", host: "localhost" },
    setTimeout,
    clearTimeout
  };
  context.globalThis = context;
  context.addEventListener = () => {};
  vm.createContext(context);
  vm.runInContext(`${source}\nglobalThis.__nightPatchTest = { state, applyEntityStatePatch };`, context);
  return context.__nightPatchTest;
}

test("night patch merges public fields without replacing entities or private data", async () => {
  const { state, applyEntityStatePatch } = await loadApp();
  const entities = [{
    player: { id: 3, name: "玩家3" },
    role: { chara_type: "myz", data: { secret: true } },
    state: { smog_count: 0, is_dead: false, private_marker: "keep" }
  }];
  state.entities = entities;

  assert.equal(applyEntityStatePatch({
    cause: "huika_smog",
    entities: [{ player_id: 3, state: { smog_count: 1 } }]
  }), true);
  assert.strictEqual(state.entities, entities);
  assert.equal(entities[0].state.smog_count, 1);
  assert.equal(entities[0].state.is_dead, false);
  assert.equal(entities[0].state.private_marker, "keep");
  assert.deepEqual(entities[0].role, { chara_type: "myz", data: { secret: true } });
});

test("night patch ignores unknown players and invalid or private fields", async () => {
  const { state, applyEntityStatePatch } = await loadApp();
  const entity = { player: { id: 3 }, role: { chara_type: "myz" }, state: { smog_count: 1 } };
  state.entities = [entity];

  assert.equal(applyEntityStatePatch({
    cause: "huika_smog",
    entities: [
      { player_id: 99, state: { smog_count: 9 } },
      { player_id: 3, state: { role: "叶子", player: { id: 99 }, smog_count: "2", unknown: true } }
    ]
  }), false);
  assert.deepEqual(entity.state, { smog_count: 1 });
  assert.deepEqual(entity.role, { chara_type: "myz" });
});
