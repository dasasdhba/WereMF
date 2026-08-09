import { readFile } from "node:fs/promises";
import { spawn } from "node:child_process";
import { basename, resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";

const root = resolve(import.meta.dirname, "..");
const logPath = resolve(root, process.argv[2]);
const port = Number(process.argv[3] || 5060);
const serverExe = resolve(root, "WereMFServer/bin/Release/net8.0/WereMFServer.exe");
const gameExe = resolve(root, "WereMF/bin/Release/net8.0/win-x64/publish/WereMF.exe");
const env = { ...process.env, HTTP_PROXY: "", HTTPS_PROXY: "", ALL_PROXY: "", NO_PROXY: "*", SILICONFLOW_API_KEY: "", LLM_FALLBACK_BASE_URL: "", WEREMF_REPLAY_TOLERANT: "1" };
const log = (await readFile(logPath, "utf8")).replace(/^\uFEFF/, "");
const seed = log.match(/^游戏种子：(-?\d+)/m)?.[1];
const serverArgs = ["--path", gameExe, "--host", "127.0.0.1", "--port", String(port), "--disable-llm-bots", "--request-timeout-seconds", "1", "--vote-seconds-per-alive", "1", "--vote-penalty-seconds", "0", "--event-interval-seconds", "0"];
if (seed) serverArgs.push("--seed", seed);
const server = spawn(serverExe, serverArgs, { cwd: root, env, windowsHide: true });
let serverOutput = "";
server.stdout.on("data", value => serverOutput += value);
server.stderr.on("data", value => serverOutput += value);

function runNode(args) {
  return new Promise((resolveRun, reject) => {
    const child = spawn(process.execPath, args, { cwd: root, env, stdio: "inherit", windowsHide: true });
    child.on("error", reject);
    child.on("exit", code => code === 0 ? resolveRun() : reject(new Error(`node ${args.join(" ")} exited with ${code}\nServer output:\n${serverOutput}`)));
  });
}

try {
  for (let i = 0; i < 100; i++) {
    try {
      if ((await fetch(`http://127.0.0.1:${port}/api/health`)).ok) break;
    } catch {}
    if (i === 99) throw new Error(`server did not start\n${serverOutput}`);
    await delay(100);
  }
  await delay(100);
  const name = basename(logPath, ".log");
  await runNode(["WereMFWeb.Tests/web_log_replay.mjs", logPath, `ws://127.0.0.1:${port}/ws`]);
  await runNode(["WereMFWeb.Tests/web_log_replay_ui_check.mjs", `replay_${name}.json`]);
  console.log(JSON.stringify({ ok: true, log: name, seed: seed ? Number(seed) : null, port }, null, 2));
} finally {
  server.kill();
  await new Promise(resolveDelay => setTimeout(resolveDelay, 300));
}
