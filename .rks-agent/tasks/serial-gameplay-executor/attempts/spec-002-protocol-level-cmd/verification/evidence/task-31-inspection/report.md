# TASK-31 执行证据

本单元完成六 Mailbox logical／五 CLR 的唯一 NonEffect whole、真实 self action 结算与共享 C2 双因修正；只闭七个 Mailbox canonical 对象。Outcome 只表示本单元执行交付，required fresh independent Result review／主控检查点及 Spec002 其余范围仍在后继。本 producer 未自审、stage、commit、推送、版本变更或运行游戏。

权威产品／测试冻结为 `product-freeze-final-01/manifest.json`（25 路径，SHA256 `f8f8c4c2f27795456d51001c7a4f715e3339a556da1a520236d088e5c3bf0fa9`）。最终 cleanup／mutation 变体全部 exact restore 后，正式 Release --no-incremental 0 warning／0 error；八 fresh full 与最终24场运行的共同 runtime/driver 冻结为 `c2972d2a1ce1bd87ad11f578ad5da618abf7e00d95d8bacacf5129bf026622e2`。正式 runner 每次保存 argv、UTC、退出码、resources、完整日志、源码快照、DLL before/after SHA 和自然退出；正式运行使用 require_escalated。结构证据 helper 独立于 runtime probe，在 `structure-source-freeze-final-04.json` 另存准确源码身份。

## 实现与边界

五 handler 只注入 ICommandBus、await 精确原 decoded message／Dispatch 的 GameMailboxCommand。命令的 Frame 只读派生原五 CLR Metadata；未知 family 拒绝，非空 GameMailboxOutcome 真持有 Result／Reply／Refusal／Failure。旧 MailboxCommand／handler／注册和 MailboxDelivery 业务删除，纯结果／Completed／composer／codec 保留。只此真实 registration 获得 SelfSettledAction；默认 None，registry 和 runner 分别守 Protocol／全部非效果 binding／ProtocolNonEffect／Ordinary／空 participants／None footprint。Application 无 Mailbox CLR 知识。

实际 DomainProxy → MailboxDomain.RunAsync → AbortAction 在 Tx 仍 Open 的入口同步 pin 原 outbox unit／owner，早于 settled、Current=null、任意 rollback await／callback／Close。Close 保 pin；实际回滚完成后 Tx 才签 own transaction／call／opaque occurrence／DomainActionAbort／cause／output／completion fact，observer 仅消费一次。opaque barrier 不存 request/job/session/callback 或另造 slot。candidate 等 unit.Close、request.EndUnit、lease.Dispose、current／authority 最终核对后才释放；同 Exception 对象在独立阶段发生仍分开，普通 handoff 传播保一次。

普通 action throw、非自身 fact、旧 fact／错误 output、取消／typed uncertain、participant／footprint／错误 route、同 call 二次、早 return、swallow/rethrow handoff、lower abort 后吞异常正常 return，均不冒健康 rollback。未 await 的 inner Invoke 真正由已被 request track 的 unit operation drain：真实 SQLite 写暂存＋TCS 等待期间不早 Commit／不早返回；release 后失败、无 durable／成功 ACK。正确 await 的普通 action rollback 仍可恢复 ACK，裸 handoff Task 不被 bus 误算成第二次独立失败。原 RequireRollback 的新增 guard 记录仅限 self mode。

KnownStorage 只在新 self runner 的 FallibleCommit／同 cause／ConfirmedRollback 恢复；Prepare／其它 phase 不恢复。原 WriteResult/default Write/ReadOnly 与三 raw 的能力和 commit-only 行为保持；无新 Main／nonRequest actor／AsyncLocal／Compose／效果足迹／active fence 或 DiscardExact。

whole 的 current guard 在业务 catch 外；lookup→unit 间真 selection 被替换时仍抛原 MailException NotOwned、claimed=0，无 ACK／无 Commit，真实 source close／chain 和 replacement 状态单独加强。健康 other-character claim 仍 ACK21。原四 fine caller 文件改同 provider 的真实 decoded whole，不以别的 provider、伪异常或空 result 替代业务；HTTP／UID-core／资产、原方法参数和强断言保留。

## 最终运行事实

