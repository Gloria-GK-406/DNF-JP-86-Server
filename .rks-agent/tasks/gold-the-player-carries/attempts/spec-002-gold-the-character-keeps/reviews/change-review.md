---
kind: rks-review
verdict: approved
---

# 完整变更评审：角色身上的金币

范围：完整变更集，对照 TaskContract revision 1 的 CSC-1..CSC-7、spec.md 的 RQ-001..RQ-010 与 plan.md 的 T-001..T-007 / CK-001..CK-018。基线 `e4f29e1d`，评审时未提交。由独立的新上下文评审者进行，两轮。执行者未参与判决。

## 第一轮：`changes_required`，五条阻断

评审者先独立核实了本次的中心设计决定——随身金币存为 `character_stats.carried_gold` 一列而非背包物品行——所依据的五点理由，**全部为真**，其中两点比执行者的陈述更强：

- 容器写只能读-改-写：`IStoreSession` 的词汇只有按位置读/写/清空，没有任何增量原语。
- 按角色的执行闸零消费方：不仅无注入，两条命令都只实现 `ICommand<T>`、未传 `UserAccessMode`，执行行为直接放行、不取租约。

故存储决定成立，不构成阻断。五条阻断是：

| ID | 发现 |
|---|---|
| BF-1 | `InventoryModels.cs` 那段「Slot 0 is read and sent but never written…Earnings currently go to the account cargo」三个分句在本次改动后**全部为假**，且 `InventoryEntryAssembly` 的注释被改成指向它——**主动把读者引向一段断言反面的记述** |
| BF-2 | `GroundPickupTests.cs` 的 remarks 仍写着金币「stored as `account_cargo_state.value32`」。执行者删了下面一段更弱的假话，**留下了上面这句更直接的** |
| BF-3 | `Docs/新功能接入规范.md:185` 仍写「落库路径仍然没有」。该文档自己定的判据（打一把、赚钱、重登、数字还在）已被实机满足，而文档说的是反面 |
| BF-4 | **携带上限链零判据**。更严重的是四处 `PlayingCharacter` 构造全用 `NoCarriedGold`（`FindAsync` 恒 null），`AddGoldAsync` 在守卫处即返回——「读上限→问规则→交给语句」这条链**一次都没被执行过** |
| BF-5 | 地面拾取的通知帧带的是**请求额**而非实际入账额。上限截断时客户端累加请求额、服务端存钳后额，二者错开直到下次登录——**重现本任务要消灭的签名失败模式** |

## 整改

五条阻断全部整改，另加两条评审列为非阻断、执行者判定必须修的：

- **BF-1/2/3**：三处记述改写为与代码一致。`Docs/新功能接入规范.md` 的历史段落保留并加注「它记的不是缺什么，而是一种失败的形状」——评审者复核时评价为「比要求的处理更好，把一条过时的事实转成了仍然有效的诊断线索」。
- **BF-4**：新增 13 条判据。`CarriedGoldCapTests`（7 条）用**记录型**替身断言交给存储的 ceiling 本身，而非观察数字变化——因为传 `int.MaxValue` 也会让数字同样地动；并成对覆盖「档案下限」与「已买升级」两个分支。`PvfGoldCarryLimitSourceTests`（6 条）按档案实值转写。
- **BF-5**：增量帧后紧跟一帧绝对值。
- **非阻断 1（执行者主动修）**：并发判据的注释声称「Actually in parallel」是**事实性错误**——该驱动的异步成员同步完成，`Task.WhenAll` 实为顺序执行，而执行者曾用这句话批评账号金库那条判据强度不足。改为 `Task.Run` + `Barrier` 真并行。证据：真并行后 32 个竞争者耗时由 371ms 升至 16 秒，降至 8 个约 1 秒。
- **非阻断 5**：`protocol/NOTI-0x000E.md` 补记第三个发送方与「赋值而非累加」语义。

## 第二轮：`approved`

