---
kind: rks-review
verdict: approved
---

# Spec-002 完整结果独立评审

日期：2026-10-27（Asia/Shanghai）。本评审由独立于实现者及挑战生成者的新鲜 reviewer 完成，没有再委派。结论是**完整 B 结果门 approved**；没有未解决的阻断发现。本文同时给出当前 A+B 的实质集成判断，但不是尚未发生的 B seal 或根完成状态的证明，也不代替用户之后安排的整分支外部 Astra 审核。

## 对象与证据身份

- 工作区：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；分支 `task/refactor-session-20261021`。
- 产品 HEAD：`0c5acb958f673d3caf4352c3914960bed78a2c68`；B baseline：`51bd1e9cb7b4a6ace5b0b05b098e7e50adc1cdea`；A seal：`2079d98d179ffd423298f18dba358949cd7ae4f1`。
- 正式源码清单：`verification/logs/task66-manifests/task66-final-r4-source-manifest.json`。本 reviewer 逐项重算 2862 项（含删除标记），重新得到 `2cd7b909da3d58d1166df489ba20e25b360ef9dae47c3c287a4c06f403b5c67c`，与现工作区一致。
- 候选文档及控制输入绑定：`verification/logs/final-report-inputs.json`，本次读取的文件 SHA256 为 `6a8501e0b63bb24ac64dbf5e7b2373347a431aff2d7adad4711f523672c4cbe9`。全部列出路径的实际字节均匹配；早期 final-evidence-map 哈希滞后已在本轮纠正，不再是发现。
- 不可变挑战：`reviews/spec-002-change-challenges.md`，仅 CH-1；未重生或改写挑战。
- 应用准则：`reviewing-work/SKILL.md`、`references/code.md`、`references/deliverable.md`；根 TaskContract r4、子 TaskContract r1、Spec B10 ready R1、当前 Plan 及其 B10/流程 §8 优先规则、`process-reset-2026-10-25.md` §8、当前 AGENTS、ArchitecturePhilosophy 与 RefactorPreferences。已读取 routing-decision 末节的最终制品绑定。

范围为整个 B 及其在 A 上的集成，覆盖 17 RQ、11 AC、40 VC、18 交接与 F-128①/无编号变长字段、六条 B 根条件和 A 的 SC-1、SC-3～SC-7。没有把 TASK-66 或挑战单项的检查冒充完整门；也没有重开已接受历史任务的全部逐协议故障矩阵。旧历史条件与用户 §8 冲突时采用当前规格与 §8。

## 完整交付判断

| 条件组 | 实际检查与判断 |
|---|---|
| RQ-001～003、009；AC-001/007；SC-2 | 实际注册捕获为 173 消息、159 业务/14 遥测、172 CMD/137 实现。检查 dispatcher、CommandBus、ProtocolModule、具体协议 CMD 与结构判据，薄 handler 交原消息及精确连接，CMD 内拥有编排。实际实现扫描与注册互核，继承、nullable/default-null、delegate 反例各有真正拒绝原因。共享阶段不再是共同 CMD 基类；TownArrivalProjection 有领域状态动作，其准确分类是固定协议阶段。 |
| RQ-004～008；AC-002～005；SC-13 | CommandBus 在执行器上建立显式 request；protocol/internal 路由与消息 CLR/metadata 校验、旧连接拒绝、Main/replay、ordinary/ReadOnly、自结算、清理与失败事实分离。检查 CommandRequestContext、CommandWorkUnits、ProducerCancellationObservation、SQLite failure source 及 Mailbox/Cera/翻牌/选角等实际消费方；恢复由真实发生与完整清理证明，未从异常类型或 IsSettled 猜测提交。选角的多个 Write、发布和补偿仍分段，不是一次大事务。 |
| RQ-002/008/013；AC-005/008 | 检查 typed timer、两塔来源、HTTP typed 输入与固定审计、事件 Consume/Settle、F-07 实际登记水位。生产者只携来源及值；两塔按获准的逐项 FIFO 执行，drain 跟踪整轮。Reliable 的业务/完成同事务，失败重试是另一单元；LiveOnly 先提交消费，再投影。事件 required 编码发生与 subscriber Dispose 的强测试直接断言持久结果和错误原因。 |
| RQ-010/011/014/015；AC-006/009；SC-8B | 首单元的自有池清理、发送失败分类修正与最终全量证据相容；未以全局串行、额外 Skip、扩大失败豁免代替隔离。八份正式 TRX、构建原 console、DFO、测试去向和 Golden 检查见下节。组件 Units/精确结果 spy 只承担算法/投影证明，不承担真实 admission 或 SQLite 提交证明。 |
| RQ-012；AC-007；SC-10B/13 | 重跑五账 strict 校验及八项坏例，pending=0 且全部坏例按具名原因拒绝。159 消息、204 旧命令、73 来源、5 保护、8 过渡闭合，14 遥测及12技术路由 retained。当前生产检索只余 GameplayJob.Running 与 DomainTransactionScope.ambient 两处 AsyncLocal；原 fine/pipeline/request ambient 路由不保留为转发层。 |
| RQ-016；AC-010；SC-9B | 原 G1、原15场及原规则条件追加一场的结果满足本次接受口径，真实下降和观测边界不能省略，详见性能裁定。 |
| RQ-013/017；AC-011；SC-10B/11B | 当前 AGENTS、架构哲学、ProtocolCommands 与三份最终报告描述现模型，报告没有提前声称 seal/根完成/外部审核。40 VC 的精确许可仍以 Spec 为准；映射完整，不把 VC-B40 扩为正常拒绝、错误码、事务点或受众可变。18 F 加另列事项均有实现或准确 retained 归属。 |

