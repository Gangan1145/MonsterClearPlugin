using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using Newtonsoft.Json;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace MonsterClearPlugin
{
    [ApiVersion(2, 1)]
    public class MonsterClear : TerrariaPlugin
    {
        public override string Name => "怪物区域清除开关";
        public override string Author => "淦";
        public override string Description => "开关式怪物中心区域清除系统（支持圆形/矩形区域、偏移持续清除）";
        public override Version Version => new(2026, 4, 5, 1); // 移除 /mcadmin reload

        private static string ConfigPath => Path.Combine(TShock.SavePath, "PluginConfig", "MonsterClearConfig.json");

        private static readonly int[] DefaultExcludedBlocks = new int[]
        {
            TileID.Containers, TileID.Containers2, TileID.Dressers, TileID.CrystalBall,
            TileID.AlchemyTable, TileID.Teleporter, TileID.LihzahrdAltar, TileID.ShadowOrbs,
            TileID.DemonAltar, TileID.FakeContainers, TileID.LihzahrdBrick
        };

        public enum AreaShape { Circle, Rectangle }

        private class PluginConfig
        {
            public List<int> ProtectedTiles { get; set; } = new List<int>();
            public Dictionary<string, int> MonsterRadiusPresets { get; set; } = new Dictionary<string, int>();
            public int ClearIntervalMilliseconds { get; set; } = 5000;
            public int MaxClearsPerInterval { get; set; } = 10;
            public bool EnableLogging { get; set; } = true;
        }

        private class ClearTask
        {
            public string MonsterIdentifier { get; set; } = "all";
            public AreaShape Shape { get; set; } = AreaShape.Circle;
            public int SizeParam1 { get; set; } = 40;
            public int SizeParam2 { get; set; } = 40;
            public HashSet<int> ExcludedTiles { get; set; } = new HashSet<int>();
            public bool IsActive { get; set; } = false;
            public int OffsetX { get; set; } = 0;
            public int OffsetY { get; set; } = 0;
        }

        private PluginConfig _config;
        private ClearTask _currentTask = new ClearTask();
        private System.Timers.Timer _clearTimer;
        private readonly object _taskLock = new object();

        public MonsterClear(Main game) : base(game) { }

        public override void Initialize()
        {
            LoadConfig();

            _clearTimer = new System.Timers.Timer(_config.ClearIntervalMilliseconds);
            _clearTimer.Elapsed += (sender, e) => ExecuteClearTask();
            _clearTimer.AutoReset = true;

            Commands.ChatCommands.Add(new Command("monsterclear.use", MainCommand, "monsterclear", "mc"));
            Commands.ChatCommands.Add(new Command("monsterclear.admin", AdminCommand, "mcadmin"));
            // 独立的重载指令 /reload (需要权限 monsterclear.admin)
            Commands.ChatCommands.Add(new Command("monsterclear.admin", ReloadConfigCommand, "reload"));
        }

        #region 配置管理
        private void LoadConfig()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                if (!File.Exists(ConfigPath))
                {
                    _config = new PluginConfig();
                    SaveConfig();
                    LogInfo("怪物清除插件: 已创建新的配置文件");
                }
                else
                {
                    _config = JsonConvert.DeserializeObject<PluginConfig>(File.ReadAllText(ConfigPath));
                    if (_config.ProtectedTiles == null) _config.ProtectedTiles = new List<int>();
                    if (_config.MonsterRadiusPresets == null) _config.MonsterRadiusPresets = new Dictionary<string, int>();
                    if (_config.ClearIntervalMilliseconds < 1) _config.ClearIntervalMilliseconds = 5000;
                    if (_config.MaxClearsPerInterval < 1) _config.MaxClearsPerInterval = 10;
                    LogInfo("怪物清除插件: 配置文件加载成功");
                }
                _clearTimer.Interval = _config.ClearIntervalMilliseconds;
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"怪物清除插件: 配置文件加载失败 - {ex.Message}");
                _config = new PluginConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
                LogInfo("怪物清除插件: 配置文件保存成功");
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"怪物清除插件: 配置文件保存失败 - {ex.Message}");
            }
        }

        private void LogInfo(string message)
        {
            if (_config.EnableLogging)
            {
                TShock.Log.ConsoleInfo(message);
            }
        }

        private void ReloadConfig(CommandArgs args)
        {
            if (!args.Player.HasPermission("monsterclear.admin"))
            {
                args.Player.SendErrorMessage("你没有重载配置的权限!");
                return;
            }
            LoadConfig();
            args.Player.SendSuccessMessage("怪物清除插件配置已重新加载!");
            LogInfo($"{args.Player.Name} 重载了插件配置");
        }
        #endregion

        #region 指令处理
        private void MainCommand(CommandArgs args)
        {
            if (!args.Player.HasPermission("monsterclear.use"))
            {
                args.Player.SendErrorMessage("你没有使用该指令的权限!");
                return;
            }

            if (args.Parameters.Count == 0)
            {
                ShowMainHelp(args.Player);
                return;
            }

            string subCommand = args.Parameters[0].ToLower();

            switch (subCommand)
            {
                case "start":
                    StartClearTask(args);
                    break;
                case "stop":
                    StopClearTask(args.Player);
                    break;
                case "status":
                    ShowTaskStatus(args.Player);
                    break;
                case "presets":
                    ListPresets(args.Player);
                    break;
                case "monsters":
                    ListActiveMonsters(args.Player);
                    break;
                case "shape":
                    ChangeShape(args);
                    break;
                case "offset":
                    OffsetClearTask(args);
                    break;
                default:
                    args.Player.SendErrorMessage("未知指令! 使用 /mc 查看帮助");
                    break;
            }
        }

        private void ShowMainHelp(TSPlayer player)
        {
            player.SendInfoMessage("===== 怪物清除开关指令 =====");
            player.SendInfoMessage("/mc start [怪物ID/名称] [尺寸1] [尺寸2] [排除物块] - 开启清除（无偏移）");
            player.SendInfoMessage("/mc offset <怪物ID/名称> <偏移X> <偏移Y> [尺寸1] [尺寸2] [排除物块] - 开启偏移持续清除");
            player.SendInfoMessage("/mc stop - 停止清除");
            player.SendInfoMessage("/mc status - 查看当前状态");
            player.SendInfoMessage("/mc shape circle - 切换到圆形区域（不改变尺寸）");
            player.SendInfoMessage("/mc shape rect - 切换到矩形区域（不改变尺寸）");
            player.SendInfoMessage("/mc presets - 查看预设列表");
            player.SendInfoMessage("/mc monsters - 查看当前活跃怪物");
            player.SendInfoMessage("示例: /mc offset zombie 5 -3 50    - 在僵尸中心右5上3处持续清除半径50圆形区域");
        }

        private void AdminCommand(CommandArgs args)
        {
            if (!args.Player.HasPermission("monsterclear.admin"))
            {
                args.Player.SendErrorMessage("你没有管理权限!");
                return;
            }

            if (args.Parameters.Count == 0)
            {
                ShowAdminHelp(args.Player);
                return;
            }

            string subCommand = args.Parameters[0].ToLower();

            switch (subCommand)
            {
                // 已移除 reload 分支
                case "interval":
                    SetInterval(args);
                    break;
                case "protect":
                    ManageProtectedTiles(args);
                    break;
                case "preset":
                    ManagePresets(args);
                    break;
                default:
                    args.Player.SendErrorMessage("未知管理指令! 可用: interval, protect, preset");
                    break;
            }
        }

        private void ShowAdminHelp(TSPlayer player)
        {
            player.SendInfoMessage("===== 怪物清除管理指令 =====");
            player.SendInfoMessage("/reload - 重载配置 (需要权限 monsterclear.admin)");
            player.SendInfoMessage("/mcadmin interval <毫秒> - 设置清除间隔（毫秒）");
            player.SendInfoMessage("/mcadmin protect add <ID> - 添加保护物块");
            player.SendInfoMessage("/mcadmin protect remove <ID> - 移除保护物块");
            player.SendInfoMessage("/mcadmin protect list - 查看保护列表");
            player.SendInfoMessage("/mcadmin preset add <名称> <半径> - 添加预设");
            player.SendInfoMessage("/mcadmin preset remove <名称> - 移除预设");
        }

        private void ReloadConfigCommand(CommandArgs args)
        {
            ReloadConfig(args);
        }
        #endregion

        #region 清除任务管理
        private void StartClearTask(CommandArgs args)
        {
            lock (_taskLock)
            {
                string monsterIdentifier = "all";
                if (args.Parameters.Count > 1)
                    monsterIdentifier = args.Parameters[1].ToLower();

                int param1 = _currentTask.SizeParam1;
                int param2 = _currentTask.SizeParam2;
                int paramIndex = 2;

                if (args.Parameters.Count > paramIndex && int.TryParse(args.Parameters[paramIndex], out int p1))
                {
                    param1 = p1;
                    paramIndex++;
                }
                else if (_config.MonsterRadiusPresets.TryGetValue(monsterIdentifier, out int presetSize))
                {
                    param1 = presetSize;
                }

                if (_currentTask.Shape == AreaShape.Rectangle &&
                    args.Parameters.Count > paramIndex &&
                    int.TryParse(args.Parameters[paramIndex], out int p2))
                {
                    param2 = p2;
                    paramIndex++;
                }

                if (param1 <= 0 || param1 > 200 ||
                    (_currentTask.Shape == AreaShape.Rectangle && (param2 <= 0 || param2 > 200)))
                {
                    args.Player.SendErrorMessage("尺寸参数必须是1-200之间的整数!");
                    return;
                }

                var excludedTiles = new HashSet<int>(_config.ProtectedTiles);
                if (args.Parameters.Count > paramIndex)
                {
                    try
                    {
                        var customExclusions = args.Parameters[paramIndex].Split(',')
                            .Select(id => int.Parse(id.Trim()))
                            .Where(id => id >= 0 && id < TileID.Count);
                        excludedTiles.UnionWith(customExclusions);
                    }
                    catch
                    {
                        args.Player.SendErrorMessage("排除物块ID格式错误! 请使用逗号分隔的整数列表");
                        return;
                    }
                }

                _currentTask.MonsterIdentifier = monsterIdentifier;
                _currentTask.SizeParam1 = param1;
                _currentTask.SizeParam2 = param2;
                _currentTask.ExcludedTiles = excludedTiles;
                _currentTask.IsActive = true;
                _currentTask.OffsetX = 0;
                _currentTask.OffsetY = 0;

                if (!_clearTimer.Enabled)
                    _clearTimer.Start();

                args.Player.SendSuccessMessage($"已开启怪物清除任务!");
                args.Player.SendInfoMessage($"目标: {monsterIdentifier}, 形状: {_currentTask.Shape}, 尺寸: {GetSizeDescription()}");
                args.Player.SendInfoMessage($"排除物块: {string.Join(", ", excludedTiles)}");
            }
        }

        private void OffsetClearTask(CommandArgs args)
        {
            lock (_taskLock)
            {
                if (args.Parameters.Count < 4)
                {
                    args.Player.SendErrorMessage("用法: /mc offset <怪物ID/名称> <偏移X> <偏移Y> [尺寸1] [尺寸2] [排除物块]");
                    return;
                }

                string monsterIdentifier = args.Parameters[1].ToLower();
                if (!int.TryParse(args.Parameters[2], out int offsetX) || !int.TryParse(args.Parameters[3], out int offsetY))
                {
                    args.Player.SendErrorMessage("偏移X和偏移Y必须是整数!");
                    return;
                }

                int param1 = _currentTask.SizeParam1;
                int param2 = _currentTask.SizeParam2;
                int paramIndex = 4;

                if (args.Parameters.Count > paramIndex && int.TryParse(args.Parameters[paramIndex], out int p1))
                {
                    param1 = p1;
                    paramIndex++;
                    if (_currentTask.Shape == AreaShape.Rectangle)
                    {
                        if (args.Parameters.Count > paramIndex && int.TryParse(args.Parameters[paramIndex], out int p2))
                        {
                            param2 = p2;
                            paramIndex++;
                        }
                        else
                        {
                            args.Player.SendErrorMessage("矩形区域需要同时指定宽度和高度!");
                            return;
                        }
                    }
                }
                else if (_config.MonsterRadiusPresets.TryGetValue(monsterIdentifier, out int presetSize))
                {
                    param1 = presetSize;
                }

                if (param1 <= 0 || param1 > 200 ||
                    (_currentTask.Shape == AreaShape.Rectangle && (param2 <= 0 || param2 > 200)))
                {
                    args.Player.SendErrorMessage("尺寸参数必须是1-200之间的整数!");
                    return;
                }

                var excludedTiles = new HashSet<int>(_config.ProtectedTiles);
                if (args.Parameters.Count > paramIndex)
                {
                    try
                    {
                        var customExclusions = args.Parameters[paramIndex].Split(',')
                            .Select(id => int.Parse(id.Trim()))
                            .Where(id => id >= 0 && id < TileID.Count);
                        excludedTiles.UnionWith(customExclusions);
                    }
                    catch
                    {
                        args.Player.SendErrorMessage("排除物块ID格式错误! 请使用逗号分隔的整数列表");
                        return;
                    }
                }

                _currentTask.MonsterIdentifier = monsterIdentifier;
                _currentTask.SizeParam1 = param1;
                _currentTask.SizeParam2 = param2;
                _currentTask.ExcludedTiles = excludedTiles;
                _currentTask.IsActive = true;
                _currentTask.OffsetX = offsetX;
                _currentTask.OffsetY = offsetY;

                if (!_clearTimer.Enabled)
                    _clearTimer.Start();

                args.Player.SendSuccessMessage($"已开启偏移清除任务!");
                args.Player.SendInfoMessage($"目标: {monsterIdentifier}, 偏移: ({offsetX}, {offsetY}), 形状: {_currentTask.Shape}, 尺寸: {GetSizeDescription()}");
                args.Player.SendInfoMessage($"排除物块: {string.Join(", ", excludedTiles)}");
            }
        }

        private void ChangeShape(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("用法: /mc shape circle 或 /mc shape rect");
                return;
            }

            string shapeType = args.Parameters[1].ToLower();

            lock (_taskLock)
            {
                if (shapeType == "circle")
                {
                    _currentTask.Shape = AreaShape.Circle;
                    args.Player.SendSuccessMessage($"已切换到圆形区域，当前半径: {_currentTask.SizeParam1}");
                }
                else if (shapeType == "rect")
                {
                    _currentTask.Shape = AreaShape.Rectangle;
                    args.Player.SendSuccessMessage($"已切换到矩形区域，当前宽: {_currentTask.SizeParam1}, 高: {_currentTask.SizeParam2}");
                }
                else
                {
                    args.Player.SendErrorMessage("未知区域形状! 可用: circle, rect");
                }
            }
        }

        private void StopClearTask(TSPlayer player)
        {
            lock (_taskLock)
            {
                _currentTask.IsActive = false;
                player.SendSuccessMessage("已停止怪物清除任务!");
            }
        }

        private void ShowTaskStatus(TSPlayer player)
        {
            lock (_taskLock)
            {
                if (!_currentTask.IsActive)
                {
                    player.SendInfoMessage("当前没有活动的清除任务");
                    return;
                }

                player.SendInfoMessage("===== 当前清除任务状态 =====");
                player.SendInfoMessage($"目标怪物: {_currentTask.MonsterIdentifier}");
                player.SendInfoMessage($"区域形状: {_currentTask.Shape}");
                player.SendInfoMessage($"区域尺寸: {GetSizeDescription()}");
                player.SendInfoMessage($"偏移量: ({_currentTask.OffsetX}, {_currentTask.OffsetY})");
                player.SendInfoMessage($"清除间隔: {_config.ClearIntervalMilliseconds} 毫秒");
                player.SendInfoMessage($"排除物块: {string.Join(", ", _currentTask.ExcludedTiles)}");
                player.SendInfoMessage($"下次清除: {_clearTimer.Interval} 毫秒后");
            }
        }

        private string GetSizeDescription()
        {
            return _currentTask.Shape == AreaShape.Circle ?
                $"半径: {_currentTask.SizeParam1}" :
                $"宽: {_currentTask.SizeParam1}, 高: {_currentTask.SizeParam2}";
        }

        private void ListPresets(TSPlayer player)
        {
            if (_config.MonsterRadiusPresets.Count == 0)
            {
                player.SendInfoMessage("没有预设配置");
                return;
            }

            player.SendInfoMessage("===== 怪物清除半径预设 =====");
            foreach (var preset in _config.MonsterRadiusPresets)
                player.SendInfoMessage($"{preset.Key}: {preset.Value} 格");
        }

        private void ListActiveMonsters(TSPlayer player)
        {
            var monsters = new Dictionary<int, List<NPC>>();
            for (int i = 0; i < Main.npc.Length; i++)
            {
                NPC npc = Main.npc[i];
                if (npc != null && npc.lifeMax > 0)
                {
                    if (!monsters.ContainsKey(npc.type))
                        monsters[npc.type] = new List<NPC>();
                    monsters[npc.type].Add(npc);
                }
            }

            if (monsters.Count == 0)
            {
                player.SendInfoMessage("当前没有活跃的怪物");
                return;
            }

            player.SendInfoMessage("===== 当前活跃怪物 =====");
            foreach (var kv in monsters)
            {
                string name = Lang.GetNPCNameValue(kv.Key);
                if (string.IsNullOrEmpty(name))
                    name = $"ID:{kv.Key}";
                player.SendInfoMessage($"{name} - 数量: {kv.Value.Count}");
            }
        }
        #endregion

        #region 管理功能
        private void SetInterval(CommandArgs args)
        {
            if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out int milliseconds))
            {
                args.Player.SendErrorMessage("用法: /mcadmin interval <毫秒>");
                return;
            }
            if (milliseconds < 1 || milliseconds > 60000)
            {
                args.Player.SendErrorMessage("间隔必须在1-60000毫秒之间!");
                return;
            }
            _config.ClearIntervalMilliseconds = milliseconds;
            _clearTimer.Interval = milliseconds;
            SaveConfig();
            args.Player.SendSuccessMessage($"清除间隔已设置为 {milliseconds} 毫秒");
        }

        private void ManageProtectedTiles(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("用法: /mcadmin protect add <ID> | remove <ID> | list");
                return;
            }
            string subCmd = args.Parameters[1].ToLower();
            switch (subCmd)
            {
                case "add":
                    if (args.Parameters.Count < 3)
                    {
                        args.Player.SendErrorMessage("用法: /mcadmin protect add <物块ID>");
                        return;
                    }
                    if (int.TryParse(args.Parameters[2], out int addId))
                    {
                        if (addId >= 0 && addId < TileID.Count)
                        {
                            if (!_config.ProtectedTiles.Contains(addId))
                            {
                                _config.ProtectedTiles.Add(addId);
                                SaveConfig();
                                args.Player.SendSuccessMessage($"已添加物块ID {addId} 到保护列表");
                            }
                            else
                                args.Player.SendInfoMessage($"物块ID {addId} 已在保护列表中");
                        }
                        else
                            args.Player.SendErrorMessage($"无效的物块ID! 必须在0-{TileID.Count - 1}之间");
                    }
                    else
                        args.Player.SendErrorMessage("无效的物块ID! 必须为整数");
                    break;
                case "remove":
                    if (args.Parameters.Count < 3)
                    {
                        args.Player.SendErrorMessage("用法: /mcadmin protect remove <物块ID>");
                        return;
                    }
                    if (int.TryParse(args.Parameters[2], out int removeId))
                    {
                        if (_config.ProtectedTiles.Contains(removeId))
                        {
                            _config.ProtectedTiles.Remove(removeId);
                            SaveConfig();
                            args.Player.SendSuccessMessage($"已从保护列表移除物块ID {removeId}");
                        }
                        else
                            args.Player.SendInfoMessage($"物块ID {removeId} 不在保护列表中");
                    }
                    else
                        args.Player.SendErrorMessage("无效的物块ID! 必须为整数");
                    break;
                case "list":
                    args.Player.SendInfoMessage("保护物块列表: " + string.Join(", ", _config.ProtectedTiles));
                    break;
                default:
                    args.Player.SendErrorMessage("未知子命令! 可用: add, remove, list");
                    break;
            }
        }

        private void ManagePresets(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("用法: /mcadmin preset add <名称> <半径> | remove <名称> | list");
                return;
            }
            string subCmd = args.Parameters[1].ToLower();
            switch (subCmd)
            {
                case "add":
                    if (args.Parameters.Count < 4)
                    {
                        args.Player.SendErrorMessage("用法: /mcadmin preset add <名称> <半径>");
                        return;
                    }
                    string presetName = args.Parameters[2].ToLower();
                    if (int.TryParse(args.Parameters[3], out int radius))
                    {
                        if (radius > 0 && radius <= 200)
                        {
                            if (_config.MonsterRadiusPresets.ContainsKey(presetName))
                                _config.MonsterRadiusPresets[presetName] = radius;
                            else
                                _config.MonsterRadiusPresets.Add(presetName, radius);
                            SaveConfig();
                            args.Player.SendSuccessMessage($"已添加/更新预设 {presetName} 半径 {radius}");
                        }
                        else
                            args.Player.SendErrorMessage("半径必须在1-200之间!");
                    }
                    else
                        args.Player.SendErrorMessage("无效的半径值!");
                    break;
                case "remove":
                    if (args.Parameters.Count < 3)
                    {
                        args.Player.SendErrorMessage("用法: /mcadmin preset remove <名称>");
                        return;
                    }
                    string removeName = args.Parameters[2].ToLower();
                    if (_config.MonsterRadiusPresets.ContainsKey(removeName))
                    {
                        _config.MonsterRadiusPresets.Remove(removeName);
                        SaveConfig();
                        args.Player.SendSuccessMessage($"已移除预设 {removeName}");
                    }
                    else
                        args.Player.SendInfoMessage($"预设 {removeName} 不存在");
                    break;
                case "list":
                    if (_config.MonsterRadiusPresets.Count == 0)
                    {
                        args.Player.SendInfoMessage("没有预设配置");
                        return;
                    }
                    args.Player.SendInfoMessage("===== 怪物清除半径预设 =====");
                    foreach (var preset in _config.MonsterRadiusPresets)
                        args.Player.SendInfoMessage($"{preset.Key}: {preset.Value} 格");
                    break;
                default:
                    args.Player.SendErrorMessage("未知子命令! 可用: add, remove, list");
                    break;
            }
        }
        #endregion

        #region 清除执行逻辑
        private void ExecuteClearTask()
        {
            lock (_taskLock)
            {
                if (!_currentTask.IsActive)
                {
                    _clearTimer.Stop();
                    return;
                }

                var targetMonsters = FindTargetMonsters(_currentTask.MonsterIdentifier);
                if (targetMonsters.Count == 0)
                {
                    LogInfo($"[怪物清除] 未找到匹配的怪物: {_currentTask.MonsterIdentifier}");
                    return;
                }

                Main.QueueMainThreadAction(() =>
                {
                    int totalCleared = 0;
                    int processed = 0;
                    foreach (var monster in targetMonsters)
                    {
                        if (monster.position.X == 0 && monster.position.Y == 0)
                            continue;

                        int centerX = (int)((monster.position.X + monster.width / 2) / 16f);
                        int centerY = (int)((monster.position.Y + monster.height / 2) / 16f);

                        int targetX = centerX + _currentTask.OffsetX;
                        int targetY = centerY + _currentTask.OffsetY;

                        int cleared = 0;
                        if (_currentTask.Shape == AreaShape.Circle)
                            cleared = ClearCircularArea(targetX, targetY, _currentTask.SizeParam1, _currentTask.ExcludedTiles);
                        else
                            cleared = ClearRectangularArea(targetX, targetY, _currentTask.SizeParam1, _currentTask.SizeParam2, _currentTask.ExcludedTiles);

                        totalCleared += cleared;
                        processed++;
                        if (processed >= _config.MaxClearsPerInterval)
                            break;
                    }

                    string offsetInfo = (_currentTask.OffsetX == 0 && _currentTask.OffsetY == 0) ? "" : $" 偏移({_currentTask.OffsetX},{_currentTask.OffsetY})";
                    LogInfo($"[怪物清除] 任务: 在 {processed} 个怪物周围清除了 {totalCleared} 个物块 ({_currentTask.Shape}{offsetInfo})");
                });
            }
        }

        private int ClearCircularArea(int centerX, int centerY, int radius, HashSet<int> excludedTiles)
        {
            int minX = Math.Max(0, centerX - radius);
            int maxX = Math.Min(Main.maxTilesX - 1, centerX + radius);
            int minY = Math.Max(0, centerY - radius);
            int maxY = Math.Min(Main.maxTilesY - 1, centerY + radius);

            int tilesCleared = 0;
            int radiusSquared = radius * radius;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                        continue;

                    int dx = x - centerX;
                    int dy = y - centerY;
                    if (dx * dx + dy * dy > radiusSquared)
                        continue;

                    if (excludedTiles.Contains(Main.tile[x, y].type))
                        continue;

                    if (RemoveTile(x, y))
                        tilesCleared++;
                }
            }

            int size = Math.Min(radius * 2, 255);
            TSPlayer.All.SendTileSquareCentered(centerX, centerY, (byte)size);
            return tilesCleared;
        }

        private int ClearRectangularArea(int centerX, int centerY, int width, int height, HashSet<int> excludedTiles)
        {
            int startX = Math.Max(0, centerX - width / 2);
            int startY = Math.Max(0, centerY - height / 2);
            int endX = Math.Min(Main.maxTilesX - 1, startX + width);
            int endY = Math.Min(Main.maxTilesY - 1, startY + height);

            int tilesCleared = 0;
            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                        continue;
                    if (excludedTiles.Contains(Main.tile[x, y].type))
                        continue;

                    if (RemoveTile(x, y))
                        tilesCleared++;
                }
            }

            int updateCenterX = (startX + endX) / 2;
            int updateCenterY = (startY + endY) / 2;
            int size = Math.Min(Math.Max(endX - startX, endY - startY), 255);
            TSPlayer.All.SendTileSquareCentered(updateCenterX, updateCenterY, (byte)size);
            return tilesCleared;
        }

        private bool RemoveTile(int x, int y)
        {
            try
            {
                WorldGen.KillTile(x, y, noItem: true);
                Main.tile[x, y].active(false);
                Main.tile[x, y].type = 0;
                TSPlayer.All.SendTileSquare(x, y, 1);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private List<NPC> FindTargetMonsters(string identifier)
        {
            if (identifier == "all")
                return Main.npc.Where(n => n != null && n.lifeMax > 0).ToList();

            if (int.TryParse(identifier, out int id))
                return Main.npc.Where(n => n != null && n.lifeMax > 0 && n.type == id).ToList();

            return Main.npc.Where(n => n != null && n.lifeMax > 0 && n.GivenOrTypeName.ToLower().Contains(identifier)).ToList();
        }
        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _clearTimer?.Stop();
                _clearTimer?.Dispose();
                SaveConfig();
            }
            base.Dispose(disposing);
        }
    }
}