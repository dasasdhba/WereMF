# WereMFServer

WereMFServer 是 WereMF 的联网房间服务：托管静态 Web 客户端，通过 WebSocket 管理 7–16 人房间，并为每个正在进行的房间启动一个独立的 WereMF CLI/API 规则进程。服务端不重新实现游戏规则，只负责会话、权限、消息路由、计时和 Bot 托管。

## Server 内部边界

`GameRoom` 仍是房间生命周期与并发协调入口，但规则无关的协议和数据边界已拆成独立组件：

- `CliEnvelope`/`CliMessageRouter` 负责解析 CLI 信封并分类公共、玩家、内部、请求、完整快照和夜间增量；`GameProcess` 只负责独立规则进程的输入输出。
- `RegularInputCoordinator`、`ConcurrentInputCoordinator` 和 `PendingDraftStore` 分别维护普通请求、并发投票/重抽阶段和预提交草稿。CLI 仍是最终合法性来源，服务端只做权限、格式和超时回退保护。
- `RoomHistory` 统一公开、房主、玩家重连历史及 Bot 时间线的序列与上限；`GameLogAssembler` 只负责把白天互动合并进 CLI 日志。
- `BotCoordinator` 负责普通 Bot 请求的模型候选与合法性回退，`BotVisibleContextBuilder` 负责完整快照及其后的 night patch 权威上下文；`LlmBotClient` 不访问房间可变状态。

这些组件通过 `GameRoom` 委派接入，保留现有 WebSocket 消息顺序、重连历史和 CLI 输入协议。确定性验证入口仍是 `node test/run-deterministic.mjs`。

## 构建与运行

从仓库根目录执行：

```powershell
dotnet publish .\WereMFServer\WereMFServer.csproj -c Release -r win-x64 --self-contained false
```

`WereMFServer.csproj` 会通过 MSBuild 自动执行 WereMFWeb 的生产构建，因此需要预先安装 Node.js 22；无需手动生成或维护 `WereMFServer/wwwroot/`。

启动示例：

```powershell
.\WereMFServer\bin\Release\net8.0\win-x64\publish\WereMFServer.exe `
  --path .\WereMF\bin\Release\net8.0\win-x64\publish\WereMF.exe `
  --host 0.0.0.0 `
  --port 5000
```

浏览器访问 `http://localhost:5000`，WebSocket 使用同源 `/ws`。公网部署应由反向代理提供 HTTPS/WSS，并转发 `/ws`。

### 部署到 Debian 测试服务器

仓库根目录提供 [`scripts/deploy.ps1`](../scripts/deploy.ps1)，会构建 `linux-x64` 自包含的 CLI 与 Server、复制指定的抽签 `config.json`、上传到临时目录、备份现有 `/root/weremf`、切换 tmux 服务并执行健康检查；新版本未在 30 秒内通过检查时会自动回滚。

```powershell
.\scripts\deploy.ps1
```

SSH 目标不写入 Git。脚本按“`-RemoteHost` 参数、当前进程的 `WEREMF_DEPLOY_HOST` 环境变量、仓库根目录 `.env`”的顺序读取；`.env` 已被 Git 忽略。远端目录默认 `/root/weremf`、tmux 会话默认 `weremf`、端口默认 `5000`，抽签配置默认取仓库中的 `WereMF/config.json`。部署脚本还会从 `.env` 白名单提取 `SILICONFLOW_*` 与 `LLM_FALLBACK_*` 配置，写入远端独立的 `/root/weremf.env`（权限 `600`）并由 tmux 启动脚本导入；Key 不进入发布包或版本备份目录。

```powershell
.\scripts\deploy.ps1 -RemoteHost root@example.com

# 或写入不会被 Git 跟踪的仓库根目录 .env
# WEREMF_DEPLOY_HOST=root@example.com
.\scripts\deploy.ps1
```

排错时可临时加 `-DebugApi`；正式临时测试不需要时不要开启。

