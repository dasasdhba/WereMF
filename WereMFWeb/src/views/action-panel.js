export function actionPanel({ state, e, roles, timerBadge, activePending, selectionRule, selectionCountValid, requiredModifier, pendingModifierOptions, copyLeafOptions, leafOptions, leafSelectionValid }) {
  const r = state.request;
  if (!r && state.pendingSkills.length) {
    const active = activePending(); const rule = selectionRule(active); const armed = Boolean(state.preSubmittedDrafts[active.id]);
    const modifierChoices = pendingModifierOptions(active.type);
    const modifierHtml = modifierChoices.length ? `<div class="choice-row" style="margin-top:10px">${modifierChoices.map(x=>`<button class="choice ${state.modifier===x[0]?"selected":""}" data-modifier="${x[0]}">${x[1]}</button>`).join("")}</div>` : "";
    const canPreSubmit = selectionCountValid(active) && (!requiredModifier(active) || state.modifier);
    const countHint = rule.min === rule.max ? `请选择 ${rule.max} 名玩家` : `可选择 ${rule.min}–${rule.max} 名玩家`;
    return `<section class="panel action-panel pending"><div class="action-kicker">提前准备 · ${armed ? "已预提交" : "尚不能正式提交"}</div><div class="choice-row">${state.pendingSkills.map(x=>`<button class="choice ${x.id===active.id?"selected":""}" data-pending="${e(x.id)}">${e(x.type)} · 优先级 ${x.priority}${state.preSubmittedDrafts[x.id] ? " · 已预提交" : ""}</button>`).join("")}</div><h2>预选「${e(active.type)}」技能目标</h2><p class="lobby-note">${countHint}。点击“预提交”后，真正轮到该技能时会按最新局面复核；合法则自动行动，失效则取消并提醒。</p>${modifierHtml}<div class="action-footer"><button class="btn btn-ghost" data-clear-draft>清除预选</button><button class="btn btn-primary" data-pre-submit ${armed || canPreSubmit ? "" : "disabled"}>${armed ? "取消预提交" : `预提交${state.selected.length ? ` · ${state.selected.join("、")}` : ""}`}</button></div></section>`;
  }
  if (!r) return `<section class="panel action-panel inactive"><div class="action-kicker">CURRENT ACTION ${timerBadge()}</div><h2>${state.timerMode === "vote" ? "投票进行中" : "等待其他玩家行动"}</h2><p class="lobby-note">${state.timerMode === "vote" ? "共享投票时间正在倒计时；每次有效投票都会扣减时间。" : "轮到你时，选择面板会自动出现。"}</p></section>`;
  const api = r.api; const boolRequest = /(?:reborn|drink_milk|give_mfa|red_ground|anonymous_game|leaf_game|leaf_chara_reroll|using_copy_skill|for_next_game|reroll_player)$/.test(api);
  const forceChoice = api.includes("force_threaten") && api !== "request_myz_skill_force_threaten"; const roleChoice = api === "request_leaf_charas"; const copyLeafChoice = api === "request_hechong_copy_leaf";
  let choices = "";
  if (boolRequest) choices = `<div class="choice-row"><button class="choice" data-value="1">确认 / 是</button><button class="choice" data-value="0">放弃 / 否</button></div>`;
  else if (copyLeafChoice) { const options = copyLeafOptions(r); choices = `<div class="choice-row">${options.map(option=>`<button class="choice" data-value="${e(option.value)}">${e(option.value)}：${e(option.label)}</button>`).join("")}</div>`; }
  else if (forceChoice) {
    const opts = api === "request_xiansong_skill_force_threaten" ? [["m","强制索要 MFA"],["x","丢咸松球"],["0","放弃"]] : [["1","选项 1"],["0","选项 0"]];
    choices = `<div class="choice-row">${opts.map(x=>`<button class="choice" data-value="${x[0]}">${x[1]}</button>`).join("")}</div>`;
  }
  else if (roleChoice) {
    const options = api === "request_leaf_charas" ? leafOptions(r) : roles.map(value => ({ value, camp: "" }));
    choices = `<div class="choice-row">${options.map(option=>`<button class="choice ${state.selected.includes(option.value)?"selected":""}" data-role="${e(option.value)}">${e(option.value)}${option.camp ? ` · ${e(option.camp)}` : ""}</button>`).join("")}</div>`;
    if (api === "request_leaf_charas") {
      const camps = [...new Set(state.selected.map(value => options.find(option => option.value === value)?.camp).filter(Boolean))];
      choices += `<p class="leaf-hint ${leafSelectionValid(r)?"valid":""}">已选 ${state.selected.length}/${r.data?.choice_count || 4} · ${camps.length ? `阵营：${camps.join("、")}` : "需同时包含吧方与爆方"} · 不可选择粉侠、彩怪、叶子</p>`;
    }
  }
  const modifierSets = {
    request_jiaohua_dead_skill: [["x","封住行动"],["p","保护玩家"]],
    request_rabi_skill: [["x","鲜奶"],["d","毒奶"]],
    request_doge_skill: [["","仅保护"],["b","保护后自爆"]],
    request_caimon_skill: [["","一根彩条"],["d","两根彩条"]],
    request_myz_skill_force_threaten: [["","普通威胁"],["f","自爆并强制"]],
    request_vote: (Array.isArray(r.data) && r.data.find(x=>x.id===state.playerId)?.can_suicide) ? [["","正常投票"],["b","脚滑人自爆"]] : [["","正常投票"]]
  };
  if (modifierSets[api]) choices += `<div class="choice-row" style="margin-top:10px">${modifierSets[api].map(x=>`<button class="choice ${state.modifier===x[0]?"selected":""}" data-modifier="${x[0]}">${x[1]}</button>`).join("")}</div>`;
  const showSubmit = !boolRequest && !forceChoice; const canSubmit = selectionCountValid(r) && (!requiredModifier(r) || state.modifier) && leafSelectionValid(r);
  const threat = r.web_myz_threaten;
  const threatWarning = threat ? `<div class="threat-warning">⚠ ${threat.force ? "你受到 myz 强制威胁，指定目标已被锁定；请重新决定当前仍可选择的附加效果。" : `你已被 myz 威胁${threat.target ? `，要求本技能包含 ${e(threat.target)} 号目标` : ""}。请重新决定；若无视威胁，下一次夜晚开始时你会死亡。`}</div>` : "";
  return `<section class="panel action-panel"><div class="action-kicker">${r.web_concurrent ? `并发输入 · 剩余 ${r.web_remaining} 次` : "轮到你行动"} ${timerBadge()}</div><h2>${e(r.message_content || "请做出选择")}</h2>${threatWarning}${choices}${showSubmit ? `<div class="action-footer">${api === "request_leaf_charas" ? "" : `<button class="btn btn-ghost" data-giveup>放弃</button>`}<button class="btn btn-primary" data-submit ${canSubmit ? "" : "disabled"}>确认选择${state.selected.length ? ` · ${state.selected.join("、")}` : ""}</button></div>` : ""}<details class="manual"><summary>高级：按 CLI 格式输入</summary><div class="manual-row"><input class="input" id="manual-input" placeholder="输入原始指令"/><button class="btn" data-manual>发送</button></div></details></section>`;
}
