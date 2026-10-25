# TASK-40 — 虚弱恢复与每日复活币归并

本 Task 承接 Plan P8 T-007 的城镇恢复与副本资源入口，迁移消息65545／65743、RecoverStaminaCommand／ClaimDailyReviveCoinCommand，并闭合 F-88-recover 保护行。TASK-39 已独立通过并以 `3e1af209`／`e8d9499f` 收口 T-006，留下可明确区分效果 owner 与保护参与者的 DeclaredNonRequest；本次只按真实旧边界消费。没有目标偏离；后续移动、传送与副本、Timer 来源仍留各自 Task。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-39；覆盖 RQ-001／003～009／013～015，AC-001～005／008。
- Outcome: 两个唯一完整协议 CMD／薄 handler，两个旧 fine 及全部实际引用删除；原金币／虚弱／免费次数、复活币钱包／按角色周期限领、响应和原子性保持，F-88 selected-not-world 静默保护落在新 owner。
- Boundary: 两个协议、实际共享纯投影／注册、正式测试、五条 ledger、AGENTS 清单与本记录。领域规则、共享 runner／raw／self-settlement 权限不扩大；原两者均非效果消息，无 Main／效果账本／同序号幂等。维持纯搬迁与语义提交分段，不能独立时在 Result 说明真实原因。
- Intentional change: 默认保原行为；Recover 原宽 catch 可能吞并不确定／独立 scope 失败，须正式旧新对照后按既有默认保护将差异逐项追加 Spec 变化表，随本 Task 快审。不能暗中扩大保护参与者或新建恢复机制。
- Review: 一次 sol6.1 执行、一次 fresh Astra 快审；不生成挑战、不建只取证 Task。

## Detailed Plan

1. 先钉旧行为：复用 WeaknessGoldRecoveryTests、WeaknessTests、RecoverStaminaProtocolTests、WeaknessProjectionTests、ReviveCoinTests 及真实入站／出站基础设施。保金币当前报价、免费资格与次数、满值／不足／非法 mode、地牢／塔内拒绝、旧 session、F-88 selected-not-world 静默；复活币重复／并发、余额非零不耗次数、花掉后同周期不可再领、跨期及旧记录、SQL 错误全回滚可重试。对迁移会触及的 Recover action SQL／真实 native commit／不确定或第二失败／scope，以及两路实际 encoder 失败，补正式旧 oracle 后再切换；不以假 sender 的 Discard 冒来源真实关闭。
2. 两个 CMD 吸收原 handler 和 fine 的查身份、判断、领域调用、clock、响应／投影，handler 只交付入队。Recover 保前后 IsInWorld／online／run／tower 与原错误码，普通写无 footprint、原空 selected 参与者；仅用既有 WriteResultAsync 的 KnownStorage commit-only 结果，窄 action 普通异常按已有 RequireRollback(cause) 模式映 Error4，正常业务拒绝保原空提交。取消、typed／Uncertain、机制 guard 与独立清理不可被普通 catch 恢复；不得改 runner、扩大 Self 或 raw。成功投影仍是当前 gold／weakness／premium 状态的原顺序。
3. DailyReviveCoin 一个 ordinary Write、空保护 Participants，按 P8 明确 DeclaredNonRequest owners=[原查询所得 session]，直接传 unit.Footprint 给原 ClaimAsync；不套 Cera 单 actor 的新增 Fence 范围，不在原 IsInWorld 领域拒绝之前新增安全服务门。无 session／online、正常拒绝、空提交、错误外抛原样；OpeningCompleted 的 ACK／钱包／claimed 状态顺序、按期事实和 Direct 记账同事务保持。日切 Timer 与消耗品 UseStackable 入口不迁，仅回归其共享业务。
4. F-88 继续保原正式 selected-not-world 测试，改为新真实路由后仍无帧、无金币和虚弱变化；至少验证新 guard 的可编译移除会使该行为断言变红，再恢复。旧 fine 直调测试换真实 whole／协议驱动，保原业务结果断言，不以新 bus 次数替代。真实 encoder／scope 用同 provider、生产 outbox／sender 和精确连接验证；披露受控 transport 与 A21 实机区别。逐个引用边界删除 fine，无其它调用方遗落。
5. 冻结后 Release build、相关聚焦及 App／Protocol／Networking 三项目 full；实际影响扩及 Host 等再补对应 full，共享 core 如确需变更先返主控。保持原类并行／5m／detailed console／独立 DFO／真实 PVF 前置、两原 Dimension 身份、逐 display/status/重数 N/D；T-007 总收口再八 full。只闭两消息／两 fine／F-88-recover 五行，全部 original 与非目标不变，strict／八坏例 selftest、diff-check。Result 一页写交付／非常规选择／真实验证与失败身份／遗留，停写交 fresh 快审，主控消费并本地提交后继续。

## Result

Outcome: done

