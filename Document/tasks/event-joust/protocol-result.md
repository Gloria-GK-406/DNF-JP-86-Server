# T-003 原生协议与在线接线结果

基准`b4bf8d0d01113c39962c885b44002453dcb740c6`，工作树`E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`。后端/UI串行交权后本worker独占生产写入；全体变更未提交、未部署，未操作客户端/PVF文件或真实数据库。当前目标已冻结，交回唯一生产写入权。

## Changed / Why

- 新增Protocol/Game/Joust原生切片，ProtocolModule注册CMD04E6/04E7/04E8、ACK和NOTI04B8..04BB。请求14/23/14B，下注slot为i32；ACK flag1+u32result；历史flag1路径成功和失败均3505B，500×7B、轮号0终止。04BB为七个四u8 record，不是两u16或padding。
- 领域State/History/Bet经Transactional命令；原生物理槽映射发生在message handler，CMD接收BusinessSlot后只调用领域。所有公开接口使用已选角色身份。查询/下注持selected transition exclusive lease直到投递，比对实际physical connection并执行session fence，防旧角色个人状态覆盖替代选角。
- 下注使用IEffectSettlementRequest/IRequestSettlementCommand和既有物理连接128frame账本；持久化key含物理Guid、selection generation、transport ordinal。新增Joust replay类别，仅刷新当前Joust和库存，不重发旧ACK，也不重执行业务。最小IRequestReplayProjectionDomainService同实例委托EffectOperationContext登记实际扣料槽，primary业务成功返回后、Freeze前准备足迹，事务提交后方可重放；失败/确认回滚不留下已提交足迹。直接成功响应也包含耗尽槽000E清空。
- 成功下注在提交后更新所有选角会话赔率/个人投注。普通确认回滚可error ACK；TransactionOutcomeUncertain不被catch为安全失败。提交后发送失败不回滚、不允许再次扣料；重复帧刷新当前状态。
- 新IJoustProjectionDomainService与现JoustDomainService同实例，只读Configuration/CurrentRoundAsync/Project，不调用补推进Current/Advance。入场EventPlayerSnapshot可选Joust并投影。JoustChanged Compose严格只读。已有EventsChanged时钟移入IEventPlayerProjectionDomainService，移除通知handler TimeProvider豁免，架构测试通过。
- 后台每秒事务Advance提交后比较Active/RoundId/Phase签名，抑制无变化重复推送；settled bool触发MailboxChanged。State瞬态SettledDuringAdvance独立于返回phase，Bet重新Project保留标志，跨轮Betting或日程外Inactive查询补派奖也会刷新邮箱；History完成后无条件刷新在线邮箱读投影。
- DailyRoundNo从保存轮次schedule与UTC+8开始时刻推导1..N，历史不会套用当前修改赛程。已settled轮次明确拒绝新下注，保护时钟倒退。审查修正赛程总阶段时间必须严格小于轮长，领域/API/UI一致拒绝零Result窗口。
- 保存逐协议人工记录/catalog、Document/JoustEvents.md和Human-Test-List。保留joust event/0/0，ClientStatic splitter需两个斜杠后resolver→01FC。

## Native policy / evidence limits

EXE/PVF/dump身份和RVA链见protocol-preparation及七条protocol人工记录。首版State沿Reference/ServerPolicy：Inactive0、Betting1、Locked3、比赛4、Result2；0..5静态scene消费并不证明业务映射，5备用路径Unknown且未发送。04BBStage0/1/2/3累计4/6/7/7结果供动画，未揭示KnightFF/Aux0为ServerPolicy。Aux是Win/Loss内容索引，实际PVF各28整数=4×7，选项0合法，非padding。Info字段A/B参考Win/LossCount，首版0，真实语义仍Unknown。特殊骑士index>=8下注/封盘隐藏flag1，比赛揭示flag0为参考策略。

Wire不含可编辑开始时间/轮长/StageSeconds；原生本地倒计时和动画是否能中断/同步短赛程仍需实机，不能承诺任意StageSeconds展示精确或静态资源日期动态更新。归档2016币的领域特许不阻止既有选角过期物品清理，测试应使用有效实例。当前ClientStatic/CurrentImplementation，无新WireCaptured/ClientVerified。

## Checks（最终源码）

PowerShell、.NET10.0.302；.NET命令串行，隔离SQLite；需要PVF时`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。本机require_escalated执行已获授权。

| 实际命令 | 结果/日志 |
|---|---|
| `dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj --no-restore --verbosity quiet` | 初始接线build成功，0警告/0错误；后续最终Host测试重编译当前依赖链 |
| `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~Joust\|FullyQualifiedName~EffectOperationReplay\|FullyQualifiedName~ReplayState\|FullyQualifiedName~NotificationHandlerArchitecture\|FullyQualifiedName~CommandHandlerArchitecture\|FullyQualifiedName~SelectCharacterLifecycle\|FullyQualifiedName~WeeklyAttendance' --logger 'console;verbosity=normal' --verbosity quiet` | 105/105，0skip；protocol-application-check.log |
| `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter 'FullyQualifiedName~Joust\|FullyQualifiedName~WeeklyAttendance\|FullyQualifiedName~SelectCharacter' --verbosity quiet` | 70/70，0skip；protocol-codec-check.log |
| `dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --no-restore --filter 'FullyQualifiedName~GmJoustHttpTests\|FullyQualifiedName~GmEventsHttpTests' --verbosity quiet` | 13/13，0skip；protocol-host-check.log；包含零结果窗口400及既有签到 |
| `npm test -- --no-file-parallelism src/events/JoustSettings.test.tsx src/Events.test.tsx`（Web/gm-admin） | 49/49；protocol-ui-check.log；包含零结果窗口前端拒写 |
| `npm run build`（Web/gm-admin） | tsc/Vite成功；protocol-ui-build.log；已有>500kB包体积提示不阻断 |
| `git diff --check` | 通过 |

新增真实dispatcher保护：同seq+body重复不双扣且刷新04BA，内容冲突拒绝，下一fresh seq独立追加，正常ushort回绕用新ordinal追加，提交后发送失败重复仅刷新，耗尽slot000E，无选角拒绝，确认SQL回滚同frame重试。查询测试证明exact physical批次投递期间replacement lease不能取得；维护测试验证无变化不推、phase推送、结算邮箱。补时钟回调已settled拒扣、跨轮/Inactive补邮件。异常不确定分支由现有effect/replay安全测试及显式catch过滤保护，不宣称本次新增客户端网络故障捕获。

期间修复测试fixture缺HostedService无关依赖、表名拼写、余额算式；新Joust枚举占用1024后两个旧“未知flag”测试改为2048，仍验证真正未知flag拒绝。最终仅以上fresh通过结果作为证据，不把初始失败算通过。没有全量SC7声明，主控随后做独立完整审查和fresh整体检查。

## Remaining / Handoff

原生日历窗口、黑骑士、State业务映射、Stage3最终文本、动画时长及真实领取按Human-Test-List待实机。跨重连相同内容新请求不是旧业务ID；128frame账本不保证任意断线重复点击跨重连去重。所有本worker测试/build session已退出，未启动服务；target frozen，后续只由主控接手修复/审查/一个本地提交，不部署。
