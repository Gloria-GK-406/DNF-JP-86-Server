---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 两笔钱各自叫对名字

## Delivered

- 账号金库那笔钱在标识符与记述两个层面都表明自己是金库的：`IAccountMoneyService`/`AccountMoneyService` 更名为 `IAccountCargoGoldService`/`AccountCargoGoldService`，含实现、DI 注册与两个消费方。
- `purse` 一词退出仓库对两笔钱的称呼，原 79 处（源码 17 / 判据 62）全部归位，包含判据的类名、字段名、方法名、四个用例名与临时库文件前缀。
- 金库服务文档中的两条假断言被拔除：「客户端从该字段覆盖它显示的钱包（`CInventory::get_money`）」与「同一账号下所有角色共用一个钱包」。前者的引用确有出处，但描述的是参照服务端修理回包里的金币，那笔钱读的是角色主背包槽 0；后者与参照服务端相反。
- `Gold` 只用于指代金币：六个魔方碎片字段改为 `BlackCube`…`GoldCube`，付费牌面 `ClearRewardBlankCard.Gold` 改为 `.GoldCard`。
- 主背包前三个虚拟槽位各自承载什么成为可从代码读出的事实，并载明槽 0 只读不写、收入现进账号金库。
- 四处走样记述与代码对齐，其中 `Docs/新功能接入规范.md` 那句把随身金币记作「已有」的取证——本次事故的源头——被改为分述并补入一节复盘。

## Success Evidence

| Condition | Evidence |
|---|---|
| CSC-1 | `./verification/completion-evidence.md`：全仓检索 `purse` 得 0 命中，检索有效性经反证 |
| CSC-2 | `./verification/completion-evidence.md`：逐条判定全部 `Gold` 命中；六个碎片字段全改且消费点同步，DB 列未动 |
| CSC-3 | `./verification/completion-evidence.md`；`./reviews/change-review.md`：以 `git show HEAD:` 取旧接口逐句对照，两条假断言均已消失，新注释未引入新的不实陈述 |
| CSC-4 | `./verification/completion-evidence.md`：四处修正逐条独立查证，含「这是参照服务端」抬头的成立性（六个类型名本仓全无、参照端全有） |
| CSC-5 | `./verification/completion-evidence.md`：`InventoryRuntimeValueState` 自身即可读出三个槽位的承载物，另两处反向指引 |
| CSC-6 | `./verification/completion-evidence.md`：新鲜运行全量判据 1993 通过 / 2 跳过 / 0 失败，与基线逐程序集一致；判据 diff 中每一处断言改动均只是标识符改名 |

## Repository Result

- Changed paths: 24 个已跟踪文件（+274/−287），加 3 个未跟踪新文件 `IAccountCargoGoldService.cs`、`AccountCargoGoldService.cs`、`AccountCargoGoldServiceTests.cs`；另有本任务的流程产物。`protocol/` 无改动。
- Baseline or accepted result: 基线 `1946c012fc7083835c742699b0bb2ad5a29fda29`，分支 `gold-the-player-carries`。接受提交由父控制器的工作区控制器创建。
- Parent rollback required: no

## Successor Guidance

- **随身金币的写路径仍然不存在，但它的变更通知帧已经存在。**`GroundGoldPickupNotification`（GET_ITEM 的金币形态）已注册并在拾取路径上实际下发，引入于 `8af7990c`。后续结果需要的是存储与写入，不是重造这个帧。
- **客户端对该帧是累加而非赋值**，且它累加的对象是随身金币。证据在参照服务端且是闭合的：`DropItemBuilder.cs:90` 写入的是本次拾取的增量而非余额，而落库走另一条路径 `DropService.cs:251` → `TryGrantGold` → `SetMainVirtualCount(MainVirtualCurrencySlotStart, 0, target)`，该常量为 0。若客户端是赋值，参照端自己的显示在第二次捡钱后就会崩坏。
- **这解释了事故中最反直觉的一环**：捡钱当场显示完全正常，因为服务端把钱记进金库、客户端把它加在随身金币上，两边各自成立，只有下一次登录会揭穿。付费翻牌那条路径不发任何金币帧，连「当场看起来正常」都算不上。
- **参照服务端翻牌收付两侧都走随身金币**：收在 `DungeonPersistentEffectApplicationService.cs:1841-1850`（发到 `MainVirtualCurrencySlotStart` 并按 `InventoryGoldCarryLimitLoader` 的携带上限截断），扣在 `CardRewardService.CanPayPaidCard`（判 `CountMainItem(0) >= cost`）。本仓两侧现在都走金库，是已知的错误落点。
- **`Docs/新版背包架构业务接入规范.md` 是读参照实现的地图**，其对随身金币的描述（主背包虚拟槽 0、`ItemCore.Value` 即金额、受 `carryLimit` 约束）正确且正是后续结果需要的材料；但它的主语是参照服务端，不是本仓。
- **不要照抄参照服务端的落库机制。**它那套脏集合加每分钟 tick 是因为其内存背包与数据库是两份数据；本仓的容器写随命令事务提交，结构更简单也更不易丢钱。
- **携带上限链已在命名与记述层面归位，功能性仍未生效。**`character_stats.gold_carry_limit` 与 `IGoldLimitRuleService` 限的正是随身金币，服务端已在向客户端下发该上限的升级等级，而被限的量尚不存在。它是后续结果现成的接入点。
- 遗留的非阻断项：`GoldCube` 仍含 `Gold`（有用户原话背书）；`Docs/新功能接入规范.md:365` 的基线计数仍写 1963（实测 1993，与两笔钱无关的既有陈旧）；`IAccountCargoGoldService` 不带 `AGENTS.md` 的分类后缀（旧名同样如此，未引入回归）。

## Artifacts

- `./TaskContract.md`
- `./routing-decision.md`
- `./spec.md`
- `./sdd-execution.yaml`
- `./reviews/change-review.md`
- `./verification/completion-evidence.md`
