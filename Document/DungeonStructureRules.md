# 地下城结构规则统一

日期：2026-10-07。改造基准：`41b26e36bf2e5ea6da8a57a900a6ed77a149b61b`，工作分支 `task/abyss-research`。

用户授权：按统一地下城结构规则服务的设计重构并部署。

## 边界与调用

`DungeonVisitDomainService → IDungeonStructureRuleService → IDungeonDefinitionSource / IDungeonMapDefinitionSource → PVF`。

原 `IDungeonLayoutRuleService` 已移除，三个能力统一在 `IDungeonStructureRuleService`：

| 能力 | 返回值与语义 |
|---|---|
| `GetDefinitionAsync` | 全部迷宫的结构声明：cell、连接、普通/Boss 地图候选、起点、Boss 候选及任务连接；不读取或写入运行中的世界 |
| `ResolveAsync` | 根据入场事实和显式种子选择本局迷宫、起点、Boss，并选出初始地图 |
| `SelectMapAsync` | 根据冻结布局、前一个 cell 和目标 cell 选择合法地图；不保存选择结果 |

`DungeonLayout.Cells` 保存结构声明。选定的 mapId 保存于 `DungeonRunStore` 中这一局的 cell 实例；回访使用原 cell，不重选、不重新创建怪物或掉落、不再次扣疲劳。`DungeonRoomRuleService` 继续解析已选地图的房间内容，`DungeonCellFactory` 创建完整 cell。

当前覆盖迷宫结构。无 `[maze info]` 的塔类副本仍走各自服务，不将空迷宫当作普通副本。武斗大会、逆袭之谷的声明可以查询，但 `EntryMode` 阻止它们绕过专属玩法从普通入口进入。读取到结构不等于实现了这些玩法。

## 选择规则

1. DGN `[greed]` 决定占用格及门连接；AA 表示空格。没有显式地图绑定的占用格也必须保留。
2. `[map specification]` 每条是种类、x、y、一个或多个 mapId。普通与 Boss 候选分开保存，不再按整数三元组截断，不再产生伪坐标。
3. 切房要求相邻且双方对应门均存在。方向位：右 1、上 2、左 4、下 8；进入右侧房间时，目标地图的入口方向是左 4。
4. 普通显式候选按 MAP 允许入口过滤；不额外要求显式候选的模式等于 cell 模式，也不在全部候选不兼容时任取其他地图。
5. 未绑定的普通格按 MAP `[dungeon]` 归属、普通种类、连接模式和入口方向查池。Boss 格使用 Boss 候选或 Boss 池；未绑定起点使用带 `[start area]` 的同模式地图。
6. MAP `[greed]` 两个 token 分别表示连接模式、允许入口掩码。例如 FF FF 为模式 5、入口掩码 5，不能作为一整段字符串比较。
7. 稳定排序、显式种子和 cell/进入方向生成选择。首次进入后由共享实例冻结；种子相同不会保证与原生随机数序列相同。
8. 无 cell、门不连通、无兼容 MAP、运行版本过期、领域状态拒绝分别记录。拒绝时不移动、不创建 cell、不扣疲劳；沿用原有静默拒绝协议行为。

MAP 归属索引只索引归属，按目标地下城解析地图元数据。实际 PVF 的其他地下城含 `[noamal]` 类型拼写，不能让这种无关类型异常阻断副本 77 的候选池。目标地图自身不支持的声明仍报内容错误，不假造正常规则。

同一 MAP 可被本局多个 cell 选用，因此 actor 序列基数加入 cell 坐标；这避免了同资源不同 cell 必然重用同一组已死亡 actor 身份。仍是有限范围的散列，不声称提供全局无碰撞分配器。

## 证据

- **CurrentImplementation / Confirmed**：基准实际日志在副本 77 的 `(1,1) → (2,1)` 以“无房间”拒绝；根因及原日志身份见 [切房诊断](DungeonRoomTransitionDiagnosis.md)。
- **ContentStatic / Confirmed**：A21 PVF SHA-256 `03154D18DE43F5A0BCB11B2935AE8BB40BABD22495700657D682E491A24A3543`；副本 77 的 maze 0 有 16 个占用格，`(2,1)`、`(2,2)` 未声明显式地图，`(1,0)` 有 15268、15316 两个候选。
- **70Reference / Confirmed**：ELF SHA-256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`。`ImportMapScript` 0x089DCF54（0x089DD607/630/65D：normal/boss/dummy 0/1/2），`load_map_files` 0x0835FE02，`CDungeon::InsertMap` 0x0834B988，`ChooseRandomMap` 0x0834BF56，`GetStartMap` 0x0834C75A，`CMap::CheckEntrance` 0x0834ECAA。
- **70Reference / Confirmed**：`CBattle_Field::get_invade_direction` 0x08307E44 确认方向掩码；`get_candidator_map` 0x08308214 区分显式候选/池；`set_map_id` 0x08308882 将选择保存到 MapInfo。不是按 MAP 文件名坐标优先匹配。
- **范围限制**：70 的特殊分层地图、机关条件和随机 Boss 替代流程仍需要各自语义接入；本次不声称覆盖全部原生特殊玩法，也不实现深渊生成和深渊爆率。

## 验证与实机入口

行为回归先复现两个问题：副本 77 仅解析 14 格（预期 16），同资源不同 cell 的 actor 基数相同（55376）；修复后相应回归通过。

实际 PVF 路线回归：`(0,1) → (0,0) → (1,0) → (1,1) → (2,1) → (3,1) → (3,2) → (2,2)`。覆盖队伍共享 mapId、回访保留 mapId、首次房间一次疲劳、无兼容地图保持房间/版本/疲劳不变。该领域回归使用空房间工厂以隔离选图行为，怪物/掉落及事务语义另由现有回归覆盖。

部署后请用角色 1123 重新进入谜之觉悟，清房后跨越上述路线并回访，再完成 Boss 通关。自动化通过不是 A21 客户端验收；本次客户端/PVF 均不修改。

最终自动化验证：完整解决方案回归后，再对最后修订的普通候选池规则运行完整 Application 回归。按各程序集最后一次结果汇总，6042 通过、0 失败、2 跳过；两个跳过是 PVF 可用时不适用的缺档测试。

发布与回退工件：`E:/Project/DNF/DNF-JP-86-Server/artifacts/dungeon-structure-20261007/`，包含 TRX、测试汇总、部署身份、数据库备份及启动日志。实际运行身份以部署完成后生成的 `deployment.json` 为准；旧运行目录保留。
