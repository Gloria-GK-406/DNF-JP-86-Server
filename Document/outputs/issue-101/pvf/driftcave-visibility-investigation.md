# DriftCave 150..155 未进入客户端候选容器：PVF 与任务投影核对

日期：2026-09-20。范围为 PVF、当前服务端源码和运行库的只读核对；没有修改生产代码、协议文档或数据库，也没有启动或操作客户端。

## 输入身份

- 调查档案：`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`
- SHA-256：`19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A`
- 当前运行服务命令行指向 `E:/Project/DNF/DNFClient-A21-debug/Script.pvf`；其 SHA-256 与上述档案相同。
- 当前运行服务使用 Release 数据库；只读快照中 account 3 的 character 1 为 85 级，与 T01 的角色映射一致。
- DGN/WDM 原始提取：[driftcave-raw.txt](driftcave-raw.txt)，SHA-256 `9BF223946A41A0E542BE11908B1A142E8EB41BEFD594BDE2B7187D115C15EC50`
- 关键任务原始提取：[driftcave-quests-raw.txt](driftcave-quests-raw.txt)，SHA-256 `621E92C8063ECA3313CF1BFAF0900E3EB549EC330CA7B6B5426B38BD7A48D767`
- 首张普通 MAP 原始提取：[driftcave-first-maps-raw.txt](driftcave-first-maps-raw.txt)，SHA-256 `F18B07F53DE9FD04653C085B74436B4FE5A91206B1AE6203B39BC614D099B44E`
- 机械牛对照提取：[vilmark-visibility-control.txt](vilmark-visibility-control.txt)，SHA-256 `AE7D9D45FDFB8C933FAF5115CE6BC8FAECF3E0A2B45F9818CE861887B2515F55`
- 客户端已加载 WDM：[t01-loaded-wdm.json](../live/t01-loaded-wdm.json)
- 客户端地下城任务门运行态：[t01-dungeon-quest-gate.json](../live/t01-dungeon-quest-gate.json)
- 客户端任务资源类型：[t01-quest-resource-types.json](../live/t01-quest-resource-types.json)

## 已闭合的 PVF 差分

| dungeonId | 名称 | minLevel | `[story mode]` | 普通 Maze | quest Maze | 额外显著字段 |
| ---: | --- | ---: | ---: | ---: | ---: | --- |
| 150 | 蜘蛛王国 | 27 | 有 | 2 | 10 | story quest list |
| 151 | 英雄冢 | 29 | 有 | 7 | 10 | story quest list |
| 152 | 暴君的祭坛 | 31 | 有 | 4 | 10 | story quest list |
| 153 | 黄金矿洞 | 33 | 有 | 2 | 12 | story quest list |
| 154 | 远古墓穴深处 | 35 | 有 | 1 | 14 | story quest list；WDM 另带 1895 |
| 155 | 邪龙的封印 | 54 | 有 | 1 | 10 | story quest list |
| 8007 | 黄金王的秘密洞穴 | 36 | **无** | 1 | 0 | `[escape hell] 1`、`[prohibit practice] 1` |

七个 DGN 的 `[difficulty level]` 都是 `1 1 1 0 0`，且当前角色 85 级。因此难度声明和最低等级都不能解释“150..155 全部不进入而 8007 进入”。六个被排除项共同具有、8007 不具有的静态声明是 `[story mode]` 及其任务表；仅凭这一静态差分尚不能断言“非 story 即显示”，后文以客户端谓词说明真正的判断单位是关联任务集合。

`worldmap/driftcave.wdm` 的原始 `[dungeon]` 序列为：

```text
150 -1 151 -1 152 -1 153 -1 154 1895 155 -1
239 [in progress] 1925 239 [in progress] 1927
8007 -1 2011 13505
```

150、151、152、153、155 与 8007 的第二操作数完全相同，都是 `-1`。因此 **WDM 的 `-1` 条件不能单独造成当前差分**。154 另有显式任务 1895；即便 1895 未满足可以解释 154，也不能解释其余五张图。`[in progress]` 只修饰 239 的两项，不覆盖 150..155。

## 显示类字段穷举

使用同一 PVF 对全部 1,047 个 DGN、10,293 个 MAP 和 54 个 WDM 脚本枚举标签，并单独读取 150..155、8007 及其首张普通 MAP：

- DGN 中没有 `[always visible dungeon]`、`[visible]`、`[unVisible dungeon select item]`、`[open visible event]`、`[map condition]`、`[required quest]`、`[enter]`、`[open]`、`[display]` 或 `[permission]` 标签。
- MAP 中没有 `[always visible dungeon]`、`[visible]`、`[map condition]`、`[quest connection]`、`[story mode]`、`[required quest]` 或 `[open visible event]` 标签。
- WDM 的标签表包含 `[dungeon]`、`[in progress]`、hell/item condition 等声明，没有另一组 show/hide/permission/display 标签。
- 字符串池全局能找到 `[always visible dungeon]`，但它没有出现在任一 DGN/WDM/MAP 脚本中，尤其不在本次比较的七个 DGN 或其 MAP 中；不能据此给这些地下城虚构一个隐藏字段。
- 154 的 `[open door even enemy]` 是房间门行为，不是地下城选择列表显示门。

