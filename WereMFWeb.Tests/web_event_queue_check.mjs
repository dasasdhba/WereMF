import { loadWebApp } from "./web-app-fixture.mjs";

const { ui, appEl, scheduled, playedSounds, source } = await loadWebApp();
const assert = (condition, message) => { if (!condition) throw new Error(message); };
const gameMessage = (api, message_content, data) => ui.onMessage({
  type: "game_message",
  payload: { api, message_type: "public", message_content, ...(data === undefined ? {} : { data }) }
});
const runInterval = () => {
  const index = scheduled.findIndex(item => item.delay === 2000);
  assert(index >= 0, "missing 2-second transition timer");
  const [{ fn }] = scheduled.splice(index, 1);
  fn();
};
const eventTexts = () => ui.state.events.map(event => event.text);

assert(ui.defaultRoomSettings.requestTimeoutSeconds === 30, "normal request timeout should default to 30 seconds");
assert(ui.isReservedNickname("叶子") && ui.isReservedNickname("ctf") && ui.isReservedNickname("DOGE"), "role names must be reserved case-insensitively");
assert(!ui.isReservedNickname("普通玩家"), "ordinary nicknames must remain available");
assert(ui.soundForGameMessage({ api: "night_summary_broadcast" }) === "nightSummary", "night summary cue must map to 今晚");
assert(ui.soundForGameMessage({ api: "vote_end_broadcast" }) === "voteSummary", "vote summary cue must map to 投票结束");
assert(ui.soundForGameMessage({ api: "game_win_broadcast", message_content: "游戏结束，吧方获胜" }) === "barWin", "bar win must use its dedicated cue");
assert(ui.soundForGameMessage({ api: "game_win_broadcast", message_content: "游戏结束，爆方获胜" }) === "bombWin", "bomb win must use its dedicated cue");
assert(ui.soundForGameMessage({ api: "game_win_broadcast", message_content: "游戏结束，叶子获胜" }) === "leafWin", "Leaf win must use its dedicated cue");
assert(ui.soundForGameMessage({ api: "game_win_broadcast", message_content: "游戏结束，无人生还" }) === "allDead", "all-dead ending must use its dedicated cue");

// room_state reaches browsers before CLI player_init, so the entire personal card must reset immediately.
ui.state.started = false; ui.state.role = "上一局身份"; ui.state.roleVisible = false;
ui.state.entities = [{ player: { id: 1 }, role: { data: { fury: true } }, state: { reversed: true, is_bar_leader: true } }];
ui.state.gameLog = { fileName: "old.log", content: "old" };
ui.onMessage({ type: "room_state", started: true, bots: [], players: [{ id: 1, name: "甲" }] });
assert(ui.state.role === "身份尚未揭晓" && ui.state.roleVisible && ui.state.entities.length === 0 && ui.state.gameLog === null, "lobby-to-game transition must reset the entire previous personal card");
assert(playedSounds.at(-1) === ui.soundFiles.gameReady, "a fresh game start must play game_ready once");
playedSounds.length = 0; ui.state.started = false; ui.state.reconnecting = true;
ui.onMessage({ type: "room_state", started: true, bots: [], players: [{ id: 1, name: "甲" }] });
assert(playedSounds.length === 0 && !ui.state.reconnecting, "reconnect history must stay silent until room state completes replay");

// A new player list is a hard game boundary; old entity/role card state must disappear immediately.
ui.state.entities = [{ player: { id: 1 }, role: { data: { fury: true } }, state: { reversed: true, is_bar_leader: true } }];
ui.state.votes = [{ id: 1, target: 2 }]; ui.state.role = "上一局身份"; ui.state.phase = "终局"; ui.state.round = 4;
gameMessage("player_init", "新一局玩家", [{ id: 1, name: "甲" }, { id: 2, name: "乙" }]);
assert(ui.state.entities.length === 0 && ui.state.votes.length === 0, "new game must clear previous entity and vote card state");
assert(ui.state.role === "身份尚未揭晓" && ui.state.phase === "准备" && ui.state.round === 0, "new game must reset identity and phase before fresh updates");

