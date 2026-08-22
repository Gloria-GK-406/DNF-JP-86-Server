---
kind: rks-completion-evidence
verdict: pass
spec: ../spec.md
contract: ../TaskContract.md
---

# Completion evidence: The character instance

证据取自整改后的工作树状态，即将被接受的那一份。

## 基线事实

| 事实 | 结果 |
|---|---|
| 构建 | `dotnet build Server/86JP.Server.slnx` **零错误零警告** |
| 全量 | `dotnet test Server/86JP.Server.slnx -m:1` = **1172 通过 / 2 跳过 / 0 失败** |
| 上一基线 | `7478e2e` = 1151 通过 / 2 跳过 / 0 失败 |
| 净增 | **+21 条**，全部在 `Dfo.Server.Application.Tests`，逐条可解释 |
| 环境 | `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf` |

新增的二十一条：纯判决 `ItemUseJudgementTests` 十七条（四条比较、六对次序、无冷却声明、两条冷却键、
两条冲突优先）；仓库接缝 `CharacterRepositoryProductsTests` 三条；用消耗品的组冷却端到端一条。

## 成功条件

| ID | 结果 | 证据 |
|---|---|---|
| SC-B1 | 成立 | `ICharacter` / `PlayingCharacter`；由 `ICharacterDomainService.Get` 交出。`CharacterRepositoryProductsTests` 三条用**工厂**而非现成实例构造仓库，证明问哪个角色拿到哪个角色；`A_character_whose_state_is_not_published_yet_is_not_the_players_fault` 证明未被选中时的答案与今天一致 |
| SC-B2 | 成立 | `UseStackableCommandHandler` 不再持有可用性服务，改向 `characters.Get(id)` 提问 |
| SC-B3 | 成立 | 全仓十个规则/判定服务接口，签名中无一出现 `CharacterId` 或 `AccountId`。唯一带账号形状的是 `IDungeonEffectRuleService.CanCarryOut(int recordedAccountId, …)`——那是被比较的值而非要解析的身份，此区分已写入 R-6 |
| SC-B4 | 成立 | 七种结果逐条可构造；六对次序由 `Every_pair_of_reasons_is_reported_in_the_settled_order` 全覆盖，六对各自实测会红 |
| SC-B5 | 成立 | 见上表 |
| SC-B6 | 成立 | 「人物实例是否即运行时会话态」已由裁定取代，写在设计文档「Settled by the character-side implementation」一节，含理由与它依赖的前提 |
| SC-B7 | 成立 | 八次可证伪演示，见下 |

## 可证伪演示

八次，每次都以 sha256 前后比对证明逐字节复原。

| # | 改坏了什么 | 变红的 |
|---|---|---|
| P1 | 把冷却检查提到等级之前（改动前的实现） | `A_permanent_reason_is_reported_ahead_of_one_that_will_expire` |
| P2 | 把「在场记录缺失」的默认翻成地下城 | 两条地点用例 |
| E1 | 未发布状态也返回一个伪造角色 | `A_character_whose_state_is_not_published_yet_is_not_the_players_fault` |
| E2 | 判决跳过职业检查 | 四条 |
| E3 | 仓库交出下一个角色 | 只红新增的两条接缝用例 |
| E4 | 落地一侧换掉冷却键 | **构建失败**（CS9113，`TreatWarningsAsErrors`）——比测试更早的一道 |
| E4b | 冷却键的分组分支塌成物品键（能编译的改法） | 三条，含新增的端到端组用例 |
| E5 | 把冷却提到等级之前（改动后的实现） | 五条，含此前恒不可能红的那一条 |

## 约束

| 约束 | 结果 |
|---|---|
| C-1 六种判定结果逐条不变 | 成立：六条原有用例期望值 `git diff -M` 一字未改 |
| C-2 三份整记录表征用例期望值不改 | 成立：`git diff` 为空 |
| C-3 判定次序不变 | 成立，且由六对全覆盖守住 |
| C-4 不另开第二套规则 | 成立 |
| C-5 实例不缓存状态、不自建表 | 成立：`PlayingCharacter` 只持身份与依赖，每次问都读当下 |
| C-6 accept 时绿 | 成立 |
| C-7 删测试文件前逐条核对 | 不适用：未删除测试文件。`ItemUsabilityDomainServiceTests.cs` 是**更名**为 `CharacterItemUseTests.cs`（`git status` 显示为 R），断言原样保留 |
| C-8 项目既有约束 | 成立：C# 文件 CRLF、无 BOM |

## 评审

两轮，均在全新上下文中独立进行。

- **第一轮**：`changes_required`。一条 A 级——「人物由仓库交出」这条接缝全仓无测试，把 `Get` 改成
  交出另一个角色，全量仍全绿。十一次独立真改真跑；未发现行为缺陷。
- **第二轮**：`passed`。确认 A 级闭合且新用例打的是真接缝；确认把冷却闸门搬进判决后行为逐条不变；
  另发现两条我在整改中引入或遗漏的：那条六对次序的 `[Theory]` 实际只覆盖五对，
  且新闸门使 `A_permanent_reason_wins_over_one_that_will_expire` 恒不可能变红。两条均已修，
  并由 E5 演示现在会红。

## 未解决且已记录

- `ApplicationModule` 里构造 `PlayingCharacter` 的工厂 lambda 本身仍无测试跑过（第二轮判 B 不判 A：
  必修的「走过 `Get`」已满足，且评审者另用真容器证明今天装配可解析、id 原样转发）。
- 两轮评审共留下若干 B 级观察记录在 `reviews/change-review.md`，均判为不阻塞。
