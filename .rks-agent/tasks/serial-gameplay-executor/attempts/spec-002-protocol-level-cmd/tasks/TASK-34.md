# TASK-34 — 收拢 AGENTS 的协议迁移说明

本 Task 承接流程重置文件 §5 的第二步，为后续 T-004～T-012 维护简明且准确的入口说明。核对迁移账全部已闭消息与实际协议路由，但不关闭任何消息、命令、来源、保护或过渡行。前一 TASK-33 已在 `27406d41` 独立修复 NoRoom 正常确认回滚并通过快审，本次只整理文档，无目标偏离。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-33（已提交）。
- Outcome: AGENTS.md「命令执行总线」的逐协议迁移说明收拢为一张已走协议级路由的清单和共同规则，协议特有例外仅一句话加准确出处；篇幅接近 A 封存版本，方便后续仅增清单。
- Boundary: 只改该节及直接相关的重复交叉引用、本 Task；不修改代码、测试、Spec、Plan、迁移账或已封存记录，不产生行为变化。
- Result review: required；一次 sol6.1 执行，一次 Astra 独立快审，无挑战。

## Detailed Plan

1. 对照当前 AGENTS、A 封存 `2079d98d` 的相同章节、当前迁移账及实际注册，列出已迁协议。旧版只通过 git 引用，不复制快照。
2. 合并重复的逐协议叙述，共同规则只陈述一次；保留显式请求、单元/足迹/账本、失败收口、三个具名 raw 出口和仍消费的旧机制等实质边界。协议特殊行为只保必要提示并链接现有 Task/领域/协议说明或拥有源码，不能让清单授权未迁路由或掩盖例外。
3. 核对清单覆盖当前已迁入口、引用存在、章节外无意外变化，执行 git diff --check。纯文档不增加或重跑正式测试，不把 TASK-33 的检查称为本次运行。
4. 写一页内 Result，交独立快审后由主控提交。迁移账数字保持不变，随后按 Plan 原顺序推进四个剩余 opening 和其余 T-004 消息。

## Result

Outcome: done

- 交付：仅收拢 `AGENTS.md` 的命令执行总线一节，形成已迁协议清单、共同边界、仍在消费的 legacy 机制及共用领域事务说明；补齐旧清单遗漏的 CeraShopPurchase、Mailbox 六入口，并移除把已迁 MoveItemSpace fine 仍列作 Transactional 的过期说明。章节外原文保持不变。
- 选择：协议特例各留一句与准确出处，Task 来源统一链接本轮任务目录再指名文件；不把迁移历史再搬一遍。保留显式请求／工作单元／足迹／物理账本、最终失败收口、三种具名 raw 出口及尚未删除的 replay／ambient／Lifecycle 边界。正文比整理前缩短约四成；仍比 A 封存章节长，因为 A 尚无协议级路由，这些现行限制不能按旧篇幅删掉。
- 实际检查：Python 对照当前 `ProtocolModule.cs` 注册与 JSON ledger，53 个协议级消息集合与 53 条 closed 消息完全相等，零遗漏／多报，全部目标文件存在；逐组人工核对清单名称及 logical／wire 分组。新章节 10 个本地链接、13 个具名 Task 引用均存在；脚本确认章节外文本与 HEAD 完全相同；`git diff --check` 通过。纯文档没有增加或重跑正式测试，也未引用前一 Task 的测试作本次运行。
- 遗留：迁移账保持消息 53 closed／106 pending／14 retained、细命令 41／163、来源 1／72／12、保护 0／5、过渡 0／8，不关闭任何行；剩余 opening、Rental 及后续簇沿 Plan 推进。待独立快审／主控消费，未提交、推送或开始下一 Task。

## Independent Quick Review R1

```yaml
kind: rks-review
verdict: changes_required
```

审查对象：Contract r1、上述 Result R1，以及 `27406d41` 上尚未提交的 AGENTS 命令执行总线章节。独立于执行者，按 `reviewing-work` 的 Task Unit＋Deliverable 判据完成本地快审；遵守流程重置 §3／§5，不生成挑战，不代表 Spec 最终审查。

**唯一阻断项：共同规则把既有 action 恢复范围写窄。** AGENTS 的「窄 action catch 仅将原具名业务拒绝带 cause 回滚」会让后续迁移误把普通存储动作故障改成默认失败。实际 `Server/src/Dfo.Server.Protocol/Game/PackageOpening/GamePackageOpeningCommand.cs:50` 捕获原领域调用的非取消、非不确定异常，带原 cause 请求回滚；`:79` 起既保 OpeningRejectedException，也将普通存储失败映 StorageFailure。Quality、Socket 等同样保留普通 action SQL 路径，TASK-15／TASK-17 已明确记录。最小修正：写成「窄 action catch 仅保各协议原有恢复范围（含已有普通存储动作异常映射），带 cause 回滚完成后才映原失败 ACK」，同时保留现有 guard／sticky、取消、不确定及独立 cleanup 失败默认保护；不要扩大为任意协议通用 catch。

其余本地检查通过：独立抽取 ProtocolModule 的协议 CMD binding，53 个消息与 ledger 的 53 条 closed 消息集合完全一致；表格对应这些 logical／wire 分组，无未迁 opening 或 Rental 冒入。三个具名 raw 出口、显式请求／足迹／账本、最终失败收口及旧 replay／ambient／Lifecycle 限制均保留。10 个本地链接和具名 Task 引用存在，逐项特例可追至所指文件；TASK-22～25 范围也含实际 TASK-23／24。脚本确认章节外文本与 HEAD 相同，`git diff --check` 通过；未改代码、测试、ledger，未重跑正式测试。

压缩结果已实质形成清单＋共同规则，虽然长于 A 封存版本，保留新增路由约束有实际理由，不以字符数设门。修正上述一句及对应 Result 后，只需围绕本项与直接回归做 R2；其余证据可沿用。

## Result R2 — 有界措辞纠正

Outcome: done。主控接受 R1 唯一发现，将 action catch 的说明改为各协议原有动作恢复范围（含已有普通存储异常映射），并明确 guard／sticky／cleanup 不在恢复范围。没有改变代码、权限或其他文档内容；原 R1 覆盖及链接检查继续适用，git diff --check 通过，待 R2 有界复核。

## Independent Quick Review R2

```yaml
kind: rks-review
verdict: approved
```

审查 Contract r1／Result R2，目标仍为 `27406d41` 上未提交的本 Task 文档改动；仅复核 R1 唯一发现及直接回归，其余检查沿用 R1。现文明确按各协议原动作恢复范围处理，包含已有普通存储异常映射，须带 cause 确认回滚后才映原 ACK；guard／sticky／cleanup 仍排除，前文取消、不确定和提交后错误的默认保护未改，Mailbox 未被扩大为通用 catch。措辞与 R1 所核实际调用路径一致，阻断项已解决，无新的本地缺陷。独立运行 `git diff --check` 通过；未重跑代码测试，未修改正文／Result，未提交。本批准仅覆盖 TASK-34 结果，不代表 Spec 封存或整体完成。

## 主控消费

R1 所指恢复范围措辞已按实际代码纠正，Astra R2 独立复核 approved；消费当前 AGENTS 差异与 Result R2，未改变生产或迁移账。按纯文档单元提交，后续进入 T-004 剩余四 opening 的正式表征与迁移。