// Selection limits come from the request, while each pending Leaf skill remains a separate single-target action.
assert(ui.selectionRule({ api: "request_paoxian_skill", message_content: "输入一名玩家" }).max === 1, "single-target skills must not allow multi-select");
assert(ui.selectionRule({ api: "request_doctor_skill", message_content: "输入要扎针的玩家编号（最多 3 个）" }).max === 3, "multi-target maximum should come from the request text");
assert(ui.selectionRule({ api: "request_myz_skill", data: {} }).min === 2 && ui.selectionRule({ api: "request_myz_skill", data: {} }).max === 2, "myz must select exactly two players");
assert(ui.selectionRule({ api: "request_leaf_charas", data: { choice_count: 4 } }).max === 4, "initial Leaf identity selection should still select four roles");
assert(ui.selectionRule({ type: "叶子" }).max === 1, "a pending Leaf skill must not be treated as the initial four-role selection");
ui.state.request = { api: "request_hechong_copy_leaf", message_content: "选择一个身份复制：1：爬行者；2：CTF；3：贤松", data: null };
const copyLeafPanel = ui.actionPanel();
assert(copyLeafPanel.includes('data-value="1"') && copyLeafPanel.includes("1：爬行者") && copyLeafPanel.includes('data-value="3"') && copyLeafPanel.includes("3：贤松"), "copy-Leaf request must render the numbered CLI options");
assert(!copyLeafPanel.includes("data-role"), "copy-Leaf request must submit an option number rather than a role name");
ui.state.request = { api: "request_xiansong_skill", message_content: "输入一名其他玩家的编号索要 mfa 文件，输入 0 放弃；在结尾输入 m 或者 x 表示强制要 mfa 或丢咸松球", data: { pending_role: { can_force_choice: true } } };
const xianSongPanel = ui.actionPanel();
assert(xianSongPanel.includes('data-modifier="m"') && xianSongPanel.includes("强制索要 MFA") && xianSongPanel.includes('data-modifier="x"') && xianSongPanel.includes("强制丢咸松球"), "reborn XianSong request must render force-choice buttons");
ui.state.request = { api: "request_xiansong_skill", message_content: "输入一名其他玩家的编号索要 mfa 文件，输入 0 放弃", data: { pending_role: { can_force_choice: false } } };
assert(!ui.actionPanel().includes("强制丢咸松球"), "normal XianSong request must not render force-choice buttons");
ui.state.players = [{ id: 2, name: "乙" }, { id: 3, name: "丙" }];
const nestedRoleState = ui.roleStateItems({
  fury: true,
  roles: [
    { chara_type: "爬行者", data: { bomb_count: 2, placed_list: [2] } },
    { chara_type: "Doge", data: { last_selected: { tonight: [3], last_night: [2] }, self_selected: false } }
  ]
});
const nestedRoleText = nestedRoleState.map(x => x.text).join("|");
assert(nestedRoleText.includes("叶子二阶段") && nestedRoleText.includes("叶子：爬行者") && nestedRoleText.includes("炸弹：2"), "nested Leaf role state should be visualized");
assert(nestedRoleText.includes("2号·乙") && nestedRoleText.includes("3号·丙"), "role player-id lists should resolve visible player names");
ui.state.entities = [{ player: { id: 2, name: "乙" }, role: { chara_type: "地鼠", summary_name: "地鼠", data: { red_ground: true, ground_pool: [0, 0, 1, 1, 1, 2] } }, state: { is_bar_leader: true, reversed: true } }];
ui.state.playerId = 2;
const stateCard = ui.playerCard(ui.state.players[0]);
assert(stateCard.includes("反·乙") && !stateCard.includes("吧主"), "reversal should prefix the board name while private bar leader state stays off the board");
const personalPanel = ui.game();
assert(personalPanel.includes("你是吧主"), "bar leader state should appear only in the left personal identity card");
assert(personalPanel.includes("红地状态") && personalPanel.includes("土地池"), "visible Mole role state should render in the left personal identity card");
const moleStateText = ui.roleStateItems({ ground_pool: [0, 0, 1, 1, 1, 2] }).map(item => item.text).join("|");
assert(moleStateText.includes("花岗岩×2") && moleStateText.includes("土地×3") && moleStateText.includes("红土地×1"), "Mole ground_pool values must render as terrain counts");
assert(!moleStateText.includes("号·") && !moleStateText.includes("0号") && !moleStateText.includes("1号") && !moleStateText.includes("2号"), "Mole terrain values must never be interpreted as player ids");
ui.state.started = true; ui.state.phase = "白天";
ui.state.entities = [{ player: { id: 2, name: "乙" }, role: null, state: { is_dead: false, myz_threaten: true } }];
assert(ui.chatAvailability().allowed, "living players threatened by myz must still be allowed to chat");
ui.state.entities[0].state.is_dead = true;
assert(!ui.chatAvailability().allowed, "dead players must not be allowed to chat");
ui.state.entities[0].state.is_dead = false;
ui.appendChat({ playerId: 2, text: "<b>白天发言</b>", sentAt: Date.now() });
const chatPanel = ui.game();
assert(chatPanel.includes("聊天与事件") && chatPanel.includes("&lt;b&gt;白天发言&lt;/b&gt;"), "chat messages must render in the unified timeline with HTML escaped");
ui.state.chatDraft = "正在输入 <计划> & 细节";
gameMessage("mole_skill_success_notify", "新事件到达");
assert(ui.state.chatDraft === "正在输入 <计划> & 细节", "incoming game events must preserve the unsent chat draft");
const draftPanel = ui.game();
assert(draftPanel.includes('value="正在输入 &lt;计划&gt; &amp; 细节"'), "a rerendered chat input must restore and HTML-escape its draft value");
const beforeCompositionHtml = appEl.innerHTML;
ui.beginChatComposition();
gameMessage("doctor_skill_success_notify", "输入法组合期间到达的新消息");
assert(appEl.innerHTML === beforeCompositionHtml, "incoming messages must not replace the DOM during IME composition");
assert(ui.state.events.at(-1)?.text === "输入法组合期间到达的新消息", "messages received during composition must still enter state immediately");
ui.endChatComposition({ value: "尚未上屏的候选文字" });
const deferredCompositionRender = scheduled.findIndex(item => item.delay === 0);
assert(deferredCompositionRender >= 0, "composition end must schedule the deferred render");
scheduled.splice(deferredCompositionRender, 1)[0].fn();
assert(ui.state.chatDraft === "尚未上屏的候选文字", "composition end must preserve the committed IME text");
assert(appEl.innerHTML.includes("输入法组合期间到达的新消息") && appEl.innerHTML.includes('value="尚未上屏的候选文字"'), "deferred render must show both the message and committed draft");
ui.onMessage({ type: "cli_input_recorded", api: "request_paoxian_skill", value: "2 <目标>", sentAt: Date.now() });
const cliInputEvent = ui.state.events.at(-1);
assert(cliInputEvent.cliInput && cliInputEvent.private && cliInputEvent.text === "2 <目标>", "recorded CLI input must be a private local event");
const cliInputPanel = ui.game();
assert(cliInputPanel.includes('class="event cli-input"') && cliInputPanel.includes("CLI 输入 · 仅你可见"), "recorded CLI input must use the dedicated event presentation");
assert(cliInputPanel.includes("&gt; 2 &lt;目标&gt;"), "recorded CLI input must be HTML-escaped");
ui.state.entities = [];
ui.state.pendingSkills = [];
ui.state.activePendingId = "";
ui.state.request = { api: "request_myz_skill", data: {} };
ui.state.selected = [];
ui.choosePlayer(2); ui.choosePlayer(2);
assert(ui.state.selected.join(",") === "2,2", "myz must allow A to send the threatened skill to A itself");
const myzAsymmetric = {
  api: "request_myz_skill",
  data: {
    invalid_choice: [{ id: 1, reason: "你不能威胁自己" }, { id: 3, reason: "被绑架" }],
    invalid_target_choice: [{ id: 4, reason: "目标不存在" }]
  }
};
ui.state.selected = [];
assert(ui.invalidIdsFor(myzAsymmetric).has(1) && ui.invalidIdsFor(myzAsymmetric).has(3) && !ui.invalidIdsFor(myzAsymmetric).has(4), "myz first input must use invalid_choice");
ui.state.selected = [2];
assert(ui.invalidIdsFor(myzAsymmetric).has(4) && !ui.invalidIdsFor(myzAsymmetric).has(1) && !ui.invalidIdsFor(myzAsymmetric).has(3), "myz second input must use invalid_target_choice");
ui.state.selected = [2, 2];
assert(ui.invalidIdsFor(myzAsymmetric).has(1) && ui.invalidIdsFor(myzAsymmetric).has(3) && !ui.invalidIdsFor(myzAsymmetric).has(4), "after two myz inputs, replacement must restart with invalid_choice");
ui.state.request = { api: "request_paoxian_skill", data: {} };
ui.state.selected = [];
ui.choosePlayer(2); ui.choosePlayer(3);
assert(ui.state.selected.join(",") === "3", "other single-target skills must still replace the previous choice");
ui.state.request = null;
ui.state.pendingSkills = [{ id: "doctor-pending", type: "庸医", priority: 1 }];
ui.state.activePendingId = "doctor-pending";
ui.state.selected = [1, 2];
assert(ui.actionPanel().includes("data-pre-submit"), "pending panel should expose an explicit pre-submit button");
assert(!ui.actionPanel().match(/data-pre-submit disabled/), "a valid pending selection should enable pre-submit");
ui.state.pendingSkills = [];
ui.state.activePendingId = "";
ui.state.selected = [];
// JiaoHua creates every pending skill first, then reports the randomly blocked skill by the same id.
gameMessage("pending_skill_created", "", { id: "jiaohua-blocked", type: "炮仙", source_player_id: 2, priority: 0 });
ui.state.pendingDrafts["jiaohua-blocked"] = [3];
ui.state.preSubmittedDrafts["jiaohua-blocked"] = true;
assert(ui.state.pendingSkills.some(skill => skill.id === "jiaohua-blocked"), "JiaoHua regression fixture must begin as a visible pending skill");
gameMessage("skill_blocked_by_jiaohua_notify", "你的炮仙技能被脚滑人禁用", { id: "jiaohua-blocked", type: "炮仙", source_player_id: 2, priority: 0 });
assert(!ui.state.pendingSkills.some(skill => skill.id === "jiaohua-blocked"), "a skill blocked by dead JiaoHua must leave pre-selection immediately");
assert(!("jiaohua-blocked" in ui.state.pendingDrafts) && !("jiaohua-blocked" in ui.state.preSubmittedDrafts), "blocking must also remove the stale draft and pre-submit state");
// Anonymous remapping must use the newly mapped bot ids from room_state.
ui.onMessage({ type: "room_state", started: true, bots: [2, 4], players: [] });
gameMessage("player_anonymous_init", "", [
  { id: 1, name: "玩家1" },
  { id: 2, name: "玩家2" },
  { id: 3, name: "玩家3" },
  { id: 4, name: "玩家4" }
]);
assert(ui.state.players.filter(player => player.isBot).map(player => player.id).join(",") === "2,4", "anonymous bot badges must follow remapped game ids");
// The browser is now a simple ordered renderer. The server owns cinematic pacing and only
// releases each following envelope once its preceding CLI message has had time to play.
ui.state.events.length = 0; playedSounds.length = 0; ui.state.phase = "夜晚"; ui.state.request = null;
gameMessage("night_summary_broadcast", "今晚");
gameMessage("night_summary_queued_broadcast", "甲身上多了一只虫子");
gameMessage("player_dead_broadcast", "乙死了");
gameMessage("day_start_broadcast", "白天开始");
gameMessage("game_update_day", "", [{ player: { id: 1, name: "甲" }, state: { potion_count: 1 } }]);
gameMessage("vote_start_broadcast", "投票开始");
gameMessage("request_vote", "请选择你的投票", [{ id: 1, can_vote: true, invalid_vote: [] }]);
ui.onMessage({ type: "request_timer", api: "request_vote", deadlineUtc: 654321, mode: "vote" });
assert(eventTexts().join("|") === "今晚|甲身上多了一只虫子|乙死了|白天开始|投票开始", `frontend order: ${eventTexts().join("|")}`);
assert(ui.state.phase === "投票" && ui.state.entities[0]?.state?.potion_count === 1, "stage state must be applied before the following request");
assert(ui.state.request?.api === "request_vote" && ui.state.timerApi === "request_vote", "request and timer must render immediately when the server releases them");
assert(playedSounds.includes(ui.soundFiles.nightSummary) && playedSounds.includes(ui.soundFiles.dayReady), "sound cues must follow the released CLI messages");

