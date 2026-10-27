# TASK-46 — 地面拾取归并

本 Task 承接 T-007，迁移 GetItem（65579）和普通／两塔拾取三条旧命令，共四行台账。TASK-45 已经唯一 Astra R2 批准并提交 `509f0a0c`／`c20f4878`，留下的两塔 Pickup 是本消息的实际分支；共享 clear 投影和精确 run 受众接缝已经存在。目标未偏离，本次只收拾取闭包，不提前关闭其它塔消息、非协议来源或 T-008。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-45；承接 RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 一个 GetItem 完整协议 CMD 和薄 handler；删除 `Game.GroundPickup.PickUpGroundDropCommand`、`Game.PickupBossTowerItemCommand`、`Game.PickupTowerItemCommand` 及实际零消费者的旧拾取编排。每条实际分支仍只有原一次 ordinary Write，普通／Illusion 空 selected，Boss 原全部 selected 及报告者 physical；三者均无 footprint，不照搬 Kill 的 owners。
- Boundary: 原地面 claim、授予、任务、提交后 clear 与投影顺序，旧新 scope／encoder／精确关闭边界；无新 Main／raw／Self、共享执行器或能力契约扩展。一个 sol6.1 执行、一个 fresh Astra 结果快审；本 Task 不另做挑战。

## Detailed Plan

1. **旧代码先绿。** 在 `c20f4878` 跑正式 GroundPickup／Codec／GetItemHandler／QuestClearVertical、DungeonPickupTransaction、PickedUpAvatarStaysWhole、MarriageCapsuleItemUse 和实际两塔拾取用例；保原 gold、epic、owner、数量、UID／avatar／creature、满包、预留回滚重试与不重复授予。补少量实际 decoder→dispatcher→SQLite／原事务→actual encoder→production outbox 的 scope／发布故障 oracle；明确纯算法 driver 与生产生命周期证据不同。
2. **路由和普通拾取。** 保 Boss 先 lookup 后 stale、Illusion 先 stale 后 lookup（即使没有塔也可能吞旧帧）的原顺序；普通 session／IsInWorld／party 精确准入不变。一次空 selected、无 footprint 的 Write 内执行原 claim 与授予算法，黄金先于 bag、epic 先于 bag；NoRoom／Unknown 释放 claim 后正常返回，不改为 RequireRollback。普通拒绝只有 InventoryFull 回 `[00,04]`，其它沉默。
3. **提交后次序。** 仅 PickedUp＋QuestProgress＋匹配 committed picker 才以 CancellationToken.None 先 Evaluate clear，不能因 requester 被替换而跳过权威状态变化。随后精确受众投影：picker 必达 ACK→pickup→gold／virtual／quest，其他成员仅 pickup；保 current/run 双门、Slot／数量／uid及通关投影顺序。拾取投影返回 false 不抑制已有 clear 结果的独立投影。复用 TASK-45 的同 clear 算法及原 registry 实例。
4. **两塔差异不合并。** Boss 一次 Write 保全部 selected、报告者 physical 与原 metadata，但无 footprint；实际满包走 InventoryThenMail、黄金／任务／clone 发布仍同事务，不把 handler 的 NoRoom 分支误当实际 mail 行为。Illusion 一次 Write 空 selected，仍限11000、tower inventory 无空位正常 NoRoom。两塔成功批次逐成员编码失败仍收集、继续并精确关闭，NoRoom ACK 在 catch 外。物理关闭用既有 contexts 准入；若旧同ID替换行为有可见改变，先旧新真实 oracle 再追加本 Task VC，不扩共享 closer。
5. **闭包与有价值测试。** whole 不注入旧 Delivery／Coordinator、不嵌套 bus；查明 DungeonPickupProjectionDelivery／PostCommitCoordinator 的真实消费者后收回编排，公共投影只保一份算法。既有 fine 测试改经实际 whole 或同生产算法 driver，原资产／事务／顺序／拒绝断言不缩减，也不为测试保留生产 fine。默认 whole scope／encoder 差异用旧新证据列 VC；保持行为不改 Spec／Plan 版本。AGENTS 只加清单与必要出处。
6. **冻结、审查、提交。** Release solution build＋聚焦，以及受影响 Application／Protocol／Networking／Host 四个完整项目；原类并行、项目串行 `-m:1`、5m、detailed、各自 DFO／TRX，正式 dotnet 首次 require_escalated。对 Task45 App r3／其余 r2 逐 display／status／重数核差集，原两 Dimension 和 skip 身份不扩大；原 DFO expected／control／零 outside 保持。核四行之外与 original／行序、实际唯一 route／Direct binding／三 fine 清零、strict／八坏例及新旧文件 diff-check。一页 Result 写命令、通过数、失败身份与限制，区分纯准备提交和不可分迁移；停止写入交独立快审，主控接受后本地提交继续 T-007，无外部操作。

## Result

