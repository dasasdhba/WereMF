# WereMF 修改与重构计划

> 状态：第一阶段核心修复、第二阶段测试保护网、第四阶段 Web 渐进拆分和第五阶段文档清理已完成；第三阶段仍保留 Channel 并发方案与更深的 `GameRoom`/`RouteAsync` 拆分评估
> 原则：先修复夜间信息暴露，再渐进重构 Server/Web；F# CLI 规则内核保持稳定，避免大范围改动。

## 1. 目标与约束

### 目标

- 修复灰卡比在夜间结算后发送完整 `GameContext`，从而携带其他夜间技能状态的问题。
- 灰卡比只向外发送“本次烟雾动作及其连锁结算造成的公开变化”。
- 保持现有 F# CLI 的规则顺序、状态结构、输入流程、日志和撤销/重做行为不变。
- 在修复稳定后，降低 `WereMFServer/GameServer.cs` 和 `WereMFWeb/src/app.js` 的复杂度。
- 建立可以持续验证匿名信息、私密身份、夜间状态、重连、预提交和 Bot 行为的测试边界。

### 明确的非目标

- [ ] 不重写 F# 游戏状态机、角色实现或技能优先级系统。
- [ ] 不进行通用的 F# API v2、全量 DTO 或按玩家投影系统改造。
- [ ] 不把 F# 项目改造成供 Server 直接引用的类库；继续把 CLI 进程视为稳定规则引擎。
- [ ] 不在本轮重构中更换 Web 技术栈或引入 React/Vue 等框架。
- [ ] 不在 Server/Web 中复制角色规则或技能结算规则。
- [ ] 不以增加更多 `showXxx` 参数或 Server 字段黑名单作为长期修复。

## 2. 当前问题基线

### 夜间状态暴露

- `WereMF/Skill/HuiKa.fs` 的 `HuiKaSkill.ExecuteQueued` 在烟雾及其连锁死亡处理后发送完整 `GameUpdateNight`。
- 此时高于灰卡比优先级的技能已经执行。完整快照会包含这些技能写入 `GameContext` 的状态。
- `4394740` 通过 `game.ToJsonValue false` 单独隐藏了 `myz_threaten`，但没有消除完整快照本身的暴露风险。
- `WereMFServer/GameServer.cs::RedactedEnvelope` 只处理身份、匿名名称和吧主标记，不负责理解所有角色状态的夜间可见性。
- `WereMFWeb/src/app.js::handleGameMessage` 将 `game_update_night` 当成完整实体快照覆盖本地状态。

### Server 复杂度

- `WereMFServer/GameServer.cs` 当前约 2000 行。
- `GameRoom` 同时承担房间生命周期、WebSocket 路由、CLI 协议、输入协调、预提交、计时器、Bot、LLM、历史、日志、脱敏和重连。
- `_gate`、`_routeLock`、后台 Bot 任务和计时器共同修改房间状态，后续功能继续增加时回归风险较高。

### Web 复杂度

- `WereMFWeb/src/app.js` 当前约 700 行。
- 单文件同时承担连接、协议处理、状态存储、输入规则、渲染、音频、通知和 DOM 绑定。
- `handleGameMessage` 既解释协议又直接修改 UI 状态，不利于单元测试和新增增量消息。

### 测试现状

- 已有真实长对局日志、WebSocket smoke test、预提交/聊天/房间生命周期脚本和假 CLI。
- solution 中没有独立的正式测试项目；不少验证仍依赖手工启动服务或脚本约定。
- 缺少专门验证“隐藏夜间行动不能改变公开观察结果”的信息隔离测试。

## 3. 第一阶段：灰卡比最小修复（最高优先级）

### 3.1 协议设计

新增一个仅用于夜间公开增量的 API，暂定：

```json
{
  "api": "game_update_night_patch",
  "message_type": "public",
  "message_content": "",
  "data": {
    "cause": "huika_smog",
    "entities": [
      {
        "player_id": 3,
        "state": {
          "smog_count": 1
        }
      }
    ]
  }
}
```

约束：

- `entities` 只包含本次烟雾结算前后公开状态发生变化的玩家。
- `state` 只包含该玩家实际发生变化的公开字段。
- 不包含 `role`、完整 `player`、完整 `EntityState` 或完整 `GameContext`。
- 不发送未变化字段；因此先前由 myz、Doge、卡比或其他技能形成但未被本次烟雾修改的状态不会进入消息。
- `cause` 固定为 `huika_smog`，用于日志、Bot 上下文和未来排错，不设计通用事件框架。
- 现有 `huika_smog_broadcast`、`huika_smog_kill_broadcast` 等演出消息保留，避免改变 CLI 文本日志和表现顺序。

