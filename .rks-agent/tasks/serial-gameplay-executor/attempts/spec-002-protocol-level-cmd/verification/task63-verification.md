# TASK-63 验证记录

本批闭合十九个现存 timer 和共享同一推进体的 Joust startup，共二十来源，以及十二个最后生产消费者 fine。消息159 closed／14 retained；fine197 closed／7 pending；来源66 closed／7 pending／12 retained。原 ID、original、顺序与非目标行逐项保持；五个保护项和八个过渡项未动，不创造 F-07 账行。严格迁移账和八反例通过。

最终采用明确的复合来源：r1 fresh Release solution --no-incremental 构建零警告错误，完成前五项目；Host r1 暴露测试筛选问题后，只改该 Host fixture，再以 r2 fresh Release 同样零警告错误及完整 Host／HttpApi／Pvf 收口。前五项目的生产和测试源码均未改变，不称八项来自同一轮。每项实际 require_escalated、原 xUnit 并行、5m、detailed、独立 DFO/TRX，严格串行且自然退出；命令、权限、起止、exit 在各 run.json。

| 项目 | 正式来源（verification/logs/） | 通过 | 失败 | 跳过 |
|---|---|---:|---:|---:|
| Application | task63-final-r1-application | 5998 | 2 | 16 |
| Protocol | task63-final-r1-protocol | 1795 | 0 | 0 |
| Networking | task63-final-r1-networking | 203 | 0 | 0 |
| Persistence | task63-final-r1-persistence | 484 | 0 | 0 |
| Infrastructure | task63-final-r1-infrastructure | 974 | 0 | 2 |
| Host | task63-final-r2-host | 208 | 0 | 0 |
| HttpApi | task63-final-r2-httpapi | 59 | 0 | 0 |
| Pvf | task63-final-r2-pvf | 80 | 0 | 0 |

总计9821＝9801通过＋2原失败＋18跳过。两失败仍为 `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot` 和 `Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`，完整 message 均与 TASK-62 一致：`System.IO.InvalidDataException : Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.`。十八跳过的 display、message、重数不变；未调用 PVF 偶发例外。

相对 TASK-62 的 r5 App／Host／HttpApi、r3 Protocol、r4 其他四项，新增十八 App、零删除：F07 FIFO／跨周期／替换／排空四例，具名 RO 取消五例，实际登记及无目标水位三例，真实嵌套 scope 三例，Creature 正常读取失败一例，Premium 双合约到期两例。逐 display／outcome／重数库存及原 DFO parser 输出在 `task63-final-combined-inventory.json`、`task63-final-combined-dfo-*.log`；原67负例＋七对照保持，outside／exe／orphan全零。

F-07 的 FIFO 是已批准 VC-B3：隔离旧 `1c67ebfd` 在 `task63-old-fifo-r1` 编译成功后实际断言红（refresh→request），同一例在新实现为 request→refresh。三条新增机制边界随迁移提交。Creature 的正常领域读取失败原路径先旧 green（`task63-creature-oldgreen-r2`），再新 green（`task63-final-focus-r1`）；真实 hosted timer／outbox 证明 Warning、原精确 owner 关闭及下一目标继续。Premium 两种合约同时到期和首轮已到期的清零顺序原例未同时覆盖，新增两例先旧 green（`task63-premium-oldgreen-r1`），新正式 App 同断言通过。纯测试 patch 为本机 `/private/tmp/task63-pure-tests.patch`，依次只有原 FIFO 一例、Creature 文件、Premium 文件，互不重叠且均基于 `1c67ebfd`；当前 F07 文件追加的三例仅随迁移。

生产者只持调度状态并提交 typed CMD；manual 单 CMD 立即入队，F07 manual 整轮在执行器外排队，hosted busy／drain覆盖扫描至最后子 CMD。配额保原周期（Mirror UTC+8午夜）、Premium保完整规范化权益指纹及前次flags／hadPrevious、Fatigue保完整余额且不加周期指纹。单条／批登记新增实际接受事实，保原 Registered 汇总含义；缺目标和空批不推进水位，接受登记不承诺落线。RO 取消仅真实 registry 授权的 Timer＋BodyOnly、对应固定快照单元；三处真实 scope Dispose 标识保双因，Joust／Room 不将它吞为普通 Warning。七具名单元只复用既有健康 WriteResult，不扩 Main／Self／DeclaredNonRequest／raw。

中间失败均已修复并完整复核：Protocol 的新 GiveUp 测试漏覆写实际 ICommandHandler 映射，改为真实协议 bus／生产祭坛阶段与实际 ambient 事务、End 前后断言；App 的唯一新增完整失败是 producer SemaphoreSlim 未在机械清单登记，补执行器外轮次用途且保反例；Host r1 207通过／1失败，是确定性 fixture 仅删 concrete 而留下同类型 hosted／drain alias，修为一并移除，再 r2 全208通过。新 Creature 表征首次因 xUnit 分析器编译失败，修正测试后在旧产品取得 green；不把该次编译错误算行为证据。本批 Networking 正式203通过，没有重现上一批地址冲突。

测试领域 driver 只证明其领域与帧事实；生产来源／bus／scope／FIFO由真实来源和集成测试负责。结构债沿主控 phase2-backlog 留第二阶段；没有 A21 实机或专项性能完成声明。未独立评审、未推送／PR／merge／tag／清理，两个旧验证 checkout 均保留；本执行者未写 Git。
