import { spawn } from "node:child_process";
import { basename, resolve } from "node:path";

const root = resolve(import.meta.dirname, "..");
const withReplay = process.argv.includes("--replay") && !process.argv.includes("--no-replay");
const env = { ...process.env, HTTP_PROXY: "", HTTPS_PROXY: "", ALL_PROXY: "", NO_PROXY: "*", SILICONFLOW_API_KEY: "", LLM_FALLBACK_BASE_URL: "" };
const dotnet = process.platform === "win32" ? "dotnet.exe" : "dotnet";
const node = process.execPath;

function run(label, command, args) {
  return new Promise((resolveRun, reject) => {
    const child = spawn(command, args, { cwd: root, env, stdio: "inherit", windowsHide: true });
    child.on("error", error => reject(new Error(`${label}: ${error.message}`)));
    child.on("exit", (code, signal) => {
      if (code === 0) { console.log(`PASS ${label}`); resolveRun(); return; }
      reject(new Error(`${label} failed with ${signal || `exit code ${code}`}`));
    });
  });
}

const runNode = (label, script, ...args) => run(label, node, [script, ...args]);
const runDotnet = (label, ...args) => run(label, dotnet, args);

const replayLogs = [
  "WereMFWeb/fixtures/logs/WereMF_260522_225843.log",
  "WereMFWeb/fixtures/logs/WereMF_260522_232757.log",
  "WereMFWeb/fixtures/logs/WereMF_260522_235200.log"
];

try {
  await runDotnet("solution Release build", "build", "WereMF.sln", "-c", "Release");
  await runDotnet("F# CLI publish", "publish", "WereMF/WereMF.fsproj", "-c", "Release", "-r", "win-x64", "--self-contained", "true");
  await runDotnet("chat fake CLI publish", "publish", "test/chat-fake/ChatFake.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "true");
  await runDotnet("pre-submit fake CLI publish", "publish", "test/pre-submit-fake/PreSubmitFake.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "true");
  await runDotnet("Server protocol unit tests", "run", "--project", "WereMFServer.Tests", "-c", "Release", "--no-build");
  await run("Web Node tests", node, ["--test", "WereMFWeb/scripts/test-night-patch.mjs", "WereMFWeb/scripts/test-web-modules.mjs"]);
  await runNode("Server routing/reconnect/permission test", "test/protocol_boundary_test.mjs");
  await runNode("pre-submit fake CLI assertions", "test/pre_submit_test.mjs");
  await runNode("chat permission assertions", "test/chat_test.mjs");
  await runNode("room lifecycle assertions", "test/room_lifecycle_test.mjs");
  await runNode("Web event queue assertions", "test/web_event_queue_check.mjs");

  if (withReplay) {
    for (const [index, log] of replayLogs.entries()) {
      const name = basename(log, ".log");
      await runNode(`real log replay and UI check ${name}`, "test/run-replay.mjs", log, String(5260 + index));
    }
  } else {
    console.log("SKIP real log replay (use --replay for extended fixture replay)");
  }
  console.log(JSON.stringify({ ok: true, deterministic: true, replays: withReplay ? replayLogs.length : 0, llm: false }, null, 2));
} catch (error) {
  console.error(`FAIL deterministic runner: ${error.message}`);
  process.exitCode = 1;
}