### 3.2 F# 修改

涉及文件预计仅限：

- `WereMF/Module/Api.fs`
- `WereMF/Common/Entity.fs` 或一个靠近 Entity 的小型辅助函数
- `WereMF/Skill/HuiKa.fs`
- `WereMF/README.md`

任务：

- [x] 在 `ApiType` 增加 `GameUpdateNightPatch`，保持现有 API 名称映射风格。
- [x] 在灰卡比真正修改目标前保存 `beforeGame`/`beforeEntities`。
- [x] 完成 `addSmog`、窒息、`requestDead`、Doge 牵连、叶子阻挡/保护和可能的复活结算后取得 `afterGame`。
- [x] 对每名玩家比较结算前后的“公开 EntityState JSON”。
- [x] 只把值发生变化的 JSON 字段加入 patch；没有变化的字段不得出现。
- [x] patch 的实体键只使用 `player_id`，不得复用包含身份数据的 `Entity.ToJsonValue`。
- [x] 如果本次动作最终没有产生任何公开状态变化，则不发送空 patch。
- [x] 用 `GameUpdateNightPatch` 替换 `HuiKaSkill.ExecuteQueued` 中的完整 `GameUpdateNight`。
- [x] 保留夜晚开始处原有 `GameUpdateNight`，本阶段不改变通用阶段快照语义。
- [x] 保留 `showMyzThreaten=false` 兼容行为；确认新 patch 稳定后再单独判断是否值得清理，不能把清理混入本修复。
- [x] 更新 F# README，明确 patch 是字段级增量，不能被消费者当成完整快照。

实现注意：

- 比较基准必须在单次 `HuiKaSkill.ExecuteQueued` 内部、紧邻烟雾修改前取得，避免把更早技能的变化归因于灰卡比。
- 比较的是 `EntityState.ToJsonValue false` 的字段值，而不是底层 F# 记录；这样 patch 与现有 Web 公共字段名保持一致。
- JSON diff 只需要处理当前扁平 `state` record，不引入通用递归 JSON Patch 库。
- 同一夜多个烟雾依次结算时，每次分别生成自己的 patch，保持现有消息顺序。
- 不从 Web 端根据中文 `message_content` 反推玩家 ID 或状态。

### 3.3 Server 适配

涉及文件预计：

- `WereMFServer/GameServer.cs`（第一阶段先做最小修改）
- `WereMFServer/README.md`

任务：

- [x] 将 `game_update_night_patch` 作为已脱敏的公开消息直接广播和记录，不进入 `RedactedEnvelope`。
- [x] 明确拒绝/忽略包含 `role`、`player` 或非对象 `state` 的非法 night patch，避免未来误用再次扩大数据范围。
- [x] 为 patch 增加轻量结构校验：`cause == huika_smog`、合法 `player_id`、合法字段名、合法值类型。
- [x] 允许的字段名以当前 `EntityState` 公共 JSON 字段为准，但消息中只接受 F# 实际发送的变化字段。
- [x] 重连历史中保存并按原顺序重放 patch。
- [x] Bot 上下文把“最近完整 night/day 状态 + 其后的 night patch”共同标记为当前权威状态。
- [x] 后来的 patch 覆盖较早的同一玩家/同一字段；不得把 patch 当作完整状态并清除未出现字段。
- [x] 更新 Server README 的 CLI 路由和隐私说明。

### 3.4 Web 适配

涉及文件预计：

- `WereMFWeb/src/app.js`
- `WereMFWeb/README.md`

任务：

- [x] 新增 `applyEntityStatePatch(data)` 纯函数或近似纯函数。
- [x] 按 `player_id` 查找已有 entity，只合并 `state` 中出现的字段。
- [x] 不替换 `state.entities` 数组，不重建身份数据，不清除未出现字段。
- [x] 收到未知玩家或非法字段时忽略该项并给开发日志提示，不能破坏整个消息循环。
- [x] 在 `handleGameMessage` 中处理 `game_update_night_patch`。
- [x] 确认玩家卡片上的烟雾、死亡、公开死亡名、叶子保护以及其他由烟雾连锁改变的公开状态立即刷新。
- [x] 确认原有 `game_update_night/day` 完整快照行为保持不变。
- [x] 更新 Web README。

