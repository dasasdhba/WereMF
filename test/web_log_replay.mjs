import { readFile, writeFile } from "node:fs/promises";
import { basename, resolve } from "node:path";
const logPath = resolve(process.argv[2]);
const endpoint = process.argv[3] || "ws://127.0.0.1:5060/ws";
const source = (await readFile(logPath, "utf8")).replace(/^\uFEFF/, "");
const lines = source.replace(/\r/g, "").split("\n");
const seed = Number(source.match(/^游戏种子：(-?\d+)/m)?.[1]);
function parsedMessage(line) {
  let m = line.match(/^\[Internal\] (.*)$/); if (m) return { kind:"internal", actor:null, text:m[1] };
  m = line.match(/^\[ToPlayer \d+: (.+?)\] (.*)$/); if (m) return { kind:"player", actor:m[1], text:m[2] };
  return null;
}
function isRequest(text) {
  return text.includes("输入") || text.startsWith("第一晚是否匿名") || text.startsWith("是否为叶子局") ||
    text.startsWith("是否重抽第一身份") || text.startsWith("红土地，要再突击一次吗") || text.includes("要喝吗") ||
    text.startsWith("是否使用复制技能") || text.startsWith("用一根粉条复活吗") || text.startsWith("移动一只 bug") ||
    text.startsWith("选择一个身份复制") || text.includes("给吗？") || text.startsWith("用一根彩条复活吗") ||
    text.startsWith("开启下一局") || text.startsWith("你可以选择") || text.startsWith("你可以输入");
}
const replay = [];
const tolerant = process.env.WEREMF_REPLAY_TOLERANT === "1";
for (let i=0;i<lines.length-1;i++) {
  const msg = parsedMessage(lines[i]);
  if (msg && isRequest(msg.text) && !lines[i+1].startsWith("[")) replay.push({ ...msg, input: lines[i+1].trim(), line:i+1 });
}
const playerEntry = replay.find(x=>x.text.includes("输入玩家列表"));
if (!playerEntry) throw new Error("No player list input found");
const playerNames = playerEntry.input.match(/"[^"]*"|\S+/g).map(x=>x.replace(/^"|"$/g,""));
const clients=[]; const events=[]; let replayIndex=0; let logContent=null; let doneResolve; let exportRequested=false;
const done = new Promise(r=>doneResolve=r); const wait=ms=>new Promise(r=>setTimeout(r,ms));
const report={ file:basename(logPath), seed, playerNames, replayInputs:replay.length, consumed:0, requestMismatches:[], actorMismatches:[], privateRouteViolations:[], anonymousNameLeaks:[], redactionViolations:[], parseErrors:[], serverErrors:[], roles:{}, phases:{night:0,day:0,vote:0}, winner:null };
function norm(x){return String(x||"").replace(/\s+/g,"").replace(/[（(].*?最多\d+.*?[）)]/g,"");}
function connect(payload){return new Promise((resolveOpen,reject)=>{const ws=new WebSocket(endpoint);const c={ws,name:payload.playerName,lobbyId:0,gameId:0};ws.addEventListener("open",()=>ws.send(JSON.stringify(payload)),{once:true});ws.addEventListener("error",e=>reject(new Error(`WebSocket connection failed: ${e?.message||endpoint}`)),{once:true});ws.addEventListener("message",e=>{const m=JSON.parse(e.data);events.push({receiver:c.name,message:m});if(m.type==="welcome"){c.lobbyId=m.playerId;c.gameId=m.playerId;resolveOpen(c);}if(m.type==="player_remapped")c.gameId=m.playerId;handle(c,m);});});}
function reply(c,value){c.ws.send(JSON.stringify({type:"game_input",value:String(value)}));}
function tolerantInput(c,payload){
  if(payload.api!=="request_vote") return "0";
  const row=Array.isArray(payload.data)?payload.data.find(x=>Number(x?.id)===Number(c.gameId)):null;
  const invalid=new Set((row?.invalid_vote||[]).map(Number));
  const target=Array.from({length:playerNames.length},(_,i)=>i+1).find(id=>!invalid.has(id))||0;
  return `${c.gameId} ${target}`;
}
function nextReplay(payload,c){
  if(payload.api==="request_for_next_game"){if(!exportRequested){exportRequested=true;clients[0].ws.send(JSON.stringify({type:"command",value:"\\log null"}));}return;}
  let entry=replay[replayIndex];
  if(payload.api==="request_player_list"){
    while(entry && !entry.text.includes("输入玩家列表")) entry=replay[++replayIndex];
    if(entry) { replayIndex++; report.consumed++; }
    return;
  }
  while(entry && entry.text.includes("输入玩家列表")) entry=replay[++replayIndex];
  if(!entry){report.requestMismatches.push({api:payload.api,actual:payload.message_content,reason:"replay exhausted"});if(tolerant) reply(c,tolerantInput(c,payload));return;}
  const textMismatch = norm(entry.text)!==norm(payload.message_content);
  if(textMismatch) report.requestMismatches.push({api:payload.api,expected:entry.text,actual:payload.message_content,line:entry.line});
  if(entry.kind==="player" && entry.actor!==c.name) report.actorMismatches.push({api:payload.api,expectedActor:entry.actor,receiver:c.name,input:entry.input,line:entry.line});
  replayIndex++; report.consumed++; reply(c,tolerant ? tolerantInput(c,payload) : entry.input);
}
function handle(c,m){
  if(m.type==="error"){report.serverErrors.push({receiver:c.name,message:m.message});return;}
  if(m.type!=="game_message")return; const p=m.payload,api=p.api||"";
  if(p.message_type?.startsWith("player_")&&p.message_type!==`player_${c.gameId}`)report.privateRouteViolations.push({receiver:c.name,gameId:c.gameId,target:p.message_type,api});
  if(api.endsWith("_parse_error"))report.parseErrors.push({receiver:c.name,api,text:p.message_content});
  if(api==="player_notify_chara")report.roles[c.name]=p.message_content;
  if(api==="night_start_broadcast"&&c===clients[0])report.phases.night++;
  if(api==="day_start_broadcast"&&c===clients[0])report.phases.day++;
  if(api==="vote_start_broadcast"&&c===clients[0])report.phases.vote++;
  if(api==="game_win_broadcast")report.winner=p.message_content;
  if(api==="player_anonymous_init")for(const player of p.data||[])if(!/^玩家\d+$/.test(player.name))report.anonymousNameLeaks.push({receiver:c.name,api,name:player.name});
  if(["game_update_night","game_update_day","cli_game_summary"].includes(api)){
    const entities=Array.isArray(p.data)?p.data:p.data?.entities||[];
    for(const entity of entities){if(entity.player?.anonymous&&!/^玩家\d+$/.test(entity.player.name))report.anonymousNameLeaks.push({receiver:c.name,api,name:entity.player.name});if(entity.player?.id!==c.gameId&&entity.role!==null)report.redactionViolations.push({receiver:c.name,api,exposed:entity.player?.id});}
  }
  if(api==="cli_log"){logContent=p.data;doneResolve();return;}
  if(api.startsWith("request_")&&!api.endsWith("_parse_error"))nextReplay(p,c);
}
const host=await connect({type:"create_room",playerName:playerNames[0]});clients.push(host);
for(let i=1;i<playerNames.length;i++)clients.push(await connect({type:"join_room",roomCode:host.ws?events.find(x=>x.receiver===host.name&&x.message.type==="welcome").message.roomCode:"",playerName:playerNames[i]}));
await wait(150);host.ws.send(JSON.stringify({type:"start_game"}));
await Promise.race([done,wait(60000).then(()=>{throw new Error("Replay timeout")})]);
clients.forEach(c=>c.ws.close());
const outPath=resolve(import.meta.dirname,`replay_${basename(logPath,".log")}.json`);await writeFile(outPath,JSON.stringify({report,events},null,2),"utf8");
const outLog=resolve(import.meta.dirname,`replay_${basename(logPath,".log")}.log`);await writeFile(outLog,logContent||"","utf8");
const exact=Boolean(report.winner&&report.consumed>5&&!report.requestMismatches.length&&!report.actorMismatches.length&&!report.privateRouteViolations.length&&!report.anonymousNameLeaks.length&&!report.redactionViolations.length&&!report.parseErrors.length&&!report.serverErrors.length);
const ok=Boolean(report.winner&&report.consumed>5&&!report.privateRouteViolations.length&&!report.anonymousNameLeaks.length&&!report.redactionViolations.length&&!report.parseErrors.length&&!report.serverErrors.length&&(tolerant||exact));
console.log(JSON.stringify({ok,exact,tolerant,...report,outPath,outLog},null,2));process.exit(ok?0:1);

