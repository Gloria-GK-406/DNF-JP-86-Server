# Handoff — 分支 `monster-drop-rates`

2026-08-30。换机器开发前的交接。这一份写给**换到别处、没有本机上下文的人**（包括以后的我）。

---

## 一、这个分支做完了什么

**一句话**：怪物死了会按档案掉东西，东西落地、能捡、金币入账、同类能并堆。

从 `main = 289a98ec` 开出。任务产物在 `.rks-agent/tasks/monster-drop-rates/`：
契约、spec、plan、评审、终验、以及**执行期间的取证 `archive-findings.md`**（最值得先读的一份）。

分两段。前一段是 SDD 走完的正式任务，后一段是**实机之后补的三个缺陷**。

### 第一段：怪物爆率（SDD 已封存，终验 pass）

| 层 | 件 |
|---|---|
| 内容源 | `PvfDropRateTableSource`（费率表 + 金币表）、`PvfDropItemPoolSource`（装备/堆叠物池） |
| 纯规则 | `DropRules`（门 / 稀有度 / 抽 / 金币额）、`DropRandom`（可复现随机源） |
| 领域 | `IMonsterDropService` / `MonsterDropService`、`IDropRateAdjuster` / `NoDropRateAdjustment`（保底空实现） |
| 本局状态 | `DungeonRunState.MonsterDrops` + `AllocateMonsterDrops`（编号分配） |
| 线级 | `NOTI 0x0026` 掉落记录、`CMD 0x002B` 拾取怪掉的东西 |

评审 `approved`（第一轮 3 阻塞 / 10 非阻塞，全部修完并回插变异验证）。
终验 `pass`，含一局实机。

### 第二段：实机暴露的三个缺陷（在终验之后，不在原契约范围内）

1. **怪掉的装备入包时耐久为 0**（准备实机时自查发现）。房间声明的掉落一直从 `.equ` 读
   `[durability]`，怪物这条路径硬编码了 0——**捡起来是把坏武器，而掉落帧根本没有耐久字段**，
   线上永远看不出来。现在耐久随物品从池子里带出来。
2. **金币捡不起来**。见下节，已实现。
3. **同类堆叠物不并堆**。`ItemArrivalRuleService.ForGrant` 两个分支对「限额 0」解读相反：
   开新格那支读作「不限」（注释白纸黑字），并堆那支读作「不堆」。而档案里**几乎没有堆叠物
   声明 `[stack limit]`**，读出来就是 0，所以材料一个也堆不上。既有缺陷，以前没有任何路径
   会给玩家两份同样的堆叠物，所以没人撞到。两支现在统一 `0 → int.MaxValue`。

### 金币服务

**整条管线本来就在，只是没人认出那个字段是钱**：

```
account_cargo_state.value32 → AccountInventory.Cargo.Value32
  → InventorySnapshotMessage.AccountValue32 → 物品列表包
```

上游在同一偏移写的正是 `AccountCargo.Money`，其修理 ack 注释写明「客户端直接覆盖显示」
（`CInventory::get_money`）。所以这是**给已有字段正名**，不是加字段。

* 入账：`IAccountCargoStateTable.AddValue32Async`，一条 upsert 内相加并夹在 `[0, int.MaxValue]`
* 发帧：金币与物品共用 `NOTI 0x0027`，**判别位在 `+4`**（物品 1、金币 0），金额是 `+6` 的 i32
* 拾取：金币先认领再入账，**不经背包**——没绑背包也能捡钱

---

## 二、还没做完的（按该做的顺序）

### 1. 设计图落在快捷栏 —— 等一个事实确认，然后是一行改动

`[recipe]` 在 `PvfEntryLifecycleContentSource.ResolveStackableItemKind` 里没有分类，
兜底成 **kind 2 = 消耗品**；而 `ItemArrivalRuleService.BandsFor` 让消耗品**优先进快捷栏**
（那是当初为新手教程发药水做的）。于是设计图被当药水塞进槽 6。

分页：装备 9–64、消耗品 65–120、材料 121–176、任务 177–232。

**卡在哪**：我没有档案或客户端的直接证据说设计图属于哪一页。凭 DNF 常识应是**材料页**
（kind 3），但这条一定要先确认再改——`ResolveStackableItemKind` 同时供每日补给用，改错了
会把补给也送错地方。确认方式：在客户端里看设计图显示在哪个标签页。

`[booster]`、`[waste]` 等也落在同一个兜底里，但**当前掉落池里只可能出现 `[recipe]` 和
`[throw]`**（可产出的堆叠物只有 760 件设计图 + 14 件投掷物），所以只有设计图这一条要紧。
投掷物进消耗品/快捷栏大概率是对的。

### 2. 怪掉的东西不进 `START_MAP`