评审者逐条核销，并特别验证了两点：

**BF-4 不是又一次空覆盖。**按「能不能改坏代码而判据仍绿」逐环推演：替身返回非 null 使守卫被穿过，链路真的执行；断言的是交出去的 ceiling 而非数字；两个分支成对钉住 `Math.Max`；用的是真规则服务与真 `PlayingCharacter`。**并独立验证档案值非自证**——评审者用另一条读法直接 dump 档案，四个 `InlineData` 逐值命中原文，同时证实「档案确实为 level 0 声明 limit 0」这条反复出现的论断是事实而非修辞。

**BF-5 不重复计数**，但依赖面确实变了：改动前拾取在两种客户端读法下都对，改动后只在「0x000E 是赋值」为真时才对。评审者据此提出放行条件。

## 放行条件与其闭合

评审者要求：拾取路径的帧集在整改中变了，而现有实机证据取自变更之前且只覆盖翻牌，故需一次地面金币拾取的实机核销。

**已由授权方执行并闭合，三方吻合：**

```
服务端日志   drop 4 is 46 gold, character now carries 199513
数据库       carried_gold = 199513
客户端显示   199513          （199467 + 46）
```

若客户端对 0x000E 是累加，显示会是 199559。**赋值语义至此有两个独立取证**：翻牌那次（累加会显示约 399467）与这次拾取。此前只有前者一个间接证据。

## 评审者自行核销的证据

- 全量判据自跑：`488+52+60+255+6+221+933 = 2015 通过 / 2 跳过 / 0 失败`，exit 0。逐程序集对基线 1993：Persistence +7、Host +2、Infrastructure +6、Application +7；Protocol / Networking / Pvf **一个不少**，无程序集下降。
- 两个跳过仍是缺档案守卫（档案已加载的证据）；`PvfGoldCarryLimitSourceTests` 的 6 条同用该守卫且**全部运行**（215→221），未静默跳过。
- 单一入口复查：`UPDATE character_stats` 仍只有两处；`AddGoldAsync`/`TrySpendGoldAsync` 调用点仍只有翻牌三处 + 拾取一处，**无新增旁路**。另核实 `SqliteCharacterStatsTable.InsertIfAbsentAsync` 是 `ON CONFLICT DO NOTHING` 且列清单不含 `carried_gold`，无整行覆盖旁路。
- `gold_carry_limit` 恒为 0 的断言属实：全仓只出现在两条 SELECT 里，无任何 INSERT 列清单或 UPDATE。
- 三处期望值变更逐处判定为**新功能的定义性后果**而非为绿而改；`CardRewardTests` 的改动是**加强**（由断言帧数改为断言金额）。
- `TimeProvider` 已在 `ApplicationModule` 注册为单例，新注入不会在运行时解析不出。

## 那个未解释的观察

评审者第一轮指出执行者遗漏了第四种日志形态：命令内部抛异常时日志出自 `CommandLoggingBehavior`，形状与「成功 / refused / no published session」三个关键词都不同。执行者据此补搜 `fail:|Exception|Unhandled exception|at Dfo\.Server`，**命中 0**，全场只余两条无关 warn。评审者据此判定「那次拾取没到 handler，与随身金币特性无关」证据完整。

## 未处理的非阻断项（已记录，不影响放行）

- 并发判据在高负载机器上可能变慢（8 个写者争文件锁，靠驱动默认 busy timeout 兜底）。目前约 1 秒。
- `IGoldCarryLimitSource` 住在 `Abstractions/Content/ItemPlacementContent.cs`，文件名与主题不符。
- `ICharacterCarriedGoldTable` 是 Abstractions 的 public 接口且已注册 DI，将来任何人自带一个 ceiling 即可绕过上限。今天无旁路，与账号金库同形。

## 结论

CSC-1..CSC-7 与 RQ-001..RQ-010 全部满足，AC-001..AC-010 全部通过。无未解决的阻断性发现。
