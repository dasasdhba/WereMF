import { spawn } from "node:child_process";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";
const port = 5199;
const serverExe = process.env.WEREMF_SERVER_EXE || fileURLToPath(new URL("../WereMFServer/bin/Release/net8.0/WereMFServer.exe", import.meta.url));
const gameExe = fileURLToPath(new URL("./chat-fake/bin/Release/net8.0/win-x64/publish/ChatFake.exe", import.meta.url));
const server = spawn(serverExe, ["--path", gameExe, "--host", "127.0.0.1", "--port", String(port), "--disable-llm-bots"], { windowsHide: true, env: { ...process.env, HTTP_PROXY: "", HTTPS_PROXY: "", ALL_PROXY: "", NO_PROXY: "*" } });
let output=""; server.stdout.on("data",x=>output+=x); server.stderr.on("data",x=>output+=x);
const connect = async first => { const messages=[]; const ws=new WebSocket(`ws://127.0.0.1:${port}/ws`); ws.addEventListener("message",e=>messages.push(JSON.parse(e.data))); await new Promise((r,j)=>{ws.addEventListener("open",r,{once:true});ws.addEventListener("error",j,{once:true});}); ws.send(JSON.stringify(first)); return {ws,messages}; };
const wait = async (client,predicate,timeout=8000) => { const end=Date.now()+timeout; while(Date.now()<end){const found=client.messages.findLast(predicate);if(found)return found;await delay(25);}throw new Error(`timeout\n${output}\n${JSON.stringify(client.messages.slice(-8),null,2)}`); };
let host, second;
try {
  for(let i=0;i<80;i++){try{if((await fetch(`http://127.0.0.1:${port}/api/health`)).ok)break;}catch{}await delay(100);}
  host=await connect({type:"create_room",playerName:"Host"}); const welcome=await wait(host,x=>x.type==="welcome");
  second=await connect({type:"join_room",roomCode:welcome.roomCode,playerName:"Second"}); await wait(second,x=>x.type==="welcome");
  for(let count=3;count<=7;count++){host.ws.send(JSON.stringify({type:"add_bot"}));await wait(host,x=>x.type==="room_state"&&x.players.length===count);}
  host.ws.send(JSON.stringify({type:"start_game"})); await wait(host,x=>x.type==="game_message"&&x.payload?.api==="game_update_day"); await wait(second,x=>x.type==="game_message"&&x.payload?.api==="game_update_day");
  host.ws.send(JSON.stringify({type:"chat",value:"myz 仍可发言"})); await wait(second,x=>x.type==="chat_message"&&x.text==="myz 仍可发言");
  second.ws.send(JSON.stringify({type:"chat",value:"死人发言"})); const denied=await wait(second,x=>x.type==="error"&&x.message.includes("已出局"));
  const botVoteMessage=await wait(host,x=>x.type==="game_message"&&x.payload?.api==="random_bot_votes_received");
  const botVotes=botVoteMessage.payload.data;
  if(!Array.isArray(botVotes)||botVotes.length!==10)throw new Error(`expected 10 queued random bot votes, got ${JSON.stringify(botVotes)}`);
  const voterCounts=new Map();
  for(const input of botVotes){const match=/^(\d+) ([0-7]|b)$/.exec(input);if(!match)throw new Error(`invalid bot vote input: ${input}`);const voter=Number(match[1]);if(voter<3||voter>7)throw new Error(`non-bot vote was queued: ${input}`);voterCounts.set(voter,(voterCounts.get(voter)||0)+1);}
  if([...voterCounts.values()].some(count=>count!==2)||voterCounts.size!==5)throw new Error(`each random bot must queue two votes: ${JSON.stringify([...voterCounts])}`);
  console.log(JSON.stringify({ok:true,threatenedLivingAllowed:true,deadDenied:Boolean(denied),randomBotVotes:botVotes.length},null,2));
} finally { try { host?.ws.close(); second?.ws.close(); } catch {} server.kill(); await delay(250); }