### 3.5 第一阶段测试矩阵

F#/协议：

- [ ] 单烟：patch 只包含目标的 `smog_count`。
- [ ] 双烟窒息：包含烟雾数和实际公开死亡字段。
- [ ] 烟雾被 Doge 阻挡：不发送状态 patch。
- [ ] 窒息触发 Doge 牵连：patch 包含所有被连锁结算且公开状态发生变化的玩家。
- [ ] 窒息触发叶子第一阶段保护/公开：只包含实际变化的公开状态字段，身份公布仍由既有事件承担。
- [ ] 窒息触发 CTF/粉侠/彩怪等既有复活或防死逻辑：最终 patch 与结算后的公开状态一致。
- [ ] 第一晚两个烟雾：两个 patch 顺序与现有技能执行顺序一致。
- [ ] myz 在灰卡比之前成功威胁：patch 中不存在 `myz_threaten`，除非烟雾动作本身确实修改该字段。
- [ ] 改变 myz 隐藏目标而保持灰卡比输入相同，两局的公开烟雾 patch 必须相同。
- [ ] patch 中永远不存在 `role`、`summary_name`、角色 `data`、`PaoXianParty` 等私密结构。

Server/Web：

- [ ] 所有在线玩家恰好收到一次同样的公开 patch。
- [ ] patch 不进入 `RedactedEnvelope`，也不因接收者不同而产生内容差异。
- [x] 断线重连按顺序重放完整快照和后续 patch，最终 UI 状态与未断线客户端一致。
- [ ] Bot 的权威状态能看到最新烟雾计数和烟雾导致的公开死亡结果。
- [x] Web 对单字段 patch 使用合并语义，已有身份和其他状态不丢失。
- [x] 现有三份真实长对局日志回放结果不变。
- [x] 现有 smoke、预提交、聊天、房间生命周期测试全部通过。

### 3.6 第一阶段完成标准

- [x] `HuiKaSkill.ExecuteQueued` 不再发送完整 `GameContext`。
- [x] 夜间烟雾 UI 能即时更新。
- [x] 任何与本次烟雾无关的夜间状态都不能通过 patch 被观察到。
- [x] CLI 文本日志、技能顺序和随机数消费顺序不变。
- [x] Server/Web README 与 F# README 对 patch 语义描述一致。
- [x] 对应回归测试可在无人值守环境中重复运行。

## 4. 第二阶段：建立重构保护网

本阶段不改变功能，先把已有脚本整理成稳定入口。

- [x] 新建 `WereMFServer.Tests` 测试项目并加入 solution。
- [x] 使用假 CLI 覆盖消息路由、玩家权限、重连历史、普通请求和并发请求。
- [x] 把 `WereMFWeb.Tests/pre-submit-fake` 的关键场景转成自动断言，而不是只依赖日志观察。
- [x] 为 `RedactedEnvelope` 增加按接收者的快照测试。
- [x] 为 night patch 增加非法字段和非法类型测试。
- [x] 为 Web 提取最小可测试入口，使用 Node 内置 test runner，避免新增运行时依赖。
- [x] 将真实日志回放统一为一个命令，输出明确的通过/失败状态。
- [x] 在根 README 或 TODO 中记录标准验证命令及所需环境。
- [x] 区分“无需外部模型的确定性测试”和“需要 LLM/网络的人工或扩展测试”。

当前确定性入口：`node WereMFWeb.Tests/run-deterministic.mjs`；`--replay` 扩展模式会启动真实 F# CLI 回放历史日志，并在报告中区分 `exact` 与兼容回退结果。需要真实 LLM/网络的脚本仍属于扩展回归测试。

完成标准：

- [x] 不启动真实 LLM 即可覆盖规则进程、Server 和 Web 的主要协议链路。
- [x] 后续每次文件拆分都能用同一组测试验证行为等价。

## 5. 第三阶段：Server 渐进拆分

原则：先移动边界清晰、规则无关的代码；每一步保持 `GameRoom` 外部行为不变。不要一次性重写房间模型。

### 5.1 CLI 协议与路由

- [x] 提取 `CliEnvelope`/协议读取辅助类型，集中处理 `api`、`message_type`、`message_content` 和 `data`。
- [x] 提取 `CliMessageRouter`：分类 public/player/internal、request、snapshot、night patch。
- [x] 将匿名映射、pending skill 定向和快照脱敏移入 `CliRouteTransforms` 路由组件。
- [x] 保留 `GameProcess` 为独立进程适配器，不让它理解房间或 WebSocket。
- [x] 为匿名映射、pending 定向和按接收者脱敏建立独立测试；`RouteAsync` 只保留薄委派，具体 payload 转换已移出。