### 参数

| 参数 | 默认值 | 说明 |
|---|---:|---|
| `--path <path>` | `WereMF.exe` / `WereMF` | WereMF CLI 可执行文件 |
| `--host <host>` | `127.0.0.1` | HTTP 与 WebSocket 监听地址 |
| `--port <port>` | `5000` | HTTP 与 WebSocket 共用端口；`--websocket-port` 是兼容别名 |
| `--config <path>` | `WereMF/config.json` | 传给 WereMF 的抽签配置 |
| `--seed <int>` | 随机 | 传给 WereMF 的固定种子 |
| `--request-timeout-seconds <n>` | `30` | 普通请求限时 |
| `--vote-seconds-per-alive <n>` | `60` | 投票阶段每名本轮可投票玩家提供的秒数 |
| `--vote-penalty-seconds <n>` | `30` | 每次有效投票后扣除的秒数 |
| `--event-interval-seconds <n>` | `2` | 两段演出区间内相邻消息的默认放行间隔；0 表示不延迟 |
| `--llm-model <name>` | `Qwen/Qwen3.5-4B` | LLM Bot 使用的 OpenAI 兼容模型 |
| `--llm-endpoint <url>` | `https://api.siliconflow.cn/v1/` | LLM API 基地址 |
| `--llm-timeout-seconds <n>` | `15` | 单次模型决策超时，范围 1–120 秒；本地小模型可按首轮提示处理耗时适当提高 |
| `--disable-llm-bots` | 关闭 | 即使存在 API Key 也强制使用随机 Bot |
| `--llm-bot-think-seconds <n>` | `10` | 投票期间按真实时间再次思考的间隔，范围 3–60 秒 |
| `--debug-api` | 关闭 | 注册仅供临时排错使用的无鉴权房间日志接口 |

`--http-port` 仅为旧命令行兼容参数，其值会被忽略；当前服务只使用 `--port`。

## HTTP API

| 路径 | 说明 |
|---|---|
| `GET /api/health` | `{ status, activeRooms, version, llmBots, llmModel, llmStats }` 健康状态；统计只含计数，不含 Key、提示词或模型回答 |
| `GET /api/rooms` | 当前仍可加入的房间：`{ code, players, maxPlayers, started }[]` |
| `GET /api/rooms/{roomCode}/log` | 仅 `--debug-api`：下载指定房间的进行中双向 CLI 记录或终局正式日志 |
| `GET /ws` | WebSocket 升级端点 |
| 其他路径 | 静态文件；未知前端路由回退到 `index.html` |

## WebSocket 客户端消息

连接 `/ws` 后，第一条消息必须创建、加入或恢复房间：

```json
{ "type": "create_room", "playerName": "玩家名" }
{ "type": "join_room", "roomCode": "012345", "playerName": "玩家名" }
{ "type": "reconnect", "roomCode": "012345", "playerName": "玩家名", "token": "..." }
```

`join_room` 同样可以携带 `token` 恢复会话。进入房间后可发送：

| `type` | 字段 | 权限与行为 |
|---|---|---|
| `start_game` | - | 仅房主；至少 7 席时开局 |
| `add_bot` | - | 仅房主、仅大厅；增加永久 Bot |
| `remove_bot` | - | 仅房主、仅大厅；删除最后一个永久 Bot |
| `restart_room` | - | 仅房主；结束当前 CLI 进程并让仍在房间的玩家返回大厅 |
| `update_room_settings` | `requestTimeoutSeconds`, `voteSecondsPerAlive`, `votePenaltySeconds`, `eventIntervalSeconds` | 仅房主、仅大厅；更新本房间计时与消息展示间隔 |
| `leave_room` | - | 彻底退出；大厅和终局立即释放席位，进行中则由 Bot 接管至本局结束 |
| `game_input` | `value` | 提交 CLI 格式输入；服务端校验当前可提交玩家 |
| `pending_draft` | `skillId`, `api`, `value`, `preSubmit` | 保存技能预选；`preSubmit: true` 表示玩家主动预提交 |
| `chat` | `value` | 白天发言，1–300 字；仅存活真人玩家可发送，服务端裁决权限 |
| `ping` | - | 返回 `pong` |
| `command` | `value` | 旧客户端兼容；房主只允许 `\restart`，效果等同 `restart_room` |

