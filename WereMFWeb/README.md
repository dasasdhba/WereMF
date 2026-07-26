# WereMF Web

WereMF Web 把稳定的 F# CLI 规则内核包装成一个 7–16 人在线房间游戏。浏览器不实现规则，只负责展示状态和提交选择；所有结算仍由 `WereMF` 完成。

## 产品结构

- **进入页**：创建房间或输入 6 位房间号加入，可连接自托管服务器。
- **候场室**：显示座位、在线状态与房主；满 7 人后可以开局。
- **游戏桌**：阶段、个人身份、在场玩家、状态物件、当前行动和事件记录集中在一个界面。
- **行动面板**：普通技能点选玩家即可；多目标、二段选择、身份选择、投票和特殊后缀都有可视控件，同时保留 CLI 原始输入作为兜底。
- **隐私边界**：`public` 广播给全房；`player_X` 只发给对应座位；`internal` 只发给房主。完整 Game 快照会在服务端按座位脱敏，其他玩家的 `role` 不会进入浏览器。

## 运行

先构建规则程序与服务：

```powershell
dotnet build .\WereMF.sln
```

构建前端（会同步到服务端 `wwwroot`）：

```powershell
cd .\WereMFWeb
npm run build
```

从仓库根目录启动：

```powershell
.\WereMFServer\bin\Debug\net8.0\WereMFServer.exe --path .\WereMF\bin\Debug\net8.0\WereMF.exe --host 0.0.0.0 --port 5000
```

访问 `http://localhost:5000`。公网部署时应由反向代理提供 HTTPS/WSS，并把 WebSocket 的 `/ws` 路径转发到同一服务。

## 客户端协议

连接 `/ws` 后第一条消息必须是：

- `create_room`：`{ type, playerName }`
- `join_room`：`{ type, roomCode, playerName }`
- `reconnect`：`{ type, roomCode, playerName, token }`

房主发送 `start_game` 后，服务端按候场顺序把昵称提交给 CLI。行动使用 `{ type: "game_input", value: "..." }`；服务端只接受当前 `message_type` 对应座位的输入。

## 验证

`node scripts/smoke.mjs` 会模拟 7 个 WebSocket 客户端，并确认每个座位只收到一条自己的身份消息。脚本默认连接 `ws://127.0.0.1:5055/ws`。

### 真实长对局回放

除了 `test/web_leaf8_full.mjs` 的固定种子 8 人叶子局，还应把下列目录中的真实长对局日志作为复杂输入回归数据源：

```text
WereMF/bin/Release/net8.0/win-x64/publish/WereMF_*.log
```

这些日志同时记录请求文本和紧随其后的玩家原始输入，适合覆盖重抽、非零技能、多目标技能、鲜奶/毒奶、MFA、叶子复制身份等自动机器人默认放弃路径没有覆盖的分支。回放时应使用日志首行种子、原始玩家输入顺序、匿名/叶子局选项，并按请求顺序从 Web 对应座位提交输入。
