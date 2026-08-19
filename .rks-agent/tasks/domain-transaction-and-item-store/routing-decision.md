# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 目标形状由用户逐轮定下并确认——领域事务的三级保证、参与者是存储而非背包、寻址用「容器与槽」、失败一律静默、范围先限于两条道具协议。可观察结果明确：帧字节零变化，加上一份可逐条核对的耦合结构消失清单。
- Requirement stability: 八条成功条件全部可枚举且可观察，其中五条（SC-2..SC-4、SC-6、SC-8）能由自动化用例直接证伪。约束九条均有出处：用户决定、实测发现，或代码库中已结账的缺陷。
- Execution direction: 中心方向已知且不需在实施阶段发明。命令管道已有预留槽位与每命令 DI scope；`System.Transactions` 的形状可照抄，其不可用的原因已由实测确证（`SqliteConnection` 在 ambient transaction 下既不入伙也不报错）。
- Remaining uncertainty: 仅剩非语义的局部选择——接口的具体签名、参与者登记的实现方式、behavior 的排序数值。契约中已记录的两条实质前提（「容器与槽」是否足以寻址、`SlotsChanged` 是否真的不可达）各自带有反例时的处置办法。

## Marginal Control Value

- User precision signal: 用户在本轮反复要求精确——逐条追问机制（「内存写入成功了，sql 写入失败时候怎么回退的呢」）、要求看真实接口、要求「详细点的说明」，并明确说「我确认后再开始任务契约」。同时其既往偏好是「用 sdd 来跑，只是为了实现别漂移」。
- Direct failure mode: 这一刀会同时改动事务边界、存储抽象、两条线上协议与两条既有业务规则的归属。Direct 之下最可能丢失的是那些没有测试会替它说话的要求：提交阶段只允许一个可失败参与者、事务外改动持久状态必须抛、内存容器不得再发号伪造标识。它们全都是「不做也能跑绿」的性质，正是最容易在实施中被静默放弃的一类。
- Useful control: 一份固定的 Spec 把八条成功条件与那份消失清单钉住，加上一次完整结果评审——评审的重点正是「哪些新写的保证删掉之后整套测试仍然全绿」。
- Time-for-quality rationale: 本次改动的失效方式是静默的。事务装错的表现不是报错而是「偶尔少写一行」，与之同类的缺陷（`Microsoft.Data.Sqlite` 静默不入伙）已在本轮实测中亲眼见过。为不可检测的失效多付一轮 Spec 与评审的时间是划算的；同样的判断在前两次道具相关任务上已被验证过。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 事务机制与存储抽象无法各自独立验收。只有协调器而没有参与者，原子性无从证明；只有存储抽象而没有事务，把 `ApplyMainMoveAsync` 这一次原子写拆成 Clear 与 Write 两次暂存反而会破坏现有保证。两者共享同一个接受与回滚边界，任何一半单独交付都不成立。
- Why Direct is insufficient: Direct 给不出「新增的保证被删掉后仍然全绿」这条检验，而本任务的核心交付恰好全部是这类保证。
- Why Multi is unnecessary: 只有一个连贯的接受边界。范围外的四项（背包坐标系统、schema 集中化、归属校验上提、入场快照）都已在契约中显式排除，各自是将来独立的一刀，不构成本任务内的子成果。
- Reroute conditions: 若实施中证明「容器与槽」不足以寻址本范围内的物品操作，需回到契约层重议，届时结构选择一并重新评估；若每日刷新与教学奖励的现有覆盖不足以证明规则外迁前后等价，补覆盖会成为一个独立的接受边界，那时应重新考虑 Multi。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: 需要的额外控制只有一项——一份 durable 的实施排序与依赖映射。本任务存在真实的次序危险：`ApplyMainMoveAsync` 今天是一次原子写，改造后移动变成 Clear 与 Write 两次暂存；若在事务机制就位之前先拆存储，中间态破坏原子性，而既有测试**照样全绿**（现有用例不覆盖原子性），完整结果评审因此发现不了。同类次序还有存储抽象必须先于容器改造存在、`StorageEntryId` 的消除贯穿效果模型与容器与多份测试、每日刷新的规则外迁触及行为敏感的入场路径。用户又明确预期实施跨会话（写完 spec 与 plan 后压缩上下文再继续），排序必须落在纸上而不是留在会话里。`code-refactor` Profile 的其余绑定与 Lite 同款：TDD 实施、完整结果评审、新鲜验证，正对本任务「对外行为一字不变」的性质。
- Why lighter is insufficient: `sdd-lite` 无 plan 阶段，实施次序只能留给实施阶段临场决定。而本任务里错误的次序不产生红灯——它产生一个测试看不见的原子性空洞，等到评审时改动已经铺开，纠正成本远高于事先排序。Lite 的完整结果评审保护的是「要求被遗忘」，保护不了「中间态曾经不正确」。
- Why heavier is unnecessary: `sdd-governed` 的独立挑战、独立工件审批与审计强度证据，需要对应一条高后果的逃逸失效路径。本任务不触及人身或系统安全、凭据与访问控制、隐私与受保护数据、资金法务合规，也不产生不可逆的外部变更。持久化虽被改写，但目标是本机开发库，且项目已定「开发阶段擦库不迁移」，不存在缺乏恢复手段的持久数据丢失路径。范围内的失效由 SC-2..SC-4、SC-8 的自动化用例直接证伪，普通评审加新鲜验证已经够。
