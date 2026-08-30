---
kind: rks-spec-result
outcome: completed
---

# Spec result: 怪物死了会按档案掉东西

一期完成。杀死一只怪现在按档案声明掉出金币与物品，东西落在地上，能捡起来；
掉什么、几件、什么品质，每个数都指得回档案里的某一行。

九条成功条件全部有新鲜证据，含一局实机（`verification/live-run-20260830.log`）。
线级那一半是对**两帧真实抓包的逐字节复现**，不是自证。

## 执行期间推翻的三条前提

这是本任务最值得记住的部分。契约与 `Docs/DropRates.md` 都写在实现之前，
下面三条是**写下来之后被自己的取证推掉的**。

### 一、装备池不是 `ItemDictionary.etc`

契约的 Context 和文档第五节都按上游写着装备取自这份索引、第 5 列 `genRate` 决定入池。
把 13474 条字典行与各自的 `.equ` 全部对上之后，五处对不上：第 1 列是图鉴的陈列分类不是稀有度
（8 个取值到 `.equ` 稀有度不是恒等映射），第 2 列是 `[minimum level]` 不是品级
（13474 行只有 5930 行与 `[grade]` 相等），被当权重的第 5 列全表只有 0 和 1。

它与 `dungeondropinfo`、`independentdropinfo` 同在 `etc/itemdictionary/`，与 `ui/itemdictionary/*.xui`
成对——**是图鉴 UI 的索引**，13474 条，而装备总数 118479。

真正的闸是物品自己的 `[creation rate]`，与堆叠物同一条规则。
**上游在堆叠物那一侧就是这么读的**，只有装备那侧换成了字典——它与自己不一致，这就是破绽。
这是上游在这一族里的第五处编造，也是本任务路由决策预判的失败形态（「照抄上游」）真正发生的地方。

### 二、稀有度第六档是远古，不是传说

`[drop upgrade rarity]` 按升序列了七个标签，物品 `[rarity]` 正好取这七个值；
用物品名核对：稀有度 4 是铁脊蛇矛与方天画戟（90 级史诗），稀有度 6 是安徒恩的抵抗者（传说）。
阶梯只有六档，所以传说走不到——不损失什么，档案里没有自称可产出的物品在史诗之上。

**Spec 的 RQ-003 仍写着「史诗与传说」。** 有意不改：事后编辑已定稿的 Spec 会抹掉
「这条读法在执行中变了」这个记录，而那正是本任务产出的最有价值的东西。
代码、注释与判据全部按远古；文档第四节与第十四节记了更正。评审同意这个处置。

### 三、怪物档次早就建模了

契约 Assumptions 写着本仓只知道 `ActorType`（区分敌我），因此一期要「按普通处理」并承认偏低。
不成立：`PvfDungeonRoomActorSource.RankOf` 读的就是地图的引号词
（`[normal]`/`[champion]`/`[super champion]`/`[boss]` → 0..3），与 `[monster type drop bonusrate]`
的 4 档一一对应。档次倍率取得到真值，那个缺省和那个偏差都不存在。

## 执行期间坐实的两条

* **`[item drop ref table]` 就是等级→品级带**（`等级 向下 向上`）。文档原先完全没提这张表，
  品级带无出处。自证：86 级往上每一行 `等级 − 向下` 恒等于 79，正是 A21 顶级装备的品级下沿。
* **48 字节的掉落记录，以及金币是物品号 0。** 契约 Assumptions 说布局只有上游构包器一个来源、
  未用真实帧验过。上游两个抓包共 6 帧带掉落，按 `3 + 48n + 4` 全部整除、该为零的 24 字节全为零；
  44 或 52 字节的读法解不开那两帧 103 字节的。编码器现在逐字节复现其中两帧。
  同一批帧还坐实了金币金额写在数量字段，以及一次击杀确实可以同时给金币和物品。

## 需要你定夺的一件事

**五个率的分母档案里根本没有。** `itemdropinfo_*` 全族没有基数段。
`Etc/ServerParameter.etc` 有 `[basis of item dicision] 2500`，但它紧挨着该文件*自己的*
`[drop prob]`（3 段 × 2 个率），归属存疑；上游用 10000，也没有出处。

**两者差四倍**：普通怪掉金币是 7.5% 还是 30%。

