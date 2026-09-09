# T-001 领域学习动作实现

日期：2026-09-09。基线：`da623c2d`。执行控制采用父任务的会话清单，没有新增持久 Task 单元；改动保持未提交。

## Changed

- `Data/Characters/CharacterSkillLearningModels.cs` 与 `Abstractions/Characters/ICharacterSkillLearningDomainService.cs` 定义学习增量、拒绝理由、最终变更技能和完整账本回执。
- `Application/Characters/CharacterSkillLearningDomainService.cs` 在环境事务内检查选中会话、在线角色、当前角色归属、场景、当前技能页、职业、等级、技能事实和奖励预算，完整准备最终技能图后检查同包前置及当前页 SP/TP，再顺序写库。
- 现有技能保留 slot/extra；新技能使用共享规则分配普通槽位、extra 为空。回执只包含本包变更项，按请求顺序排列。重复 ID、空包、退点、零增量、非零模式及不支持状态拒绝；写入异常与更新影响行数异常直接抛出。
- `ICharacterSkillRule.FindAvailableSlot` / `CharacterSkillRule` 采用参照 `ServerS4A21/.../SkillSlotAllocator.cs` 的活动栏及分组槽位规则；不按 TP 或技能 ID 范围推导 FF 槽位。
- `ApplicationModule` 注册领域服务；`Application.Tests/Characters/CharacterSkillLearningTests.cs` 增加领域行为和真实 SQLite 持久化覆盖。

## Why

- 同包前置检查使用最终目标等级字典，因此请求顺序不会导致合法批量失败。
- 页授权：存储 `skill_tree_index=-1` 仅允许页 0；存储 0/1 仅允许对应当前页。本次不处理切页或解锁。
- 场景沿用当前会话的频道策略及城镇在场状态；自由决斗、未知模式、无当前在场或未完成发布的会话保守拒绝。
- 普通页预算相互独立，只要求当前页 SP/TP 非负。未知技能仍按共享账本全局 `IsComplete` 拒绝，防止零成本兼容诊断用于学习结算；未知技能不会被删除。
- 本包变更技能的 slot 必须在 0..254、extra 长度不超过 255，避免提交后应答无法编码。技能 ID 来自 ushort 请求。未变更技能的配置不改动。
- 调用方必须持在线用户及账号执行门；接口注释明确此契约。领域服务要求已有环境事务，协议命令接线由父任务随后实现。

## Checks

测试价值：新增学习动作会修改持久等级、槽位和预算；部分保存、累计成本或字段覆盖错误会在客户端表现为错误加点。已有测试仅覆盖规则和事实查询，无法覆盖写入原子性。测试从返回结果与独立 SQLite 重读判断，允许内部实现重构。

1. 行为 RED：

   `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter FullyQualifiedName~CharacterSkillLearningTests --logger "trx;LogFileName=learning-domain-red.trx" --results-directory .rks-agent/tasks/ordinary-skill-learning/verification`

   生产动作暂返回 InvalidRequest，构建成功，退出 1；实际 1 Failed，首个成功学习测试期望 None、得到 InvalidRequest。

2. 首次 GREEN：同一命令加 `--no-restore`，输出名 `learning-domain-first-green.trx`。退出 0；实际 1 Passed。

3. 扩展验证：设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，同一筛选及 `--no-restore`，输出 `learning-domain-green.trx`。退出 0；实际 43 Passed，无失败或跳过。

   覆盖单次多级、批量顺序前置、固定技能、职业/等级/上限、SP/TP 不足、当前页权限和实时事实、过期会话、自由决斗/缺失在场、extra 范围、未知账本、槽位分组/溢出/耗尽、写入异常整包回滚、独立重读及真实 PVF 守护者初始技能 id 1 从 1 学到 3。验证另一页及无关角色保持原值。

4. 相关回归：同环境变量，执行：

   `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterSkill|FullyQualifiedName~RemainingSkillPoint|FullyQualifiedName~CurrentCharacterState|FullyQualifiedName~CommandHandlerArchitecture" --logger "trx;LogFileName=learning-domain-regression.trx" --results-directory .rks-agent/tasks/ordinary-skill-learning/verification`

   退出 0；实际 78 Passed，无失败或跳过。TRX 的每条 UnitTestResult 已与 Counters 对照一致。`git diff --check` 通过。

扩展测试初次执行的 `learning-domain-expanded-first.trx` 保留 33 Passed / 1 Failed：失败源是测试试图在真实表中插入重复 `(character,mode,page,skill_id)`，被既有 UNIQUE 约束拒绝。删除这个不可能形成的 SQLite 测试状态后重跑；这不是生产缺陷或行为 RED。

## Remaining

父任务接入 Protocol 命令及在线用户/账号串行事务管线，并补 handler 提交后应答覆盖；本结果不表示已经完成协议/网络验证或本仓客户端实机验证。未修改运行 Host、客户端、活动数据库、协议文档或控制规格。

测试复用入口为同项目 `internal LearningFixture`：`Services`、`Session`、`Scope`、`SeedAsync`、`LearnAsync`、`Sql`、`Scalar` 和 `Path` 可用；默认假内容源，`useArchive:true` 使用真实 PVF。fixture 只清理自身连接池。
