# 结果 B：协议级 CMD 最终汇报

当前产品检查点为 `0c5acb958f673d3caf4352c3914960bed78a2c68`，位于原分支 `task/refactor-session-20261021`。迁移、结构收口和最终测量已完成，完整独立结果评审已批准。最终核验见[completion-evidence.md](completion-evidence.md)，本报告随[结果 B](../spec-result.md)交付；根接受与封存状态以[根映射](../../../multi-spec-map.md)的实际提交记录为准。

## 结果与范围

已注册的 173 个入站消息中，159 个业务消息显式绑定协议 CMD，14 个纯遥测保留原例外。运行时库存为 172 个 CMD、137 个 handler 实现；它是实际注册导出的结果，不是写死的验收数量。204 个旧命令、73 个非协议来源、5 项保护和 8 项过渡机制在五类迁移账中全部闭合。12 条原技术 HTTP 路由继续按原技术边界处理。

网络 handler 交付解包消息和精确连接身份并等待入队结果。具体 CMD 拥有整个用例的判定、领域调用、显式工作单元、补偿和投影登记；请求权威在执行器内建立并作为显式输入传递。多阶段选角及补偿保留各自事务点，没有合并成一个大事务。非协议来源按自己的 typed 输入进入同一执行器。

共享 CMD 基类及 59 个为测试覆盖而保留的生产虚方法已经移除。共用代码限于固定协议阶段、受众查询及既有领域能力；具体 CMD 不以可空依赖、`null!` 或 `Func` 能力隐藏协作关系。`TownArrivalProjection` 含领域状态变更，是固定城镇到达阶段；不能把它称为纯函数。规则同时检查真实注册集合和违规反例。

五账当前路径见 `migration-ledger.json` / `migration-ledger.md`，结构、测试夹具变化和证明边界见 `verification/task66-verification.md`，文档与判定依据见 `verification/task66-documentation.md`。账本闭合本身不替代行为证据。

## 正式测试与证据边界

最终 fresh Release 构建为 `task66-final-r4-build`，0 警告、0 错误。正式八项目组合结果为 **9817 = 9797 通过 + 2 条既有失败 + 18 条既有跳过**。

| 项目 | 总数 | 通过 | 失败 | 跳过 | 实际运行 |
|---|---:|---:|---:|---:|---|
| Application | 6012 | 5994 | 2 | 16 | task66-final-r2-application |
| Protocol | 1795 | 1795 | 0 | 0 | task66-final-r4-protocol |
| Networking | 203 | 203 | 0 | 0 | task66-final-r4-networking |
| Persistence | 484 | 484 | 0 | 0 | task66-final-r4-persistence |
| Infrastructure | 976 | 974 | 0 | 2 | task66-final-r4-infrastructure |
| Host | 208 | 208 | 0 | 0 | task66-final-r4-host |
| HttpApi | 59 | 59 | 0 | 0 | task66-final-r4-httpapi |
| Pvf | 80 | 80 | 0 | 0 | task66-final-r4-pvf |

Application 沿用 r2，其他七项目采用 r4：r2 到 r4 只有 Protocol 测试 `InitialConnectionDeliveryFailureTests.cs` 的夹具补正，生产、共享测试支持、Application 源码和项目配置逐字节不变。已核对 2862 项源码清单及项目引用边界；没有复制或重命名旧运行，也没有把各轮早期失败删除。准确的 run 映射和差异清单留在原始证据目录。

两条失败仍是原有 Dimension 内容不兼容，错误身份和信息与已接受基线相同；18 条跳过的身份也相同。没有额外失败豁免。相对 A 的显示用例库存为 **8350 + 1510 − 43 = 9817**，其中包含改名与替换，不能把 1510 全称为新增业务测试。43 个旧显示用例的去向逐项核对；TASK-66 相对 TASK-65 只新增 7 个结构用例、零删除。

两条失败的完整方法名为 `Dfo.Server.Application.Tests.World.DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot` 和 `Dfo.Server.Application.Tests.World.DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`。二者错误均为 `Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.`，没有将其他内容错误并入这一豁免。

10 个 Characterization 全部通过、0 跳过；15 个 Golden 文件与 A 逐字节相同。表征源码相对 A 仅有 PartyTower 注释和 mask 说明名称两处变化，掩码位置、宽度和断言未放宽。DFO 原解析器检查通过：Application 62 + Networking 6 = 68 个负例，七个程序集的正对照齐全，反例/对照之外、exe、orphan 均为 0。exe=0 只说明该观测边界内没有记录，不代表能发现任意吞错。