### 5.2 输入协调

- [x] 提取 `RegularInputCoordinator`：当前请求、目标玩家、计时器、超时回退。
- [x] 提取 `ConcurrentInputCoordinator`：重抽、投票、剩余次数和 CLI 队列。
- [x] 提取 `PendingDraftStore`：草稿、预提交、myz 取消和合法性复核。
- [x] 保持合法性最终由 CLI 决定；Server 校验只用于权限、格式和超时候选保护。
- [x] 避免三个组件分别持有重复的“当前请求”状态。

### 5.3 历史、重连和日志

- [x] 提取 `RoomHistory`，统一 public、player、host 历史和 Bot 时间线的写入顺序。
- [x] 把 250 条重连限制、4000 条 Bot 时间线限制集中为命名配置。
- [x] 提取 `GameLogAssembler`，负责 CLI 日志和白天互动合并。
- [x] 用序列号保证公开消息、私密消息和 patch 的确定顺序。

### 5.4 Bot/LLM

- [x] 提取 `BotCoordinator`，负责普通技能 Bot 候选、模型校验和合法回退；永久 Bot、临时接管和投票调度仍由 `GameRoom` 协调。
- [x] 提取 `BotVisibleContextBuilder`，集中处理完整快照与后续 night patch。
- [x] `LlmBotClient` 只负责模型调用、超时、结果解析和熔断，不访问房间可变状态。
- [x] 所有 Bot 候选输入继续通过与真人/超时相同的合法性检查。

### 5.5 房间状态所有权

- [x] 在上述组件拆分稳定后评估 `Channel<RoomCommand>`；当前结论是保留已有 `_routeLock` + `_gate` 边界，暂不引入会改变时序的 actor/channel 改造。
- [ ] 如果采用 Channel，先覆盖 CLI 输出、玩家输入、超时和断线同时发生的测试。
- [ ] 迁移后只允许一个执行上下文修改房间核心状态；网络发送可在状态提交后并发执行。
- [x] 不在同一 PR 中同时拆组件和改并发模型；Channel 方案留作后续独立变更。

Server 完成标准：

- [ ] `GameRoom` 主要负责协调，不再实现所有协议、Bot 和日志细节。
- [ ] `RouteAsync` 只做解析、分类和委派。
- [ ] 房间核心可变状态有明确唯一所有者。
- [ ] 无角色规则被搬入 C#。
- [x] 全部协议回归测试和真实日志回放通过。

## 6. 第四阶段：Web 渐进拆分

保持零框架和现有构建方式，优先提取纯逻辑。

建议模块：

```text
WereMFWeb/src/
  app.js
  protocol.js
  store.js
  reducers/game-message.js
  reducers/server-message.js
  input/selection.js
  input/pending-drafts.js
  views/room.js
  views/game.js
  views/action-panel.js
  effects/audio.js
  effects/notifications.js
  socket.js
```

任务：

- [x] 先提取 `applyFullEntitySnapshot` 和 `applyEntityStatePatch`，建立 reducer 测试。
- [x] 将 `handleGameMessage` 改成协议归一化 + reducer 分发。
- [x] 将选择数量、非法目标、叶子选角和 modifier 处理提取到 `input/selection.js`。
- [x] 将 pending、draft、pre-submit、myz 取消提取到 `input/pending-drafts.js`。
- [x] 将 WebSocket 连接、重连和发送封装到 `socket.js`。
- [x] 将音频、通知和标题闪烁作为 reducer 结果触发的 effects，不直接改变游戏状态。
- [x] 按 landing/room/game/action panel 拆渲染函数，但保持生成的 HTML 和 CSS 类名不变。
- [x] 每完成一次提取，运行日志回放并比较最终 state 和关键 DOM 文本。

Web 完成标准：

- [x] 协议消息可以在没有浏览器 DOM 的情况下进行 reducer 单元测试。
- [x] 完整快照与字段级 patch 有不同且明确的处理函数。
- [x] `app.js` 只保留应用装配、启动和少量顶层协调。
- [x] 不改变现有页面行为、移动端布局、音效和通知语义。

本轮补全的 Web 测试包括：协议归一化、完整快照与夜间 patch 合并、非法字段过滤、选择规则、叶子选角、modifier、pending 草稿，以及基于浏览器桩的事件队列、UI 渲染和真实日志回放检查。统一验证入口为 `node WereMFWeb.Tests/run-deterministic.mjs`；Web 模块单测入口为 `npm test`。

