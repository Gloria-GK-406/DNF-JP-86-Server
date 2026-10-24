---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-2, SC-8B, SC-9B, SC-10B, SC-11B, SC-13]
depends_on: []
---

# TaskContract: 结果 B——协议级 CMD

## Context

### Source Request

本契约是根契约 `../../TaskContract.md`（revision 4）的结果 B。当前接手授权（2026-10-24）为用户：“你要继续做这个交接任务。是一个标准rks sdd任务。先看交接文档和任务契约以及spec执行情况收集上下文。然后继续完成spec2剩余的工作。”该授权延续既有范围，不解除推送、PR、Astra和收尾限制。来源请求、用户原话、授权与术语以根契约 Context 为准。拆分与「同一分支、一次收尾」的依据是用户 2026-10-23 的原话：“继续吧，在同一个分支干。不过可以拆成你说的另一个spec单出”。

核心来源是用户原话：“不，这里的CMD直接把其他的都吸收了。命令变成唯一实体。不过这里的命令含义就是以协议为单位的了。要改的估计怕是很大。……早期的架构设计有点随意了。现在要还债了”。

### Current Situation

以结果 A 封存提交 `e6f55558182800b656b14c4b863a52fe672ad921` 为已接受起点（根映射接受提交 `2e742a47`）。A 已经交付执行器、提交后发件箱、删锁与调用链；B 的输入是 A 的过渡机制清单（根 SC-12）。改道前的 Spec（`../../spec.md`）中移交给 B 的需求、附录与验收，见 `../../verification/reroute-t008.md` §3，激活后作为 B 的 Spec 的起点材料。

另有移交给 B 的 follow-ups，A 封存时为 18 项（TASK-35 更新；改道时的 11 项是其中前 11 个）：F-07、F-12、F-34、F-58、F-86、F-88、F-93、F-109、F-110、F-112、F-122，加上 F-69、F-124、F-127、F-129，以及 TASK-35 的 F-130（首个单元，只改测试）、F-131、F-132（前提提醒）；11 ＋ 4 ＋ 3 ＝ 18。另有 F-128 ①，与一条无编号事项（扇出变长字段的上限约定是否设判据）。清单以 `../../verification/t009-closure.md` §6 为准。依据有三处：
- `../../verification/reroute-t008.md` §4；
- 同文件文末「主控方裁定」第 6 条之后的合计更正（F-87 改为留在 A）；
- `../../verification/follow-ups.md` 中标为「〔A 封存：→ 子 Spec B〕」的条目。

其中 F-88、F-93 有先后要求：R5 当前性检查、`DiscardExact` 与 `DemandCurrent(recipient)` 迁进 CMD 之前，先补守护用例。F-110 只有结构保证一半归 B，「记为已知约定」一半已由 A 完成。

## Goal

命令成为唯一实体，以协议为单位：

- 每个入站协议对应一条协议级 CMD，handler 只解包入队；
- 具名判定、领域调用、多段提交、投递、补偿都并入该协议的 CMD；
- 非协议来源的内部 CMD 按同样的形状改写；
- 细粒度命令并入、改成应用服务或删除；
- 请求级上下文成为 CMD 的显式数据；
- 结果 A 列明的过渡机制删净。

除经审查的 Spec 逐条列出的有意变化外，协议行为、落线顺序与持久化结果相对 A 封存状态不变。

## Scope

### In

根契约「In — 结果 B」的全部各项：

- 协议级 CMD；
- 请求级上下文成为显式数据；
- 迁移保留项与工作单元对照；
- 删净过渡机制；
- B 的判据、测试、测量与文档。

### Out

- 根契约「In — 结果 A」已交付的各项：B 不重新设计执行器、发件箱、发送端、调用链与删锁。在 B 中发现 A 的缺陷时，按回归处理并记录，不改写 A 已封存的结论。
- 根契约 Scope Out 的全部各项。

这里的不重新设计A不排除已由根控制器移交的有界修正：F-130测试清池和F-127写端完成日志判定在B首单元处理；F-88/F-93在迁移对应保护前补测试，F-132在使用事务内组装前处理。依据为A最终汇报、closure §6及根映射后继决定。

## Root Intent Coverage