按用户既有约束「pvf 外的服务端配置最好能配置化」做成 `DropRates:RateBasis`，
默认 10000（复现文档第三节那张与游玩记忆吻合的形态）。规则本身吃传入的基数、
不认识任何字面量，并有一条专门的判据（`The_basis_is_whatever_it_was_handed`）盯着这一点。
改默认值是改一行配置的事。

## 交付

新增 9 个文件、修改 11 个。

| 层 | 件 |
|---|---|
| 内容抽象 | `Content/DropRateContent.cs`、`Content/DropItemPoolContent.cs` |
| 内容源 | `Content/PvfDropRateTableSource.cs`（费率表 + 金币表）、`Content/PvfDropItemPoolSource.cs` |
| 纯规则 | `World/DropRules.cs`（门 / 稀有度 / 抽 / 金币 + `DropRateOptions`）、`World/DropRandom.cs` |
| 领域 | `World/IMonsterDropService.cs`、`World/IDropRateAdjuster.cs`、`World/MonsterDropService.cs`、`World/NoDropRateAdjustment.cs` |
| 本局状态 | `IDungeonRunDomainService` / `DungeonRunDomainService`：`AllocatedMonsterDrop` 与编号分配 |
| 线级 | `MonsterDeathSlice`（掉落记录）、`GroundPickupSlice`（取走怪掉的东西）、`DungeonEntrySlice`（共享判别值入口） |
| 登记 | `InfrastructureModule`、`ApplicationModule` |
| 文档 | `Docs/DropRates.md` 第十四节；任务内 `archive-findings.md` |

## 范围外、已记录

| 件 | 为什么 |
|---|---|
| 翻牌 | 二期。**注意：不是「换一张表」那么简单**——`itemdropinfo_clearreward.etc` 的 `[drop prob]` 是按 profile 命名分组、每段只有 1 个率，那一段要另写读法；其余各段可直接复用 |
| 等级差掉落惩罚 `[drop bonus of dungeon level increase]` | 档案里有（+8→0.4 … −3→1.225），本期验收未要求。缺它是偏高的有界偏差，不是编造 |
| ~~拾取金币~~ | 封存时列为 Out，实机之后补上了——见下一节 |
| 怪掉的东西不进 `START_MAP` | 重进同一间房，房间声明的箱子会重画、怪掉的不会。编号仍认得出、捡得到。二期之前要补 |
| 独立掉落 / 世界掉落 / 次元 / 深渊 / 可破坏物另掷 / 怪物自有池 | 各自是后面的一件 |
| 补偿与保底的实现 | 只留了改率那条缝，一期空实现 |

## 封存之后又改的（实机暴露）

终验封存、判 `pass` 之后跑了两局实机，暴露三个缺陷。**它们都不在本契约范围内**，
但都在同一个分支上修掉了，因为放着不修会让这一期的成果在玩家手里是坏的。
交接见分支根目录的 `HANDOFF.md`。

1. **怪掉的装备入包时耐久为 0** —— 我自己引入的。掉落帧没有耐久字段，所以线上看不出来，
   只有捡起来才知道是把坏武器。耐久现在随物品从池子里带出来（4218/6062 件带耐久，
   其余 1844 件是戒指项链魔法石等本来就没有的）。
2. **金币捡不起来** —— 本契约把「金币进角色账户」列为 Out，实机里它占了 23 次拾取尝试中的 11 次，
   体感就是坏的。查明金币管线**本来就通到底**（`account_cargo_state.value32` 已落库、已在
   物品列表包里发出去，正是上游写 `AccountCargo.Money` 的偏移），于是补齐：入账 + 那条
   117 字节的拾取帧（两个抓包逐字节对死）。
3. **同类堆叠物不并堆** —— 既有缺陷。`ItemArrivalRuleService.ForGrant` 的两个分支对
   「限额 0」解读相反，而档案里几乎没有堆叠物声明 `[stack limit]`。以前没有任何路径会给
   玩家两份同样的堆叠物，所以没人撞到。

一件**未决**：`[recipe]` 未被分类、兜底成消耗品，因而设计图落进快捷栏。需要先确认
客户端里设计图属于哪一页再改，见 `HANDOFF.md`。

## 判据强度

十一次变异十一次变红，含上游确实犯过的三处、评审找出的两处、分母写死，
以及封存后实机暴露的耐久与堆叠两处。清单在 `verification/completion-evidence.md`。