因此，在实际 DGN/WDM/MAP 脚本层没有发现类似“机械牛式”的未列出显示 flag；客户端后续取证证明真正的普通列表门来自 DGN 定义内的关联任务集合，而不是另一个隐藏文本标签。

## 机械牛（比尔马克试验场）对照

PVF 中存在两套比尔马克定义，恰好能区分“显示门”和“进入消耗”：

1. 任务版 dungeon 240（`dungeon/Ancient/Q_Vilmark.dgn`）有 `[story mode]`，quest list 为 2309..2312，全部 Maze 都带 quest connection。`worldmap/Abnoba.wdm` 将同一 dungeon 240 重复写成 `240 [in progress] 2091`、`240 [in progress] 2092`、`240 [in progress] 2097`、`240 [in progress] 2098`。这里 **WDM `[in progress] questId`** 才是明确的任务进行中显示条件。
2. 普通/远古版 dungeon 1500（`dungeon/Ancient/N_Vilmark.dgn`）有 `[required item] 3037 120 1 3332 10 1` 和 `[ancient dungeon] 1`；`worldmap/Ancient.wdm` 对它使用 `1500 -1`。`[required item]` 是进入成本，不能当成列表显示门。

DriftCave 150、151、152、153、155 使用 `-1`，没有任务版机械牛的 `[in progress]` 条件；154 使用裸任务操作数 `1895`，其确切判断方向仍需读客户端谓词或做单变量实测。故机械牛机制不能解释前五张 DriftCave 普通图同时缺失。

## 150..155 的具体 PVF 任务 ID

| dungeonId | `[story mode]/[quest list]` | quest-connected Maze |
| ---: | --- | --- |
| 150 | 1856、1857、1859、1863、1864 | 1856、1857、1859、1863、1864；另有 14930..14934 |
| 151 | 1866、1867、1869、1871、1872 | 同左；另有 14935..14939 |
| 152 | 1875、1877、1878、1881、1882 | 同左；另有 14940..14944 |
| 153 | 1885、1887、1889、1890、1891、1892 | 同左；另有 14945..14950 |
| 154 | 1895、1896、1899、1900、1919、1920、1923 | 同左；另有 14951..14957 |
| 155 | 2100、2107、2113、2115、2116 | 同左；另有 14958..14962 |

每张图的首个连接任务及直接前置为：`1856 <- 1854`、`1866 <- 1855`、`1875 <- 1874`、`1885 <- 1884`、`1895 <- 1894`、`2100 <- 2099`。这些任务均存在于当前 PVF；不是缺文件或错误 task id。

## 当前角色实际任务状态与服务端投影

Release 数据库的只读快照：

```text
character_active_quests:       quest 2489 only
character_invisible_flags:     101, 1016, 1776, 1777, 1778, 1779, 1780, 1781, 1782
character_quest_notify_selections: empty
character_dungeon_permissions:     empty
```

任务 2489 是 85 级安徒恩主线“与安徒恩的对决”，不是 DriftCave 任务。当前角色在 1782 与 2489 之间没有连续的已清主线状态，且没有上述任一 DriftCave story/maze 任务或它们的直接前置。

客户端内存取证已闭合 NOTI `0x0164` 的落点：handler `0xD641F0` 调用 `0x15A59C0`，将 30,000 字节复制到 `0x03A77108`；getter `0x15A5940` 直接读取 `base[questId]`。T01 实际读取结果见 [t01-client-quest-flags.json](../live/t01-client-quest-flags.json)：101、1016、1776..1782 为 1，而 1854、1855、1856、1857、1859、1863、1864、1866、1875、1885、1895、2100 及 active quest 2489 均为 0，与数据库一致。因此这里不是 `0x0164` 同步丢失；当前角色的源数据本身没有这批完成标记。

服务端并非完全漏发任务状态：

1. `CharacterEntryAcceptedMessage`（CMD `0x0004`）携带 active quests。
2. `AvailableQuestListMessage`（NOTI `0x0015`）由 PVF 任务定义、等级/职业和已清任务计算可接列表。
3. `QuestVisibilityMessage`（NOTI `0x0164`）发送 30,000 字节、按 quest id 索引的 `character_invisible_flags`。

