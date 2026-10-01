# TASK-001 — 共享效果执行与普通使用／饲料

## Task Unit Contract

- Revision: 1
- Outcome and references: 建立后续迁移可复用的显式效果契约、注册分发及执行结果；普通使用由规则构建列表，饲料列出自耗与pet饱食增量，移除Feed业务入口。Plan T-001，RQ-001—004/008，AC-001—003/007。
- Owned boundary: 共享效果Abstractions／Application及DI，普通使用切片、可用性事实接缝、必要宠物基础接口适配与Feed调用者、直接相关测试，本Task结果和coverage.md的本分区记录。不得修改装备改造／开包／任务经验业务实现，不动运行服务／DB／PVF，不提交。
- Required checks: 构建相关项目；普通使用／规则判断／宠物饲料直接相关既有回归；多effect同槽累计、跨栏同索引、未知类型、过期拒绝、提交失败冷却秘药及pet不生效；检查Feed调用和其他普通使用状态变化旁路。

## Detailed Plan

1. 复用现有事务、业务container和宠物instance基础方法；新增共享record效果、执行上下文、注册式类型分发和结果契约。上下文可表达账户／角色和可选选中会话，后续共用经验调用不能被强迫依赖在线会话。效果数据不携带委托，不用“运行旧业务”包装。
2. 分离规则构建和执行。规则接收源BusinessSlot、物品Core／数量、角色使用事实和必要pet目标，返回明确成功列表或拒绝。合法不自耗与内容缺失区分。目标含完整空间／预期ItemID，装备身份等留给后续注册类型。
3. 显式分发，执行前确认全部类型受支持；同槽消费从事务当前状态累计，不再次使用源陈快照。类型／目标错误抛出，调用者不能吞掉执行异常正常返回导致部分提交。
4. 冷却和秘药使用提交参与者，保留会话代次保护。饲料规则保留当前增加30语义，列表包含消费和AddPetStomach；handler调用pet.AddStomachAsync并保留同步／待发奖励事务能力。删除运行态Feed业务方法及直接分支，不重构宠物成长。
5. 返回可供协议转换的实际效果结果；原普通ACK／CreatureChange语义保留。共享注册要能让后续单位增加handler且不形成服务循环依赖。
6. 对新增行为缺口采用有价值的行为回归，记录真实RED／GREEN或说明纯结构调整采用既有测试。更新测试替身但不新增只检查构造器／依赖身份的测试。
7. 完成后检查生产写入并记录覆盖：当前源消耗／冷却／秘药／饲料到effect与底层handler的映射；其他分区列待迁移，不能宣称全项完成。追加Changed／Why／Checks／Remaining，报告共享契约供下一单位使用。

## Planning Evidence

基线 b5506390；真实PVF相关600项通过。现有三轮OfType忽略未知类型；冷却在提交前写内存；源消费复用原item快照；pet已有AddStomachAsync和事务同步能力可复用。既有UseStackableHandlerTests覆盖普通自耗、不自耗、共享冷却和秘药提交／回滚；追加跨effect故障与目标检查填补其无法覆盖的缺口。

## Execution Result — Round 1

**Changed:** 新增 `Abstractions/Effects/DomainEffects.cs`、`Application/Effects/ApplyEffectsDomainService.cs`／`ItemEffectHandlers.cs` 与普通使用 `ItemUseEffectRuleService`。效果为显式 record，计划和执行结果使用只读列表。执行上下文包含 AccountId／CharacterId／At／可选 Session；共享执行器在当前事务校验角色归属和可选会话，完整确认类型／目标后顺序调用注册 handler。普通使用切片只投影领域结果，规则构建自耗、冷却、秘药、饲料饱食计划。消费目标含 BusinessSlot 和 ExpectedItemId，每次执行读取事务当前值；冷却／秘药由提交参与者生效，保留会话代次保护。

`ICharacter.ItemUseFactsAsync` 提供角色使用事实；`CanUseAsync` 复用该事实查询，保持既有拒绝顺序。删除 `ICreatureRuntimeDomainService.FeedAsync` 与实现，纯算术改名为 `CreatureRules.AddStomach`，饲料通过 `AddPetStomachEffect` 调用现有 pet 实例。实例的在线时钟、待发经验和提交投影机制保持原归属。相关替身只追加必要事实接口，Host 既有组合测试增加共享服务解析检查。

**Why:** 分发器先确认所有类型，避免消费后静默忽略未知效果；执行期重新读消费目标，避免同槽累加丢失。账户归属不能由只含角色地址的库存绑定推导，因此复用 `ICharacterDomainStore.SessionIn(tx).HoldsAsync(OwnedCharacterId)`统一预检。Session 可空，离线调用不依赖选中会话。执行器不派嵌套 CommandBus，效果没有委托／旧业务包装，也没有通知逻辑。执行异常由普通命令调用链传播，事务管线回滚，不转换为成功或可提交的部分 Failure。

**Checks:** 所有 dotnet 命令串行，真实内容变量为 `$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'`。

