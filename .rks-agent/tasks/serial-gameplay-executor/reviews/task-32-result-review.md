---
kind: rks-review
target: tasks/TASK-32.md
verdict: approved
---

# TASK-32 结果快速审查（最终测量 M-3，独立审查者）

- 审查对象：`tasks/TASK-32.md` Revision 1，第 1 轮执行，基准 `2bdb1afd`（代码冻结点），相对 HEAD 的未提交差异与未跟踪文件：`verification/measurement/m3-*`（19 份）、`raw/m3-*`（16 份）、`measurement/README.md`、`follow-ups.md`、`logs/task-32-*.txt`（6 份）。`tasks/TASK-33.md` 不在范围内。
- 适用标准：reviewing-work 的 Task Unit 与 Deliverable 准则。
- 方式：只读。从 scratchpad `t32/m3/` 的 18 份 host.log、samples.csv 与 `t32/classify`、`t32/signals` 的输出独立复算，脚本另放在 `scratchpad/review32/`，没有重跑测量。

## 结论：`approved`

「M-3 通过」成立。S1～S5 的数字能从原始数据独立复算；没有一处越线被归为收尾、探测或污染而放过。发送端层 126 行的归类与 `t009-logs-and-cases.md` §4 及裁定 3、4 一致。归为 P 的 20 行全部来自就绪探测连接。下面四条发现都不阻塞，只涉及记录措辞、反例覆盖和后续去向。

## 发现（按严重度）

1. **L1（低，记录措辞）判定日志的运行前部分不是「逐字节相同」。**
   - 位置：Result「Why」最后一条与 `m3-summary.md` 开头。
   - 情况：`t32/prelog-snapshot.txt`（22,825 字节）与 `logs/task-32-judgement.txt` 开头相比，从第 159 行第 22,787 字节起不同。第 159 行是运行前部分的末行时刻行，原文是 `(pre-run part written 2026-10-23T19:44:45Z; the formal matrix starts after this)`。运行后 `judgelog.sh` 重建文件时，把这一行改写成带「header text written 19:43Z; … unchanged」以及开跑时刻的版本。
   - 判断：规则、§4 五条、预期、回归核对与自检这些实质内容没有在运行后改动。三份输入的修改时刻都早于第一次运行（19:44:52Z）：`judgement-header.txt` 19:44:44Z，`regress-out.txt` 19:42:02Z，`selftest-out.txt` 19:42:58Z。所以这不影响判定，但「逐字节比对相同」说过了头。
   - 修法：改为「除末行时刻行在运行后补写了说明外，逐字节相同」。
2. **L2（低，反例覆盖）P2 没有单独被反例拒绝过。**
   - 位置：`t32/rules_selftest.py` 的 R1；`follow-ups.md` F-119 写的「反例 R1～R4 各自被拒」。
   - 情况：R2、R3、R4 分别只让 P3、P4、P1 不成立，但 R1 同时让 P1、P2、P3 不成立（自检输出 `P fails ['P1', 'P2', 'P3']`）。如果 P2 的实现失效，这组反例发现不了。
   - 判断：读码可确认 P2 实现正确（`not mentions.get(c)`，与 D 共用同一张提及表，R1 的 D 一侧正是靠它被拒）。本次 P 类只有 `Game:01-00-00-00` 一条连接，不影响结论。
   - 修法：给子 Spec B 复用时补一例 R1′（编号最小、bootstrap 后 0.1 s、只有 `Failed`，但被一行 handler 提到），或把 F-119 的措辞改为「R2～R4 各自拒绝 P3、P4、P1，R1 同时违反 P1～P3」。