ui.state.events.length = 0; ui.state.request = null; ui.state.timerDeadline = 0; ui.state.timerApi = "";
gameMessage("vote_end_broadcast", "投票结束");
gameMessage("vote_result_broadcast", "投票结果：甲出局");
gameMessage("player_dead_broadcast", "甲出局");
gameMessage("night_start_broadcast", "晚上开始");
gameMessage("game_update_night", "", [{ player: { id: 2, name: "乙" }, state: { bug_count: 1 } }]);
gameMessage("request_paoxian_skill", "请选择目标", { invalid_choice: [] });
assert(eventTexts().join("|") === "投票结束|投票结果：甲出局|甲出局|晚上开始", "vote-to-night envelopes must retain CLI order");
assert(ui.state.phase === "夜晚" && ui.state.entities[0]?.player?.id === 2, "night state must precede the night request");
assert(ui.state.request?.api === "request_paoxian_skill", "night request must render after fresh night state");

ui.state.events.length = 0; ui.state.gameLog = null;
gameMessage("night_summary_broadcast", "今晚");
gameMessage("night_summary_queued_broadcast", "乙身上多了一只虫子");
gameMessage("game_win_broadcast", "游戏结束，吧方获胜");
gameMessage("game_role_list_broadcast", "甲：实物；乙：地鼠");
ui.onMessage({ type: "game_log_available", fileName: "terminal.log", content: "log" });
ui.onMessage({ type: "game_ended", message: "对局已结束" });
assert(ui.state.phase === "终局" && ui.state.gameLog?.fileName === "terminal.log", "terminal envelopes must also apply directly in server order");
assert(eventTexts().join("|") === "今晚|乙身上多了一只虫子|游戏结束，吧方获胜|甲：实物；乙：地鼠|本局日志已生成，所有玩家均可下载|对局已结束", "terminal rendering must preserve server order");
// Messages outside the two explicit ranges remain immediate.
ui.state.events.length = 0;
gameMessage("paoxian_kill_broadcast", "炮仙击杀了乙");
assert(eventTexts().join("|") === "炮仙击杀了乙", "unrelated broadcasts should render immediately");
assert(source.includes('new Blob(["\\uFEFF", state.gameLog.content]'), "downloaded log should start with a UTF-8 BOM");
console.log(JSON.stringify({
  ok: true,
  pacingOwner: "server",
  nightRange: ["今晚", "甲身上多了一只虫子", "乙死了", "白天开始"],
  voteRange: ["投票结束", "投票结果：甲出局", "甲出局", "晚上开始"],
  outsideRangeImmediate: true,
  utf8Bom: true
}, null, 2));
