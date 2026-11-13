---
kind: rks-task-contract-review
contract_path: E:\Project\DNF\DNF-JP-86-Server\.rks-agent\tasks\adventure-group\TaskContract.md
contract_revision: 5
reviewer: Claude Opus 5.5 子代理（全新上下文，未参与建模、意图对齐、路由选择，也未参与此前各轮评审）
selection_basis: 用户与项目政策没有要求最强评审者；按控制方要求使用新鲜、独立、胜任的评审者
verdict: approved
---

# TaskContract 评审：冒险团按账号派生、通关经验加成与升级推送（revision 5）

## 权威来源集

- 契约 Source Request 第 1–3 项的用户原话与方案要点。拿不到对话原文，以契约引文为准。
- 提案 `E:\Project\DNF\Document\冒险团实现提案-20261112.md`：
  - §2 规则：阈值 `1375 5425 10850 21700 … 175350`；Lv50=1375、Lv70=5425、Lv86=10785；21570 为 3 级。
  - §4 实现方案、§4.4 重算时机、§4.5 组合方式待核、§4.6 `0x01A0`。
  - §5 分期验收：P0 实机项包括「查看他人显示对方等级」和四维不重复。
  - §8 两项待决。
- 仓库政策：
  - 工作区 `AGENTS.md`、服务端仓 `AGENTS.md`；
  - `Document/FeatureDevelopment.md` §4（使用当前主干库、迁移先在备份上验证、编号测试流程、多人条件要列明、`Document/tasks/<功能名>/` 记录）；
  - `Document/Architecture.md`、`Document/RefactorPreferences.md`、`Document/DatabaseSchemaMigration.md`、`protocol/evidence.md`。
- 对照 `origin/main@17eb734d4` 独立核对的事实：
  - `CharacterDatabaseSchema.Version = 4`；版本不符时抛异常拒绝打开。因此「原地迁到 v5 后 0.3.0 无法再打开」成立，把原地迁移列入 Requires Confirmation 有依据。
  - `manage_level` 命中：
    - `Server/src` 2 处（DDL、`CharacterEntryStateTables`）；
    - `Server/tests` 4 个文件；
    - `Tool/DbMigrate/Legacy/LegacyUpgrade.cs` 1 处。
    与 SC-6 的允许清单相符。
  - 等级写入都汇到 `PlayingCharacter.TrySetExpAsync`，`CharacterLevelChanged(Before, After)` 只在那里发出。直接设等级的调用方有：
    - `SetCharacterLevelEffectHandler`；
    - `InitializationAssetGrants`（建角）；
    - `TutorialLevelUpSlice`；
    - `DungeonEffectRecoveryService`（可能降级）。
    GmWeb 与 HttpApi 中没有改等级或经验的路径。另外，`characters.level` 还由 `CharacterExperienceStoragePolicy` 的触发器按经验同步，支持 Assumptions 最后一条。
  - 四个协议页 `0x01BA.md`、`NOTI-0x0002.md`、`NOTI-0x0023.md`、`NOTI-0x01A0.md` 都存在。

## 根意图核对

| Intent | 处置 | 核对结论 |
|---|---|---|
| RI-1 提案 | not_applicable | 成立，信息请求已交付 |
| RI-2 删列 | in_scope | 成立，见 schema 条目与 SC-6 |
| RI-3 不分期 | in_scope | 成立，P0–P2 都在 Scope In |
| RI-4 领域服务给出等级与加成 | in_scope | 成立，见 Constraints 第 1 条与 SC-1/4a/4b |
| RI-5 现算、不持久化 | in_scope | 成立，见 Constraints 第 2 条与 SC-2。升级判定可以用 `CharacterLevelChanged` 的 Before/After 现算前后两个账号值，不需要「上次告知等级」 |
| RI-6 升级判定与推送 | in_scope | 成立，见推送条目与 SC-5 |
| RI-7 按方案执行 | in_scope | 成立 |
| RI-8 只加通关经验，组合方式沿用现有管线 | authorized_change | 成立。依据是方案原话「通关时加一项」「击杀经验和死亡之塔不加」，用户以「出任务契约」接受。提案 §4.5 原本要求先核 70 的组合方式，契约在 Constraints 中明确把它写成用户已接受的例外，并声明不在本任务核对，没有被悄悄丢掉 |
| RI-9 相邻内容不做 | not_applicable | 成立 |
| RI-10 新功能流程 | in_scope | 成立。§4 要求使用当前主干库；契约改为在一致备份副本上测试，并把原地迁移列入 Requires Confirmation。理由（v5 删列后 0.3.0 拒绝打开）已核实；副本保留了真实账号与进度，测试期间的进度不回写，测试流程开头会写明 |

没有重复或遗漏的意图，没有 `unresolved` 行，也没有 `preserved_upstream` 行。

## Scope Out 交叉核对