## 7. 第五阶段：文档与清理

- [x] 更新三个子项目 README 中的职责边界图。
- [x] 在 F# README 中列出完整快照 API 与增量 patch API 的区别。
- [x] 在 Server README 中记录每类 CLI 消息的路由、历史和重连规则。
- [x] 在 Web README 中记录 reducer、effects 和 patch 合并语义。
- [x] 审核 `showMyzThreaten`：已证明 night patch 使用 `ToJsonValue false`，而完整快照仍需要按接收者隐藏 myz 威胁，因此保留该参数，不删除必要的隐私兼容逻辑。
- [x] 所有旧兼容调用点和回放测试已核对；没有删除仍被 `command`、完整快照隐私或重连流程使用的兼容代码。
- [x] 临时诊断输出未纳入版本控制；三份 `.log` 仅保留在 `WereMFWeb/fixtures/logs/` 可重复测试资产目录。

## 8. 推荐提交/PR 顺序

1. `test: characterize huika night visibility`
2. `cli: emit huika public state patch`
3. `server: route and validate night state patch`
4. `web: merge night state patch`
5. `test: formalize protocol and replay coverage`
6. `server: extract cli routing`
7. `server: extract input coordination`
8. `server: extract history and bot coordination`
9. `web: extract reducers and protocol modules`
10. `web: split views and effects`
11. `docs: finalize architecture and remove compatibility code`

每个提交必须可独立构建和验证；灰卡比修复不得等待后续 Server/Web 重构完成。

## 9. 总体验收清单

- [x] 灰卡比夜间不再发送完整游戏状态。
- [x] myz 或未来高优先级技能的隐藏状态不会因烟雾 UI 更新而暴露。
- [x] 烟雾计数及烟雾直接造成的公开连锁结果能即时显示。
- [x] F# CLI 的规则行为、随机数消费、输入格式和现有日志保持兼容。
- [x] Server 仍是规则无关的进程协调和联网层。
- [x] Web 仍是展示和输入组织层，不自行推导结算结果。
- [x] 匿名、重连、预提交、投票、Bot、LLM 回退和日志下载均通过回归测试。
- [x] 真实长对局回放结果与重构前一致。

## 10. LLM Bot 状态与发言策略修复

## Work

- [x] 为 Bot 发言引入阶段内权威状态门禁和异步结果版本校验。
  - Scope: `WereMFServer/GameServer.cs`, `WereMFServer/BotVisibleContextBuilder.cs`，以及相关状态模型。
  - Outcome: 新白天未收到本阶段 `game_update_day` 时不得沿用上一阶段快照；LLM 请求期间若权威状态或阶段变化，旧结果不得广播，并可在仍合法时用新上下文至多重试一次；有效状态增量也必须使旧结果失效。
  - Accept: 新增确定性回归测试，覆盖“新白天快照未就绪”和“LLM 返回前状态更新”两种情况；相关测试通过。

- [x] 强化高价值局部信息的主动公开策略，同时避免无信息刷屏。
  - Depends on: 状态门禁完成，确保主动公开不会放大过期信息。
  - Scope: `WereMFServer/LlmBotClient.cs`, `WereMFServer/BotGameKnowledge.cs`, `WereMFServer/GameServer.cs`，必要时扩展发言上下文/决策结构。
  - Outcome: prompt 明确要求：从自身可见的私密身份、状态、技能结果或事件中能推导出有价值的非公开信息，且公开有利时必须主动公开；脚滑人等信息特化角色具有更强优先级。直接点名/明确提问以及纯 Bot 开场全体沉默时有单一短发言兜底，普通无信息场景仍可沉默。
  - Accept: fake LLM/上下文测试能观察到上述强制规则；全体返回 silent 时至多指定一名 Bot 发言，不产生群体刷屏。

- [x] 增加面向实际广播结果的发言观测，区分模型沉默与玩家看到的全体沉默。
  - Scope: Bot 调度统计与 `/api/health` 的聚合统计，不记录 prompt、回答正文或隐藏状态。
  - Outcome: 至少可观测触发次数、实际广播数、全体沉默数、过期结果丢弃数和状态变化重试数；保持原统计字段兼容。
  - Accept: 自动化测试断言新统计随确定性场景正确变化，且健康接口不包含敏感正文。

## Final verification