重新描述房间时只带房间自己声明的可破坏物，怪掉的不会重新报一遍。
服务端这侧没丢（`FindMonsterDrop` 仍答得出、仍捡得到），但客户端重建地面列表后可能不再画它。
本期实机里回头进过房间、之后仍捡起来了，所以**未观测到实际损害**，但这是明确的缺口。

### 3. 上一个任务那三帧仍无实机确认

`CMD 0x0075 BOSS_DIE_CHECK`、`0x0045 SCORE_SCROLL`、`0x0048 EPLP_COMMAND`（提交 `6ce69953`）。
本期两局实机**一帧都没来**：新手副本和副本 144 都走 `GIVEUP_GAME` 退出。
结算与回城确实成了，但走的是另一条路。要验这三帧，得找一个真正用结算卷轴/离开按钮的副本。

### 4. 地面材料的数量

房间声明的箱子在本期实机里没被打开取走过，那条仍未验。

---

## 三、必须知道的三个环境事实

1. **跑任何测试前必须设 `PVF_ARCHIVE_PATH`**，否则读档案的判据会**假绿**（跳过）而不是红：

   ```
   export PVF_ARCHIVE_PATH=/d/project/DNF/DNFClient-A21-protocol/Script.pvf
   ```

   **档案必须与客户端版本一致。** A21 客户端配 A21 的 `Script.pvf`（72 MB, 2017）；
   设错版本不会报错，只会发错内容。

2. **起服**（`appsettings.json` 已配好频道 7001 / 游戏 10010、10011，不需要中继）：

   ```bash
   cd Server/src/Dfo.Server.Host/bin/Debug/net10.0
   NETWORKING__ENABLED=true \
   Pvf__ArchivePath="D:/project/DNF/DNFClient-A21-protocol/Script.pvf" \
   ./Dfo.Server.Host.exe
   ```

   客户端连 `127.0.0.1:7001`。数据库为空时服务端自建账号与角色。

3. **`DropRates:RateBasis` 默认 10000，实机调试时我用的是 100。**
   100 会让每刀必掉金币、约一半掉装备，方便一眼看出来；**测完务必拿掉**。
   见下一节——这个数本身是未定的。

---

## 四、留给下一个人的判断题

### 五个率的分母，档案里根本没有

`itemdropinfo_*` 全族没有基数段。两个候选：

* `Etc/ServerParameter.etc` 的 `[basis of item dicision] 2500`，但它紧挨着该文件**自己的**
  `[drop prob]`（3 段 × 2 个率），归属存疑
* 上游用 10000，它自己也没有出处

**两者差四倍**：普通怪掉金币是 7.5% 还是 30%。

做成了配置项 `DropRates:RateBasis`，默认 10000（复现 `Docs/DropRates.md` 第三节那张与
游玩记忆吻合的形态）。**规则本身吃传入的基数、不认识任何字面量**，并有判据
`The_basis_is_whatever_it_was_handed` 盯着这一点。改默认值是改一行配置的事。

### 金币帧那 107 字节尾巴没读懂

按常量原样搬的，依据是**两个独立抓包逐字节相同**（金额不同、拾取者不同）。
它按 14 字节的周期重复，含义未查明。

### 等级差掉落惩罚没做

`[drop bonus of dungeon level increase]`（+8→0.4 … 0→1 … −3→1.225）档案里有，本期验收未要求。
缺它是**偏高的有界偏差**，不是编造。

---

## 五、这次最该记住的一件事

**上游读错了装备池的整个来源，而且它的错法看起来完全正常。**

它从 `Etc/ItemDictionary/ItemDictionary.etc` 建装备池。那是**图鉴 UI 的索引**——13474 条，
而装备总数 118479——它的列义与上游的读法有五处对不上（第 1 列是陈列分类不是稀有度，
第 2 列是 `[minimum level]` 不是品级，被当权重的第 5 列全表只有 0 和 1）。

真正的闸是物品自己的 `[creation rate]`，与堆叠物同一条规则。**上游在堆叠物那一侧就是这么读的**
——它跟自己不一致，这是唯一的破绽。

本页 `Docs/DropRates.md` 第一版照抄了这条，第十四节记了推翻的过程。

同一份文档的第十四节还记了另外两条被推翻的前提和三条被坐实的事实。
**开工前先读 `archive-findings.md` 和 `Docs/DropRates.md` 第十四节。**

---

## 六、状态

* 构建 0 警告 0 错误；全套 **1690 通过 / 2 跳过 / 0 失败**（基线 1614）
* 那 2 个跳过是既有的、与本分支无关的档案相关判据
* 判据强度：**十一次变异，十一次变红**，含上游确实犯过的三处、评审找出的两处、
  以及耐久与堆叠这两个实机暴露的缺陷。清单在 `verification/completion-evidence.md`
* 分支**未合并到 main**。合并与清理工作区一直需要单独确认
