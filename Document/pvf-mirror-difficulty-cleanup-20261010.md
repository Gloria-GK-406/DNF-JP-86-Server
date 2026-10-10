# 镜像阿拉德指定难度字段清理

用户授权：清理镜像候选中重复的零难度字段；随后明确授权重启服务。保留严格解析器，只修改已扫描确认的 PVF 内容。

## 修改

11 张图：1～5（洛兰、洛兰深处、幽暗密林、幽暗密林深处、雷鸣废墟）、130～135（滚石监狱、黑石监狱、火石监狱、艾尔文防线突袭战、赫顿玛尔攻防战、机密重地）。

每个文件仅删除原来的 `[designate dungeon difficulty]` 标记及紧随的 Int32 `0`，共 22 个 token；保留末尾指定难度 `1` 和 `[difficulty] 1`。不改迷宫、怪物、掉落或解析器。档案内其余 7 个重复指定难度文件（6 个活动图和 1 个未列入目录的样例）不在本次范围。

可复现工具：`Tool/PvfDungeonDifficultyPatch`。输入必须匹配本次扫描的 SHA-256，并且 11 个目标文件都必须恰好包含先 0 后 1 的两次标量声明；工具拒绝原地修改、覆盖既有输出及未知输入。输出另存，并附 `.json` 差异清单。

```powershell
dotnet run --project Tool/PvfDungeonDifficultyPatch/PvfDungeonDifficultyPatch.csproj -- <原档.pvf> <新档.pvf>
```

## 档案身份和检查

- 原档 SHA-256：`03154D18DE43F5A0BCB11B2935AE8BB40BABD22495700657D682E491A24A3543`。
- 修正版 SHA-256：`D9723FC9A1BEDEEB1BC2EFBF16CA65E3E5537A58E3D911008C73240EA719BF37`。
- 档案文件总数 712010，修改 11 个；其余 711999 个文件表条目不变，原有 5769 个数据块和名称/哈希区不变。新增加一个保存修正版脚本的数据块。
- 全部 50 个镜像候选均通过部署版本的地下城定义解析，重复指定难度候选数为 0。
- 每图种子 0～15、难度 1、无任务、Interactive 的布局预检：46 张全部通过，4 张仍失败。有限种子检查不是实机验收或全部随机组合证明。
- 剩余布局问题：[#143](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/143)，包括原三张图和清理后新暴露的幽暗密林深处。详情见 [布局调查](investigations/mirror-arad-layout-followup-20261010.md)。

## 正式应用

已同步：`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` 和 `E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf`。上游参考客户端未改。

现有服务从原 executable 重启，版本仍为 `0.2.3+quota-direct.20261010.r1`，PID 26240。程序集 SHA-256 保持不变；配置和数据库路径不变。GM health、Launcher bootstrap 为 ready，6 个 TCP 监听均属于新 PID。

原 PVF 与停服后的 SQLite/WAL/SHM 备份：`artifacts/pvf-dungeon-difficulty-audit-20261010/backup-20261010-144646/`，数据库备份 quick_check 为 ok。首次落位遇到 PowerShell 空备份路径参数错误，已自动恢复旧档并重启旧服务；在临时文件验证明确备份路径后再次应用成功。

最新启动记录：`artifacts/pvf-dungeon-difficulty-audit-20261010/activation.json`，日志为同目录 `activation-stdout.log`、`activation-stderr.log`。原配额任务的 deployment.json 是早一轮启动历史，本次 PVF 更新以 activation.json 为准。

待实机确认：除 issue #143 中四张图外，镜像图入场不再因重复难度字段被拒绝。
