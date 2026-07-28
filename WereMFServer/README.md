# WereMFServer

WereMFServer 是 WereMF 的联网房间服务：托管静态 Web 客户端，通过 WebSocket 管理 7–16 人房间，并为每个正在进行的房间启动一个独立的 WereMF CLI/API 规则进程。服务端不重新实现游戏规则，只负责会话、权限、消息路由、计时和 Bot 托管。

## 构建与运行

从仓库根目录执行：

```powershell
cd .\WereMFWeb
npm run build
cd ..
dotnet publish .\WereMFServer\WereMFServer.csproj -c Release -r win-x64 --self-contained false
```

启动示例：

```powershell
.\WereMFServer\bin\Release\net8.0\win-x64\publish\WereMFServer.exe `
  --path .\WereMF\bin\Release\net8.0\win-x64\publish\WereMF.exe `
  --host 0.0.0.0 `
  --port 5000
```

浏览器访问 `http://localhost:5000`，WebSocket 使用同源 `/ws`。公网部署应由反向代理提供 HTTPS/WSS，并转发 `/ws`。

### 参数

| 参数 | 默认值 | 说明 |
|---|---:|---|
| `--path <path>` | `WereMF.exe` / `WereMF` | WereMF CLI 可执行文件 |
| `--host <host>` | `127.0.0.1` | HTTP 与 WebSocket 监听地址 |
| `--port <port>` | `5000` | HTTP 与 WebSocket 共用端口；`--websocket-port` 是兼容别名 |
| `--config <path>` | 无 | 传给 WereMF 的抽签配置 |
| `--seed <int>` | 随机 | 传给 WereMF 的固定种子 |
| `--request-timeout-seconds <n>` | `60` | 普通请求限时 |
| `--vote-seconds-per-alive <n>` | `60` | 投票阶段每名本轮可投票玩家提供的秒数 |
| `--vote-penalty-seconds <n>` | `30` | 每次有效投票后扣除的秒数 |
| `--event-interval-seconds <n>` | `2` | 连续公开消息的默认展示间隔；0 表示不延迟 |

`--http-port` 仅为旧命令行兼容参数，其值会被忽略；当前服务只使用 `--port`。

## HTTP API

| 路径 | 说明 |
|---|---|
| `GET /api/health` | `{ status, activeRooms, version }` 健康状态 |
| `GET /api/rooms` | 当前仍可加入的房间：`{ code, players, maxPlayers, started }[]` |
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
| `leave_room` | - | 彻底退出；大厅立即释放席位，进行中则由 Bot 接管至本局结束 |
| `game_input` | `value` | 提交 CLI 格式输入；服务端校验当前可提交玩家 |
| `pending_draft` | `skillId`, `api`, `value` | 保存尚未轮到提交的技能预选 |
| `ping` | - | 返回 `pong` |
| `command` | `value` | 旧客户端兼容；房主只允许 `\restart`，效果等同 `restart_room` |

昵称长度为 1–20 个字符；房间号固定为 6 位数字。

## WebSocket 服务端消息

| `type` | 主要字段 | 说明 |
|---|---|---|
| `welcome` | `roomCode`, `playerId`, `playerName`, `token`, `isHost` | 创建、加入或重连成功；客户端应保存令牌 |
| `room_state` | `roomCode`, `started`, `settings`, `bots`, `players` | 房间完整公开状态；`settings` 是本房间计时与消息间隔，玩家含在线、房主和 Bot 标记 |
| `session_state` | `playerId`, `isHost` | 编号重排或房主移交后的当前会话状态 |
| `player_remapped` | `playerId` | 匿名第一晚后，该浏览器实际使用的游戏编号 |
| `game_message` | `payload` | WereMF CLI API 消息，格式见 [`../WereMF/README.md`](../WereMF/README.md) |
| `input_accepted` | `api`, `remaining` | 并发输入已接受；`remaining` 是该玩家还可提交次数 |
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
- `pending_skill_created`：按 `source_player_id` 提前发给技能拥有者，允许先预选；CLI 仍按优先级轮流接受最终提交。
- `game_update_night`、`game_update_day`、`cli_game_summary`：逐玩家脱敏；其他玩家的身份不会发送到该浏览器。匿名玩家名也按当前接收者可见范围处理。

重连时会重放最多 250 条公开历史、该玩家的私密历史，以及房主专属历史（仅当前房主）。

## 计时器与 Bot

- 普通请求默认限时 60 秒。截止时若预选仍合法则优先采用；否则随机选择一个合法项并通知玩家。房主可在开局前覆盖本房间的默认值。
- 叶子选角的随机回退遵守角色限制：不能选粉侠和彩怪，并且不能只选择同一阵营。
- 投票总时限为“本轮 `can_vote: true` 的玩家数 × 本房间每人投票秒数”；死亡、被 myz 威胁或被禁票而无法参与本轮投票的玩家不计入，每接受一票扣除本房间设置的秒数；所有人完成后 CLI 会自然进入下一阶段，服务端不会再补输入。
- 投票超时且玩家一票未投时，服务端向 CLI 发送 `0`，即 CLI 的默认弃票。
- 永久 Bot 对请求立即选择随机合法输入。真人断线后若连续两轮请求未响应，会转为临时 Bot；使用原令牌重连后立即恢复真人控制。
- 进行中的对局关闭 Tab 只算临时断线，可以用原令牌重连。等待大厅或终局阶段一旦断线即视为彻底退出：服务端立即删除席位并令旧令牌失效；若房主断线，会从在线真人中随机选择新房主；最后一名真人离线后房间立即解散。

## 日志

终局时服务端向 CLI 请求 `cli_log`，再以 `game_log_available` 发给所有仍在房间的玩家。服务端不把日志写入固定服务器目录，持久化由客户端下载完成。Web 客户端在下载内容前添加 UTF-8 BOM，避免 Windows 编辑器把中文日志误判为本地代码页。
