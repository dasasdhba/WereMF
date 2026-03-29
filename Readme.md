# WereMF

MF 杀是由 [Mario Forever 社区](https://www.marioforever.net) 的大爷、xfx 等众多玩家共同设计的类狼人杀游戏
本项目是该游戏的机器实现

## CLI 参数

* `--help`: 显示帮助
* `--api`: API 模式（还没做）
* `--config <path>`: 使用自定义的抽签配置文件
* `--seed <int>`：使用自定义的种子

## 游戏内辅助功能

* `\undo`: 撤销
* `\redo`: 重做
* `\restart`: 重开一局（使用当前玩家列表）
* `\reboot`: 重启程序
* `\exit`: 退出
* '\log`: 保存当前对局作为日志
* `\rename <idx> <newName>`：将第 `idx` 个玩家重命名为 `newName`，这里的 `idx` 以输入玩家列表的顺序为准，不以第一晚匿名模式打乱的顺序为准
* `\night`: 打印晚上的总结
* `\day`: 打印白天的总结
* `\vote`: 打印投票的总结
* `\summary`: 打印对局结束的总结