但是，当前角色的六个首个 DriftCave story 任务都不能由现有 offer 规则进入 `0x0015`：规则要求 `[pre required quest]` 已清，而 1854、1855、1874、1884、1894、2099 均不在当前 clear flags。源码/数据库分别表明它们不在 active quests 或服务端 clear flags；客户端内存进一步确认 `0x0164` clear bitmap 中相应值也是 0。这里的 available 状态是服务端规则推导结果，不是已经抓到并解码的 `0x0015` 实帧。

源码还给出两个明确的实现边界：

- `PvfDungeonDefinitionSource` 不读取 DGN 的 `[story mode]` 或 `[quest list]`，所以当前服务端不能从地下城定义推导这组客户端可见性关系。
- `character_invisible_flags` 的生产写入只来自任务完成；未找到“角色升到 85 级时补齐此前主线”的投影或初始化路径。当前数据表现为早期任务清到 1782、随后直接持有 2489，这个进度断层是明确存在的数据缺口。

上述服务端与任务状态证据证明当前角色没有 DriftCave active/clear 状态，且按现行服务端规则也不会获得首批 available 状态；这些材料本身不证明客户端 gate。下节的客户端反汇编与 RPM 证据独立闭合实际谓词。

## 客户端普通列表任务门（High：静态反汇编 + RPM）

客户端运行态首先排除了 WDM 加载缺失：[t01-loaded-wdm.json](../live/t01-loaded-wdm.json) 中 `driftcave.wdm` 的十个条目全部存在，包括 150..155、239 的两个任务条件条目、8007 和 2011。

普通地下城列表函数 RVA `0x231D490` 调用 RVA `0xA62D90` 检查每个地下城定义。该谓词遍历 `definition + 0x168` 的关联 quest id 集合，并按任务资源类型与角色状态判定：

- active 集合（manager 的 `+0x08/+0x0C` 区间）存在满足类型条件的关联任务时返回 true；
- manager `+0xB0` 的 completed 集合命中 type 0 关联任务时返回 true；
- 若遍历中存在 type 0 任务，但 active/completed 都没有符合项，则返回 false。

[t01-dungeon-quest-gate.json](../live/t01-dungeon-quest-gate.json) 证明运行态解析出的关联集合与 PVF 一致；8007 的集合仅为 `[-1]`。[t01-quest-resource-types.json](../live/t01-quest-resource-types.json) 直接读取任务资源：150、151、152、153、155 对应的 1856、1866、1875、1885、2100 等主线任务 `+0x9C` 类型值为 0，而 14930、14935、14942 等 circle 任务为 15。154 另受 WDM 1895 条件，留在下一小节单独说明。

T01 运行态 active 集合只有 2489，completed 集合只有 101、1016、1776..1782。150、151、152、153、155 的 type 0 关联任务均未命中，因此谓词返回 false；8007 没有 type 0 关联任务触发失败分支，因而通过。这一链条解释了“WDM 十项都加载，但普通候选只剩 8007”，也排除了“8007 必须靠 escape-hell/circle 特殊强制包含”才能解释当前现象的假设。

证据等级为 **High（静态反汇编 + RPM）**。尚未通过改变任务状态后重新打开 UI 观察列表完成行为复测，因此不能标为 `ClientVerified`。

最小人工验证路径是：先完成/补齐 quest 1854，再进入暗黑城接受 quest 1856“探索蜘蛛王国”，重新打开 DriftCave 地下城选择 UI，确认 dungeon 150 出现。验证前后应同时保存 active/completed 集合和普通候选容器，避免只凭 UI 名称判断。

### dungeon 154 的独立 WDM 条件

154 与前五项不同：其 WDM 条目先携带裸任务操作数 1895，而普通列表任务门内还会遍历 154 自身的关联任务集合。故 154 需要单独验证 WDM `1895` 条件，不能用前述五张 `-1` 普通图直接类推。当前证据已经解释 150、151、152、153、155 的共同缺失，不需要依赖 154 才能成立。

## 当前可下的结论

1. world-map 23 和 WDM 入口已匹配；运行态已确认十个 WDM 条目全部加载，所以不是 WDM 资源缺项。
2. 普通列表函数通过地下城定义 `+0x168` 的关联 quest id 集合实施任务门。存在 type 0 关联任务但 active/completed 无命中时，该地下城被过滤。
3. 机械牛任务版确有 WDM `[in progress] questId` 显示门；DriftCave 前五图没有该门，154 的裸 `1895` 需要另行判义。`[required item]` 只证明进入成本。
4. 当前 85 级角色没有任何 DriftCave 关联 active/clear 状态，按服务端现行规则也不会产生首批 available 状态；客户端 clear bitmap 与数据库一致，所以这不是 `0x0164` 同步遗漏，而是角色进度源数据断层。
5. 150、151、152、153、155 因各自 type 0 主线任务未命中 active/completed 而被过滤；8007 只有 `[-1]`，因此通过。该结论为 High，仍需完成 1854、接受 1856 后观察 dungeon 150 的 UI 行为才能升级为 `ClientVerified`。