| Intent ID | Source anchor and material intent | Disposition | Contract location or distinct active owner / authority | Rationale and evidence |
|---|---|---|---|---|
| RI-1 | 根RI-1：单分发模型替代细粒度锁探索 | not_applicable | 已封存A（e6f55558）SC-4；本SC-8B防回归 | 本结果不重新交付锁探索或删锁，根Scope In B明确；保留其结果 |
| RI-2 | 根RI-2：协议为单位，CMD吸收编排 | in_scope | Goal；SC-2、SC-13 | B的核心结果 |
| RI-3 | 根RI-3：唯一执行器有序 | in_scope | SC-8B保留A SC-1 | 改写CMD形状仍不得破坏唯一执行器，不重新设计它 |
| RI-4 | 根RI-4：先单执行器，只预留分流 | authorized_change | 根RI-4/Scope Out1；SC-8B | 沿用已接受单执行器决定，B不拆分 |
| RI-5 | 根RI-5：Event保留，NOTI归拢 | in_scope | SC-8B保留事件/发件箱语义 | 内部CMD改写仍保留可靠/在线投递 |
| RI-6 | 根RI-6：调用链、深度与超限 | in_scope | SC-8B保留A SC-6 | 显式请求上下文不能削弱调用链保护；F131复核按既有归属 |
| RI-7 | 根RI-7：玩法门与全局/个人锁删除 | in_scope | SC-8B保留A SC-4 | 不重做A，B不得重新引入这些门 |
| RI-8 | 根RI-8：短lock暂留 | authorized_change | 根RI-8/Scope Out2 | B沿用暂留决定，不扩成删短锁任务 |
| RI-9 | 根RI-9：每段SQLite、不回内存副本 | in_scope | SC-13工作单元对照；Scope Out | 保留提交点与持久化形状 |
| RI-10 | 根RI-10：governed SDD与独立调研 | preserved_upstream | 活跃根TaskContract ../../TaskContract.md RI-10及multi-spec-map.md | 根控制器仍负责工作流强度，B不重做根调研 |
| RI-11 | 根RI-11：初始规划后暂停、compact后开始 | not_applicable | 根Requires Confirmation1已兑现；当前用户明确继续 | 不重复历史停点，不解除其他确认门 |
| RI-12 | 根RI-12：同分支、一次收尾 | in_scope | Authorization；Constraints | 不推送/PR/远端变更，仍在原任务分支 |
| RI-13 | 根RI-13：替代#182/#204原加锁方向 | preserved_upstream | 活跃根TaskContract RI-13，A seal e6f55558，根外部收尾门 | 已实现的替代方向不重开，远端issue由根后续收尾承担 |
| RI-14 | 根RI-14：A/B独立验收、同一分支 | preserved_upstream | 活跃根multi-spec-map.md与根TaskContract RI-14 | 根负责串行激活、接受与一次收尾，B只交自己的六项条件 |
| RI-15 | 根RI-15：单条≤2秒接受 | authorized_change | 根RI-15；SC-9B | B用同套工具再测、与A对照；测试套件耗时/内存只记录 |
| RI-16 | 根RI-16：Astra通过后才收尾 | in_scope | SC-11B；根Requires Confirmation2；Authorization | B汇报是整个分支外审输入；A不重开，未获明确收尾前不推送/PR |

## Constraints

根契约 Constraints 的各项都适用。所有用户文字用中文；不运行下载的df_game_r或本机DNF.exe；本地提交带Co-Authored-By；不使用bare stash。在本结果里，有两条要具体说明：

- 「一次到位、删净」：根 SC-12 清单中的每一项，在 B 封存时检索为零。
- 「顺序保证没有例外」：A 建立的判据（执行器外写入、等待网络、自我等待）不得削弱。

## Success Conditions

编号沿用根契约，条件全文与证据以根契约同号行为准。

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-2 | 每个入站协议一条协议级 CMD，handler 只解包入队，非协议来源各自是 CMD，注入面判据已更新 | 根契约 SC-2 |
| SC-8B | B 封存状态下 SC-8 的每一项成立，A 的 SC-1、SC-3～SC-7 仍然成立、对应断言不放宽，相对 A 的测试变化逐条说明 | 根契约 SC-8B |
| SC-9B | B 封存状态下用同一套工具再测，单条 CMD ≤ 2 秒即接受，与 A 对照 | 根契约 SC-9B |
| SC-10B | 文档与协议级 CMD 一致，A 记下的已知缺口相应移除 | 根契约 SC-10B |
| SC-11B | B 的最终汇报 | 根契约 SC-11B |
| SC-13 | 保留项在 CMD 内位置等价，事务边界逐一对应，SC-12 清单检索为零 | 根契约 SC-13 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 沿用根契约Authorization、2026-10-22执行中授权、10-23同分支拆分决定，以及2026-10-24本会话“继续完成spec2剩余的工作” | 根契约授权范围内的结果 B：实施、验证及其检查点提交。不含合并、推送、PR、远端 issue 修改、部署或清理工作区 |

## Assumptions

根契约的吞吐够用、SQLite等待可接受这两条继续适用；CMD形状改变后由SC-9B重新验证。A的封存与核验是已接受历史，不把未做实机验收当成实机通过。构建/测试接受规则以根SC-8及A已披露基线为准，不将“全绿”解释为必须修复根Scope之外的两条既有内容基线。
