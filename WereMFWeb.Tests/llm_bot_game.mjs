import { spawn } from "node:child_process";
import { createServer } from "node:http";
import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";

const root = resolve(import.meta.dirname, "..");
const port = 5206;
const fakeLlmPort = 5207;
const envText = await readFile(resolve(root, ".env"), "utf8");
const env = { ...process.env, HTTP_PROXY: "", HTTPS_PROXY: "", ALL_PROXY: "", NO_PROXY: "*" };
for (const line of envText.split(/\r?\n/)) {
  const match = line.match(/^\s*([^#=\s]+)\s*=\s*(.*?)\s*$/);
  if (match) env[match[1]] = match[2].replace(/^['"]|['"]$/g, "");
}
env.SILICONFLOW_API_KEY = "local-test-key";
env.SILICONFLOW_BASE_URL = `http://127.0.0.1:${fakeLlmPort}/v1/`;
env.SILICONFLOW_TIMEOUT_SECONDS = "1";
env.SILICONFLOW_BOT_THINK_SECONDS = "3";
env.LLM_FALLBACK_BASE_URL = "";
let fakeRequestCount = 0;
let fakeSpeechRequestCount = 0;
const capturedPrompts = [];
const fakeLlm = createServer((request, response) => {
  let body = "";
  request.on("data", chunk => body += chunk);
  request.on("end", () => {
    const parsed = JSON.parse(body || "{}");
    if (request.url !== "/v1/chat/completions" || parsed.model !== "Qwen/Qwen3.5-4B") {
      response.writeHead(400).end(); return;
    }
    const requestNumber = ++fakeRequestCount;
    capturedPrompts.push(parsed.messages || []);
    const systemPrompt = String((parsed.messages || []).find(message => message.role === "system")?.content || "");
    if (systemPrompt.includes('"text"')) {
      fakeSpeechRequestCount++;
      if (fakeSpeechRequestCount === 3 || fakeSpeechRequestCount === 4) { response.writeHead(503).end(); return; }
    }
    const content = systemPrompt.includes('"summary"')
      ? '{"summary":"保留身份、公开死亡、投票和关键怀疑的测试摘要"}'
      : systemPrompt.includes('"text"')
        ? (() => {
            const userPrompt = String((parsed.messages || []).find(message => message.role === "user")?.content || "");
            const legalVotes = userPrompt.match(/合法 vote：([^。\n]+)/)?.[1].split("、") || [];
            const chosenVote = legalVotes.find(value => value !== "0" && value !== "b") || "0";
            const vote = userPrompt.includes("合法 vote：") ? JSON.stringify(chosenVote) : "null";
            const text = fakeSpeechRequestCount === 1 ? "2号昨晚的状态变化值得关注。" : "";
            const speechIntent = text ? "new_deduction" : "silent";
            return `{"speech_intent":"${speechIntent}","text":"${text}","vote":${vote}}`;
          })()
        : (requestNumber % 5 === 0 ? '{"input":"definitely-invalid"}' : '{"input":"0"}');
    const complete = () => {
      if (response.destroyed) return;
      response.setHeader("content-type", "application/json");
      response.end(JSON.stringify({ model: parsed.model, choices: [{ finish_reason: "stop", message: { role: "assistant", content } }] }));
    };
    complete();
  });
});
await new Promise(resolveListen => fakeLlm.listen(fakeLlmPort, "127.0.0.1", resolveListen));
const server = spawn(resolve(root, "WereMFServer/bin/Release/net8.0/WereMFServer.exe"), [
  "--path", resolve(root, "WereMF/bin/Release/net8.0/win-x64/publish/WereMF.exe"),
  "--config", resolve(root, "WereMF/config.json"),
  "--host", "127.0.0.1", "--port", String(port), "--event-interval-seconds", "0", "--request-timeout-seconds", "2", "--vote-seconds-per-alive", "2", "--vote-penalty-seconds", "1", "--debug-api"
], { windowsHide: true, env });
let serverOutput = "";
server.stdout.on("data", value => serverOutput += value);
server.stderr.on("data", value => serverOutput += value);
const messages = [];
let ws;
const waitFor = (predicate, timeout = 10_000) => new Promise((resolveWait, reject) => {
  const start = Date.now();
  const timer = setInterval(() => {
    const result = messages.findLast(predicate);
    if (result) { clearInterval(timer); resolveWait(result); }
    else if (Date.now() - start > timeout) { clearInterval(timer); reject(new Error(`wait timeout\n${serverOutput}\n${JSON.stringify(messages.slice(-10), null, 2)}`)); }
  }, 25);
});

try {
  for (let i = 0; i < 100; i++) {
    try { if ((await fetch(`http://127.0.0.1:${port}/api/health`)).ok) break; } catch {}
    await delay(100);
  }
  const initialHealth = await (await fetch(`http://127.0.0.1:${port}/api/health`)).json();
  if (!initialHealth.llmBots || initialHealth.llmModel !== "Qwen/Qwen3.5-4B") throw new Error(`LLM disabled: ${JSON.stringify(initialHealth)}`);
  ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  ws.addEventListener("message", event => messages.push(JSON.parse(event.data)));
  await new Promise((resolveOpen, reject) => { ws.addEventListener("open", resolveOpen, { once: true }); ws.addEventListener("error", reject, { once: true }); });
  ws.send(JSON.stringify({ type: "create_room", playerName: "LLM Host" }));
  const welcome = await waitFor(message => message.type === "welcome");
  for (let count = 2; count <= 8; count++) {
    ws.send(JSON.stringify({ type: "add_bot" }));
    await waitFor(message => message.type === "room_state" && message.players.length === count);
  }
  ws.send(JSON.stringify({ type: "start_game" }));
  await waitFor(message => message.type === "room_state" && message.started);
  const modeMessage = await waitFor(message => message.type === "game_message" && message.payload?.api === "game_mode_broadcast", 12_000);
  const modeData = modeMessage.payload?.data || {};
  if (modeData.player_count !== 8 || modeData.bar_count + modeData.boom_count + modeData.leaf_count !== 8)
    throw new Error(`invalid game mode broadcast: ${JSON.stringify(modeMessage)}`);
  ws.send(JSON.stringify({ type: "leave_room" }));
  await waitFor(message => message.type === "left_room");

  let completedLog = "";
  let runningLog = "";
  let health = initialHealth;
  const deadline = Date.now() + 180_000;
  while (Date.now() < deadline) {
    await delay(1000);
    health = await (await fetch(`http://127.0.0.1:${port}/api/health`)).json();
    const response = await fetch(`http://127.0.0.1:${port}/api/rooms/${welcome.roomCode}/log`);
    if (response.ok) {
      const disposition = response.headers.get("content-disposition") || "";
      const content = await response.text();
      runningLog = content;
      if (!disposition.includes("_running")) { completedLog = content; break; }
    }
  }
  if (!completedLog) { await writeFile(resolve(import.meta.dirname, "llm_bot_game.running.log"), runningLog, "utf8"); throw new Error(`game did not finish; health=${JSON.stringify(health)}\n${serverOutput.slice(-4000)}`); }
  if (/(?:_parse_error|未知格式)/.test(completedLog)) throw new Error("Bot game produced a CLI parse error");
  if (!health.llmStats || health.llmStats.requests < 1 || health.llmStats.successes < 1 || (health.llmStats.failures + health.llmStats.speechFailures + health.llmStats.memoryFailures) < 1 || !(health.llmStats.accepted >= 1) || !(health.llmStats.rejected >= 1)) throw new Error(`LLM success/fallback paths not all exercised: ${JSON.stringify(health)}`);
  if (!(health.llmStats.httpStatusFailures >= 1)) throw new Error(`LLM HTTP failure classification was not exercised: ${JSON.stringify(health)}`);
  if (!(health.llmStats.circuitSkipped >= 1)) throw new Error(`LLM circuit breaker was not exercised: ${JSON.stringify(health)}`);
  if (!/\[Server\] 第 \d+ 天聊天与投票记录/.test(completedLog)) throw new Error("downloaded log did not contain server-side day interaction sections");
  if (!/(?:投票给|确认投票给)/.test(completedLog)) throw new Error("downloaded log did not contain semantic vote records");
  if (health.llmStats.speechRequests < 1 || health.llmStats.speechSuccesses < 1 || health.llmStats.speechMessages < 1 || health.llmStats.speechSilences < 1) throw new Error(`LLM speech/silence paths not all exercised: ${JSON.stringify(health)}`);
  const conversationStats = health.llmStats.conversationStats;
  if (!conversationStats || !["triggers", "chatBroadcasts", "allSilentTriggers", "staleSpeechDiscards", "stateChangeRetries", "broadcastRate", "allSilentRate"].every(key => typeof conversationStats[key] === "number"))
    throw new Error(`LLM orchestration health statistics missing: ${JSON.stringify(health)}`);
  if (Object.keys(conversationStats).some(key => !["triggers", "chatBroadcasts", "allSilentTriggers", "staleSpeechDiscards", "stateChangeRetries", "broadcastRate", "allSilentRate"].includes(key)))
    throw new Error(`LLM orchestration health statistics leaked sensitive fields: ${JSON.stringify(conversationStats)}`);
  const privacyViolations = [];
  for (const messagesForDecision of capturedPrompts) {
    const userPrompt = String(messagesForDecision.find(message => message.role === "user")?.content || "");
    const ownSeat = Number(userPrompt.match(/你是 (\d+) 号玩家/)?.[1]);
    for (const match of userPrompt.matchAll(/"message_type":"player_(\d+)"/g))
      if (Number(match[1]) !== ownSeat) privacyViolations.push({ ownSeat, leakedSeat: Number(match[1]) });
  }
  if (privacyViolations.length) throw new Error(`cross-player private context leaked: ${JSON.stringify(privacyViolations.slice(0, 5))}`);
  if (!capturedPrompts.some(rows => String(rows.find(message => message.role === "system")?.content || "").includes("八、游戏模式与获胜条件"))) throw new Error("full design.txt rules were not included in the system prompt");
  if (!capturedPrompts.some(rows => {
    const systemPrompt = String(rows.find(message => message.role === "system")?.content || "");
    const userPrompt = String(rows.find(message => message.role === "user")?.content || "");
    return systemPrompt.includes("valuable_private_information") && systemPrompt.includes("脚滑人等信息特化身份") && userPrompt.includes("自身合法私密身份、状态、技能结果或事件") && userPrompt.includes("silent 无效");
  })) throw new Error("speech prompts did not require valuable local-information disclosure");
  if (capturedPrompts.some(rows => String(rows.find(message => message.role === "system")?.content || "").includes("CREDITS："))) throw new Error("design credits leaked into the system prompt");
  if (!capturedPrompts.some(rows => { const prompt = String(rows.find(message => message.role === "user")?.content || ""); return prompt.includes("【本局模式】") && prompt.includes("【当前权威状态】") && prompt.includes("当前状态未显示的临时效果已经失效"); })) throw new Error("authoritative state layers were not included in the bot context");
  if (!capturedPrompts.some(rows => { const prompt = String(rows.find(message => message.role === "user")?.content || ""); return prompt.includes("投票阶段实际经过") && prompt.includes("当前投票预算剩余"); })) throw new Error("vote prompt did not distinguish elapsed voting time from adjusted vote budget");
  await writeFile(resolve(import.meta.dirname, "llm_bot_game.log"), completedLog, "utf8");
  console.log(JSON.stringify({ ok: true, roomCode: welcome.roomCode, llm: health.llmStats, logBytes: Buffer.byteLength(completedLog), privacyViolations: privacyViolations.length, serverErrors: messages.filter(x => x.type === "error") }, null, 2));
} finally {
  try { ws?.close(); } catch {}
  server.kill();
  fakeLlm.close();
  await delay(300);
}