`final-matrix-01` 来自显式24 case-list，经真实 decoder／dispatcher／whole／同 provider domain／SQLite／adapter／原 OutboundSender 与 controlled physical transport。`check-final.py` 从 raw 重核全部110生产 user tables／644列、schema、稳定 row identity、SQLite值类型、NULL／BLOB；native Commit 另2表3列，112表647列。action SQL19/1811、GoldLimit claimed 后拒绝、native Commit19/787与 C2 同 run 完整回滚逐值相等。早 rollback callback 看到 pin=true、Current=null、completion=Open、fact=null；完成后才有 fact。native 真实子表写已暂存、ForeignKeys=1、栈含 SqliteTransaction.Commit、KnownStorage/FallibleCommit 与同 cause；不把它当 action fact。

single 3帧、multi 4帧，claim／open／read／delete／query／unsupported 原 bytes／顺序保留。成功、健康 action／claim refusal、native confirmed rollback 不关闭。action/native ACK3，GoldLimit ACK10；query null 为正常 Committed ACK21，pre-refusal 是 rollback-only 普通拒绝 ACK7、无 action fact，无 session 静默零 Tx／帧。

C1 nonfallible durable uncertain 与 C2 rollback uncertain 均默认失败、零普通失败 ACK，真实 source CloseAsync 1次且记录原 chain。C2 最终 typed RollbackFailure 图同时可达原 SQL cause 与第二 rollback cause。C3 真实 whole DI scope Dispose 发生在 unit Committed、原投影已登记后；成功四帧或 query-null 拒绝 ACK21 按原 sender 先发送，再 actual source close，且不补普通 failure ACK。controlled transport 的 Fence／Block／DiscardExact 为0不冒正常全断线链为0；现有正常断线/teardown保持，Char10 fresh full 通过。

两个真实 lower DomainProxy catch 窗口均在 inner rollback 完成、outer 接错之前执行 Register 或 Begin→SQLite write→Commit；最早 admission 拒绝，第二 write／Commit均0。catchguard 再抛旧 cause 仍 sticky，原 cause 与 guard 独立 occurrence 可见，无恢复 ACK。wrong-output 和 lower 吞原异常正常 return 都默认失败、原 action cause 诊断仍可达。物理 replacement 两场分别复用／更换 connectionId，source 与 replacement 真对象引用不同：原 source 先帧后只关原 physical，replacement 和另一用户运行态/physical 都保留，replacement 下一合法请求成功。

`compare-before-after.py` 逐一对 Task30 accepted freeze07/build07/final03 的14场完整 Before/After schema/typed cells做比较。仅4个具名 CURRENT_TIMESTAMP 列、marriage_housing_migrations(version1).applied_at 的真实 UTC ms按来源/类型/各run UTC区间与同case稳定验证；domain_events 三GUID按不可变sequence/所有其它semantic列建双射，delivery.event_id FK关系保留后重排比较。没有剥去任意时间／GUID／数值，尤其不用于同run rollback。14输入 wire/Metadata精确同原；除既定 C1/C2/C3 默认失败收口差异外，原 physical frame bytes全部精确同原。原旧 report 的 single/multi 合写帧数笔误不改历史证据。

共享 standalone／ambient C2 两场真实SQL＋第二rollback故障，typed图双因、110表全回滚，无 observer/pin；ambient caught 后 Commit仍拒绝。ItemDelivery 原 MailOnly／overflow prepared 与 UID/core 保；GM 五 trigger 真HTTP整批与第二目标/receipt/audit同撤，独立失败audit保；QuestHelper 原子拒绝与成功保。Housing第二邮件和 Wedding Complete／Refund 真实晚期邮件失败全表回滚再成功重试，Complete 最后 guest74触发前真已有5份邮件暂存，成功完整6份。LiveOnly原多场消费者在同provider drain9，无在线收件人所以新增帧0，不能据此声称在线客户端通知验收。

## 行为反例与恢复

`final-controlled-boundaries.json` 四条最终源轮全部编译成功、目标自然结束、finally原bytes恢复、恢复后 GREEN，父级已只读消费原raw：

- unit.Close 实方法临时 source-fault 抛已存在同 handoff；保 SQL原来源＋独立handoff cleanup occurrence、110表回滚、无ACK、实际sourceclose/chain。不是自然生产故障率证明。
- lease.Dispose 实方法在实际dispose后临时抛同SQL对象；两独立 source occurrence保留，完整回滚／默认失败。因封闭原cleanup无自然注错边界，明确为受控临时source-fault，无永久hook、假Completion或私有已提交状态篡改。
- owner.Close 清pin 可编译 mutation，两个永久lower-window目标实际RED；真实Begin窗口进一步写1/Commit1 RED。finally exactrestore后两个永久target和实际Register/Begin窗口均GREEN、后续写0/Commit0。
- 仅新self native phase条件删除的可编译 mutation，让 KnownStorage Prepare 错恢复，实际“No exception was thrown” RED；exactrestore后目标GREEN。未误触原 WriteResult。

