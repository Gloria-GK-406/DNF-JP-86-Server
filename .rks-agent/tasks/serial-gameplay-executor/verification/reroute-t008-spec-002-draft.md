# 子契约草案：结果 B（协议级 CMD）

- 日期 2026-10-23，基准提交 `06ca5e0f`。改道提案的一部分，汇总见 `reroute-t008.md`。
- **这不是生效的 TaskContract。** 按 `multi-spec-workflow`「Model Exactly One Child」，下一个子契约只能在结果 A（spec-001）封存、根评估之后写到确定路径。所以这份草案不放在 `attempts/` 下：放在那里，控制器会按 phase 推导第 7 条把它当成待激活的子契约，与仍在执行的 spec-001 冲突。
- 激活时：
  1. 控制器按根评估的缺口复核本草案；
  2. 写到 `attempts/spec-002-protocol-level-cmd/TaskContract.md`；
  3. 派独立的子契约审查；
  4. 审查通过后再写子路由决定。

  本草案只写到 ready 所需的程度，不写 Spec。
- 落位时的 frontmatter：

```yaml
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-2, SC-8B, SC-9B, SC-10B, SC-11B, SC-13]
depends_on: []
```

`depends_on` 为空：A 封存后是已接受的历史，按 `controller-validation.md` 不作为依赖边。

---

# TaskContract: 结果 B——协议级 CMD

## Context

### Source Request

本契约是根契约 `../../TaskContract.md`（revision 4）的结果 B。来源请求、用户原话、授权与术语以根契约 Context 为准。拆分与「同一分支、一次收尾」的依据是用户 2026-10-23 的原话：“继续吧，在同一个分支干。不过可以拆成你说的另一个spec单出”。

核心来源是用户原话：“不，这里的CMD直接把其他的都吸收了。命令变成唯一实体。不过这里的命令含义就是以协议为单位的了。要改的估计怕是很大。……早期的架构设计有点随意了。现在要还债了”。

### Current Situation

以结果 A 封存后的提交状态为起点（激活时填 A 的封存提交）。A 已经交付执行器、提交后发件箱、删锁与调用链；B 的输入是 A 的过渡机制清单（根 SC-12）。改道前的 Spec（`../../spec.md`）中移交给 B 的需求、附录与验收，见 `../../verification/reroute-t008.md` §3，激活后作为 B 的 Spec 的起点材料。

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

## Root Intent Coverage

| Intent ID | Source anchor and material intent | Disposition | Contract location or distinct active owner / authority | Rationale and evidence |
|---|---|---|---|---|
| RI-2 | 根 RI-2：命令变成唯一实体，以协议为单位，CMD 吸收其他一切 | in_scope | Goal；SC-2、SC-13 | 本结果的核心 |
| RI-5 | 根 RI-5：Event 组件保留，NOTI 归拢 | in_scope | SC-8B（事件语义在内部 CMD 改写后不变） | A 已经交付执行层面的部分；B 改写事件 CMD 的形状时不得削弱 |
| RI-8 | 根 RI-8：短 lock(sync) 先留 | authorized_change | 根 Scope Out 第 2 项 | 同根契约 |
| RI-9 | 根 RI-9：持久化不变 | in_scope | SC-13（工作单元对照） | — |
| RI-15 | 根 RI-15：单条指令 ≤ 2 秒即接受 | authorized_change | SC-9B | 同根契约 |
| RI-16 | 根 RI-16：GPT Astra 审核通过后才收尾 | in_scope | SC-11B；根 Requires Confirmation 第 2 项 | Astra 的审核默认在 B 完成之后对整个分支一次进行，B 的最终汇报（SC-11B）与 A 的最终汇报都是输入。用户若先让 Astra 看过 A，Astra 对 A 的意见可以在不跨边界的前提下并进 B，否则作为编号接在 B 之后的修正子 Spec；封存后的 A 不重开。收尾门属根契约 Requires Confirmation 第 2 项，B 不推送、不开 PR |

根 RI-1、RI-3、RI-4、RI-6、RI-7、RI-13 由结果 A 交付；RI-10、RI-11、RI-12、RI-14 由根契约承担。B 不重复处置这些。

## Constraints

根契约 Constraints 的各项都适用。在本结果里，有两条要具体说明：

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
| task | 沿用根契约 Authorization 一行与 2026-10-22 的执行中授权（见根契约 Context revision 3 一段） | 根契约授权范围内的结果 B：实施、验证及其检查点提交。不含合并、推送、PR、远端 issue 修改、部署或清理工作区 |

## Assumptions

- 结果 A 的封存状态可以独立运行；B 在其上按功能簇分批归并，每批之后构建与全量测试都是绿的。
- 根契约 Assumptions 的前两条（吞吐够用；SQLite 等待可接受）同样适用；B 改变了 CMD 的形状，所以由 SC-9B 重新验证。
