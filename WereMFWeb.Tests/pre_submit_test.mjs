import { spawn } from "node:child_process";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";
const port = 5198;
const serverExe = fileURLToPath(new URL("../WereMFServer/bin/Release/net8.0/WereMFServer.exe", import.meta.url));
const gameExe = fileURLToPath(new URL("./pre-submit-fake/bin/Release/net8.0/win-x64/publish/PreSubmitFake.exe", import.meta.url));
const server = spawn(serverExe, ["--path", gameExe, "--host", "127.0.0.1", "--port", String(port), "--request-timeout-seconds", "5", "--debug-api"], { windowsHide: true, env: { ...process.env, HTTP_PROXY: "", HTTPS_PROXY: "", ALL_PROXY: "", NO_PROXY: "*" } });
let output = ""; server.stdout.on("data", x => output += x); server.stderr.on("data", x => output += x);
const messages = []; let ws;
const wait = (predicate, timeout = 8000) => {
  const found = messages.findLast(predicate); if (found) return Promise.resolve(found);
  return new Promise((resolve, reject) => { const started = Date.now(); const timer = setInterval(() => { const match = messages.findLast(predicate); if (match) { clearInterval(timer); resolve(match); } else if (Date.now() - started > timeout) { clearInterval(timer); reject(new Error(`timeout\n${output}\n${JSON.stringify(messages.slice(-8), null, 2)}`)); } }, 25); });
};
const send = value => ws.send(JSON.stringify(value));
try {
  for (let i=0;i<80;i++){ try { if ((await fetch(`http://127.0.0.1:${port}/api/health`)).ok) break; } catch {} await delay(100); }
  ws = new WebSocket(`ws://127.0.0.1:${port}/ws`); ws.addEventListener("message", e => messages.push(JSON.parse(e.data)));
  await new Promise((resolve,reject)=>{ws.addEventListener("open",resolve,{once:true});ws.addEventListener("error",reject,{once:true});});
  send({type:"create_room",playerName:"Host"}); const welcome = await wait(x=>x.type==="welcome");
  for(let count=2;count<=7;count++){ send({type:"add_bot"}); await wait(x=>x.type==="room_state"&&x.players.length===count); }
  send({type:"start_game"}); await wait(x=>x.type==="room_state"&&x.started);

  await wait(x=>x.type==="game_message"&&x.payload?.api==="pending_skill_created"&&x.payload.data.id==="draft-normal");
const runningLogResponse = await fetch(`http://127.0.0.1:${port}/api/rooms/${welcome.roomCode}/log`);
  const runningLog = await runningLogResponse.text();
  if (!runningLogResponse.ok || !runningLog.includes("pending_skill_created")) throw new Error("debug running log did not contain raw CLI output");
  send({type:"pending_draft",skillId:"draft-normal",value:"2",preSubmit:false});
  await wait(x=>x.type==="game_message"&&x.payload?.api==="request_paoxian_skill"&&x.payload.data.skill_id==="draft-normal");
  if(messages.some(x=>x.type==="pre_submit_accepted"&&x.skillId==="draft-normal")) throw new Error("unarmed draft auto-submitted");
  send({type:"game_input",value:"3"});
  await wait(x=>x.type==="cli_input_recorded"&&x.api==="request_paoxian_skill"&&x.value==="3");
  await wait(x=>x.type==="game_message"&&x.payload?.message_content==="draft-normal:3");

  await wait(x=>x.type==="game_message"&&x.payload?.api==="pending_skill_created"&&x.payload.data.id==="draft-armed");
  send({type:"pending_draft",skillId:"draft-armed",value:"2",preSubmit:true});
  const accepted = await wait(x=>x.type==="pre_submit_accepted"&&x.skillId==="draft-armed");
  await wait(x=>x.type==="cli_input_recorded"&&x.api==="request_paoxian_skill"&&x.value==="2");
  await wait(x=>x.type==="game_message"&&x.payload?.message_content==="draft-armed:2");
  if(messages.some(x=>x.type==="game_message"&&x.payload?.api==="request_paoxian_skill"&&x.payload.data.skill_id==="draft-armed")) throw new Error("valid armed request leaked to browser");

  await wait(x=>x.type==="game_message"&&x.payload?.api==="pending_skill_created"&&x.payload.data.id==="draft-invalid");
  send({type:"pending_draft",skillId:"draft-invalid",value:"2 3",preSubmit:true});
  await wait(x=>x.type==="pre_submit_rejected"&&x.skillId==="draft-invalid");
  await wait(x=>x.type==="game_message"&&x.payload?.api==="request_paoxian_skill"&&x.payload.data.skill_id==="draft-invalid");
  send({type:"game_input",value:"4"}); await wait(x=>x.type==="game_message"&&x.payload?.message_content==="draft-invalid:4");
  await wait(x=>x.type==="game_message"&&x.payload?.api==="pending_skill_created"&&x.payload.data.id==="threat-normal");
  send({type:"pending_draft",skillId:"threat-normal",value:"2",preSubmit:true});
  await wait(x=>x.type==="pre_submit_rejected"&&x.api==="myz_threaten_notify"&&x.skillId==="threat-normal");
  await wait(x=>x.type==="game_message"&&x.payload?.api==="myz_threaten_notify");
  await wait(x=>x.type==="game_message"&&x.payload?.api==="request_paoxian_skill"&&x.payload.data.skill_id==="threat-normal");
  if(messages.some(x=>x.type==="pre_submit_accepted"&&x.skillId==="threat-normal")) throw new Error("normally threatened skill auto-submitted its old draft");
  send({type:"game_input",value:"3"});
  await wait(x=>x.type==="game_message"&&x.payload?.message_content==="threat-normal:3");

  await wait(x=>x.type==="game_message"&&x.payload?.api==="pending_skill_created"&&x.payload.data.id==="threat-force-doge");
  send({type:"pending_draft",skillId:"threat-force-doge",value:"2",preSubmit:true});
  await wait(x=>x.type==="pre_submit_rejected"&&x.api==="myz_threaten_force_notify"&&x.skillId==="threat-force-doge");
  await wait(x=>x.type==="game_message"&&x.payload?.api==="request_doge_skill_force_threaten");
  if(messages.some(x=>x.type==="pre_submit_accepted"&&x.skillId==="threat-force-doge")) throw new Error("force-threatened Doge reused its target draft as the suicide choice");
  send({type:"game_input",value:"1"});
  await wait(x=>x.type==="game_message"&&x.payload?.message_content==="threat-force-doge:1");

  await wait(x=>x.type==="game_message"&&x.payload?.api==="pending_skill_created"&&x.payload.data.id==="threat-force-no-choice");
  send({type:"pending_draft",skillId:"threat-force-no-choice",value:"2",preSubmit:true});
  await wait(x=>x.type==="pre_submit_rejected"&&x.api==="myz_threaten_force_notify"&&x.skillId==="threat-force-no-choice");
  await wait(x=>x.type==="game_message"&&x.payload?.message_content==="threat-force-no-choice:auto");
  if(messages.some(x=>x.type==="pre_submit_accepted"&&x.skillId==="threat-force-no-choice")) throw new Error("force threat without auxiliary choice auto-submitted an obsolete draft");

  await wait(x=>x.type==="game_message"&&x.payload?.api==="pending_skill_created"&&x.payload.data.id==="myz-same");
  send({type:"pending_draft",skillId:"myz-same",value:"2 2",preSubmit:true});
  await wait(x=>x.type==="pre_submit_accepted"&&x.skillId==="myz-same");
  await wait(x=>x.type==="game_message"&&x.payload?.message_content==="myz-same:2 2");  for (const [id, value] of [["leaf-second-ctf","2"],["leaf-second-xiansong","3"],["leaf-second-creeper","4"]]) {
    await wait(x=>x.type==="game_message"&&x.payload?.api==="pending_skill_created"&&x.payload.data.id===id);
    send({type:"pending_draft",skillId:id,value,preSubmit:true});
  }
  for (const [id, value] of [["leaf-second-ctf","2"],["leaf-second-xiansong","3"],["leaf-second-creeper","4"]]) {
    await wait(x=>x.type==="pre_submit_accepted"&&x.skillId===id);
    await wait(x=>x.type==="game_message"&&x.payload?.message_content===`${id}:${value}`);
    if(messages.some(x=>x.type==="game_message"&&x.payload?.api==="request_paoxian_skill"&&x.payload.data.skill_id===id)) throw new Error(`second Leaf request leaked: ${id}`);
  }
  const copyLeafBot = await wait(x=>x.type==="game_message"&&x.payload?.api==="copy_leaf_bot_received");
  const copyLeafValue = String(copyLeafBot.payload?.data ?? "");
  if (!["1","2","3"].includes(copyLeafValue)) throw new Error(`copy-Leaf bot submitted invalid value: ${copyLeafValue}`);
  if (messages.some(x=>x.type==="game_message"&&x.payload?.api==="request_hechong_copy_leaf_parse_error")) throw new Error("copy-Leaf bot caused a CLI parse error");
  await wait(x=>x.type==="game_message"&&x.payload?.api==="game_win_broadcast");
  await new Promise(resolve => { ws.addEventListener("close", resolve, {once:true}); ws.close(); });
  let terminalRoomReleased = false;
  for (let i=0;i<40;i++) { const health = await (await fetch(`http://127.0.0.1:${port}/api/health`)).json(); if (health.activeRooms === 0) { terminalRoomReleased = true; break; } await delay(50); }
  if (!terminalRoomReleased) throw new Error("terminal disconnect retained the finished room");
  console.log(JSON.stringify({ok:true,unarmedStayedDraft:true,armedAutoSubmitted:accepted.value==="2",invalidMultiRejected:true,normalThreatRequiresFreshDecision:true,forceThreatDogeKeepsAuxiliaryChoice:true,forceThreatWithoutChoiceDoesNotWait:true,myzSelfTargetAccepted:true,secondLeafAllAutoSubmitted:true,copyLeafBotUsesNumericOption:true,debugRunningLogAvailable:true,terminalDisconnectReleasesRoom:true},null,2));
} finally { try { ws?.close(); } catch {} server.kill(); await delay(250); }