3. **L3（低，后续去向）F-127 与 F-126 是同一种先后竞态，记录没有把两者连起来。**
   - 72 行 Warning 的事实：
     - 全部落在收尾窗口起点之后 0.007～0.231 s，而且每次运行都集中在同一毫秒，是工具同时关闭全部连接；
     - 例如 A-def-f256：「logins done, flooding」21:19:52.523Z，＋ 30 s ＋ 2 s ＝ 21:20:24.523Z，告警出现在 21:20:24.563；
     - 异常都是 `InvalidOperationException: Writing is not allowed after writer was completed`；
     - 同一批里另有带后缀的 Information（`IOException: The connection closed before its send pipe drained`），说明同一刻关闭的连接里，有的被判为「已不在」，有的没有。
   - 归为 T 正确：符合 §4 第 1 条「收尾窗口内不计」。这是工具收尾断开时的预期结局，不是服务端的发送故障。
   - 机制：`OutboundSender.IsGone` 只问 `Resolve`，也就是 `entry.Session.Online`。TouchSocket 关闭时先完成管道写端，后翻 `Online`，两者之间的发送拿到的就是这种异常，而 `IsGone` 仍为假。这正是 F-126 描述的「失败先于摘除被观察到」，只是异常类型不是 `SocketException`。所以它确实暗示 F-121 的判定漏掉了一种常见形态：任何客户端在服务端发送途中断开，都可能留下一对 Warning。
   - 风险：低。只影响日志级别，不影响行为（队列照 RQ-036 以 `Failed` 结束，连接本来就在关闭），与性能无关。对子 Spec B 的最终测量，T、D 规则仍覆盖它；只有它出现在收尾窗口外的 agent 连接上时才会计入 S5，那是偏严的方向，`m3-summary.md` §9 已写明。
   - 去向建议（代码已冻结）：F-127 保持未关闭，交主控方，在冻结之后的日志收尾或子 Spec B 中处理。可选修法：在 `DeliverAsync` 的失败分支里，把「会话正在断开或写端已完成」也视为已不在；或等传输层的断开回调落定后再判 `IsGone`；再补一条类似 `LifecycleExecutorWireTests` 的线级用例。F-127 一行加一句「与 F-126 同一机制」即可。F-126 按裁定 7 关闭不受影响：F-126 形态定义为 `SocketException`，0 行；即使按机制算，72 行也全在 T 内。
4. **L4（信息）受污染的 B-def-2 稳态窗参与了三处计算**：控制运行的区间、「相对 M-2 显著变差」和基线重测触发。三处都只会把结论推向更保守的一侧（未证明等价、不触发），不改变任何判定，不需要修改。

## 已核实无问题

