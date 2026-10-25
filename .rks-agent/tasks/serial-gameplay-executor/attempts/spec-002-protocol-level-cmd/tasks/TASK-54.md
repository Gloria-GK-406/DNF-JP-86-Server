# TASK-54 — 客户端设置与通知已读入口归并

本 Task 承接 T-007，闭合 SaveGameOption1（65733）、SaveHotkeys（65734）、SaveQuickChat（65904）、SaveCharacterOption（65984）、NoticeSeen（66014）、InformNoticeSecond（66040）六消息，以及 SaveClientSettings／SaveCharacterOption／MarkNoticeSeen 三 fine，共九行。TASK-53 已获唯一 Astra 批准并由主控接受，以 e3b353d3／ae0fd603 分段本地提交，工作区干净。目标未偏离：保 blob 字节、profile 捕获与现有归属、原事务、无 ACK；ActivateClientHotkeyProfile 与事件来源继续留在后继。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-53；承接 RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 六消息各有唯一 Protocol／Direct whole binding，原通知二消息可共用具名有限家族；普通写归入 whole，删除三 fine 与仅有编排。客户端设置保 nullable selected：原 origin 有 selected 才以精确 actor 保护，否则空 participants；CharacterOption 保原 actor；两 Notice 均空 participants。全部 None footprint，无 Main／效果 ledger／raw／Self，不改核心。
- Nature/Gates: 保持行为迁移；正式旧新证明的 whole 默认生命周期差异才追加 VC，由唯一 fresh Astra 随结果审查；不改 Spec B8／Plan P10。一个 sol6.1 执行、一个 Astra 快审，无挑战；纯测试准备独立提交，不外部收尾。

## Detailed Plan

1. **先钉旧行为。** 先跑 App Accounts/ClientSettingsBlobTests、Characters/CharacterOptionsTests／NoticeStateTests，Protocol 同名 codec 强例。补注册 decoder／dispatcher→真实领域与 SQLite→生产 outbox／精确 physical 的正式旧新表征，至少有账号态和 selected 态、两 hotkey profile 与双 quick-chat bank、两 notice owner，以及多字段隔离／无帧。保既有 entry 读回完整 blob、提交后 appearance event、捕获 profile 失效与 exact cleanup 的强断言，不以假 bus 调用替代。
2. **账号设置三入口。** 原 online account 门后建立 ClientSettingsOrigin（精确连接、账号、generation、nullable selected）；每种实际写一个 ordinary unit，原 selected 非空才参与保护。Hotkeys 的 CaptureHotkeyUpload 必须在原事务外且在原位置；未知 profile 仍 Warning 后无事务／无帧，事务内只用原已捕获 context，不重猜职业或 profile。保 Main150B、QuickChat bank0/1各300B、Hotkeys最长65536B的原 decoder，完整 bytes 不拆解；保各日志及原实际保存方法。
3. **角色设置与已读。** CharacterOption 仍完整516B且首u32=512，需原 selected／online 门，一个 [actor] 普通写到原 SaveAsync；不新增反馈。Notice id仍 exact4B且0..127；Primary 属账号，Secondary 需 CurrentCharacterId 才属角色，原 GameSessionIdentity 传给领域。旧 fine 只有 IAccount，无 selected，所以两路 Write 都 []／None，不能因角色 owner 而增加保护或外层 InWorld 门；原 Notice 领域仍检查角色 InWorld／数据库 owner，原 CharacterOptions 领域仍检查同样事实。两者无 ACK、无额外快照。
4. **失败与结构。** 六消息真实 action／native Commit 回滚、已提交 scope.Dispose 和替换连接隔离按旧保护范围表征；无 ACK 的路径不要凭空加 encoder 案例。没有业务错误 catch 的路径继续传播原因；普通 rollback 不升级业务成功。真实单位观察应区分 account-only 与 selected，未命中 origin／profile 门不进单位。新薄入口只委托 whole；三 fine 的类型／生产引用／注册清零，ActivateClientHotkeyProfile 保留真实消费者，不关闭来源行。
5. **只闭九行。** AGENTS 只加清单一行，ledger 只改六消息三 fine，原值与其他行不变。原强测试若直接 TestGameplay 后再调 handler，适配到实际新入口边界，保原业务断言；旧代码纯准备 patch 与结构适配分开，不把新结构断言塞旧复验。
6. **冻结与交付。** 最终旧复验前完整 CURRENT 持久备份、锁 HEAD／branch／owned、全部 runner结束，交主控全文读脚本后执行并 finally 逐文件恢复。恢复后 fresh Release solution --no-incremental 0警告0错误，再 Application／Protocol／Networking full严格串行，原类并行／5m／detailed／独立DFO；若实际触及其他项目据实扩展。逐 display／outcome／重数核增删，保原两项 Dimension 失败与18有效skip，不新设豁免。Result、runtime、strict／八坏例、含新文件 diff-check 后 STOP，主控交唯一 fresh Astra，接受后本地分段提交并继续 T-007。

