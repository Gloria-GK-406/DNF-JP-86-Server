# 清房后无法切房：谜之觉悟布局漏房

日期：2026-10-07。阶段：诊断，尚未修改生产逻辑或重新部署。

## 结论

本次失败发生在地下城 77「谜之觉悟」、普通迷宫 0、冒险难度，从 `(1,1)` 请求进入 `(2,1)`。PVF 的 `[greed]` 将目标格声明为 `FF`，存在属于地下城 77 的普通地图资源 `15305`，但 `[map specification]` 没有显式列出该格。当前实现只将显式地图声明放入 `DungeonLayout.Rooms`，因此将合法房间当作不存在，未返回新的 START_MAP。

这是服务端布局构建缺失，不是本次证据中的清怪开门通知丢失。不能因此断言其他切房故障都属于同一原因。

## 当前运行证据

- 服务端：`artifacts/difficulty-unlock-20261007/runtime`，提交 `41b26e36bf2e5ea6da8a57a900a6ed77a149b61b`。
- 客户端进程路径：`E:/Project/DNF/DNFClient-A21-protocol2/DNF.exe`，PID 32312。
- 角色：CharacterId 2，用户测试角色 1123。
- `stdout.log:3704`：11:33:06 新 run，地下城 77，Adventure。
- `stdout.log:4442`：11:34:04 成功进入 `(1,1)`，地图 `15304`。
- `stdout.log:4708`：11:34:28 怪物死亡正常处理。
- `stdout.log:4737-4738`：11:34:32 收到切房请求，但拒绝 `(2,1)`，留在 `(1,1)`。
- 服务端使用的 `DNFClient-A21-protocol/Script.pvf` 与实际客户端 protocol2 的 PVF SHA-256 相同：`03154D18DE43F5A0BCB11B2935AE8BB40BABD22495700657D682E491A24A3543`。

证据边界：没有本次全量抓包；上述请求与拒绝来自服务端实机日志。客户端的 FrameCapture 配置虽启用，但当前插件清单未加载 NetworkHook，未生成对应日志。

## PVF 与代码证据

通过只读 `Tool/PvfProbe` 查询实际 PVF：

- `dungeon/dungeon.lst` 中 77 对应 `dungeon/timegate/consciousness.dgn`。
- maze 0 大小 `5×4`，第二行网格为 `CC LL FF NN GG`，即 `(2,1)=FF`。
- `(2,1)` 未在 `[map specification]` 中显式列出。
- `map/map.lst` 中 15305 对应 `map/consciousness/15305(2.1)normal.map`，声明 `[dungeon] 77`、`[type] [normal]`、`[greed] FF FF`。
- `(2,2)` 同样未显式列出；存在地图 15310，属于地下城 77、普通类型，网格为 `FF FF`。

当前实现：

- `PvfDungeonDefinitionSource.cs:209-241` 只解析 `[map specification]` 并填充 Rooms，没有读取 `[greed]` 或解析隐式房间。
- `DungeonVisitDomainService.cs:135-136` 在目标不属于 Rooms 时返回 null。
- `DungeonEntrySlice.cs:710-723` 收到 null 后记录拒绝并返回，不发送 NOTI 0x001D START_MAP。此日志将所有 null 原因都称为「没有房间」，后续应区分失败原因。
- 最近难度解锁提交没有修改 DungeonEntry 或 DungeonLayoutRuleService；这次解锁后进入新的路线暴露了现有缺口。

上游参考（Current upstream implementation，不能当作 A21 原生算法已确认）：`ServerS4A21/Server/DfoServer/GameWorld/DungeonMazeTopology.cs:146` 将 greed 中开放格纳入拓扑；`DungeonMapResolver.cs:255-305,709-766` 先处理显式声明，再按资源归属、房间类型、greed 等选择隐式房间。

## 修复方向

1. 内容层保留完整网格拓扑与地图候选信息，不把显式声明当成房间全集。
2. 布局规则服务按所属地下城、房间类型、门布局与资源约束解析隐式房间，入场时冻结完整布局；显式指定的地图仍优先。
3. 切房继续查询本次实例冻结的布局，不能临时换迷宫或放开无效目标。
4. 回归覆盖地下城 77 的 `(1,1)→(2,1)`、`(1,2)→(2,2)` 与完整路线，并保持无房格拒绝。

未执行修复、部署或修复后的客户端验收。


## 后续修复

2026-10-07 按统一结构规则服务完成重构，修复范围与验证边界见 [DungeonStructureRules.md](DungeonStructureRules.md)。上文保留诊断基准，不代表部署后的固定坐标/mapId 实现仍存在。
