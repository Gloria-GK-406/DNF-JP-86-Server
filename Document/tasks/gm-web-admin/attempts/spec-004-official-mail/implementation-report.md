# Implementation Report: 官方邮件

基线 `573be46b075d8363f0882c2f40a5a0135d1ada0c`；批准 Spec SHA256 `4F3763FDE5FEC7B507D18131FCF541AB53F29152A7FC840EB4D5871DBEF5D91F`。生产者 `/root/gm_mail_producer`，planless focused；唯一工作区 `.worktrees/gm-web-admin`，没有 Git mutation、现服切换、真实玩家发放或客户端/PVF 写入。

## 当前恢复点

2026-10-02：生产已冻结供独立检查。完整生产接缝、中文页面、使用说明及隔离夹具已落地；第二轮全 solution 5,355 通过/0 失败/2 跳过，8 个测试项目，exit0。完整 build 0 警告/0 错误，前端全量 41/41、TypeScript/Vite production build 通过。fresh SQLite/18083 的 VERIFY_ONLY 1/1，正常停止后 listener=0。实际浏览器及独立审查尚待主控，不能宣称整个子任务已接受。

## 最终实现

- `GmMailRuleService` 归一文本、收件 ID 与输入界限，复用 package-opening/placement 内容事实，准备合法永久实例；拒绝期限、时装、宠物、服务/虚拟资源及不支持类别。
- `GmOfficialMailDomainService` 和四个 CommandBus handler 提供个人草稿/模板、窄收件人查询、预览、整批发送、持久收据及失败审计。发送实际进入事务后复核会话/权限/角色归属，在既有排序账号租约下复用系统邮箱。
- `SqliteGmMailPersistence` 和统一 schema 增加三张个人稿件/模板/收据表。mail、附件、新世界 UID、成功收据和成功审计同事务；永久 detached 装备领取前没有 slot，领取仍走原转移领域。
- GM HTTP 接口继承独立端口、SendMail、同源、防伪与 no-store。只有已提交操作绑定归一输入；相同管理员/UUID 重试返回原收据，不要求旧目标此刻仍有效。拒绝、确认回滚和未知提交分别反馈及独立审计。
- 提交后通过 Abstractions 中立 `MailboxChanged` 进入 Protocol，投影读取当前邮箱计数。中立程序集加入启动通知 catalogue；线上提醒异常只影响提醒标记，不回滚或再次创建邮件。既有 wire 布局没有修改。
- 中文页面覆盖编辑/保存/显式加载/个人模板/跨页多选/预览/确认/结果及历史。发送使用冻结快照与单一 UUID；sessionStorage 保存待确认请求，刷新只恢复，查询 404 不当作未发送，未知/超时仅查询或原输入重试。失权/退出后的旧响应不能恢复资料。
- 操作记录补新增动作与 RolledBack/Unknown 的中文映射。使用说明见 `Document/gm-official-mail.md`，协议接线事实更新 `protocol/game/server-to-client/NOTI-0x0063.md`，证据标签仍为 CurrentImplementation。

## TDD 与失败取证

Value gate：新后台 HTTP 入口具有现有邮箱测试未覆盖的跨请求权限、防重和原子风险；判据观察真实 HTTP、SQLite 邮件/附件/成功审计，内部服务重排仍有效。前端行为判据保护冻结快照与未知恢复。没有用编译失败充当行为 RED。

| 证据 | 观察与处理 |
|---|---|
| `red-gold-send.log` | 首个真实 HTTP RED：编译成功，尚不存在的发送路径被封闭边界拒绝 403，期望 200，1 个用例失败 |
| `green-gold-send-r2.log` | 1/1 GREEN：金币邮件及持久收据/成功审计各一份，原 UUID 重试不重复。首次 GREEN 尝试 500 根因是已有 detached allocator 尚未注册，补 IWorldItemTable DI 后恢复 |
| `red-mail-ui.log` / `green-mail-ui-r2.log` | 首个 UI 缺邮件编辑入口 RED → 1/1 GREEN；一次测试误在草稿尚未加载的 disabled 输入上 fireEvent，等待用户实际可编辑状态后复核，不作为产品 RED |
| `http-mail-r4.log` | 核心 HTTP 32/32，包括实际 PVF 领取、五个回滚故障点、未知提交、跨管理员及防重重启 |
| `http-mail-final.log` / `http-boundary-final.log` | 扩展首轮 35/36；唯一失败是错误地用 Host 头模拟实际监听端口。真实双监听 startup 判据扩展邮件 GET/POST 的 404，修正后受影响 5/5，保留原日志 |
| `solution-tests-final.log` | 全量首轮 5,353 通过、2 失败、2 跳过。NotificationRegistrationTests 的 Abstractions 已含中立通知，改用真正无通知的 DTO 程序集；SchemaOwnershipTests 从 51 表同步为 54，并补三表逐列断言。保留通知缺失及 schema 精确断言 |

以上日志均在 ignored `artifacts/gm-tests/spec004/`。故障注入日志中 SQL/事务异常是断言的一部分，不能仅通过搜索 Error 判断运行失败；以测试身份、最终统计及 exit code 判断。

