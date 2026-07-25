# MonsterClear 怪物区域清除开关插件

- 作者: 淦
- 开关式怪物中心区域清除系统，支持圆形/矩形区域、偏移持续清除

## 指令

### 玩家指令
| 语法                                                         |     权限      | 说明                                                                         |
|------------------------------------------------------------|:-----------:|:---------------------------------------------------------------------------|
| `/mc start <怪物ID/名称> [尺寸1] [尺寸2] [排除物块]`           | monsterclear.use | 开启清除任务（无偏移）；尺寸1为半径/宽度，尺寸2仅在矩形时使用；排除物块用逗号分隔           |
| `/mc offset <怪物ID/名称> <偏移X> <偏移Y> [尺寸1] [尺寸2] [排除物块]` | monsterclear.use | 开启偏移持续清除任务，每次清除将原点偏移指定格数                                     |
| `/mc stop`                                                   | monsterclear.use | 停止当前清除任务                                                               |
| `/mc status`                                                 | monsterclear.use | 查看当前任务状态（目标、形状、尺寸、偏移、间隔、排除物块）                               |
| `/mc shape circle`                                           | monsterclear.use | 切换到圆形区域（保留当前尺寸参数）                                                 |
| `/mc shape rect`                                             | monsterclear.use | 切换到矩形区域（保留当前尺寸参数）                                                 |
| `/mc presets`                                                | monsterclear.use | 查看预设的怪物清除半径                                                         |
| `/mc monsters` 或 `/lm`                                      | monsterclear.use | 查看当前世界活跃的怪物列表（类型、数量）                                             |

### 管理员指令
| 语法                                             |     权限      | 说明                                                                 |
|------------------------------------------------|:-----------:|:------------------------------------------------------------------|
| `/reload`                                       | monsterclear.admin | 重载插件配置（与 TShock 内置 reload 同名，仅重载本插件配置）                   |
| `/mcadmin interval <毫秒>`                      | monsterclear.admin | 设置清除间隔（1-60000毫秒）                                           |
| `/mcadmin protect add <物块ID>`                 | monsterclear.admin | 添加物块到保护列表（这些物块不会被清除）                                   |
| `/mcadmin protect remove <物块ID>`              | monsterclear.admin | 从保护列表移除物块                                                     |
| `/mcadmin protect list`                         | monsterclear.admin | 查看当前保护列表                                                       |
| `/mcadmin preset add <名称> <半径>`             | monsterclear.admin | 添加/更新怪物半径预设                                                   |
| `/mcadmin preset remove <名称>`                 | monsterclear.admin | 移除指定预设                                                           |
| `/mcadmin preset list`                          | monsterclear.admin | 查看所有预设                                                           |

### 参数说明
- **怪物ID/名称**：可直接输入怪物ID（如 1）或部分名称（如 `zombie`），`all` 表示所有怪物。
- **尺寸1**：圆形时为半径，矩形时为宽度，单位物块格数（1-200）。
- **尺寸2**：矩形时的高度（1-200），圆形时可省略。
- **偏移X/Y**：水平/垂直偏移量（正数向右/下，负数向左/上），单位物块格数。
- **排除物块**：逗号分隔的物块ID，这些物块不会被清除（如 `3,4,5`）。
- **物块ID**：可在 [官方Wiki](https://terraria.wiki.gg/zh/物块ID) 查询。

### 使用示例
```bash
/mc start zombie              # 清除所有僵尸周围区域（使用预设半径）
/mc start all 50              # 清除所有怪物周围50格圆形区域
/mc start skeletron 60 40     # 清除骷髅王周围60x40矩形区域
/mc offset zombie 5 -3 50     # 在僵尸中心右5格上3格处，持续清除半径50圆形区域
/mc offset all 10 0 60 40     # 在所有怪物中心右10格处，持续清除60x40矩形区域
/mc shape rect                # 切换到矩形区域
/mc stop                      # 停止清除
/mc status                    # 查看当前状态
/reload                       # 重载插件配置
```

## 配置
配置文件位置: `tshock/PluginConfig/MonsterClearConfig.json`
首次运行自动生成默认配置。

```json5
{
  "ProtectedTiles": [],                     // 不会被清除的物块ID列表（初始为空）
  "MonsterRadiusPresets": {},                // 怪物半径预设（初始为空，可手动添加）
  "ClearIntervalMilliseconds": 5000,         // 清除间隔（毫秒），默认5秒
  "MaxClearsPerInterval": 10,                // 每次最多清除的怪物数量
  "EnableLogging": true                      // 是否输出插件日志到控制台
}
```

## 配置项说明
- ProtectedTiles：手动添加需要保护的物块ID，例如 `[3, 4, 5]`。
- MonsterRadiusPresets：为常见怪物预设清除半径，格式如 `{"zombie": 30, "eye": 40}`。
- ClearIntervalMilliseconds：定时清除任务执行的间隔，单位毫秒，取值范围 1-60000。
- MaxClearsPerInterval：单次清除周期最多处理的怪物数量，防止大面积清除造成服务器卡顿。
- EnableLogging：设为 `false` 可关闭插件在控制台的常规信息输出（错误日志仍会输出）

## 更新日志
### v2026.4.5.1
- 将热重载指令改为与 TShock 内置 /reload 一致，移除 /mcadmin reload。

- 简化形状切换指令：/mc shape circle 和 /mc shape rect 不再需要附带尺寸参数，仅切换形状。

- 优化状态显示，添加偏移量信息。

### v2026.2.22.2
- 简化形状切换指令：`/mc shape circle` 和 `/mc shape rect` 不再需要附带尺寸参数，仅切换形状

- 优化状态显示，添加偏移量信息

### v2026.2.22.1
- 新增偏移持续清除指令 `/mc offset`，支持对怪物中心应用偏移后持续清除区域

- 重构任务管理，使偏移成为任务的一部分

### v2026.2.21.1
- 配置文件增加 `EnableLogging` 日志开关，可关闭常规日志输出

- 将清除间隔单位改为毫秒，配置文件字段更名为 `ClearIntervalMilliseconds`

### v2025.6.26.1
- 增加区域形状切换功能（圆形/矩形）

- 优化状态显示，包含形状和尺寸信息

- 添加矩形区域清除算法

### v2025.6.25.1
- 初始开关式版本发布

- 支持以怪物为中心定时清除

- 添加保护物块列表

- 添加预设管理系统

- 增加管理员管理指令

### v2025.6.21.1
- 重构为以怪物为中心的模式

- 移除玩家中心功能

## 反馈
优先提交 Issue：[https://github.com/ICU-Club](https://github.com/Gangan1145/MonsterClearPlugin.git)