- **交付**：两条唯一 whole CMD／薄 handler 已接线，两个 fine 类型、注册与调用删除；仅闭合两消息、两 fine、F-88-recover 五行。Recover 保无足迹的 ordinary WriteResult／空 Participants；Daily 为 ordinary Write／空 Participants，P8 owners 仅原查询 session，原领域 IsInWorld 与同事务 Direct 记账不变。无 Main、账本、raw、Self 或 core 增权；Timer／UseStackable 来源未迁。
- **选择**：有意变化列 VC-B9：不可恢复失败默认收口、fine scope 折 whole 后的成功帧→精确来源关闭、已普通空提交拒绝的直接 ACK 编码失败改 typed；成功投影 encoder 仍按 NotificationBus 原隔离。正式测试采用同 provider 的生产 outbox／sender与受控 transport，实际 CloseAsync／原链及同 ID 替换隔离均有断言；未做 A21 实机。旧测试只改 whole／dispatcher 驱动与精确旧查询准备，保业务断言。纯测试准备可在 `e8d9499f` 独立应用（本机 `verification/logs/task40-old/pure-preparation.patch`）；whole 的默认失败变化与结构迁移不可分：显式 runner 必须持有真实 completion／独立清理事实，保旧宽 catch 会要求未授权恢复能力，故不引兼容层。
- **验证**：规定原环境 `require_escalated` 下 Release solution build 零警告错误；旧默认聚焦92/92，新94/94，Protocol11/11；F-88 可编译去 guard 正式红（多出 Error4 帧），恢复后绿。三项目按原类并行、项目串行执行 `dotnet test -c Release --no-build --no-restore -m:1 --blame-hang-timeout 5m --logger 'console;verbosity=detailed'`（独立 DFO／TRX）：App5462＝5444通过＋2原内容失败＋16原skip，Protocol1781/1781，Networking203/203，总7446＝7428通过＋2失败＋16skip。两失败仅 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，均为7100/maze0/(1,0)缺 Left map。逐 display／status／重数相对 TASK-39：App5436＋26−0＝5462，其他 N/D=0；DFO70＝原67反例＋本轮三control，outside／exe／orphan=0。strict、八坏例 selftest、diff-check通过；证据见 `verification/task40-verification.md`。
- **遗留与披露**：首次误在 sandbox 跑 full，App 原宿主 timer 用例在5m非活动门中止，mac dump权限不足；Protocol诊断后取消、未启动该轮Net，原现场留 `verification/logs/task40-final/`，不充正式结果。按规定环境重跑的独立 timer 1/1及三full均完整结束，未声称已证具体 OS 根因。正式最终输入／输出为 `verification/logs/task40-final-r2/`；其它五项目未在本 Task 重跑，T-007 总收口仍待后续。未提交、推送、PR、部署或清理；交唯一 fresh 快审。

## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

范围：独立审查 TASK-40 contract r1／Result 与 `e8d9499f` 上本轮未提交改动，按 process-reset、Plan P8 T-007、Spec B6（含 VC-B9）及当前 AGENTS，采用 reviewing-work 的 Task Unit／Code 判据。仅本 Task 局部快审，不代表 T-007 或 Spec B 完成；无阻塞发现。

- 实际产品 diff 仅两个协议切片与 ProtocolModule：唯一 whole／双向绑定、薄 handler、原细命令类型和生产引用移除。Recover 保前后 session／online、run／tower、金币／免费额度判定；窄 action catch 经 RequireRollback(cause)，取消／typed／不确定和独立 sticky／cleanup 不获普通恢复。Daily 沿原 ClaimAsync 事务与 IsInWorld 拒绝，DeclaredNonRequest 仅原查询 owner，双方 Participants 为空，无 Main／账本或 core 增权。VC-B9 与实际默认失败及投递时序相符。
- 直接读新增 flow 测试与旧测试差异：真实 decoder／SQLite／encoder／生产 outbox-sender、受控 transport.CloseAsync，覆盖 native Commit 19/787、action 19/1811、双因／scope／取消／独立 request、精确来源链及替换连接隔离；旧业务断言保留。独立解析旧 TRX 92/92、新聚焦94/94及Protocol11/11；F-88 mutation 真实因多出 Error4 帧失败，最终同名用例通过。
- 独立解析 `verification/logs/task40-final-r2` 的三 full TRX：Application 5444通过／2失败／16未执行，Protocol1781通过、Networking203通过；逐 display/status/重数与 task39-final-r3 比较仅 Application +26／−0，另两项零变化。两失败仍为 DimensionDungeonTests 的 Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource 与 All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，均7100/maze0/(1,0)/Left缺图。build日志零警告错误，DFO解析70条均属既有反例或三control、无outside/exe/orphan；strict有效、八坏例全部拒绝，当前 diff-check通过。
- 逐值比较账本仅两消息／两fine／F-88-recover五行变化，全部 original 及非目标不变。AGENTS只加清单；纯测试准备与迁移可分提交，结构与默认失败语义不可分的说明符合本轮范围。

限制：首次 sandbox full 中止是诊断证据，不计正式通过；final-r2 的 timer 单例与完整三 full 已结束，不能由环境对照推断具体 OS 根因。其它五项目为历史证据，本轮不冒八项目 fresh full；未做 A21 实机。审查后停止写入，交主控消费及提交。

## Controller Consumption R1

接受独立快审 R1，TASK-40 通过。主控实读产品／测试差异、VC-B9、审核和正式 TRX，独立重算三项目 7446＝7428通过＋原两条失败＋16跳过，App N26／D0、另两项零变化；重新运行 strict 与八坏例 selftest，并逐值核实只有五目标行改变、全部 original 和非目标保持。首次 sandbox 配置偏差已纠正，原中止现场保留；正式验收只取规定环境的 final-r2，不据此推断具体系统根因。

纯准备默认可运行，独立应用检查通过，已本地提交为 `e8ea2f93`；随后提交 whole、正式测试追加、VC-B9 与本记录，暂存分段不改变最终受测文件。当前业务消息73/159、旧命令60/204、来源1/73、保护1/5、过渡0/8；T-007 尚未收口，继续城镇移动／传送。未推送或分支收尾。