- Outcome: done
- Changed: GetItem唯一whole／薄bus handler，普通／Boss／Illusion各保原一次ordinary Write；三fine、两拾取编排及两塔Pickup adapter段删除。普通算法／投影收为CMD自身partial，不留只为测试可构造的新生产助手；复用原Party／clear算法。保三种selected／physical集合、无footprint、claim／资产／邮件／临时槽与投影顺序；只闭四台账行。AGENTS仅清单与VC出处。
- Why: whole生命周期默认失败与搬迁不可分，差异具名VC-B19；仅Pickup精确physical cleanup为VC-B20，保contexts准入，不扩共享closer。纯准备只有新oracle文件，可独立提交；旧fine／projection测试使用同CMD算法受限driver且不冒生产scope，原强断言保持。
- Checks: 最终纯准备精确旧c20f4878为24＝22pass＋2原PVFskip（18真实生产链oracle＋4原事务／资产＋2skip）；新侧同组加原public-flow为25＝23pass＋2skip，Protocol拾取44/44。Release solution build0/0；最终App r4＋Protocol r1＋Net r3＋Host r3四完整共7760＝7742pass＋2原Dimension＋16原skip。App5573＝5555＋2＋16，另三项1781／203／203全过；逐display／status／重数App N18/D0，其余N0/D0。原DFO71＝67反例＋4control，outside／exe／orphan0。runtime唯一ProtocolCommand→Direct单binding／三fine0，四行之外／original／行序保持，strict／八坏例／含新文件diff-check通过。真实命令、轮次与日志见[核验摘要](../verification/task46-verification.md)。
- Remaining: 原两Dimension仍7100／maze0／(1,0)／Left缺compatible map；原PVF skip和受控physical不等于A21实机。r1 App旧fixture嵌套bus及调度重叠均如实记录，前者只适配同算法驱动，后者诊断结果不纳验收、已串行重跑。T-007／T-008／Timer／Spec B未闭合；未动index、提交、推送或其它外部操作。完成记录后停止全部写入，交唯一fresh Astra。

## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

审查对象为 `task/refactor-session-20261021`、HEAD `c20f487819b699b7616745da2801332ccc1078f0` 上未提交的 TASK-46 Result 最终轮（旧／新 focus r10；App full r4、Protocol r1、Networking／Host r3），合同 revision 1。按 `reviewing-work` 的 Task Unit＋Code 判据，核对工作树 AGENTS、架构哲学／重构偏好、process-reset、Spec B7 的 RQ-003／007／009／014 与 Plan P9 T-007；只作本 Task 的明显缺陷及契约快审，不代表完整 Spec 批准。

- **无阻塞发现。** 实际注册与 runtime capture 均为 GetItem 唯一 Direct／Protocol whole binding，三 fine 及仅其消费的拾取编排已删除；台账相对 HEAD 恰四个目标对象变化，其余对象、original 与顺序保持。未改共享机制、保护账或其它塔操作。
- 普通 `PickUpAsync`、`CarryOverAsync` 方法体与 HEAD 逐字一致；投影差异限于归属、既有受众查询和 outbox 登记。三个分支各保一次 ordinary Write、原 selected／physical 集合且无 footprint；gold／epic 在 bag 前，claim 拒绝普通返回、仅 Full 回 `[00,04]`。Boss lookup→stale、Illusion stale→lookup、提交后 None-token clear 先于当前性投影，以及拾取投影 false 不阻断独立 clear 均保留；共享 clear 原双 current/run 门未变。
- VC-B19／20 与代码及正式旧新 oracle 相符：whole 默认 scope／未吞 encoder 失败的时点与 typed 差异明确；两塔成功批次仍逐成员 catch、继续并正常结束，NoRoom ACK 在 catch 外。拾取 cleanup 捕获原 physical，沿原 IsInWorld 与既有 contexts 精确准入，不误关 replacement。Boss 实满包邮件一次、Illusion 六临时槽拒绝、真实 Grant 后 Prepare 失败的 SQLite／claim 回滚及新 physical 重试只授予一次均有直接断言。算法 driver 没有被当作生产生命周期证明，原资产／帧序断言未缩减。
- 独立解析实际 TRX／run.json：旧 r10 为 22 通过＋2 原 skip，新 r10 为 23＋2；四完整项目为 **7742 通过＋2 原 Dimension 失败＋16 原 skip＝7760**。独立按 display／status／重数对 Task45 重算，App N18/D0、另三 N0/D0；两失败仍为摘要所列 7100／maze0／(1,0)／Left。正式轮次无项目重叠，诊断轮未混入。build 日志 0 警告／0 错误，原始 DFO 为 62＋1＋7＋1＝71、各一个 control，既有分类摘要 outside／exe／orphan 均零；纯准备补丁仅新增 oracle 且与当前文件一致。

残留限制沿用 Result：受控故障／physical 与原 PVF skip 不建立 A21 实机验收；本结论不关闭 T-007、T-008、Timer 或 Spec B。仅追加本评审，未改产品、测试或其它记录，未提交、未外部操作。**评审写入完成，停止全部写入，交主控消费。**

## Controller Consumption R1

主控读取并接受唯一 fresh Astra 的 approved。已独立核最终旧24／新25的逐项结果、App r4的5573＝5555通过＋原两失败＋16原跳过及N18/D0，另外三个完整项目均无新增或删除；有效轮次时间与恢复旧代码的时点没有重叠。原DFO parser四次核71条及各控制样例，额外违规为0。实际唯一绑定与三fine闭包、四行台账差集、original／行序，以及Result写入后的strict／八坏例均已复核通过。

接受范围包括VC-B19／20、普通真实回滚后新physical重试一次授予、Boss邮件与Illusion满包的区别；保留原内容失败、跳过和受控实验证据限制。诊断重叠轮和旧fixture入口故障不计入最终通过结果。先独立本地提交单文件纯准备，再提交迁移与记录，随后推进死亡／复活；本Task不关闭功能簇或Spec，不做外部操作。
