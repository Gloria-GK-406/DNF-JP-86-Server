# Routing Decision

- TaskContract: `.rks-agent/tasks/protocol-vertical-slices/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 目标形态经四轮对话收敛，用户就四个分歧点逐一作出决定并给出理由——枚举保留（要一处通览全量协议）、完备性检查取消（600+ 类型下不成立）、编解码由 protocol 层隔离、handler 不得承载业务语义。可观察结果明确：三处按消息分支的 switch 消失，一个协议的五类代码位于同一目录，线级行为不变。
- Requirement stability: 12 条成功条件全部可观察且证据可取——switch 消除与分层可由检索与引用图证明，切片聚合可由目录列示证明，行为不变可由既有集成测试证明，无回归可由 563 项测试基线证明。基线已实测采集（498 通过 / 65 跳过 / 0 失败，编译 0 警告）。
- Execution direction: 中心方向已定且经代码验证——解包器按线上帧键索引（`GET_USERINFO` 一型两义已证实键空间分离），注册形态参照既有 `MessageHandlerRegistry` 而非 `CommandHandlerRegistry`，切片落于新建 `Dfo.Server.Protocol` 项目（用户认同 protocol 层隔离）。
- Remaining uncertainty: 仅剩非语义的局部选择——`GameLoginOptions` 与 `ChannelPayloadCipher` 的归属、切片目录的具体命名、注册扩展方法的粒度。均已在契约中标为 Spec 决定，不改变任务语义。

## Marginal Control Value

- User precision signal: 用户明确要求「按ssd跑完吧」，并在前一个同类重构（`typed-command-dispatch`）中主动提出「别忘了走sdd这个重构还是有点危险的」。偏好精确性高于速度。
- Direct failure mode: 第三步是跨项目搬迁约 30 个文件、含 1500 行字节表与 765 行编排器。Direct 下最可能的逃逸失败是**搬迁中夹带逻辑修改**——diff 规模大到逐行审阅失效，一处静默的行为改动会以线级不兼容的形式在真实客户端上才暴露，而现有测试未必覆盖（`SelectCharacter` 的 59 帧有序投递与失败补偿次序尤其如此）。第二个失败模式是**契约漂移**：三步串做时，前一步确立的机制会在后一步被顺手改动，使「行为不变」这一性质失去可归因的验证点。
- Useful control: Spec 对每一步固定「本步之后什么必须为真」，使搬迁步骤可以被约束为零逻辑变更并单独审查（SC-9）；完整结果复核对照 563 项测试基线与既有集成测试断言（SC-10、SC-12），把「行为不变」变成每步的独立验收面而非最终的整体印象。
- Time-for-quality rationale: 该重构不产生任何新功能，其全部价值在于结构，而其全部风险在于顺带改坏线级行为——这正是复核成本最划算的形状：一次逃逸的字节级回归需要在真实客户端上复现和二分定位，代价远高于三次结果复核。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Boundary evidence: 三个成果各自独立可接受、可验证、可回滚。入站解包 DI 化只触及 Networking 内部与 `MessageHandlerRegistry`，完成后系统可用且完整；出站编码 DI 化触及 sender 与 `SelectCharacter` 投递链路，是另一条风险等级不同的链路；切片重排是纯搬迁，其验收性质（零逻辑变更）与前两者的验收性质（机制替换正确）根本不同。合成单一 Spec 会使 SC-9 这一最有审查价值的性质被稀释进一个包含真实机制改动的大 diff 中，从而不可验证。三者的依赖是单向的：切片重排必须在编解码脱离 Networking 之后，故顺序固定而边界独立。
- Why Direct is insufficient: Direct 无法为「搬迁步骤零逻辑变更」提供可归因的验收点——它需要一个在搬迁开始前就已固定、且与搬迁 diff 分离的行为基线。
- Reroute conditions: 若第一步执行中发现入站解包无法与出站编码分离（例如某 codec 的解包与编码共享不可拆的私有状态），则三个边界塌缩为两个，需重新路由；若发现入站 payload codec 普遍依赖传输类型（推翻契约 Assumptions 第一条），则切片落点需重新决策，回到 `modeling-task-intent`；若某一步的实际改动量收缩到仅剩机械替换且无行为面，则该步降级为 Direct。