昵称长度为 1–20 个字符，且不能与身份名相同（英文身份名不区分大小写）；房间号固定为 6 位数字。

## WebSocket 服务端消息

| `type` | 主要字段 | 说明 |
|---|---|---|
| `welcome` | `roomCode`, `playerId`, `playerName`, `token`, `isHost` | 创建、加入或重连成功；客户端应保存令牌 |
| `room_state` | `roomCode`, `started`, `settings`, `bots`, `players` | 房间完整公开状态；`settings` 是本房间计时与消息间隔，玩家含在线、房主和 Bot 标记 |
| `session_state` | `playerId`, `isHost` | 编号重排或房主移交后的当前会话状态 |
| `player_remapped` | `playerId` | 匿名第一晚后，该浏览器实际使用的游戏编号 |
| `game_message` | `payload` | WereMF CLI API 消息，格式见 [`../WereMF/README.md`](../WereMF/README.md)；`game_update_night_patch` 是公开字段级增量 |
| `chat_message` | `playerId`, `text`, `sentAt` | 公开聊天消息；昵称由客户端按当前匿名映射解析，不传真实昵称 |
| `input_accepted` | `api`, `remaining` | 并发输入已接受；`remaining` 是该玩家还可提交次数 |
| `cli_input_recorded` | `api`, `value`, `sentAt` | 已通过校验并提交/排队的实际 CLI 格式输入；只发给提交者，并写入其私密重连历史 |
| `pre_submit_accepted` | `api`, `skillId`, `value`, `message` | 预提交经最新请求数据复核合法，已自动发送给 CLI |
| `pre_submit_rejected` | `api`, `skillId`, `message` | 预提交因局面变化或数量/格式不合法而解除，玩家需重新确认 |
| `request_timer` | `api`, `deadlineUtc`, `mode` | 当前请求或投票阶段的绝对截止时间 |
| `request_timeout_resolved` | `api`, `value`, `source`, `message` | 超时已按预选、随机合法项、Bot 或弃票处理 |
| `bot_takeover` | `playerId`, `playerName?`, `message` | 玩家断线超限或彻底退出后由 Bot 接管 |
| `room_restarted` | `message` | 当前对局已终止并返回大厅 |
| `left_room` | - | 彻底退出已完成，随后服务端正常关闭连接 |
| `game_log_available` | `fileName`, `content` | 终局日志，可由所有仍在房间的玩家下载 |
| `game_ended` | `message` | CLI 正常结束或异常退出 |
| `server_notice` | `message` | CLI 的非 JSON 输出，仅发给房主 |
| `error` | `message` | 客户端可见错误 |
| `pong` | - | `ping` 的响应 |

## CLI API 路由与隐私

WereMF 的每行 JSON 都包含 `api`、`message_type`、`message_content` 和 `data`。服务端以 CLI 返回值为状态真相，并按以下规则转发：