TASK-66 的组件测试改为组合真实具体 CMD 和既有领域接缝。少数精确结果 spy 只证明结果后的受众、顺序和投影；本地 Units 驱动不冒充生产 request admission 或 SQLite 事务。真实注册流程、事务、物理连接及线级证据分别由相应强测试承担。旧弱化、疲劳、quota 和翻牌夹具遗漏的必需能力已按真实 DI 补齐，具体断言增强与旧断言保持情况逐项记录在 TASK-66 验证文件。

测试运行器报告的八套件时间合计从 A 的 321.639 秒变为 405.955 秒，约 1.262 倍。Application 为 117.456 → 172.086 秒。B 另记录进程墙钟和 `RUSAGE_CHILDREN` 高水位；它不是同时存活子进程的 RSS 总和，A 缺少同口径内存数据，因此不作内存改善/退化判断。套件时间不是玩法 CMD 的 2 秒接受门。

## 最终测量

完整对照见[最终测量](final-measurement.md)。原G1二进制与15场参数保持，按原条件追加1场清洁2秒爬坡，16场均exit0、客户端错误0、S5命中0。可观测逐CMD最大356.0821 ms，默认日志无样本场记N/A，不计作零。冻结2937项与PVF在前后复核不变。

清洁原p99爬坡可持续实际率24999→15000，最高稳定目标25000→10000；Debug洪峰吞吐约下降9%～17%。追加2秒条件爬坡通过至实际25007，但原趋势算法最高稳定15000，不替代首次下降。混合2秒爬坡稳定目标两边均800、可持续1163→1151；下一档因排队端到端约10秒停止，CMD本体未越2秒。12普通场次的端到端2秒计数为0。

本轮9个窗口受原P1/P3规则判污染，A原有2个；无阈值越线，不按污染本身重跑。最大cold stall1394.45 ms、最大排队1392.5001 ms。全部样本、原分位、环境、对照及追加条件原因保留。逐协议标签改变后只使用实际唯一映射，不能把旧inbound子集合无样本当0通过。没有运行A21实机或做性能根因诊断。

## 验收与可见变化索引

[完整证据映射](final-evidence-map.md)列出17 RQ、11 AC、六条B根条件、40项VC和18项交接及另列事项。它按当前Spec B10边界解释实现；不是把全量通过或迁移行数当作业务证明，也不将有意whole-scope变化称为行为完全不变。

## 已知限制

- 翻牌待办在排队未执行时的取消已有覆盖；自动翻牌进入真实 Write action 后的已请求停机取消可能仍记为失败 CMD/执行器 Error。该位置尚无专项 native 探针，不能将排队取消证据外推到 action 内。这是完整独立评审 CH-1 确认的低风险诊断残余，不扩大任何通用取消恢复资格。

- 称号查询的真实 ReadOnly 路径仍会被 `ItemLifecycleScope` 可写要求拒绝；回归测试守住原失败和不发帧。`QueryRows` 夹具只证明目标与投影，不能宣称真实查询成功。
- F-131 保留 A 的已披露精度：登记关闭尚未执行时传输先断开，仍沿登记链继承；它不是物理断开唯一原因的证明。
- F-132 的事务内 Compose 在生产禁用，结构正反例守住不可达；机制本身没有修复。
- A 的 CH-1 多结束挂钩首错跳过后项、CH-2 异常日志提供器的 Warning 再抛错，仍作为原已接受低风险残余保留，不凭本次迁移声称修复。
- F-113 人群范围、F-114 外部 SQL 导入、F-116 长时间累积、F-117 客户端称号证据沿原范围保留。既有 TCP bind/refusal 偶发没有被证明修复，不能据此豁免本轮未知失败。
- 本轮使用合成客户端、受控物理连接和真实 SQLite；没有运行 A21 游戏客户端，自动与线级测试不等于实机验收。

## 独立门与交付状态

[一次独立挑战](../reviews/spec-002-change-challenges.md)保留1条低风险CH-1；[独立Astra完整结果审查](../reviews/spec-002-final-review.md)为approved，确认当前A+B实质集成，未发现阻断项。该审查要求的精确取消限制已按原文纳入上节，产品未改动。最终核验与结果封存见上述制品，根控制器在接受B后承接同一reviewer的确切提交确认。此内部评审不替代用户安排的整分支Astra外部审核。

所有工作仍在原本地任务分支；未推送、开PR、合并、打tag、修改远端issue、部署或清理分支/工作树。