| 项目 | 核实方式与结果 |
|---|---|
| S1 | 独立解析 host.log：作业 max 269.33 ms（A-dbg-1，`inbound:GameSelectCharacter`）；p99.9 构成 A 26.76／27.53／26.91 ms，ramp-A-2s 94.99 ms；内层命令 max 134.22 ms；ramp-A-2s 作业 max 232.95 ms。全部与记录相符。构成 B 的 p99.9 我算得 0.28／0.27／0.31 ms，记录是 0.29／0.28／0.32 ms，差别来自分位取法，不影响结论 |
| S2 | samples.csv 中全部 `run` 运行端到端 ≥ 2 s 的样本为 0，最大值 1231.08 ms（A-f256）；入队等待 max 872.46 ms（A-dbg-2）。只有 ramp-A-2s 有 ≥ 2 s 的样本，那是 2 s 口径爬坡停止档的预期 |
| S3 | `m3-ramp-full-2s.md`：1200 档实际 1163（96.9% ≥ 95%）、max 1251.1 ms，判为通过；1600 档 max 10012.4 ms，停止。ramp-B 16 档 max ≤ 66.61 ms，错误 0。ramp-B 受污染，但它只用来判「通过」，没有用来排除越线 |
| S5 | 15 份 summary.json 的 errors 与 failedCommands 都是 0；直接用 grep 数 host.log，`Command … failed in handler` 与 `Gameplay … failed:` 0 行，`OverLimit` 0 行，`Game handler failed` 0 行，`fail:`／`crit:` 0 行。Warning 只有每次 1 行启动期的 `IndependentDropRuleService`（不在归类范围内），加上收尾那 4 次运行各 18 行。带后缀的行数（各次 0／2／4／10／16）与 P、T 的计数逐次吻合 |
| 归类实现（`classify.py`） | `OverLimit` 先于 T 判定：§4 第 2 条要求任何时刻都计入，判 T 之前先拦下，不会被收尾窗口放过。P1～P4 照计划实现。`closed: Failed` 的异常取同一连接之前那条级别和后缀都相同的 `entry Failed`，取不到就按 S5 命中处理。D 要求全部行落在同一轮的时间窗内，超出连接数上限时全部不归 D，比计划更严。没有连接号的行在收尾窗口外一律计入。这几处偏差都只会让豁免变少，合理 |
| P 的 20 行 | 构成 B 的 10 次运行，每次恰好 `Game:01-00-00-00` 上 2 行（entry 与 closed），在 bootstrap 200 之后 0.114～0.117 s，都是带后缀的 Information，异常是 `ClientNotConnectedException`。构成 A 的 8 次，最小编号是 `Game:02`，它被 handler 行提到，所以 P2 不成立，没有误归。没有吞掉别的连接 |
| 回归核对 | `t32/judge.m2-copy.py` 与 `t28/judge.py` 逐字节相同，`regress.py` 中的 `ATTRIBUTED` 与之一致。M-2 的 16 份 host.log 上：P 的行号逐行相同，计入 0，T 24 ＋ 12 ＝ 36，PASS |
| 自检 | R1～R7 都归为计入；三类正例不计；级别分支两例（`ClientNotConnectedException` 为 S5 命中，`SocketException (54)` 为 F-126 不计）。20／20 |
| 「相对 M-2 显著变差」 | `judge.py` 实现「两次都 > 2 × M-2 较差值，且超出 M-2 差距」，速率类量反向判定。表中 34 行（17 × 2），0 项；最大比值 1.15（189.9 ÷ 165.5）、1.13（269.3 ÷ 239.1）。(b) 是单次运行，没有列入检验，1163 对 1169 也明显不构成变差 |
| 269.3 ms 的归因 | `samples/A-dbg-1.tsv` 中 20:14:08.999Z 那一秒，其他进程合计 360%，其中 `Microsoft` 进程 325%；作业结束于 20:14:08.811Z，与这一秒重叠。记录把它标为推断，且照常使用数值、写进 F-81。另外两次 debug 运行的选角作业 max 为 234.0、237.1 ms，同量级 |
| 控制运行 | 12 项区间逐项复核正确，结论写「未证明等价」，与裁定 2 一致。(e) G0 口径 9.84／14.84／8.47 ms：中位数 ÷ 4.94 ＝ 1.99，÷ 4.42 ＝ 2.23，范围 1.71～3.36 倍，记录同时给出逐次值与范围。「M-2 那次 17.84 ms 多半含噪声」标为推断：M-2 那次 G0 的稳态 p99.9 为 84.19 ms、max 164.4 ms，明显异常，可以支撑这一推断 |
| 四方对照 | 抽查 `m3-B-g0*.md` 中 (e) 其余连接的 p99（9.84／14.84／8.47）与汇总一致；M-2 G0 为 17.84 |
| 记录与状态 | F-02、F-119、F-126 写「已在 TASK-32 关闭」，与裁定 5、7 一致；F-77～F-82、F-120 追加 M-3 值，仍未关闭；F-120 中「G0 只跑了 1 次」那一句已按三次 G0 改写；F-121 追加真实进程中的证据，状态不变；F-127 新增，未关闭，归主控方裁定。AC-005 与 AC-021 只记在 `m3-summary.md` §1 和 F-02 一行，`spec.md` 未改 |
| 边界 | `git diff 2bdb1afd -- Server Tool` 与 `git status --porcelain -- Server Tool` 为空；`git -c core.whitespace=cr-at-eol diff --check` 无输出；新文件与改动文件没有 CR，也没有行尾空白；记录 18 ＋ 汇总 1 ＝ 19，raw 15 ＋ 1 ＝ 16，与合同一致 |