- `public`：广播给所有在线玩家。
- `player_X`：只发给匿名映射后的游戏编号 X。
- `internal`：通常只发给房主，但不是“房主消息”的同义词。
- `request_player_list`：由服务端自动提交房间玩家列表，不转发给浏览器。
- `request_reroll_player`：展开为所有有资格玩家各一次的并发输入。
- `request_vote`：展开为所有有资格玩家各最多两次的并发输入，每次投票仍由 CLI 作为公开信息广播。
- `pending_skill_created`：按 `source_player_id` 提前发给技能拥有者，允许先预选。玩家可主动将草稿标为预提交；真正轮到该技能时服务端用最新请求数据复核，合法才自动发送，否则解除预提交并展示正常请求。叶子的各身份技能仍分别 pending、按优先级轮流处理。
- 收到 myz_threaten_notify 或 myz_threaten_force_notify 时，服务端按 data.skill_id 清除对应技能的旧预选与预提交，避免普通威胁被自动服从，也避免旧目标被误作强制威胁后的附加选项。普通威胁会展示原技能请求供玩家重新决定；强制威胁固定目标，但 CLI 仍可能为 Doge 等角色发出附加选项请求。
- `game_update_night`、`game_update_day`、`cli_game_summary`：逐玩家脱敏；其他玩家的身份不会发送到该浏览器。匿名玩家名也按当前接收者可见范围处理。
- `game_update_night_patch`：仅接受 `cause=huika_smog`、已存在玩家、公开 `EntityState` 字段及匹配的 JSON 基本类型；拒绝包含 `role`、`player`、未知字段或非法值的消息。该消息已是公开数据，直接写入公开/玩家重连历史并按原顺序广播，不经过逐玩家脱敏。重连时必须先应用最近完整快照，再按顺序合并其后的 patch；Bot 上下文也将它们标记为当前权威状态。

重连时会重放最多 250 条公开历史、该玩家的私密历史，以及房主专属历史（仅当前房主）。

白天聊天从 `day_start_broadcast` 开启，到 `night_start_broadcast` 或终局关闭。服务端根据最新 `game_update_day` 的 `state.is_dead` 维护发言资格；myz 威胁不影响聊天。聊天记录进入公开历史，因此断线重连后会一并回放。

## 计时器与 Bot

- 普通请求默认限时 30 秒。截止时若预选仍合法则优先采用；否则随机选择一个合法项并通知玩家。房主可在开局前覆盖本房间的默认值。
- 叶子选角的随机回退遵守角色限制：不能选粉侠和彩怪，并且不能只选择同一阵营。
- myz 的两个玩家编号有顺序，分别表示“被威胁者”和“其技能接收者”，两项必须分别按 API 的 `invalid_choice` 与 `invalid_target_choice` 校验，不能合并不可选集合；只要各自在对应位置合法，就允许两者相同，例如 `2 2`。
- 投票总时限为“本轮 `can_vote: true` 的玩家数 × 本房间每人投票秒数”；死亡、被 myz 威胁或被禁票而无法参与本轮投票的玩家不计入，每接受一票扣除本房间设置的秒数；所有人完成后 CLI 会自然进入下一阶段，服务端不会再补输入。
- 投票超时且玩家一票未投时，服务端向 CLI 发送 `0`，即 CLI 的默认弃票。
- 永久 Bot 与临时托管 Bot 在设置 `SILICONFLOW_API_KEY` 时优先请求 LLM；未配置、超时、HTTP 错误、响应格式错误或答案未通过现有合法性校验时，立即回退随机合法输入。真人使用原令牌重连后仍会立即恢复控制。
- Bot 名称先按 `bots_prefer.txt` 的顺序选取，全部占用后再使用 `bots.txt`，最后才回退为 `Bot N`；名称不会改变策略或阵营。
- 真人新加入时若昵称与房内玩家重名（忽略大小写），服务端会在昵称后追加四位随机编号并通过 `welcome.playerName` 同步给客户端；持原令牌重连不会改名。
- 进行中的对局关闭 Tab 只算临时断线，可以用原令牌重连。等待大厅或终局阶段一旦断线即视为彻底退出：服务端立即删除席位并令旧令牌失效；若房主断线，会从在线真人中随机选择新房主；最后一名真人离线后房间立即解散。

## LLM Bot