40 VC 按其实际共同机制及具名例外评判：B1/2 日志；B3 日切拆分；B4～10 资产与效果失败边界；B11～22 副本与两塔；B23/24 结算翻牌；B25～36 退出、成长、任务、设置与轻量消息；B37～39 两塔内部回城；B40 冲刺统一基础设施失败。共享默认收口与实际业务差异分别查验，不要求当前 §8 已取消的逐协议罕见路径重复矩阵。已批准的三个 raw 登记出口、自结算及固定取消资格没有因本次评审扩大。

交接判断采用 `final-evidence-map.md` 的完整20行：F-07/12/34/58/69/86/88/93/109/110/112/122/124/127/130/129/131/132，F-128①及变长字段。特别保留 F-131 的登记关闭因果精度、F-132 的生产 Compose 禁用而非机制修复。F-112 的结果组件与真实事务证据不能互换；F-122 的 RegistrationAccepted 不是物理送达。

## 测试、原始证据与限制

本 reviewer 没有重新启动 native build/test 或负载；实际读取当前源、基线差异、正式原始输出并运行只读校验。已有原生运行仍是执行证据，不把此次静态评审说成新的运行。

独立从 final-run-map 指定的八份 TRX 重算：Application 6012=5994/2/16，Protocol 1795、Networking203、Persistence484、Host208、HttpApi59、Pvf80全部通过，Infrastructure976=974/0/2；合计 **9817=9797通过+2失败+18跳过**。两失败完整身份与错误均为已登记 DimensionDungeonTests 的两条 `7100 maze0 cell(1,0) entrance Left` 内容问题，没有新增豁免。构建 r4 原 console 为0警告0错误。

Application 使用 r2、其他七项 r4 是可接受的组合：清单只显示 `InitialConnectionDeliveryFailureTests.cs` 在两轮间改变；该文件属于已重跑 Protocol，生产、Application 与共享支持没有改变。15 Golden 文件与 B baseline 逐字节比较一致；正式 TRX 中10个 Characterization 全 Passed。表征 diff 仅 PartyTower 注释及说明名称，未改 mask/断言。A8350+1510−43=9817，43个旧显示实例有最终逐项去向，TASK-66 +7/−0；1510不能全称新增业务测试。

DFO 正式解析输出是68个负例及七程序集正对照、反例/对照外0、exe0、orphan0；这是该观测范围的结论，不是任意吞错可检测的证明。真实注册/物理连接/SQLite 与组件算法测试分别承担其实际范围。TitleQuery 的真实 ReadOnly 路径仍拒绝 ItemLifecycleScope 写要求，不能用 QueryRows 夹具声称成功。

## CH-1 裁定：confirmed，低风险非阻断残余

确认挑战描述的**静态可达诊断差异**，不把它拒绝为“现有取消测试已覆盖”。

