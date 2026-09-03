---
kind: rks-review
verdict: approved
---

# 完整变更评审：两笔钱各自叫对名字

范围：完整变更集（24 个已跟踪文件 +274/−287，加 3 个未跟踪新文件），对照 TaskContract revision 1 的 CSC-1..CSC-6 与 spec.md 的 RQ-001..RQ-009 / AC-001..AC-009。基线 `1946c012`，未提交。

评审由独立的新上下文评审者进行，两轮。执行者未参与判决。

## 第一轮：`changes_required`，一条阻断

**B-1 —— `Docs/新功能接入规范.md:185` 把一个已存在并已接线的帧记作「仍然没有」。**

原文档的断言是复合的：「角色金币**既没有落库路径，也没有告诉客户端金币变了的帧**」。执行者把它整体翻转为「仍然没有」，但后半句不成立：

- `Server/src/Dfo.Server.Protocol/ProtocolModule.cs:330-331` 注册 `GroundGoldPickupEncoder`
- `Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs:457-464` 在 `Banked` 分支实际下发 `GroundGoldPickupNotification`
- `git log -S` 确认该帧引入于 `8af7990c`（2026-08-30），**晚于**取证记录的 2026-08-29

违反 CSC-4 与 RQ-007。且与本次改动自己在 `GroundPickupSlice.cs:121-126` 写下的注释互相矛盾。

**这是本任务要防的失败模式的反方向**：第一次是高报（把没有的说成有），这次是低报（把有的说成没有）。两次的机制相同——一个复合断言被当作单个断言处理，而判据对两者都是全绿。

## 修复

三处，全部是文字，无代码行为改动：

1. `Docs/新功能接入规范.md:185` 改为分述：落库路径仍然没有；变更通知帧有，但无存储支撑，重登即归零。
2. 同文件补一段，把该帧纳入事故叙述：它本身是对的、确实作用于随身金币，**正因如此捡钱当场看起来完全正常**——服务端记进金库、客户端加在随身金币上，两边各自成立，只有下一次登录会揭穿。
3. `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs` 的定语从句拆成独立句，消除就近／远指歧义（第一轮的非阻断观察 3）。

## 第二轮：`approved`

新写下的陈述由评审者逐条独立查证，全部属实；改动面未扩散，`purse` 仍为 0 命中，编译 0 警告。

**「客户端收到后会把数字加上去」取得了本仓注释之外的独立佐证。**评审者在参照服务端做了交叉验证，证据链闭合：

- `ServerS4A21/…/DropItemBuilder.cs:90` 写入的是 `pickup.GoldAmount` / `ExtraGold`，即**本次拾取的增量而非余额**
- 参照端的落库在另一条路径上：`DropService.cs:251` → `TryGrantGold` → `SetMainVirtualCount(MainVirtualCurrencySlotStart, 0, target)`，而该常量为 0，即随身金币

两者合起来是决定性的：参照端**存的是槽 0、发的是增量**。若客户端是赋值而非累加，参照端自己的显示在第二次捡钱后就会崩坏。此前这句话只有本仓自己的注释作根据，属自证。

评审者同时验证了 `IClearRewardRuleService` 那条改写的断言：参照端翻牌**收付两侧都走随身金币**——收在 `DungeonPersistentEffectApplicationService.cs:1841-1850`（发到 `MainVirtualCurrencySlotStart` 并按携带上限截断），扣在 `CardRewardService.CanPayPaidCard`（判 `CountMainItem(0) >= cost`）。

## 评审者自行核销的证据

不采信执行者结论，均由评审者自己运行：

- **CSC-6**：自带档案跑全量判据，`488+52+60+4+248+215+926 = 1993 通过 / 2 跳过 / 0 失败`，exit 0，0 编译警告。2 个跳过出自 `PvfTownDefinitionSourceTests` 与 `PvfQuestDefinitionSourceTests` 的档案守卫，即档案确已加载。与基线逐程序集一致。
- **断言未变**：逐处核对 `git diff -U0 -- Server/tests/`，每一处 `Assert.*` 改动都只是标识符改名；期望值 300 / 10299 / 100 / 1357 / 44 / 1000 与实参、用例数全部未变。
- **删除+改名的文件**：以 `git show HEAD:` 取出旧版逐条比对，3 进 3 出，断言与期望值一字未动。
- **CSC-1**：全仓大小写不敏感检索 `purse` 得 0 命中，并先用 `.rks-agent` 内的命中验证了检索本身有效。
- **CSC-2**：逐条判定全部 `Gold` 命中。六个碎片字段确已全改（`BlackCube`…`GoldCube`），唯一消费点同步；DB 列 `cube_*` 未动（动了就破坏行为不变）。
- **CSC-3**：以 `git show HEAD:` 取旧接口逐句对照，两条假断言均已消失，新注释未发现新的不实陈述。
- **文档属实性**：`character_gold_limits` 确已折进 `character_stats`；「这是参照服务端」的抬头判断成立——六个类型名在本仓 `Server/src` 全部不存在、在 `ServerS4A21/Server/DfoServer/` 全部存在。
- **超范围检查**：`protocol/` 无改动；`AccountWallet*` 未动；未新增任何随身金币写路径；携带上限链只改注释。

## 非阻断观察（评审者确认均不构成阻断，本 Spec 内不处理）

- `GoldCube` 仍含 `Gold`：与 AC-004 的宽泛措辞字面冲突，但有用户原话「比如 GoldCube」直接背书，Constraint 优先，且已加消歧注释。
- `DropRandom.GoldenGamma`：SplitMix64 的黄金比常量，与钱无关。
- `Docs/新功能接入规范.md:365` 基线仍写 1963（实测 1993）：与两笔钱无关的既有陈旧，在 Spec 的 In 范围之外。
- `IAccountCargoGoldService` 不带 `AGENTS.md` 的分类后缀：旧名同样如此，未引入回归；归类会牵动 DI 与两个消费方，放进「行为逐值不变」的改名里反而扩大风险面。
- `Docs/Refactor/CharacterEntryStateCoverage.md:33` 措辞略绕：RQ-007 要求的已达成，剩下的是行文偏好。

## 结论

CSC-1..CSC-6 与 RQ-001..RQ-009 全部满足，AC-001..AC-009 全部通过（AC-004 按 Constraint 口径）。无未解决的阻断性发现。