## AC coverage anchors

HTTP 主判据文件为 `Server/tests/Dfo.Server.Host.Tests/GmOfficialMailHttpTests.cs`；以下按方法名定位，不把内部 mock 当成玩家实机证据。

| AC | 当前决定性证据 |
|---|---|
| AC-001 | AuthCsrfIndependentPermissionsAndQueuedSessionRevalidationProtectWrites、MailEndpointsInheritActualRemoteOriginAndPlayerCredentialBoundaries、个人对象跨管理员用例；LauncherHttpHostStartupTests.GmComponentHasItsOwnListenerAndDoesNotExposeLauncherRoutes 验真实错误端口查询/写入拒绝；仅 SendMail HTTP/UI 成功且角色私有查询 403 |
| AC-002 | DraftAndTemplatePersistWithOptimisticVersionsAndPrivateOwnership 真重启恢复；TemplateQuotaAndConcurrentRevisionDoNotOverwriteDurableContent 20/21、名称 1..64、并发相同版本、删除版本与释放配额；前端加载模板保留收件人/显式加载草稿 |
| AC-003 | 511/512 ASCII/中文、CRLF 预览、同标题正文、真实 mailbox 归一输入；原始 JSON 非法代理及控制字符拒绝；前端预览冻结禁改输入 |
| AC-004 | 单人/归一批量/重复 ID；SameAccountCharactersEachReceiveTextOnlyMailAndNormalizedBodyReplays 角色 1/47 同账户各一封；预检后删除/归属变化复核；无效目标/101 ID 拒绝 |
| AC-005 | ActualPvfEquipmentStackAndAccountMaterialsAreDetachedThenClaimedThroughExistingMailbox 验27312普通装备、3堆叠物、3037账号材料，2人/4个独立UID，领前0slot，领取原UID与gold/账号材料；零gold 0附件；FullEquipmentBagAndGoldLimitLeaveAttachmentsPendingThenCanBeRecovered 容量/金币失败保持pending、释放容量后领取；不支持/期限/数量及展开上限拒绝 |
| AC-006 | MidBatchStorageFailureRollsBackEntireBatchAndSeparatelyRecordsFailure 五处注入：第二人mail、equipment、attachment、receipt、success audit；每次四类持久行均0，另记RolledBack；UnknownPostDurableCommitFailureReturnsUnknownAndOriginalKeyRecoversCommittedReceipt 验SQLite已提交后nonfallible participant违约，返回Unknown，原收据/重试恢复；两种wire ID耗尽整批回滚 |
| AC-007 | 5并发/原UUID重启、删除旧目标后恢复；角色排序/重复及CRLF不改变语义；改变正文/物品顺序/数量/gold 409，UID不新增；另一管理员同UUID独立且原收据404；恢复同mailId/期限 |
| AC-008 | ReminderExceptionAfterCommitNeverRegrantsAndSuccessRemainsQueryable 的通知回调先核对成功收据已提交，通知两次异常仍只有一封；MailboxProtocolTests.NeutralMailboxChangeComposesCurrentCountAndRetainsExistingAlarmWire 同通知先后current count 2→0→7，错接收角色无输出，编码保持既有2-byte Alarm；全量既有邮箱/通知/注册回归 |
| AC-009 | 前端全量41/41，其中邮件16：草稿/模板/分页选择/附件/预览/冻结确认/结果、400/401/403/409/500/timeout、刷新无自动提交、404未断言回滚、查询成功及原快照重试、退出后迟到响应；实际隔离浏览器截图及操作由主控冻结后执行，尚未通过 |

## 隔离资产和夹具

实际 PVF 环境显式只读路径 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，SHA256 `3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06`（本轮重新读 hash 一致）。测试只生成隔离新库，不使用现服库。无真实游戏 ClientVerified 声明，文档列出 MAIL-01..09 供后续实机验收。

`GmMailBrowserFixtureTests.ExplicitMailBrowserFixture`：`DNF_GM_MAIL_BROWSER_FIXTURE=1`，`DNF_GM_MAIL_BROWSER_FIXTURE_DIRECTORY` 指向 ignored `artifacts/gm-mail-browser/spec004/<fresh UUID>`。127.0.0.1:18083，游戏/Launcher 网络关闭，operator / `gm-isolated-test-password` 为合成身份；45账号/47角色，1/2/3可发，预置“测试补偿”模板但0邮件。设置 `DNF_GM_MAIL_BROWSER_FIXTURE_VERIFY_ONLY=1` 做自检即退出，实际浏览器模式最多15分钟，stop.txt正常停止。不可复用既有test.db，也不可与共享.NET build/test常驻并行。

## owned change identity

29个生产/测试/使用说明/协议文件精确逐文件 SHA256 在 [owned-files.sha256](verification/owned-files.sha256)。汇总规则：git ls-files --modified --others --exclude-standard -- Server Web protocol 加两份使用说明，排序去重；每行 UPPERCASE_SHA256 两空格 repo/relative/path，LF、UTF-8无BOM、尾部一个LF。manifest SHA256 `963AF5D85502E0647C943437118CAB2DE591992D05808B3D6C429BD16A1B3908`。