早轮脚本错误 wording、driver fixture generation、结构helper缺using／把原WriteResult owner误写5、DFO复制文件名误归assembly等原raw均保留，见 correction 记录；不是产品/行为mutation RED。最终产品freeze已在上述修正及两mutation重跑之后，正式 full不复用旧Task29/30日志。

## 新八 fresh full、原断言及结构全集

正式八程序集：App5295=5277pass＋原确切Dim2fail＋16skip；Protocol1778pass、Networking199pass、Persistence484pass、Infrastructure976=974pass＋2skip、Host203pass、HttpApi57pass、Pvf80pass。合计9072=9052pass＋2fail＋18skip，9029起算N43/D0、无旧display/status/multiplicity漂移。两个DimensionDungeon原失败的精确display与 `Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.` 保原基线。`inventory-*.json` 保存逐identity/参数/重数 Counter，未把full失败exit1写为exit0。

原Core30（含partial14）＋ProtocolIntegration19＋codec10＝59个原case身份在本次fresh full全部pass，`original59-fresh-full-closure.json`不冒另跑59-only native。`strong-coverage.json` 对8旧文件101个原method/参数/attrs和直接Assert点逐一核，原assert没有减少，stale新增真实状态/关闭证据。新增43条单列于inventory added；Golden15 byte HEAD精确不变、Char10 fresh pass。原DFO九expected类67＋七control仍74/7，outside／exe／orphan0；正式七jsonl的原bytes/SHA与native provenance在 formal-dfo-provenance.json。第一次bundle label前缀使原CLI按文件名错误推assembly，失败raw保留，仅纠正复制名称后原CLI第二轮exit0，未改tool/expected或重跑full。

实际capture173routes／201commands＝37Protocol＋1Internal＋163Legacy，135Transactional／53Direct／9ReadOnly／4Workflow，消息53Protocol／120Legacy，14telemetry。pure registered family逐对象差异仅六message与fine→whole，一self capability owner；三raw出口种类、18个全部registration owner/exits逐值同Task29已接受基线。实际IL扫描self entry只whole、AbortAction只MailboxDomain.RunAsync、fact ctor只DomainTransaction；13个编译坏例确实拒绝额外actualIL caller、fact creator、业务handler注入、nestedbus/sender/locator、缺/未知/错误route/效果binding/额外owner。

事实勘误：现有计划/P6的“原五WriteResult consumer”是计数不全。固定HEAD与当前实际均10个owner，四opening＋Cera具名五子集仍保，另Warehouse／ResetRandomOption／ResetItemQuality／ItemSockets／AvatarCompound也保持。original-write-result-owners.json逐个原source bytes=HEAD，structure04记录实际10个完整IL callsites；结构02/03误写5断言的失败raw保留。主控只读确认并裁定此为输入计数事实勘误，非扩大能力/WHAT/HOW/写界；原Plan/Spec不改，fresh reviewer独立审查本解释，如认material由主控affected gate处理。结构JSON旧字段名 original_five_write_result_calls为继承诊断标签，实际数组含完整10条，不裁剪。

## 完整性与未测

原Task计划41665B前缀精确SHA另存，不把后写Result混入冻结循环。完整 git NUL tracked范围、nonowned=current=before=HEAD（中文路径/旧Tasks/tools/P6/B5/Contract/Golden均含）、exact change白名单、原行尾、四类 authored whitespace、最后manifest与native自然停止会在最终完整性文件中封存。仅七canonical对象变更，其余逐对象原值；Result后才真实运行原strict validate与八坏例selftest，实际通过后补录，不声称review/commit已通过。

本次controlled in-memory physical transport不是TCP／A21实机；套件/probe时间不是单CMD≤2秒性能验收。T004残余、Rental两Main/refresh、T006～012、Astra/Spec终审/主控checkpoint仍后继。没有把受控cleanup变体冒成自然生产故障，未给其它consumer恢复许可。

Result已在全部技术检查与preclose通过后实际写入；原strict validate（01:29:40Z，exit0，strict/valid/无pending Result）和八坏例selftest（01:29:40～41Z，exit0，全8 rejected）实际完成，随后才补录Task。最终文件/白名单/whitespace与完整NUL记录见final-integrity.json，manifest-final.json排自身、Task只绑定原计划前缀；最终完整Task身份由final-stop.json及producer停止后的外报给出。