1. `DungeonCardPresentationCoordinator.RunTimerAsync` 把 pending token 放进 typed 命令，但向 `SendInternalAsync` 传 None；自动确认 handler 把 pending token 传给真实 ordinary Write。
2. action 内 OCE 经 CommandWorkUnits 完成 Abort 后记录为 request failure。外层 handler 的已请求取消 catch 不清除该失败；正常 body 返回后的 DemandFinished 会再传播。
3. ProducerCancellationObservation 要求实际 producer token 已取消且同 token；此处 None 不满足，而且正常 body 返回会使观察失效。因此该失败可由执行器记 Error。producer 最终仍等待 CMD 完整结束，finally 才减少 inflight；AwardBody 的 catch 释放 claim。不能从日志差异推导重复支付、丢回滚、额外来源关闭或提前 drain。
4. `Task48_real_queued_timer_is_cancelled_in_fifo_order_and_drain_waits` 在 timer 未执行时取消，明确断言无事务；它只证明该位置，未证明 action 内已请求取消不会 Error。本轮没有新增该位置的 native 探针，故不把上述实际 rollback/claim/drain 组合写成新动态实证。

判定理由：VC-B23 明确把 timer 收入完整请求/scope 默认失败，不再维持作业内吞一般失败后成功的整体诊断模型；B8 的固定取消控制、None 排队、待办身份和 drain 业务约束仍保持。这里是已进入真实失败工作单元之后的终局诊断残余，尚无证据表明取消未生效或业务安全改变。它不能被解释为所有 producer 取消都获准记 Error，也不能把两塔/HTTP 已具名的纯取消资格外推给翻牌。按实际影响不足以阻断完整 B，不要求修改产品或扩大通用恢复。

**交付时的最小披露**：在 B 最终汇报的已知限制中携带一句：“翻牌待办在排队未执行时的取消已有覆盖；自动翻牌进入真实 Write action 后的已请求停机取消可能仍记为失败 CMD/执行器 Error。该位置尚无专项 native 探针，不能将排队取消证据外推到 action 内。”本评审已记录这一边界；它是说明修正，不要求新 Spec、通用取消修复或重跑完整测试。不得以清 sticky、吞独立 cleanup/guard/不确定失败消除诊断。

## 性能与根数量级条件

直接核对16份 run-meta、G1 原 summary/ramp 输出、源冻结及对照材料。16场自然 exit0，记录无停止事件；有逐作业样本场最大356.0821ms。默认日志 parsedJobs=0 的场次必须 N/A，不能把 run-meta 的0当测得耗时。逐协议标签只按实际唯一映射比较。

清洁原 p99 爬坡实际24999→15000、原趋势稳定目标25000→10000是真实下降。追加 max:2000 场到25007，但趋势稳定只有15000，且没有对应 A 条件场，不能替换首次结果。Debug 洪峰约下降9%～17%也应保持披露。混合2秒爬坡稳定目标仍800，可持续1163→1151；1600档的约10秒端到端积压是原有负载容量边界，该档 CMD 本体约116ms。

根原吞吐目标是2000 CMD/s，不是A清洁场测到的25000；清洁下降仍高于原目标，混合未达原目标属于已接受的原范围，未出现新数倍吞吐崩溃。12普通场端到端未越2秒。cold stall约1394ms及9个污染窗口不应隐去，但按原污染/重跑规则与当前用户单条CMD≤2秒门，不构成本轮新的数量级停止条件。测试套件405.955秒相对321.639秒不属于此门；没有同口径A内存数据，不判内存改善。此结论只适用于固定合成负载及现有观测，不是A21/任意人群/长期积压保证。

## A+B 集成及根门的边界

当前实质集成判断为**通过**：A的唯一队列/单消费者未被重建，执行器差异保留原程序错误并改进已包含cause的错误图；同一outbox适配器经DI别名提供具名登记，CMD不等待网络。来源链、深度拒绝、提交后发送、逐目标隔离、删锁及原短锁边界有当前直接测试与对应源码支持。Reliable/LiveOnly与投递结果承接未因更换CMD形状被统一成错误的事务模型。B新增分项可见性只按VC-B3及B37～39接受。

SC-1、SC-3～SC-7 的正式测试组见 final-evidence-map 末表；本评审检查其对应机制、当前相关断言与正式结果，不仅依赖账行数。A封存制品实际 Git diff 只有已明确排除的旧B草案迁出；A证据本身未改写。A的原CH-1/CH-2低风险残余、F-113/114/116/117、F-131及F-132仍保留，未以B通过冒称修复。

六条 B 根条件在当前交付内容上有足够证据，**但根map尚未登记B seal，不能消费根完成门**。控制器先核验并封存B、提交根子项accepted/satisfied状态，再把确切seal与根commit返回同一reviewer，在封存B之外完成一次有界身份/一致性确认；这属于本次同一完整评审周期，不重生挑战或重新展开完整评审。最终 fresh Root Verification 与状态推进归控制器。外部整分支Astra、推送/PR/合并/tag/issue/部署/清理均未由本文授权。