- 以下排除都与提案 §4.7、§6 和用户决定一致，有依据：
  - 金币加成（PVF 表为空）；
  - 支援兵、佣兵；
  - 冒险团名与仓库；
  - AdventurerMaker；
  - `0x0466`；
  - subtype2 非零；
  - 服务端四维（提案 §5 要求不得加）；
  - 合并、推送、PR。
- 击杀、任务、死亡之塔、迷妄之塔：在 RI-8 的授权范围内，代码落点清楚，没有越界。

## 上一轮阻塞项的复核

revision 4 的阻塞项是：Goal 中「查看他人」没有可观察的验收。revision 5 的处理：

- SC-9 加入第二客户端（例如 `DNFClient-A21-protocol2`）登录另一账号、在城镇查看测试账号角色，要求用户明确反馈显示的冒险团等级与预期一致；
- 结合 SC-7「有实机样本才标 ClientVerified」，subtype3 +3 只有实测通过后才会升级置信度。

Goal 中的四个观察面现在都有对应验收：

| 观察面 | 验收 |
|---|---|
| 冒险团窗口 | SC-9 |
| 查看他人 | SC-9 |
| 进游戏 | SC-9，加 SC-3 字节断言 |
| 刷新与升级后 | SC-5 |

阻塞项已解决。

上一轮的非阻塞建议 1–3 也已采纳：

- SC-1 把 21570 单列，封顶写成「点数取 175350、等级为 10」，并加入 0 级加成率为 0；
- Constraints 补上领主之塔的 110 万封顶；
- 用户测试条目点名 `Document/tasks/adventure-group/`。

建议 4 中的帧序问题，已由 SC-5「不会因同时收到带新等级的 USERINFO1 或 `0x01BA` 而重复加级」转成结果条件。

## 非阻塞建议

1. **双客户端前置条件。** `FeatureDevelopment.md` §4 要求把多人条件单独列明。编号测试流程应写出：
   - 副本库中可用的第二账号和角色；
   - 两个角色位于同一频道、同一城镇；
   - 第二客户端 `ClientHack.ini` 的 `[ServerRedirect]` 指向测试服端口。

   这些由用户在自己的客户端上完成，不需要新授权。
2. **subtype3 +3 实测失败的出口。** `0x01A0` 在 Requires Confirmation 中有降级出口，subtype3 +3 没有。这个偏移只有参照转抄依据。如果第二客户端实测发现客户端不读该位或宽度不对，而任务内又取不到正确布局，按通则应修复后复测，修不了就回到用户。可以在 Spec 或测试流程中写明。不改契约也不影响路由。
3. **Reliable 投递与推送（Spec 层）。** `CharacterLevelChanged` 的现有订阅走 Reliable 投递。如果 `0x01A0` 推送挂在可重放的订阅上，重启或重试后可能补发，客户端就会重复 +1。Spec 需要定下推送挂载点与幂等口径，满足 SC-5「b−a 帧」。

## 五维评估

| 维度 | 证据类型 | 证据 | 后果 |
|---|---|---|---|
| quality | observed | 我独立对照提案、仓库政策和 `origin/main` 代码核对了以下各项，与契约一致：<br>• schema 版本与拒绝打开行为；<br>• `manage_level` 的分布；<br>• 等级写入点与 `CharacterLevelChanged`；<br>• GM 路径不改等级；<br>• SC-1 的阈值边界；<br>• 四个协议页都存在。<br>Goal 的每个观察面都有对应 SC，上一轮阻塞项已解决 | 可以作为 Spec 的可靠输入，交给结构路由 |
| efficiency | proxy | 一个契约覆盖三期，复用现有部件：<br>• `CharacterLevelChanged`；<br>• 现有经验加成管线；<br>• 账号角色查询。<br>不新增持久化 | 改动面集中，执行路径短 |
| cost | proxy | 工作量包括：<br>• schema v5 迁移与测试；<br>• 副本测试服；<br>• 四个协议页；<br>• 新增的双客户端实测（一次额外的用户操作）。<br>`0x01A0` body 只有 70Reference，可能返工，但有需用户确认的降级出口 | 成本与诉求相称，新增的实测负担不大，换来了 Goal 中「查看他人」的可证性 |
| cognitive burden | proxy | 规则、作用场合、发包位置、显示与封顶、各观察面的验收都已逐条列出，实现者不需要替契约决定验收口径。剩下的都是 Spec 层的机制选择（推送挂载点、帧序） | 修订后理解成本低 |
| actual human participation | proxy | 用户的各项决定只以契约引文出现，我拿不到对话原文：<br>• 删列；<br>• 不分期；<br>• 领域服务形态；<br>• 现算；<br>• 升级推送；<br>• 「出任务契约」。<br>以下事项留给用户：<br>• 实机验收（SC-4a、SC-5、SC-9）；<br>• 原地迁移、停服、改角色进度；<br>• `0x01A0` 降级；<br>• 合并与 PR | 本评审不替代这些人工决定与实机反馈。用户明确反馈通过前，不得声称实机验收通过 |
