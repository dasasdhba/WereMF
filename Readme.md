# WereMF

MF 杀是由 [Mario Forever 社区](https://www.marioforever.net) 的大爷、xfx 等众多玩家共同设计的类狼人杀游戏
本项目是该游戏的机器实现

## CLI 参数

* `--help`: 显示帮助
* `--api`: API 模式
* `--config <path>`: 使用自定义的抽签配置文件
* `--seed <int>`：使用自定义的种子

## 游戏内辅助功能

* `\undo`: 撤销
* `\redo`: 重做
* `\restart`: 重开一局（使用当前玩家列表）
* `\reboot`: 重启程序
* `\exit`: 退出
* `\log`: 保存当前对局作为日志
* `\rename <idx> <newName>`: 将第 `idx` 个玩家重命名为 `newName`，这里的 `idx` 以输入玩家列表的顺序为准，不以第一晚匿名模式打乱的顺序为准
* `\night`: 打印晚上的总结
* `\day`: 打印白天的总结
* `\vote`: 打印投票的总结
* `\summary`: 打印对局结束的总结

---

以下为 API 模式的文档 

## 1. Common 数据结构

### 1.1 CharaType (角色类型)

```json
"脚滑人" | "Doge" | "庸医" | "地鼠" | "兔子" | "铯郎" | "法猫" | "卡比" | "粉侠" | "爬行者" | "炮仙" | "实物" | "灰卡比" | "音魔" | "CTF" | "合虫" | "彩怪" | "贤松" | "江仙" | "myz" | "叶子"
```

阵营对应关系：

- **吧方**: 脚滑人, Doge, 庸医, 地鼠, 兔子, 铯郎, 法猫, 卡比, 粉侠, 爬行者
- **爆方**: 炮仙, 实物, 灰卡比, 音魔, CTF, 合虫, 彩怪, 贤松, 江仙, myz
- **叶子**: 叶子

### 1.2 Player (玩家)

```json
{
    "id": 1,
    "name": "玩家名",
    "anonymous": false // 用于第一晚匿名
}
```

### 1.3 Role (身份)

```json
{
    "role": {
        "chara_type": "脚滑人", // 身份名
        "summary_name": "脚滑人", // 对局结束时显示的身份名，可能与 `chara_type` 不同，如「粉侠（炮仙）」等
        "data": { ... }
    },
}
```

---

### 1.4 各身份 data 结构

#### JiaoHua

```json
null
```

#### Doge

```json
{
    "last_selected": { "tonight": [1], "last_night": [] },
    "self_selected": false
}
```

#### Doctor

```json
{
    "capsule": 3
}
```

#### Mole

```json
{
    "red_ground": false,
    "ground_pool": [0, 0, 1, 1, 1, 2] // 0: 花岗岩；1：土地；2：红土地
}
```

#### Rabi
```json
{
    "round": 1
}
```

#### SheLang

```json
{
    "last_selected": { "tonight": [], "last_night": [1] }
}
```

#### FaMao

```json
{
    "first_round": false
}
```

#### Kirby

```json
{
    "copied_role": { // 可能为 null
        "chara_type": "Doge", 
        "data": { ... } 
    }
}
```

#### FenXia

```json
{
    "fen_count": 3,
    "copied_roles": [
        { "chara_type": "Doge", "data": { ... } },
        ...
    ]
}
```

#### Creeper

```json
{
    "bomb_count": 3,
    "placed_list": [1, 2]
}
```

#### PaoXian

```json
null
```

#### ShiWu

```json
{
    "last_selected": { "tonight": [], "last_night": [] },
    "broadcasted": false // 播报身份技能
}
```

#### HuiKa

```json
{
    "first_round": false
}
```

#### YinMo

```json
{
    "disc_count": 3,
    "disabled": false // 技能冷却
}
```

#### CTF

```json
{
    "bug_count": 3,
    "reborn": false // 是否复活过
}
```

#### HeChong

```json
{
    "copied_role": { // 可能为 null
        "chara_type": "Doge", 
        "data": { ... } 
    }
}
```

#### CaiMon

```json
{
    "cai_count": 3,
    "reborn_list": [1, 2] // 已复活的玩家列表
}
```

#### XianSong

```json
{
    "mfa_list": [1, 2], // 已获得 mfa 的玩家列表
    "can_reborn": true, // 是否可以复活
    "can_force_choice": false, // 复活后，是否使用过强制选项
    "disabled": false // 技能是否被禁用（虫子）
}
```

#### JiangXian

```json
{
    "dead_voted": false // 是否使用了死亡投票
}
```

#### Myz

```json
{
    "revealed": false // 是否自爆了身份
}
```

#### Leaf

```json
{
    "fury": false, // 是否为二阶段
    "roles": [ // 只会包含当前阶段的 role
        { "chara_type": "炮仙", "data": { ... } }
    ]
}
```

### 1.5 EntityState (玩家状态)

```json
{
    "is_bar_leader": false, // 是否为吧主
    "is_dead": false, // 是否死亡（实际状态）
    "is_dead_public": false, // 是否死亡（显示状态，针对彩怪复活，但仍然显示为死亡的情况）
    "dead_showing_name": "", // 死亡后显示的名称
    "reversed": false, // 是否被法猫反转
    "smog_count": 0,
    "capsule_count": 0,
    "potion_count": 0,
    "xian_song_count": 0,
    "bug_count": 0,
    "myz_threaten": false, // myz 威胁（白天无法行动）
    "jiaohua_vote_blocked": false, // 脚滑人禁票
    "jiaohua_protected": false, // 被脚滑人保护
    "jiaohua_blocked": 0, // 被脚滑人封技能
    "leaf_protected": false // 叶子保护
}
```

### 1.6 Entity (玩家实体)

```json
{
    "player": { ... },
    "role": { ... },
    "state": { ... }
}
```

### 1.7 PendingSkill (即将处理的技能)

`PendingSkill` 会在晚上开始时全部提交给前端，届时前端可以平行处理玩家输入；但是，`cli` 会严格按照优先级请求输入，届时前端需要根据 `id` 判断当前应该输入哪位玩家的技能

```json
{
    "id": "0123456789abcdef0123456789abcdef",
    "type": "Doge",
    "source_player_id": 1,
    "priority": 10, // 前端无需管这个
    "kidnapped": false, // 是否被绑架禁用
    "threaten": { // 是否被威胁
        "target": 2, // 威胁目标
        "force": false // 是否为强制威胁
    }
}
```

---

## 2. State 数据结构

### 2.1 Roll (抽签结果)

```json
{
    "roll_pairs": [
        { "player_id": 1, "chara_type": "Doge", "reset": false } // 玩家，身份， 是否已重抽
        ...
    ],
    "leaf_charas": ["炮仙", "音魔", "地鼠", "铯郎"] // 叶子的四个身份，首位为第一身份
}
```

### 2.2 Day (白天上下文)

```json
{
    "votes": [ // 投票情况
        {
            "id": 1, // 玩家 id
            "target": 2, // 投票值，0 代表弃票，null 代表没有投票
            "confirmed": false // 是否投票两次
        }
    ]
}
```

### 2.3 Game (游戏上下文)

```json
{
    "entities": [...] // 全体玩家实体
}
```

### 2.4 Main (主上下文)

```json
{
    "players": [...], // 玩家列表
    "roll": { ... } // 抽签结果
}
```

---

## 3. 消息API

### 3.1 Message 格式

```json
{
    "api": "api_name",
    "message_type": "public" | "internal" | "player_X",
    "message_content": "消息内容",
    "data": { ... }
}
```

若无特殊说明，带 `broadcast` 的 API 通常为 `public`，带 `notify` 的通常为 `player_X`，其余的通常为 `internal`

### 3.2 API 列表

#### 客户端命令类

| API | 说明 |
|-----|------|
| `command_error` | `\rename` 等 cli 命令错误提示 |
| `roll_init_error` | 身份池初始化错误信息 |
| `roll_bar_not_enough` | 身份池的吧方角色不足 |
| `roll_bar_error` | 身份池的吧方错误信息 |
| `roll_boom_not_enough` | 身份池的爆方角色不足 |
| `roll_boom_error` | 身份池的爆方错误信息 |
| `cli_night_summary` | 夜晚总结 |
| `cli_day_summary` | 白天总结 |
| `cli_game_seed` | 游戏种子 |

#### 玩家状态类

| API | 说明 | 举例 |
|-----|------|------|
| `player_dead_broadcast` | 玩家死亡广播 | xxx 出局 |
| `player_dead_reborn_broadcast` | 玩家复活广播 | 但是 xxx 复活了 |
| `player_dead_reveal_broadcast` | 玩家身份公开广播 | xxx 是炮仙 |
| `player_reborn_notify` | 复活通知（彩怪） | 你复活了 |
| `leaf_dead_p1_broadcast` | 叶子死亡 p1 | xxx 是叶子 |
| `leaf_dead_p2_broadcast` | 叶子死亡 p2 | 叶子是叶子 |

#### 技能类

| API | 说明 | 举例 | Data |
|-----|------|------|------|
| `pending_skill_created` | 技能创建通知 | - | `PendingSkill` |
| `skill_blocked_by_jiaohua_notify` | 技能被脚滑人禁用通知，这类情况不会被上一条发出 | 你的 xxx 技能被脚滑人禁用 | `PendingSkill` |
| `invalid_pending_skill_notify` | 无效技能通知 | 你的 xxx 技能不可用 | 无效的 `PendingSkill.id` |
| `skill_fail_by_sudden_death_notify` | 技能失败(暴毙) | - | `PendingSkill.id` |
| `skill_fail_by_unexpected_behavior` | 技能失败(内部错误) | - | `PendingSkill.id` |
| `skill_fail_by_leaf_protected_notify` | 技能失败(叶子保护) | - | `PendingSkill.id` |
| `skill_fail_by_kirby_notify` | 技能失败(被复制) | - | `PendingSkill.id` |
| `skill_execute_fail_notify` | 技能执行失败通知 | 失败 | `PendingSkill.id` |
| `jiaohua_vote_block_broadcast` | 脚滑人禁票广播 | xxx 可以禁票一人 |
| `jiaohua_dead_skill_broadcast` | 脚滑人死亡技能广播 | xxx 可以取消一人下一个晚上的一次行动，或令一人不可被其他人的技能选中 |
| `jiaohua_skill_failed_by_smog` | 脚滑人技能失败(烟雾) |
| `jiaohua_skill_result_notify` | 脚滑人技能结果通知 | 炮仙 |
| `doge_suicide_broadcast` | Doge自爆广播 | xxx 自爆了 |
| `doge_involve_broadcast` | Doge死亡牵连广播 | 由于 xxx 保护了 yyy ... |
| `doctor_skill_broadcast` | 庸医技能广播 | yyy 被扎了一针 |
| `doctor_save_broadcast` | 庸医救活广播 | 但是 yyy 被救活了 |
| `mole_skill_success_notify` | 地鼠技能成功通知 | 成功 |
| `mole_skill_fail_notify` | 地鼠技能失败通知 | 失败 |
| `mole_red_twice_notify` | 地鼠两次红土地通知 | 红土地，你死了  |
| `mole_red_twice_broadcast` | 地鼠两次红土地广播 | 地鼠两次冲到了红土地上！ |
| `mole_kill_broadcast` | 地鼠击杀广播 | yyy 被地鼠突击了！ |
| `mole_kill_spring_broadcast` | 地鼠弹簧击杀广播 | 地鼠想突击 yyy，被弹簧弹回！ |
| `rabi_milked_notify` | 喂奶通知 | 你被喂鲜奶 |
| `rabi_milk_type_notify` | 喂奶类型通知 | 鲜奶 |
| `rabi_kill_broadcast` | 兔子击杀广播 | yyy 被喂了毒奶 |
| `famao_skill_broadcast` | 法猫技能广播 | yyy 被丢了药水 |
| `famao_reverse_failed_broadcast` | 法猫反转失败广播（法猫不能反转自己） |
| `famao_reversed_broadcast` | 法猫反转广播 | yyy 的阵营反转了！ |
| `famao_save_broadcast` | 法猫救活广播 | 但是 yyy 被救活了 |
| `kirby_skill_fail_by_spring_notify` | 卡比技能失败(弹簧) |
| `kirby_skill_fail_by_doge_notify` | 卡比技能失败(Doge) |
| `kirby_skill_fail_by_invalid_chara_notify` | 卡比技能失败(无效身份) |
| `kirby_skill_success_chara_notify` | 卡比技能成功，通知获得的身份 |
| `kirby_get_skill_notify` | 卡比技能成功，通知获得的技能，并且可以理解使用 |
| `fenxia_skill_no_fen_notify` | 粉侠粉条耗尽通知 |
| `fenxia_skill_failed_by_doge_notify` | 粉侠技能失败(Doge) |
| `fenxia_skill_failed_by_unknown_dead_notify` | 粉侠技能失败(身份不明) |
| `fenxia_skill_failed_by_smog_notify` | 粉侠技能失败(烟雾) |
| `fenxia_skill_failed_by_invalid_chara_notify` | 粉侠技能失败(无效身份) |
| `fenxia_skill_success_chara_notify` | 粉侠技能成功，通知获得的身份 |
| `creeper_skill_fail_by_doge_notify` | 爬行者技能失败(Doge) |
| `tnt_boom_broadcast` | 炸药爆炸广播 | xxx 身上的炸药爆炸了！ |
| `paoxian_skill_fail_by_doge_notify` | 炮仙技能失败(Doge) |
| `paoxian_kill_broadcast` | 炮仙击杀广播 | yyy 被炮仙杀了 |
| `paoxian_kill_spring_broadcast` | 炮仙弹簧击杀广播 | 炮仙想杀 yyy，被弹簧弹回！ |
| `shiwu_skill_fail_by_doge_notify` | 实物技能失败(Doge) |
| `shiwu_kidnap_broadcast` | 实物绑架广播 | yyy 被实物绑架了！ |
| `shiwu_broadcast_failed_notify` | 实物广播失败通知（灰卡烟雾等原因） |
| `shiwu_broadcast_chara_broadcast` | 实物身份广播 | 实物公布了 yyy 的身份！ |
| `shiwu_broadcast_result_broadcast` | 实物身份广播结果广播 | yyy 是炮仙 |
| `shiwu_kidnapped_skill_disabled_notify` | 实物技能被绑架禁用通知 | 你的 xxx 技能被绑架 |
| `shiwu_exposed_kill_broadcast` | 实物暴露广播 | 实物暴露了身份，实物撕票了！ |
| `shiwu_involve_broadcast` | 实物牵连广播 | 由于 xxx 绑架了 yyy... |
| `huika_skill_fail_by_doge_notify` | 灰卡比技能失败(Doge) |
| `huika_smog_broadcast` | 灰卡比烟雾广播 | yyy 被烟雾弥漫！ |
| `huika_smog_kill_broadcast` | 灰卡比烟雾击杀广播 | yyy 窒息了！ |
| `yinmo_skill_fail_by_doge_notify` | 音魔技能失败(Doge) |
| `yinmo_kill_broadcast` | 音魔击杀广播 | 音魔给 yyy 发了唱片！ |
| `yinmo_kill_spring_broadcast` | 音魔弹簧击杀广播 | 音魔想给 yyy 发唱片，被弹簧弹回！ |
| `ctf_skill_fail_by_doge_notify` | CTF技能失败(Doge) |
| `bug_kill_broadcast` | 虫子过多暴毙广播 | xxx 身上的虫子过多！ |
| `hechong_skill_fail_by_smog_notify` | 合虫技能失败(烟雾) |
| `hechong_skill_fail_by_leaf_notify` | 合虫技能失败(叶子) |
| `hechong_skill_fail_by_invalid_chara_notify` | 合虫技能失败(无效身份) |
| `hechong_skill_success_copy_notify` | 合虫复制成功，通知获得的身份 |
| `caimon_skill_no_cai_notify` | 彩怪彩条耗尽通知 |
| `caimon_skill_fail_by_doge_notify` | 彩怪技能失败(Doge) |
| `xiansong_skill_fail_by_doge_notify` | 贤松技能失败(Doge) |
| `xiansong_get_mfa_smog_notify` | 贤松获得mfa，但是无法获得身份（烟雾） |
| `xiansong_get_mfa_notify` | 贤松获得mfa，通知对方身份 |
| `xiansong_get_mfa_fail_notify` | 贤松未获得mfa通知 |
| `xiansong_boom_broadcast` | 咸松球爆炸广播 | xxx 身上的咸松球爆炸了！ |
| `myz_self_reveal_broadcast` | myz自爆广播 | myz 自爆了自己的身份！myz 的威胁将强制生效！ |
| `myz_threaten_notify` | 威胁通知 | 你被威胁 xxx |
| `myz_threaten_force_notify` | 强制威胁通知 | 你被强制威胁 xxx |
| `myz_threat_failed_by_already_notify` | myz威胁失败通知(目标已被威胁过) |
| `myz_threat_failed_by_no_skill_notify` | myz威胁失败通知(目标无技能可威胁) |
| `myz_threat_failed_notify` | myz威胁失败通知(威胁不可能达成) |
| `myz_threaten_block_broadcast` | 因威胁无法行动广播 | xxx 昨晚被威胁，白天无法行动  |
| `myz_ignore_broadcast` | 无视威胁广播 | xxx 无视了威胁！ |


#### 请求类

若无特殊说明，技能请求类的 Data 均为如下结构：

```json
{
    "skill_id", "0123456789abcdef0123456789abcdef" // 技能 id
    "invalid_choice": [ // 不能选择的目标列表
      {
        "id": 1,
        "reason": "你不能查自己"
      }
    ],
    "pending_role": { ... } // 身份数据
}
```

请求的消息类型通常都是 `player_X`，若无明显指定玩家则为 `internal`

| API | Content | Data |
|-----|---------|------|
| `request_player_list` | 输入玩家列表（X~Y 人） | - |
| `request_leaf_game` | 是否为叶子局？(1: 是；0: 否) | - |
| `request_anonymous_game` | 第一晚是否匿名？(1: 是；0: 否) | - |
| `request_reroll_player` | 输入需要重抽身份的玩家，输入 0 以继续 | - |
| `request_leaf_charas` | 输入叶子的四个身份 | - |
| `request_leaf_chara_reroll` | 是否重抽第一身份？（1：重抽；0：放弃） | - |
| `request_jiaohua_skill` | 输入一名玩家的编号查询其身份，输入 0 以放弃 | 技能类 |
| `request_jiaohua_vote_block` | 输入要禁票的玩家编号，输入 0 放弃 | 无法被选中的玩家列表 `{[{"id": 1, "reason": "xxx"}...]}` |
| `request_jiaohua_dead_skill` | 输入玩家编号和行动类型（x=封住行动，p=保护玩家），输入 0 放弃 | 同上 |
| `request_doge_skill_force_threaten` | 你可以选择是否自爆（1：是；0：否） | 技能类，不含 `invalid_choice` |
| `request_doge_skill` | 输入要保护的玩家编号（输入 0 放弃），结尾加 b 表示自爆；输入 0 放弃 | 技能类 |
| `request_doctor_skill` | 输入要扎针的玩家编号（最多 X 个），输入 0 放弃 | 技能类 |
| `request_mole_red_ground` | 红土地，要再突击一次吗？（1：突击；0：放弃） | - |
| `request_mole_skill` | 输入一名玩家的编号进行突击，输入 0 放弃 | 技能类 |
| `request_drink_milk` | 你被喂奶，要喝吗？（1：喝；0：不喝） | - |
| `request_rabi_skill_force_threaten` | 你可以选择给鲜奶还是毒奶（1：鲜奶；0：毒奶） | 技能类，不含 `invalid_choice` |
| `request_rabi_skill` | 输入要投喂的玩家编号和奶类型（x=鲜奶，d=毒奶），输入 0 放弃 | 技能类 |
| `request_shelang_skill` | 输入要扔弹簧的玩家编号，输入 0 放弃 | 技能类 |
| `request_famao_skill` | 输入要投掷药水的玩家编号（最多 X 个），输入 0 放弃 | 技能类 |
| `request_kirby_using_copy_skill` | 是否使用复制技能（xxx）？（1：使用；0：放弃并使用吸入技能） | 复制技能的相关信息 `{"chara_type": doge, "data": {...}}` |
| `request_kirby_skill` | 输入一名玩家的编号吸入，输入 0 放弃 | 技能类 |
| `request_fenxia_skill` | 输入要获取技能的角色编号（剩余 X 根粉条），输入 0 放弃 | 技能类 |
| `request_fenxia_reborn` | 用一根粉条复活吗？（1：是；0：否） | - |
| `request_creeper_skill` | 输入要在谁身上埋炸药（剩余 X 个炸弹），输入 0 放弃 | 技能类 |
| `request_paoxian_skill` | 输入一名玩家的编号令其死亡，输入 0 放弃 | 技能类 |
| `request_shiwu_skill_force_threaten` | 你可以选择是否公开被绑架者的身份（1：是；0：否） | 技能类，不含 `invalid_choice` |
| `request_shiwu_skill` | 输入一名玩家的编号进行绑架，输入 0 放弃 | 技能类 |
| `request_huika_skill` | 输入要投掷烟雾弹的玩家编号（最多 X 个），输入 0 放弃 | 技能类 |
| `request_yinmo_skill` | 输入要发唱片的玩家编号（剩余 X 张唱片），输入 0 放弃 | 技能类 |
| `request_ctf_skill` | 输入要释放虫子的玩家编号（剩余 X 只虫子），输入 0 放弃 | 技能类 |
| `request_ctf_reborn` | 移动一只 bug 到自己身上并复活吗？（1：是；0：否） | - |
| `request_hechong_copy_leaf` | 选择一个身份复制 | - |
| `request_hechong_skill` | 输入一名其他玩家的编号复制其身份，输入 0 以放弃 | 技能类 |
| `request_xiansong_give_mfa` | 你被要mfa了，给吗？（1：给；0：不给） | - |
| `request_xiansong_skill_force_threaten` | 你可以输入 m 或者 x 表示强制要 mfa 或丢咸松球，输入 0 放弃 | 技能类，不含 `invalid_choice` |
| `request_xiansong_skill` | 输入一名其他玩家的编号索要 mfa 文件，输入 0 放弃 | 技能类 |
| `request_caimon_skill_force_threaten` | 你可以选择用一根还是两根彩条（1：两根；0：一根） | 技能类 |
| `request_caimon_skill` | 输入要复活的死亡玩家编号，在结尾输入 d 表示使用两根彩条（剩余 X 根彩条），输入 0 放弃 | 技能类 |
| `request_caimon_reborn` | 用一根彩条复活吗？（1：是；0：否） | - |
| `request_jiangxian_skill` | 江仙设计未完成 | 技能类 |
| `request_jiangxian_real_vote` | 输入你真正想投的票 | 无法被投票的玩家列表 `{[{"id": 1, "reason": "xxx"}...]}` |
| `request_jiangxian_dead_vote` | 你有一次死亡后投票的机会，输入你想投票的玩家，输入 0 放弃 | 同上 |
| `request_myz_skill_force_threaten` | 输入威胁目标的编号；在结尾输入 f 以自爆身份，并使威胁强制生效 | 技能类，不含 `invalid_choice`，含 `invalid_target_choice` 表示不能指定的威胁目标 |
| `request_myz_skill` | 输入要威胁的玩家编号，威胁目标的编号，输入 0 放弃 | 技能类，含 `invalid_target_choice` 表示不能指定的威胁目标  |
| `request_vote` | 输入 x y 表示 x 给 y 投票，若 x 是脚滑人，可以输入 x b 自爆；输入 0 结束投票环节 | `valid_votes`，具体见下文 |
| `request_for_next_game` | 开启下一局？（1：是；0：否） | - |

##### `valid_votes`

投票阶段所有的合法操作列表

```json
{
    [
        {
            "id": false // 玩家 id
            "can_vote": true // 是否可以投票
            "can_suicide": false // 是否可以自爆（脚滑人）
            "invalid_vote": [ ... ] // 无法被投票的目标 id
        },
        ...
    ]
}
```

#### 游戏状态更新类

| API | 说明 |Data|
|-----|------|------|
| `game_update_night` | 夜晚状态更新 | `Game` |
| `game_update_day` | 白天状态更新 | `Game` |
| `game_update_vote` | 投票状态更新 | `Day` |

#### 游戏流程类

| API | 说明 | 举例 | Data |
|-----|------|------|------|
| `player_init` | 打印本局玩家 |
| `player_notify_chara` | 通知玩家抽到的身份 |
| `player_notify_chara_reset` | 通知玩家重抽的身份 |
| `leaf_notify_first_chara` | 通知叶子的第一身份 |
| `leaf_notify_first_chara_reroll` | 通知叶子重抽的第一身份 |
| `barleader_notify` | 吧主通知 | 你是吧主，脚滑人是 xxx |
| `jiaohua_start_notify` | 脚滑人获知两个身份通知 | 本局有合虫和兔子 |
| `xiansong_start_notify` | 贤松获知炮仙通知 | 炮仙是 xxx |
| `paoxian_party_notify` | 炮仙队友通知 | 队友：yyy |
| `night_start_broadcast` | 夜晚开始广播 | 晚上开始 |
| `night_summary_broadcast` | 夜晚总结广播 | 今晚 |
| `night_summary_queued_broadcast` | 夜晚缓存的事件广播 | xxx 想杀 yyy，被 Doge 挡了 |
| `day_start_broadcast` | 白天开始广播 | 白天开始 |
| `vote_start_broadcast` | 投票开始广播 | 投票开始 |
| `vote_end_broadcast` | 投票结束广播 | 投票结束 |
| `jiaohua_suicide_broadcast` | 脚滑人自爆广播 | xxx 自爆了 |
| `vote_result_broadcast` | 投票结果广播 | 投票结果是 |
| `vote_tie_broadcast` | 平票广播 | 平票 |
| `baidu_broadcast` | 度娘广播 | 半数玩家都弃票了！度娘要抽贴了！ |
| `baidu_result_broadcast` | 度娘结果广播 | xxx 被抽帖了 |
| `vote_baidu_fail_broadcast` | 度娘与投票结果重合广播 | 由于 xxx 被抽帖，本轮投票没有其他玩家出局 |
| `game_win_broadcast` | 游戏胜利广播 | 游戏结束，无人生还 | 获胜方阵营 或 null |
| `game_win_summary` | 游戏总结 | - | `Game` |

#### 错误处理类

通常发生在 `request` 类 `api` 收到了不合法的输入请求，此时会返回以 `_parse_error` 为后缀的 `api`，其中包含了具体的错误信息

| API | 说明 |
|-----|------|
| `xxx_parse_error` | 解析错误 |