## Result

- Outcome: done（执行完成，待唯一 fresh Astra 快审与主控接受）；本轮 owned 为六消息、三 fine、相应正式测试及记录，基线 `ae0fd603`，不关闭 T-007。
- **Changed:** 六薄入口绑定五 Direct／Protocol whole；三个设置保 nullable selected，CharacterOption 保原 actor，两 Notice 始终空 selected；各 ordinary Write／None，完整 blob／profile 捕获、原归属门及无 ACK 保留。三 fine 删除，ActivateClientHotkeyProfile 和事件来源继续保留。仅九 ledger 行 closed，AGENTS 仅表一行；默认 scope 旧 raw→whole typed／原精确保护差异追加 VC-B34，不改核心、白名单或 Plan。
- **Why:** 角色 owner 不等于 selected 参与者；hotkey 事务外捕获不能重猜。35 项正式 wire 表征覆盖真实领域／SQLite／scope／physical，新发现的 Notice wire 归属断言精度由主控裁定补强同用例后，重新旧新 r2。原 entry 强例仅把假构造 physical 改用实际在线实例，保存与 entry 全 blob 读回断言保持；此结构适配与纯准备分离。
- **Checks:** 主控最终旧复验 r2 App50／Protocol56 全 pass，并核21 CURRENT逐 bytes恢复。其后 fresh Release 非增量 solution 0警告0错误；r2 App5788＝5770pass＋2原Dimensionfail＋16skip、Protocol1789pass、Net203pass。逐 display／outcome／重数新增 App35／Proto1、无删除，原两失败名称／消息及 skip 完全不变；三 fresh7780，有效历史合9580，不冒八 fresh。DFO70＝67反例＋3control，类外／exe／orphan0；runtime 六 binding／三 fine0／空 exits／Self None；Result后strict、八坏例、含新文件 whitespace、全部 runner结束／最终owned恢复材料通过，详见 `verification/task54-verification.md`、`logs/task54-final-r2`、`evidence/task54/`。
- **Remaining:** 无执行 blocker；保原两 Dimension 内容失败与后继范围。唯一独立快审、本地分段提交由主控办理；无 A21 实机／专门性能验收，无 Git暂存／提交／推送／外部操作。STOP。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

**结论：通过本 Task 结果快审，无阻断发现。** 目标为合同 revision 1、基线 `ae0fd6030c445dd83b93bb9432839edeccc4eaa0` 上未提交的 TASK-54 最终 r2；按 `reviewing-work` 的 Task Unit／Code 判据及 `process-reset-2026-10-25.md`，核 Spec B8／Plan P10 的既有边界。此结论只覆盖六消息、三 fine 与 VC-B34，不关闭 T-007 或 Spec。

