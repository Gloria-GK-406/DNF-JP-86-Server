# 累计实现独立挑战记录

- Target：`task/solo-play-roadmap` 工作区，基线 `b5506390` 之后的全部当前生产代码、测试与交付证据，包含未跟踪文件。控制器冻结标识为 230 个 Server 路径、digest `022A677546B6B07D5F887340B342F8E0DFE4F424A5CE20C64C16E7AF5D492A25`；本次独立枚举确认路径数 230，digest 引用控制器及 drift-review 的冻结记录，未另称独立重算。
- Governing sources：`../TaskContract.md` revision 1、`../spec.md`、`../plan.md`、`drift-review.md`（approved／aligned）、`../sdd-execution.yaml` 的 change-adversarial 阶段；`generating-adversarial-challenges/SKILL.md`。
- Attack scope：从漏效果、部分提交、重复扣费／抽签／奖励、合法下一次被拒绝、会话替换、回滚后运行态、通知重放和范围误分类的伤害路径反查当前实现。读取实际调用及当前测试／覆盖／完整验证，不将施工前侦察报告中的未闭合项当作现存缺陷。只读检查期间未运行 dotnet、未改变代码／测试／运行库／部署；本文件是唯一写入。

## 保留挑战

本轮没有保留 CH 项。检查未形成足以改变当前交付决策的具体缺陷与伤害因果链；这不是审查批准，也不是无缺陷或完整性证明。独立完整结果 reviewer 仍应据当前目标及要求给出自己的判定。

## 反查边界与当前证据

| 攻击路径 | 本次检查的当前证据与保留理由 |
|---|---|
| 旧完整业务 writer 绕过 effects、隐式自耗或奖励 | 从入口、规则计划、shared Apply 与底层 handler 追踪普通 use/feed、装备、opening/recipe/special、Finish/成长与共享 XP；对照当前 caller-scan、coverage 和 implementation。实际普通无退款 Learn 是 TASK-004 明确豁免；有退款的 mixed batch 在首写前整批分流。未把基础 setter、宠物时钟或具名范围外生命周期当作范围内旁路。 |
| 库存已扣而宠物／技能／任务写失败仍提交 | `ApplyEffectsDomainService` 整体 preview／Validate 后在同一事务 Apply。`RealConsumableIntegrationTests` 使用真实物品源与实际同库 pet／quest／skill；晚 SQL/Prepare 故障、运行投影保持及 retry 有具体断言。当前四组组合已补齐，历史 fake-source 或缺失组合不再作为挑战。 |
| 冻结 footprint 太晚，未知变化已 durable | `Commands/Pipeline/UnitOfWorkBehavior.cs` 在 CommitAsync **之前**调用 operations.Freeze；`Effects/EffectOperationContext.cs` Record 对实际 AppliedEffect／change 做穷尽匹配，冻结后禁止新增变化。58 settlement behavior 在 60 返回后只确认已冻结状态；第二主命令与 readonly 后续的执行 identity 分开。未沿用旧“58.next 返回后才冻结”的设计疑点。 |
| 失败标记误作成功、部分发布后再付 | 当前 confirmed rollback／uncertain／postcommit publication 分类和 exact-session fence 分开；ledger 只有已确认 rollback 可重新结算，committed 进入当前绝对投影而非重新执行 handler。`EffectOperationContext`、`SelectedSessionSettlementBehavior` 与现有实际 bus／SQLite 故障证据相互对应。 |
| ignored tail／序号复用／合法连续操作混为重放 | 当前 ledger 使用完整 body hash／length、selector／type、exact session 与 transport ordinal，而非 parsed fields 或参数相等。fresh／nonfresh 的状态机、128 入站窗口、unknown ordinal、nextFresh／wrap、当前 18 request／20 wire／20 main 注册与 3 XP 事件 claim 在当前测试中分别覆盖。没有把所有旧测试一律强制 Fresh=true 来代替语义迁移。 |
| 当前投影污染状态、旧奖励回放、ABA 发送 | `ReplayStateDomainService` 走无维护写的当前读取；`ReplayStateProjectionDelivery` 自身持 Exclusive，读取及发送前校验 exact recipient。`TouchSocketNetworkMessageSender.SendBatchAsync` 先校验预期 physical context，整批预编码并拥有缓冲区，之后始终发送到捕获的 transport。重放不发旧 ACK／增量／earned popup；不将 I/O 的部分发送称为原子网络提交。 |
| XP、任务、转职和技能成为分散第二次写入 | 当前 QuestCompletion 规则包含交付／cleared／奖励／XP／career／技能／连带进度；ApplyAwardAsync 使用已经决定的 CharacterExperienceEffect。检查当前 active callers，未发现共享经验入口重新落回旧完整 Award writer；earned 与成长 cap 的语义仍分开。 |
| 测试绕过真实接缝或误触生产库 | 当前 replay 注册／dispatcher／真实 CommandBus／SQLite 接缝与四组 domain 同库证据分层声明；Host carried-gold composition 采用唯一绝对临时库。独立解析 8 个正式 TRX 得到 4912 Passed／0 Failed／2 NotExecuted，两个 skip 为可达 PVF 下的 missing_archive 负例。专项没有和完整 suite 累加，末次 build 记录为 0 warning／error。 |

Application 源码基路径为 `Server/src/Dfo.Server.Application`；当前投影 delivery 为 `Server/src/Dfo.Server.Protocol/Game/ReplayStateProjectionDelivery.cs`，transport sender 为 `Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs`，组合测试为 `Server/tests/Dfo.Server.Application.Tests/Inventory/RealConsumableIntegrationTests.cs`。其他实际完整路径与入口映射见 `../coverage.md`、`../verification/TASK-011-caller-scan.txt`。当前自动证据只属于 CurrentImplementation。

## 未升级为缺陷的边界

- `../human-test-list.md` 的 24 项仍为 HumanPending。客户端换角色选择再进入的实际 sequence reset、没有 nonce 的古老 raw 碰撞及 TouchSocket 实际 callback 调度有明确 Unknown 边界。没有用缺少额外外部取证本身推导已存在资产伤害，也不要求本轮扩大 wire 逆向或增加可选证明机器。
- 普通首次通知继续使用既有 best-effort delivery。实际装备和 opening Compose 会再次核对完整 receipt.Session；共享首次 SendAsync 与 FinishQuest 仍按连接 ID 投影，没有新增“整组均绑定原 physical context＋exact session”的保证。当前 replay batch 已有的 exact-session／physical-context 保护不能外推为所有首次通知的保证。本次没有形成由这条既有投影边界导致重复结算、部分资产提交或常规刷新无法廉价恢复的具体伤害路径，未将其强留为高风险挑战。
- 未部署、未修改用户运行库／PVF／客户端；固定等级券及其他尚未实现物品不属于本次新增能力。没有将 HumanPending 或批准范围外内容重写成完成证据。

本挑战集合现冻结。后继 review 可复核以上观察；本记录不提供 verdict，也不替代父 verification 或最终人工接受。