设置 `SILICONFLOW_API_KEY` 即启用 LLM Bot。可选环境变量为 `SILICONFLOW_MODEL`、`SILICONFLOW_BASE_URL`、`SILICONFLOW_TIMEOUT_SECONDS`、`SILICONFLOW_BOT_THINK_SECONDS`；命令行可以覆盖这些设置。当前默认使用 SiliconFlow 的 OpenAI 兼容 `POST /chat/completions` 接口、`Qwen/Qwen3.5-4B`、关闭 thinking 和 JSON 输出。

```dotenv
SILICONFLOW_API_KEY=sk-...
SILICONFLOW_MODEL=Qwen/Qwen3.5-4B
SILICONFLOW_TIMEOUT_SECONDS=15
SILICONFLOW_BOT_THINK_SECONDS=10
```

### 本地 llama.cpp

`llama.cpp` 的 OpenAI 兼容端点可以直接使用。Qwen2.5 7B Q4_K_M 的实测启动参数如下：

```powershell
llama serve `
  -m "D:\path\to\qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf" `
  --host 127.0.0.1 --port 8081 `
  --ctx-size 49152 `
  --parallel 4 `
  --reasoning off `
  --jinja `
  --no-webui
```

新开一个 PowerShell 启动 WereMFServer；本地 llama 未设置 API Key 时，客户端 Key 只需使用任意非空占位值：

```powershell
$env:SILICONFLOW_API_KEY = "local-llama"
$env:SILICONFLOW_BASE_URL = "http://127.0.0.1:8081/v1/"
$env:SILICONFLOW_MODEL = "qwen2.5-7b-instruct"
$env:SILICONFLOW_TIMEOUT_SECONDS = "60"
.\WereMFServer\bin\Debug\net8.0\WereMFServer.exe `
  --path .\WereMF\bin\Release\net8.0\win-x64\publish\WereMF.exe
```

当前完整发言上下文实测可超过 8K token，因此 `32768 / 4` 的每槽 8192 token 不足；`49152 / 4` 提供每槽 12288 token。四槽同时生成时单次发言可能超过 30 秒，本地测试建议将模型超时设为 60 秒；云端默认 15 秒。

决策边界：

- 模型只负责提出候选 CLI 输入；服务端仍使用与计时器相同的规则校验，模型没有直接写 CLI、调用管理命令或改状态的权限。
- 每个 Bot 的提示只包含该席位自己的私密/脱敏历史、公开历史、当前请求和仓库 `design.txt` 去掉 Credits 后的完整规则；不会使用原始全量 CLI 日志，也不会把其他玩家隐藏身份放入上下文。
- 规则焦点不再扫描场上文字猜身份：服务端直接从该 Bot 的个性化权威状态提取自身身份、叶子二阶段身份与合虫复制身份，并用 `pending_skill_created.id → type` 映射当前技能；完整规则仍作为兜底。
- 开局会公开 `game_mode_broadcast`，并将人数、标准/叶子模式及吧/爆/叶构成持续放入 Bot 上下文。最新 `game_update_day/night` 是唯一权威现状；旧事件和滚动记忆只用于追溯，不能让已经消失的临时效果继续生效。
- 普通技能每个请求决策一次，重抽身份决策一次。白天开始、真人发言以及投票期间的定时唤醒都会让每个存活 Bot 独立决定“发言或沉默”以及“投一票或暂缓”；每轮思考至多提交一票，下一次思考才可确认或改票，脚滑人仍可选择 `b` 自爆。
- 投票提示同时提供投票阶段实际经过时间、距离最近一次公开发言或投票的静默时间，以及扣票后的投票预算剩余时间。定时唤醒按真实时间间隔触发，不会因一张票扣减预算而递归触发。Bot 第一票尚未使用且场上连续一个思考间隔无人发言、无人投票时，服务端会从合法玩家中随机提交首票，避免半数弃票风险；第二票仍留给模型确认或改票。预算不超过 60 秒或只剩最后一次机会时也强制采用合法非零票。
- 发言默认选择沉默，投票本身可作为表态；只有新增信息、有效推导、必要自保、被点名回应、临近截止拉票或必要干扰才发言。真人或 Bot 发言后约 3 秒再允许一个相关 Bot 接话，每个白天最多连续跟进 3 次；开场等待约 4 秒，连续 Bot 发言间隔约 1.5 秒。带发言的投票立即展示，沉默投票随机延迟约 0.8–4.5 秒，避免同时刷屏。
- 同时最多进行 4 个模型请求。任意类型的模型调用连续失败两次后，全局熔断 60 秒；熔断期间普通技能立即采用随机合法输入，投票对话沿用连续失败后的随机合法非零票回退，不再让每个 Bot 逐个等待同一故障。熔断期满只放行一个探测请求，成功后恢复，失败则重新熔断。模型失败不会制造固定占位发言。
- 投票请求一到达就立即向玩家展示，不等待仍在进行的 Bot 开场思考；尚未发出的开场发言会在投票开始时取消。投票开始后的首轮 Bot 思考在后台运行，不占用 CLI 输出路由锁，因此玩家投票及其公开广播不会等待模型响应。`/api/health` 的 `llmStats` 还提供四类失败计数及 `consecutiveFailures`、`circuitOpen`、`circuitOpenUntilUtc`、`circuitProbeInFlight`、`circuitSkipped`，不记录提示词、模型原始回答或 API Key。

