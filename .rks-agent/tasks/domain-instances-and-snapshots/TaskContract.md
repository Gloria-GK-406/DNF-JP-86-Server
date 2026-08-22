---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Domain instances and snapshots

## Context

### Source Request

用户在本轮对话中先提出设想（CMD 的 AsyncLocal 预填充角色实例、实例自带持久层、仓库类返回有语义的实例），
经三轮往复收敛为一份设计裁定，写在 `Docs/Refactor/DomainInstanceDesign.md`。随后用户要求实施：

> 这里跑一个特殊规则的multi-spec。流程我觉得是先改造领域层。这里是为了后续改业务逻辑打基础。
> 也就是获取instance和record。……这个spec要一次性改造所有的代码。量很大，难度其实没多少。你怎么看？

Agent 论证后建议改为「四个子成果、每个 accept 时绿、spec 内部随便红」，用户答：

> 既然你有更好的想法，那就按你的跑吧。重构结束后再找我

### Current Situation

业务逻辑今天有两种写法并存。创建角色是目标形状：`CreateCharacterCommandHandler` 一行转发，
全部规则在 `CharacterDomainService.CreateCharacterAsync`。移动物品不是：`MoveItemSpaceCommandHandler`
自己持有 `PlanAsync`（约 50 行规则）且该方法位于 `Dfo.Server.Protocol` 工程。

第二种写法已经产生可测量的代价：决定「到货的东西放哪个格子」的 `ItemArrival` 三分支
写了两遍——`GrantTutorialRewardCommand.cs` 与 `DailyRefillService.cs` 各一份。掉落、商店、
邮件领取、任务奖励每加一个入口就要再拼一遍。

`Docs/Refactor/DomainInstanceDesign.md` 已裁定形状：仓库类返回快照与实例两种产物；
实例内部调用注入的规则服务；规则服务保持纯。该文档是本任务的设计权威，本任务是实施它。

## Goal

按 `Docs/Refactor/DomainInstanceDesign.md` 的六条规则改造领域层与其调用方，使一个业务用例的写法
成为「向仓库要它需要的东西——读用快照、做用实例——做一件事、翻译结果」，且对外协议行为一字不变。

## Scope

### In

- 仓库类同时提供快照与实例两种产物。
- 物品侧的规则从 `Dfo.Server.Protocol` 收回到实例内部；「到货放哪」收敛到一处。
- 人物实例，以及人物侧业务经由它表达。
- 规则服务按 R-6 去掉身份解析。
- 快照从包形状改为一组领域记录，段布局移到协议侧。
- 随上述改动而来的测试改写。

### Out

- 持久层形状。43 张表、索引、按表建仓储的既有形状不在本任务内。
- 新的游戏功能。跨容器移动、交易、邮寄等只在设计上留出位置，不在本任务内实现。
- 客户端错误码与其翻译。
- 性能优化。规模前提已在设计文档中裁定，性能不是本任务的判据。

## Constraints

- **C-1** 快照只用来读出，不得参与任何判断。判断一律经由实例。
- **C-2** 三份整记录表征用例的**期望值一个字节都不得修改**。它们是挡住静默丢列的唯一控制；
  改动只允许发生在产生这些期望值的那条路上，不允许发生在期望值本身。
- **C-3** 客户端错误码及其翻译留在 `Dfo.Server.Protocol`。
- **C-4** 不得在 `doX` 方法旁另开 `canX` 并行查询，除非已有调用方确实需要在决定行动之前拿到判决。
- **C-5** 实例不得自带数据库。持久化经由既有的按表类与既有的环境事务。
- **C-6** 每个子成果在被接受时必须构建通过且全量测试通过；子成果内部允许红。
- **C-7** 沿用既有项目约束：C→S `0x0001` LOGIN 样本不得入库；`protocol/` 不得含凭据、令牌、
  聊天隐私或个人可识别信息；不得改动 `Tool/` 与 `.worktrees/legacy-reference/`；
  `DNFClient-ClientHackLab-20260812` 只读；不得手工编辑客户端可执行文件与 DLL；
  开发阶段擦库不迁移；测试须 `-m:1`，`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` =
  `D:/project/DNF/DNFClient-debug/Script.pvf`；C# 文件 CRLF、无 BOM UTF-8；net10.0、
  `TreatWarningsAsErrors`。
- **C-8** 全程不产生任务级提交；各子成果经评审与新鲜验证后各做一次已接受结果提交。
- **C-9** 删除任何测试文件前，须逐条核对其断言证的是不是即将消失的东西。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 仓库类同时提供快照与实例两种产物，两者在生产代码中都有调用方 | 人物侧与物品侧的仓库接口上各能指出取实例与取快照的方法，并各能指出至少一个生产调用方 |
| SC-2 | 「到货的东西放哪个格子」在整个代码库中只有一处定义 | 全仓搜索 `ItemArrival` 的分支处理，只出现一次；教程奖励与每日补充两条路都经由它 |
| SC-3 | 判定物品移动是否允许的规则不在协议工程内 | `Dfo.Server.Protocol` 不再引用槽位、摆放或到货这三类规则服务；移动的命令处理器只剩转发与结果翻译 |
| SC-4 | 存在人物实例，且人物侧至少一个业务用例经由它表达 | 能指出该类型、它的领域动词方法，以及一个改为经由它的用例 |
| SC-5 | 规则服务不解析身份 | 逐个列出规则服务接口，其方法签名中不出现 `CharacterId` 或 `AccountId` |
| SC-6 | 快照是一组领域记录；包的段布局发生在协议侧 | 领域侧不再有以协议段命名的记录类型；段的选取与排序可在 `Dfo.Server.Protocol` 中指出 |
| SC-7 | 对外协议行为一字不变 | 三份整记录表征用例的期望值 `git diff` 为空；全量测试通过；选人下发帧比对无差异 |
| SC-8 | 每个子成果在被接受时是绿的 | 每个已接受提交处：`dotnet build` 零错误零警告，`dotnet test Server/86JP.Server.slnx -m:1` 全量通过 |
| SC-9 | 上一轮确立的持久层形状未回退 | 43 张表；每个持久化类仍只指名一张表；跨表组装仍在领域层；四个领域记录定义文件仍各只有一个定义点 |
| SC-10 | 设计文档与落地一致 | `Docs/Refactor/DomainInstanceDesign.md` 的六条规则逐条可在代码中指出其落点；落地过程中被推翻的条目已在文档中修订而非留成矛盾 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 「既然你有更好的想法，那就按你的跑吧。重构结束后再找我」——回应的是 Agent 提出的四子成果跑法（仓库两种产物 → 物品侧迁实例 → 人物实例与规则拆分 → 快照重塑），且明确要求跑完再汇报 | 创建或复用一个任务工作树；范围内创建并执行子契约；范围内执行与验证；各子成果经评审与新鲜验证后各做一次已接受结果提交；一次未接受尝试的精确工作流回滚 |

## Requires Confirmation

- 合并到主干、推送、PR、分支或工作树清理。本轮未授权。

## Assumptions

- `Docs/Refactor/DomainInstanceDesign.md` 是本任务的设计权威。若实施中发现其中某条规则站不住，
  应修订该文档并记录理由，而不是绕过它执行。
- 该文档「What this rests on, and what is still open」一节列出的三处未裁定项
  （人物实例是否即运行时会话态、人物仓库最终持有多少规则服务、`ItemUseEffectDomainService.ApplyAsync`
  的归属）由相应子成果在有实物可判时裁定，并写回文档。