名单排除主控拥有的 root map/workspace/spec003 游标和当前 Spec004 流程资料，避免混淆生产者与控制者写入。流程资料的自哈希不递归塞入生产 manifest。最后 git diff --check exit0；原 accepted HEAD 没有变动；当前没有常驻 fixture、共享 build/test 或现服操作。

## 验证结果与剩余边界

| 最后检查 | 命令与结果 |
|---|---|
| Frontend | Web/gm-admin 下 npm test -- --reporter=verbose，frontend-all-final.log，41/41 exit0；npm run build，frontend-build-final.log，exit0 |
| .NET build | dotnet build Server/86JP.Server.slnx -c Release --no-restore -m:1，solution-build-final-r2.log，0 warnings/0 errors、exit0 |
| .NET tests | readonly PVF_ARCHIVE_PATH 明确设置，两个 fixture opt-in 均关闭；dotnet test Server/86JP.Server.slnx -c Release --no-build -m:1 --logger console;verbosity=detailed，solution-tests-final-r2.log，5,355 pass/0 fail/2 skip、exit0 |
| Fixture | fresh selfcheck-9432c03552a741a8b798fbd29eda0af7/test.db，browser-fixture-selfcheck.log，真实页面200/preview200/template200/0mail，1/1 exit0；正常停止后18083 listener=0 |
| Worktree | git diff --check exit0；29-file manifest重读SHA一致，HEAD仍573be46b |

两个 skip 分别为 PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message 与 PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog：因为本次实际只读档案可用，不适用“缺档案”条件。不存在新增失败或不可用检查。邮件 HTTP36个判据已包含最后Host91/91，Protocol整体1232/1232。

前端 bundle1,121.24kB/gzip358.78kB，Vite500kB chunk警告仍存在，属于后续root集成优化观察，不调阈值掩盖。fixture 控制台个别中文字因测试进程输出编码显示异常，HTTP/SQLite中文字判据正常；未以改测试/忽略断言处理此输出差异。

未决：主控真实浏览器/漂移/独立挑战及结果审查。生产者交付自动化及可操作夹具，主控拥有 workflow transition；此文档不代替接受判决。冻结之后不再自行修改生产文件，审查整改需回到授权生产阶段。

## 有界整改 Round1：F-001（当前恢复点）

独立 change-review.md changes_required 已由主控消费，本轮只修每人每件展开装备的正常初始 Value；Spec/合同/immutable challenge 不变。此前冻结/浏览器证据属于旧 manifest963AF...3908，其 core.Value=0 不作为修复通过证据。

生产仅改 `GmOfficialMailDomainService.cs`：每个展开装备在实际创建时生成与既有正常初始化一致的 1..int.MaxValue-1 随机 Value，本批共享 HashSet 防重复；with-copy保留其余 core 字段。原收据重放仍早于创建/随机值生成。没有改规则、schema、wire布局、前端或资产架构。

测试只扩 `GmOfficialMailHttpTests.cs` 既有 ActualPvfEquipmentStackAndAccountMaterialsAreDetachedThenClaimedThroughExistingMailbox：两人各两件真实27312装备，从SQLite读取实际core，四个值正且不同；用实际MailboxCommand Open/Claim、MailboxCompleted通知组合和已注册的真实encoder检查邮箱摘要/领取后Inventory delta写的是原Value，原世界UID/core、耐久/封装保持。领取前后同UUID重试不新增装备或成功审计。合成selected session只用于服务器协议投影，不升级为实机证明。

TDD：`remediation-round1-red-r2.log` 编译成功，真实HTTP发送后四个SQLite core.Value均为0，正值断言失败，1/1 fail。`remediation-round1-green-r3.log` 1/1 pass、exit0。首次日志red.log只xUnit analyzer编译失败，不计行为RED；两个中间green尝试先暴露测试接缝误用（handler通过泛型接口注册）、再暴露测试过宽地把领取堆叠物正常新增core行当作装备变化，修正为同时比较原装备core及领取后完整行防重；保留全部原始日志，没有扩大生产修改。

新29-file manifest `verification/owned-files-r2.sha256` SHA256 `9D31E9639EF5E2B161DB24C788AB584A517BA604BD4B31D46B4184BCC5FFF770`；旧 owned-files.sha256 保留且hash仍963AF...3908，差异仅上述两个文件。当前Host完整及必要Protocol/架构回归在串行执行，结果补于下方；主控独立复审后再fresh全量验证，不把旧5355pass升级为修复后全量证据。

Round1最后结果：Host完整91/91（remediation-round1-host.log），必要Mailbox/Inventory delta/生产注册24/24（remediation-round1-protocol.log），命令/通知架构及启动注册34/34（remediation-round1-architecture.log）；均exit0。当前29-file r2 manifest逐一校验一致、git diff --check exit0、HEAD未动，18083无listener。已再次冻结，等待主控current drift/final review及fresh全量；本轮未重跑前端或实际浏览器，旧浏览器core0库保持原finding证据。