Bot 拥有保持沉默和暂缓投票的权利。公开与该 Bot 私密事件先合并为一条严格按接收顺序编号的时间线，再截取近期事件；上下文较长时，服务端会让同一模型把更早的可见历史压缩为滚动摘要，摘要不会跨局复用。

## 日志

终局时服务端向 CLI 请求 `cli_log`，将每个白天的真人/Bot 聊天、首次投票和确认投票合并到对应的首条 internal 投票请求之后，再以 `game_log_available` 发给所有仍在房间的玩家。互动行采用 `名字: 消息`、`名字 投票给 目标`、`名字 确认投票给 目标` 格式。服务端不把日志写入固定服务器目录，持久化由客户端下载完成。Web 客户端在下载内容前添加 UTF-8 BOM，避免 Windows 编辑器把中文日志误判为本地代码页。

临时排错可在启动时添加 `--debug-api`，随后访问 `GET /api/rooms/{roomCode}/log`。进行中的下载内容按实际顺序包含 CLI 的每条原始 JSON 输出、服务端写入 CLI stdin 的 `debug_direction: "input"` 记录和已经产生的白天互动；因此即使 CLI 卡在 request、尚未生成 `cli_log`，也能看到最后一次请求和服务端是否实际提交了输入。终局后同一路径返回合并了互动记录的 CLI 正式日志。该接口无鉴权且可能暴露所有身份与私密消息，只应在可信网络临时启用。

### OpenCode 备用模型

配置 `LLM_FALLBACK_BASE_URL` 后，主模型与备用模型会同时接收同一份已脱敏的 Bot 可见上下文；服务端采用最先返回的有效结果。任一端点失败或被自身熔断时仍继续等待另一端点，双方独立统计和熔断。匿名端点无需设置 `LLM_FALLBACK_API_KEY`。

```dotenv
LLM_FALLBACK_BASE_URL=https://opencode.ai/zen/v1/
LLM_FALLBACK_MODEL=big-pickle
LLM_FALLBACK_TIMEOUT_SECONDS=15
LLM_FALLBACK_MAX_TOKENS=1024
```

对应命令行参数为 `--llm-fallback-endpoint`、`--llm-fallback-api-key`、`--llm-fallback-model`、`--llm-fallback-timeout-seconds` 和 `--llm-fallback-max-tokens`。`--disable-llm-fallback` 只关闭并行端点；`--disable-llm-bots` 同时关闭两个模型。健康接口的 `llmStats.fallbackAttempts` 表示并行竞速次数，`fallbackSuccesses` 表示备用模型赢得竞速的次数，`fallbackStats` 保存备用端点自身的独立统计和熔断状态。