- 宠物迁移前基线：`dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-build --filter 'FullyQualifiedName~CreatureRuntimeDomainServiceTests|FullyQualifiedName~CreatureGoesBackOnTests' --verbosity minimal`：19通过、0失败、0跳过。历史报告的两条 pet-return 失败本次未复现。最初误设 `DNF_PVF_PATH` 的运行16通过／3跳过不作为真实PVF证据；已用正确变量重跑。
- RED 1：`dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter 'FullyQualifiedName~Rolled_back_use_leaves_the_item_immediately_usable' --verbosity minimal`，退出1，1失败；旧实现第二次使用 `Expected: Used / Actual: OnCooldown`。该测试保护回滚后数量和可用性，旧秘药测试未覆盖冷却；与内部接口无关，可用于行为保持重构。
- RED 2：`dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter 'FullyQualifiedName~Cross_account_context_is_rejected' --verbosity minimal`，退出1，1失败；共享执行器尚未校验账户时 `No exception was thrown`。测试保护错误账户不能消费其他角色库存，用真实消费结果作判据；仅校验 Session 内字段一致不足以保护离线调用。
- GREEN／最终直接回归：`dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter 'FullyQualifiedName~UseStackableHandlerTests|FullyQualifiedName~ItemUseJudgementTests|FullyQualifiedName~CharacterItemUseTests|FullyQualifiedName~ApplyEffectsTests|FullyQualifiedName~CreatureRuntimeDomainServiceTests|FullyQualifiedName~CreatureRulesTests|FullyQualifiedName~CreatureGoesBackOnTests|FullyQualifiedName~CommandHandlerArchitectureTests' --verbosity minimal`：107通过、0失败、0跳过。包括两条真实RED的GREEN、同槽累计／超额回滚、跨栏同索引、未知／换物／过期预检、第二步执行异常、Commit故障注入下消费／冷却／秘药不可见、旧会话冷却不生效、无Session消费、真实SQLite饲料／pet饱食联合提交与Prepare失败回滚及投影。
- `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --filter 'FullyQualifiedName~UseStackableCodecTests' --verbosity minimal`：6通过。
- `dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --filter 'FullyQualifiedName~CarriedGoldCompositionTests' --verbosity minimal`：2通过；实际组合模块可解析普通使用领域服务和全部effects handler，无循环依赖。
- `dotnet build Server/86JP.Server.slnx --verbosity minimal`：0警告、0错误。全方案包括修改了 ICharacter 替身的 Protocol.Tests。随后直接回归／Protocol／Host测试重新构建了最终普通使用代码。`git diff --check`无问题；生产范围内 `FeedAsync`、`CreatureRules.Feed`、旧 `IItemUseEffect` 和 OfType 效果执行均零命中。

除上述两个真实RED行为，新增执行器故障／目标案例作为实现后验证记录，结构移动采用既有回归与编译，不计作TDD。

**Shared API for next unit:** 继承 `DomainEffect`／`EffectChange`，handler 继承公开 `EffectHandler<TEffect>`，实现 `ValidateAsync` 与 `ApplyAsync`，在 `ApplicationModule` 以 `TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, THandler>())`注册。领域调用 `IApplyEffectsDomainService.ApplyAsync(EffectExecutionContext, IReadOnlyList<DomainEffect>, ct)`，从 `ApplyEffectsResult.Changes<TChange>()`构建原receipt。Validate须避免误拒绝由前序效果创建的合法目标；库存容量模拟由后续库存单位提供。当前 consume handler预检已有来源、执行时读最新值，超过累计余额会抛出并由整个事务回滚。

**Remaining:** TASK-001生产边界已实现，等待控制器验收；未提交、未部署、未操作运行数据库或客户端。装备／开包／任务／经验仍由后继单位迁移。普通UseStackable原命令没有携带 `GameMessageMetadata.Sequence`且无重放缓存，本单元未声称AC-007普通重试已满足；后续覆盖须用会话代次与请求生命周期区分提交后同次重试和合法下一次操作，不能按业务参数去重。测试只提供CurrentImplementation，未新增ClientVerified声明。当前无运行中的build/test会话。

控制器检查（2026-10-01）：已核对实际共享契约、分发器、普通规则／编排与handlers、CanUse事实拆分、Feed删除、真实事务回归及覆盖记录；本单位边界接受，TASK-001置done。普通重放明确转交完整覆盖单位，未声明AC-007全部完成；下一步为TASK-002装备和资产效果迁移，整体仍在实施。

分阶段独立遗漏检查：effects_stage_audit只读追踪本单位，未发现生产Feed／OfType执行／事务异常吞没／隐式内存写入旁路。收窄上述SQLite用例证据：CreatureRuntimeDomainServiceTests中pet／character是真实SQLite，FoodInventory是FakeMainInventory；其证明混合事务参与者联合提交及Prepare失败回滚，不单独证明饲料和pet同一生产SQLite库的路径。最终整合单位须补真实生产同库故障验证。该检查没有运行测试，也未检查TASK-002。
