---
kind: rks-verification
decision: pass
---

# 马战开发交付验证

2026-10-06，目标为基线 `b4bf8d0d01113c39962c885b44002453dcb740c6` 到冻结工作树的完整改动。精确声明：契约定义的开发、静态协议接线与自动验收完成，可供后续部署和游戏客户端验收；不声明已经部署或ClientVerified。

独立完整审查见 [review](../reviews/change-review.md)。Round1审查67个输入的摘要 `51090C84E6EEE74BC43678470A2A3E623933C189CF572D047C08FF1BB9DBEA2C` 在格式修正前重算匹配（仅还原预期游标），见 [身份检查](artifact-identity.log)。暂存后新文件的CRLF/EOF触发默认whitespace检查；新增文本随后统一LF和末尾单换行，日志去行尾空格。涉及4个源码/测试文件的CRLF/EOF变更经逐字节规范化断言确认语义保留；UI/spec/plan字节未变。Round2独立审查复核此delta及最终记录，当前输入身份由其报告给出，不将旧raw摘要当作新版本摘要。

## Fresh checks

工作区 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`，PowerShell，SDK10.0.302。所有.NET项目命令串行，测试使用隔离SQLite/临时Host；前端与.NET输出目录独立。下列命令均exit0；测试650/650通过，0失败/0跳过，不是整个仓库全量测试声明。

| 命令/范围 | 结果 | 日志 |
|---|---|---|
| `dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj --no-restore --verbosity quiet` | 0警告/0错误 | [Host构建](host-build.log) |
| Application测试，筛选见下 | 318/318 | [领域与回归](application.log) |
| Protocol测试，筛选见下 | 165/165 | [协议与投影](protocol.log) |
| Host HTTP测试，筛选见下 | 28/28 | [GM接口](host-http.log) |
| Persistence：`FullyQualifiedName~CharacterExperienceStoragePolicyTests` | 1/1 | [经验存储](persistence-experience.log) |
| `npm test -- --no-file-parallelism`，cwd=`Web/gm-admin` | 10文件138/138 | [完整UI](ui.log) |
| `npm run build`，同上 | tsc/Vite通过 | [UI构建](ui-build.log) |
| 默认 `git diff --cached --check`及必需制品存在/审查输入身份 | 格式修正后通过，接受前再检查 | [制品身份](artifact-identity.log)、review Round2 |

.NET测试共同命令形式：`dotnet test Server/tests/<项目>/<项目>.csproj --no-restore --filter '<筛选>' --logger 'console;verbosity=normal' --verbosity quiet`。需要PVF的测试设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。

- Application项目=`Dfo.Server.Application.Tests`；筛选为每项加 `FullyQualifiedName~` 并用 `|` 连接：`Joust, Architecture, Replay, CharacterExperience, SkillMaterialConsumption, MailboxCore, ItemCatalog, Refill, CharacterEntry, SelectedCharacterCurrentView, SelectCharacterEntryLifecycle, SessionAdjacentEntry`。
- Protocol项目=`Dfo.Server.Protocol.Tests`；筛选：`Joust, WeeklyAttendance, ReplayState, SelectCharacter, StateSynchronization, GrantKillExperience, GrantClearExperience`。
- Host项目=`Dfo.Server.Host.Tests`；筛选：`GmJoustHttpTests, GmEventsHttpTests, ItemCatalogHttpTests, GmInitializationHttpTests, GmDailyRefillHttpTests`。
- Persistence项目=`Dfo.Server.Persistence.Tests`，筛选见表。

650测试及两端构建首先针对语义冻结版本执行。格式修正后Host再次构建0警告/0错误，Application318/318、Protocol165/165再次通过（上表对应日志已换成最新运行）。Host HTTP、经验存储和UI检查输入的业务语义不变，UI文件字节未变，不重复无变化检查。Round2原始67路径摘要 `6320C9F1D03D456FE87D1892D55F0AA11A406C752828A600DD185864622EBF0E` 已重算匹配，仅还原预期游标，见身份日志。构建保留已有Vite主包大于500kB提示；Host回归保留既有独立掉落PVF缺失项警告，均未导致失败，没有用删弱测试消除警告。

## Success conditions

| 条件 | 当前决定性证据 |
|---|---|
| SC-1 管理/日历同状态 | Application集成的同singleton断言、启停/期限EVENT_INFO过滤；原生Entry接线测试、LinkKey两斜杠ClientStatic链；独立审查确认分层 |
| SC-2 专属设置 | HTTP保存/重启/revision/权限/CSRF/审计回滚和零Result窗拒绝；UI草稿/冲突/未知结果/外部修改/只读名称(ID)测试；实际PVF定义读取 |
| SC-3 下注 | 真实PVF/SQLite两币择一、同币跨槽、限额/等级/封盘/非法材料/池边界；SQL扣料后失败回滚、稳定键碰撞/重复、真实dispatcher耗尽槽重放和同帧重试 |
| SC-4 阶段/恢复 | UTC+8绝对阶段边界、保存赛程/内容/结果、重启无重抽；快照轮号和4/6/7/7投影codec；已settled时钟回调拒扣 |
| SC-5 奖励 | 真实附件数量与领取、Final不提前派奖、关闭/到期仍结算、邮件故障同事务回滚/重试一次；跨轮/Inactive补派奖邮箱通知、后台维护通知 |
| SC-6 原生布局/接线 | 7条protocol人工记录、14/23/14请求、3/90/46/31通知、完整3505历史错误路径；codec/handler/entry与实际dispatcher测试、选角lease与精确连接身份测试 |
| SC-7 回归/交付 | fresh650测试和两端构建；目录/初始化/每日补给/签到/经验与重放回归；完整审查approved、使用说明和人工清单齐备 |

## Evidence limits and repository disposition

[人工清单](../Human-Test-List.md)仍包含原生日历窗口、黑骑士、State真实业务映射、Aux动画、Stage3最终文本、短赛程本地时长同步、真实游戏领奖。线包没有可编辑赛程时长字段；当前证据为ClientStatic/ReferenceImplementation/ServerPolicy/CurrentImplementation。归档2016币的局部特许不绕过选角过期清理，需有效测试币实例；跨重连相同内容的新请求不属于旧请求键。这些限制在契约里作为独立人工验收边界，不将自动检查提升为实机成功。

本次未部署、未改客户端/PVF或真实玩家数据。fresh确认PVF SHA256=`3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06`；EXE SHA256=`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。本地结果接受前确认HEAD仍基线，主checkout HEAD仍 `6ee658cdf14208d819360c40b3b47c4717ae3499`。一次接受提交包含产品、测试、流程及证据；其实际SHA由提交后的外部Git状态给出，不回写自引用。