- **实现与保护：** 独立对读全部产品 diff、新五 whole 及正式测试。六入口唯一 Direct／Protocol binding，Notice 是精确两 CLR 家族；三 fine 类型与生产引用删除，ActivateClientHotkeyProfile 消费者保留。Hotkeys 在原事务外捕获，未知 profile 不进单元；三设置仅 nullable selected 非空时声明精确 actor，CharacterOption 一个 actor，两 Notice 始终空参与者。原领域 InWorld／owner 门仍在，普通 Write／None、完整 blob、双 profile／bank、原日志和无 ACK 保持，没有 Main／raw／Self 或核心放宽。实际注册／invoker 测试与 `evidence/task54/runtime-r2/{runtime.actual.json,run.json}` 相符；冷探针仅证明登记与程序集身份，不冒完整 provider 验收。
- **旧新行为：** `SettingsProtocolCommandFlowTests` 的35项正式表征使用真实 decoder／dispatcher、领域／SQLite、事务、DI Dispose 和生产 outbox；核 SQL action／native Commit 原 cause 与回滚、已提交 scope 的默认 typed／精确 Fence／Block／DiscardExact／旧 physical 关闭及同ID替换隔离。Notice 断言已具体到 owner_type／owner_id／group／id。VC-B34仅记录这一默认 scope 生命周期差异。原完整 entry 读回等强例未删弱；纯准备 patch 只有两个 App 测试文件，保旧 new-connection 夹具，当前 actual Connection 适配另属迁移。
- **真实执行证据：** 独立解析最终 r2 TRX，并逐 display／outcome／重数对 TASK-53：App5788＝5770通过＋原2失败＋16跳过，Protocol1789、Networking203全通过；增量分别35／1／0、删除0。两条 DimensionDungeonTests 失败名称和缺图 Message 逐字不变。build日志0警告0错误，run记录证明新 build 后三项目串行。最终旧复验真实 App50／Protocol56全通过；完整阅读 `/private/tmp/task54-oldcheck-20261026-r2/run-old-final.executed.py`，其 report 与保存报告一致、finally无恢复错误，当前产品／测试逐字节等于冻结 CURRENT。有效历史五项目复算后总9580＝9560通过＋2失败＋18跳过，明确不是八项目 fresh。
- **账与完整性：** 重新运行原 violations.py，70＝67反例＋3 control，outside／exe／orphan均0；重新运行 strict及八坏例全部通过／拒绝。对 HEAD 逐行比较 ledger 恰好六消息三 fine 变化，original、顺序、非目标均不变；AGENTS仅清单一行，Spec仅VC-B34。含新增文件 whitespace检查无问题，HEAD未变且index为空。

本审查未运行 dotnet、旧源切换、Git写入或外部操作；只追加本节。保留原两项内容失败、未做A21实机／专门性能验收的限制；后续接受与分段提交归主控。STOP。

## Controller Consumption R1

主控接受唯一 fresh Astra 的 R1 approved，无未决发现；本 Task 完成，T-007 继续。已独立对读五 whole／薄入口／接线及全部新表征，核真实 r2 build 0警告0错误、App／Protocol／Net 的逐 display／outcome／重数与两项原失败完整 Message，确认新增35／1／0、无删，DFO70 原反例／control及类外零值、runtime 与当前产物一致、strict与八坏例；结果为三 fresh7780，有效历史库存9580，不冒八 fresh。

主控亲自执行最终旧复验 r2，App50／Protocol56均通过，21路径 finally恢复与HEAD／status／空index经独立核验。Notice精确owner/group补强后产品未变；最终22 owned与备份核同，纯准备patch可独立应用，actual Connection夹具适配归迁移提交。仅九ledger行变化，original／顺序／非目标不变，VC-B34与AGENTS清单符合批准范围。按授权将纯测试准备及迁移结果分段本地提交，然后启动TASK-55；未授权外部收尾，原Dimension内容失败、无A21实机及专门性能验收的限制保留。