- [x] Run `dotnet build WereMF.sln` and record the result.
- [x] Run `dotnet run --project WereMFServer.Tests/WereMFServer.Tests.csproj` and record the result.
- [x] Run the relevant WereMFWeb deterministic/LLM Bot regression command selected after inspecting the test harness, and record its exact command and result.
- [x] Inspect `git diff --check` and the final diff for unrelated edits.

## Progress log

- 2026-08-09: TODO created from the read-only diagnosis. Repository was clean at diagnosis time; preserve any later unrelated user changes.
- 2026-08-09: 完成阶段内权威状态门禁与异步结果版本校验；`dotnet run --project WereMFServer.Tests/WereMFServer.Tests.csproj --no-restore` 通过（11/11）；`dotnet build WereMF.sln --no-restore` 通过（4 projects，0 errors，0 warnings）；`git diff --check` 通过。
- 2026-08-09: 复核并修正首项的断线临时托管漏洞：`game_update_day` 现在为所有未离席会话构建并写入按玩家脱敏快照；`SendAsync` 仍仅向已连接且 socket 打开的会话发送，因此不改变网络投递或隐私边界。门禁保持全局：其就绪前，全部潜在 Bot 决策者均已有当前阶段快照；已离席席位不会再参与 Bot 决策，故不保留新私密状态。新增确定性用例覆盖断线且 `IsBot=true` 的临时托管席位以及已离席席位排除；`dotnet run --project WereMFServer.Tests/WereMFServer.Tests.csproj --no-restore` 通过（12/12），`dotnet build WereMF.sln --no-restore` 通过（4 projects，0 errors，0 warnings），`git diff --check` 通过，首项验收保持完成。
- 2026-08-09: 完成高价值局部信息主动公开策略：新增 `valuable_private_information` intent，明确合法私密局部视角得出且公开有利的结论必须以最小身份暴露主动公开；脚滑人焦点有更强规则。直接点名改为必答短回应；纯 Bot 白天开场全体沉默时至多指定一名 Bot 作 `information_probe`，普通可选触发仍可沉默。补正并发调度：按完成顺序收集结果后，先为 `Required`/点名 Bot 选择保留的发言额度，再以完成顺序填充剩余额度，仍保持总额度上限；新增确定性用例模拟两个可选结果先于点名结果完成。纯 Bot 全沉默 fallback 已复核：只在全部初始结果沉默时选取一名 Bot，且不会追加到已有发言之后。`dotnet run --project WereMFServer.Tests/WereMFServer.Tests.csproj --no-restore` 通过（16/16）；`dotnet build WereMF.sln --no-restore` 通过（4 projects，0 errors，0 warnings）；`git diff --check` 通过，本项验收保持完成。
- 2026-08-09: 完成实际广播编排观测：`/api/health` 的既有 `llmStats` 字段及 `fallbackStats` 保持兼容，并新增仅含数值的 `conversationStats`（`triggers`、`chatBroadcasts`、`allSilentTriggers`、`staleSpeechDiscards`、`stateChangeRetries` 及两项比率）。开场和每次 reply/vote 调度各只计一个外部触发；纯 Bot 全沉默 fallback 复用同一触发；仅成功写入并广播的 Bot 聊天计入实际广播。状态版本门禁的丢弃、有限重试及最终广播状态版本拒绝分别计数。确定性测试精确断言聚合值、旧字段和序列化白名单；fake LLM 回归检查健康字段形状且不依赖特定时序数值。`dotnet run --project WereMFServer.Tests/WereMFServer.Tests.csproj --no-restore` 通过（17/17）；`dotnet build WereMF.sln --no-restore` 通过（4 projects，0 errors，0 warnings）；`dotnet build WereMF.sln --configuration Release --no-restore` 通过（4 projects，0 errors，0 warnings）；`node WereMFWeb.Tests/llm_bot_game.mjs` 通过；`git diff --check` 通过。
- 2026-08-09: 主代理最终独立验收：`dotnet build WereMF.sln` 通过（4 projects，0 warnings/errors）；`dotnet run --project WereMFServer.Tests/WereMFServer.Tests.csproj` 通过（17/17）；`node WereMFWeb.Tests/run-deterministic.mjs` 全部通过；`node WereMFWeb.Tests/llm_bot_game.mjs` 完整对局通过（7 triggers、6 broadcasts、0 all-silent、0 privacy violations）；`git diff --check` 通过。最终审查范围为第 10 节对应的 7 个已修改文件及 4 个新增内部组件，无无关改动。
