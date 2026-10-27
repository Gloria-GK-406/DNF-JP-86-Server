---
kind: rks-review
target: tasks/TASK-19.md
verdict: approved
---

> 第 1 轮结论为 `changes_required`（M1）；第 2 轮定点复核后更新为 `approved`，见 `## Round 2`。评审者按调用方要求只读，本文件由主控方按评审回报落盘。

# TASK-19 结果快速审查（独立审查者）

- 审查对象：`tasks/TASK-19.md`，合同 Revision 1，含执行前裁定 1～4、回报后裁定、第 1／2 轮 Result；改动为基准 `0c9c59c8` 之上的未提交差异。
- 适用标准：reviewing-work 的 Task Unit 快速审查，加 Code 与 Deliverable 标准。
- 评审者自跑（`--no-build`，二进制新于源码）：`GameplayJobHostStructureTests` 与 `GameplayExecutorSelfWaitTests` 15／15，`InboundExecutorWireTests` 5／5。

## Round 1 — `changes_required`

- **M1（中，唯一阻断）**：`follow-ups.md` 的 F-62、F-61 状态写作「信息」（文件头定义为「不需要动作」），而 F-62 是 AC-006 的承接者、F-61 按 TASK-18 处置 N4 交给 T-006 之后复核；`t004-closure.md` §7 也把 F-61 归在「信息」组。按「未关闭」筛选时两项会漏掉。引用核对只查「不是完全关闭」，没能拦住。
- **L1（低）**：`t004-closure.md:99` RQ-008 行的「留给后续」漏了「执行器等待作业上直发的交付（每帧 ≤ 2 s）」，归 T-006（F-20）。
- **L2（低）**：`follow-ups.md` 文件头的归属改正清单漏了 F-62。
- **L3（低，残余风险）**：源码一支可被刻意绕过：csproj `<Using Alias>`（生成在 `obj/`）、Unicode 转义标识符、字符串内 `//` 被注释剥离器吃掉。今天 `Server/src` 中均不存在。
- **L4（信息）**：`FaultRunningJob` 在 `:51` 重读 `GameplayJob.Active`，同步路径上与基准等价，泄漏流情形下更正确；测试类注释漏列 Utilities；F-73 的既有弱点（Order 100 的 `CommandExecutionPolicyBehavior` 也调 `DemandCurrent()`，新旧写法都钉不住「before_uow」），改写本身保住了区分力，建议关闭；AC-006 缺口可补写附录 B 生产者一行缺两个名字。

第 1 轮逐项通过：
- 归属断言：7 个调用点的令牌都来自本执行器；`OwnedOrThrow` 先判空再查归属；`FaultRunningJob` 与改前等价且未给派生类新增能力；S7～S9 证明黏滞保留、方案 B 是唯一保住原语义的写法。
- 结构判据：13 个 csproj，反射 8 个程序集 ＋ 源码一支 1574 个文件，覆盖全部项目；H1、H2、S5 证明两支各自有区分力。
- AC-003 第 3 条：装饰器入队加一、作业体开始减一（同步拒绝也减一）；观测点不依赖时序；S6 下读到 `waiting flooding 63`。
- VC-8 与 UDP：补查 `.WriteAsync(`、`SafeSend`／`TrySend`、`PipeWriter`、`Response.Body`、`WriteAsJsonAsync` 等，作业上的客户端连接写入只有一处；`A21UdpDiscoveryService.cs:91-131` 不在作业流上，不触发停止条件 4。
- 收口对账：20 项交付与 RQ-005～015、040～043、091 逐一有行；三项未满足的 Spec 验收如实列出；131 条交接中对不上的 15 条（12 个事项）处置成立；「没有一项阻断 T-005」成立。
- 数字：8224 ＝ 8202 ＋ 2 ＋ 20；取证 71 条三种加法；新增 11 条。

主控方处置见 `tasks/TASK-19.md`「主控方处置（第 1 轮评审后）」。

## Round 2 — `approved`

- M1：`follow-ups.md:73`、`:74` 状态改为「未关闭」；§7 中 F-61 移入 T-006 组，新增数目列，12 组逐组计数与合计 58 对上，无重复编号；`logs/task-19-closure-refs.txt` 记录新口径，拿基准版跑会拦下 F-62，重跑后违规 0。
- L1、L2、L3（F-76，归 T-009）、L4（F-73 关闭并记既有弱点、AC-006 缺口补名、测试注释补 Utilities）均已落实。
- 生产 diff 与第 1 轮相同（两个文件，＋39 −10）；`cr-at-eol` 口径 `diff --check` 无输出。
- 用脚本把 `follow-ups.md` 全部 76 行与 §7 交叉核对：未关闭与部分关闭事项都在 §7，§7 不含已完全关闭事项（F-14 状态为「T-006 之后消失」，不需列入）。

非阻断残余：新口径按关键词判定，F-40、F-41、F-43、F-44、F-70 这类条件触发的复核仍归「信息」或「人工把关」，分组站得住；F-61 归属列的「信息；」前缀已由主控方在提交前删除。
