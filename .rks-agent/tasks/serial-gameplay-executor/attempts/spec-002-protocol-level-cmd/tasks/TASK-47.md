# TASK-47 — 角色死亡与复活归并

本 Task 承接 T-007，迁移 CharacterDeath（65576）、UseRevivalCoin（65577）及普通死亡、复活和两塔死亡四条旧命令，共六行台账。TASK-46 已由唯一 Astra 批准并提交 `672294f3`／`fff95b5d`，留下的两塔 Die 仍是 CharacterDeath 的实际分支；现有精确关闭、受众与投影算法可继续复用。目标未偏离，本次不迁移 PartyWipe／ClearReturn Timer 或关闭其它塔消息与 T-008。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-46；承接 RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 两个完整协议 CMD、两个薄 handler；删除 `Game.CharacterDeath.MarkDungeonParticipantDeadCommand`／`UseRevivalCoinCommand`、`Game.DieInBossTowerCommand`／`DieInIllusionTowerCommand` 及其已无消费者的适配段。
- Boundary: 普通死亡保持原 Direct 内存修改，零事务工作单元；复活保持一次 ordinary Write、空 selected／无 footprint；两塔死亡各一次 ordinary Write，保原区别保护与实际奖励 owners。无新 Main／raw／Self、能力或共享执行器变更。一个 sol6.1 执行、一个 fresh Astra 结果快审；Result review required，无本Task挑战。

## Detailed Plan

1. **先固定旧行为。** 在 `fff95b5d` 跑 CharacterDeathCodec／Handler、DungeonRevivalTests 及两塔死亡／团灭相关正式测试，保普通重复死亡不重启UI、队友复活由payer付币、零余额仍保虚拟槽、重复复活拒绝、二次死亡新deadline、真实持久失败、Tournament／Altar计数与Mirror任务。补最小生产链旧新oracle：实际 decoder／dispatcher、真实事务或零事务、actual encoder／production outbox、scope故障和精确physical；复活至少保实际回滚后一次支付与两个ACK flag，不以领域driver冒whole生命周期。
2. **普通死亡保 Direct。** 先取source session，再Boss／Illusion；两塔原stale均在各自lookup前。普通精确 run／registry／IsInWorld 门后直接调用原 TryMarkParticipantDead，不添事务、Main或保护集合。原重复报告无变化即无帧；ACK→死亡life通知必达source，peer仍按原冻结参与者、current会话和同run筛选。fine scope→whole scope的无事务异常及先后帧由正式测试记录，不能套用拾取的Committed／typed结论。
3. **两塔死亡保结算边界。** 原 Die 走 RewardingMutation，按实际 run members 声明 P8 DeclaredNonRequest owners；Boss保全部selected和报告者physical／原metadata，Illusion原空selected，owners不等于围栏集合。不新增current门或合并事务；保死亡／团灭结果、奖励／邮件／经验与原提交后发布顺序。成功批次逐成员捕获投影失败并继续，沿原IsInWorld及既有contexts精确关闭；替换差异如有则以本Task旧新oracle列VC，不扩共享closer。
4. **复活保 payer／target。** Boss入口原拒绝、普通run／冻结payer／target／InWorld／same-run／online门不变。旧fine仅IAccountCommand、不是ISelectedSession，故一次空selected／无footprint Write；币扣减、目标life／wipe取消、Tournament／Altar限额与Mirror任务仍同事务，正常拒绝不改RequireRollback。成功payer收到life(state1)→ACK→coin计数，peers仅life；拒绝ACK `[00,00]`，原静默入口继续静默。无相邻Timer迁移。
5. **归属与静态协议输入。** 只供本CMD使用的编排收为其自身方法／partial，真实共用算法才留窄适配；旧测试改同算法driver或真实whole，强断言不减。RevivalOptions 是本slice已验证的不可变启动ACK形状：主控允许 decoder 从同singleton将AckFlag作为非wire协议字段交入，保持默认1、0／1两种实际字节及原配置拒绝；薄handler仅bus，whole不增配置DI豁免。默认生命周期／精确关闭可见差异追加Spec表并更新“其余”引用范围，保持B7／P9与RQ／AC；AGENTS只加清单。
6. **冻结与交付。** Release solution build＋聚焦＋Application／Protocol／Networking／Host完整，原类并行、严格项目串行、`-m:1`／5m／detailed／独立DFO与TRX，正式dotnet首次require_escalated；全部运行结束后才能临时恢复旧源，禁止重叠。对Task46 App r4／Protocol r1／Net与Host r3逐display／status／重数核差集，原两Dimension／skip、DFO分类计数与control不扩大。核六行之外／original／行序、两唯一route／Direct单binding／四fine清零、strict／八坏例及含新文件diff-check。一页Result写结果、非常规选择、具体核验和限制；纯准备有最终旧绿才分段，其余不可分处说明。执行完停写交唯一快审，主控接受后本地提交并继续T-007，无外部操作。

## Result

- Outcome: done
- Changed: CharacterDeath／UseRevivalCoin各唯一whole与薄bus handler，删除四fine、旧MarkDeadResult和两塔Die适配段。普通死亡保持Direct零事务；复活一次空selected／无footprint ordinary Write；两塔各一次ordinary Write、原members DeclaredNonRequest owners，Boss全selected／仅报告者physical／原metadata、Illusion空selected。原死亡／团灭奖励、复活payer／target、拒绝与静默、帧序及提交点保持；只闭六台账行，AGENTS仅清单出处。
- Why: whole scope默认生命周期与迁移不可分，逐项列VC-B21；两塔死亡精确physical cleanup列VC-B22，沿原IsInWorld／contexts，不扩共享closer。AckFlag是既有immutable启动options的非wire输入，由decoder显式交入；不增CMD配置DI豁免。纯准备仅新oracle＋测试访问级别，补丁独立交付；原fine／projection fixtures改同算法限定driver，不冒生产scope或保生产转发层。
- Checks: 最终纯准备精确旧fff95b5d **195/195**（24新oracle＋171原强例），新侧同组全部在最终App通过；new focus App32/32、Protocol74/74（old69/69）。`dotnet build Server/86JP.Server.slnx -c Release -m:1` 0警告0错误；`dotnet test ... -c Release -m:1 --blame-hang-timeout 5m`＋detailed／独立DFO／TRX，最终App r2＋Protocol／Net／Host r1四完整 **7789＝7771pass＋2原Dimension＋16原skip**，分别5597＝5579＋2＋16、1786／203／203全过，逐display／status／重数App N24/D0、Protocol N5/D0、其余N0/D0。原DFO **71＝67反例＋4control**，outside／exe／orphan0。实际两唯一ProtocolCommand→Direct单binding、四fine0；六行之外／original／行序、Result后strict／八坏例及含新文件diff-check通过。真实命令、轮次与日志见[核验摘要](../verification/task47-verification.md)，纯准备补丁`/private/tmp/task47-pure-preparation.patch`与最终旧绿同内容。
- Remaining: 原两Dimension仍7100／maze0／(1,0)／Left缺compatible map，原skip身份保持。App r1旧fixture嵌套薄bus触正确guard，仅适配test driver后focused1/1＋App r2通过，未混作基线或放宽生产保护；所有项目串行无重叠。受控故障／physical及Altar／Mirror collaborator只证明具名边界，不等于A21实机或全部引擎验收。T-007／T-008／Timer／Spec B未闭合；未动index、提交或外部操作。完成记录后停止全部写入，交唯一fresh Astra。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

本 Task Result 快审通过，无未解决阻塞项。审查对象为 `fff95b5d` 上 TASK-47 revision 1 的最终未提交改动及 Result（Application final-r2，其余三项目 final-r1），不是 Spec B 或完整功能簇验收。依照 `process-reset-2026-10-25.md`、Spec B7／Plan P9 的 T-007 与 P8 owner 边界，应用 `reviewing-work` 的 Task Unit／Code 判据；独立读取实际新增文件、旧源码、diff 和原始证据，未以执行者结论替代审核。

- 实现符合六行范围：两条唯一 ProtocolCommand 路由与 Direct 单 binding，四 fine 及两塔 Die 旧适配删除。普通死亡仍零工作单元；复活 ordinary Write 空 selected／None footprint；Boss 全 selected、仅报告者 physical 和原 metadata，Illusion 空 selected，两者显式声明实际 members owners。薄 handler、payer／target 门、重复死亡、帧序及真实共享 PartyMutation 投影保持；PartyWipe／ClearReturn／Timer 未迁移。AckFlag 来自既有校验启动值，经 decoder 非 wire 输入交入，未新增 CMD 配置能力。
- VC-B21／B22 与实现和正式 oracle 相符：普通零 Tx 保原异常；复活与两塔默认 scope／编码边界、两塔 query-only 与 contexts 替换精确关闭均有实际 decoder／dispatcher、事务或零事务、SQLite、encoder／生产发件箱证据。原 fixture 的 driver 复用实际 whole／结算／共享投影，业务断言保留，不将其冒充 scope 证据。
- 独立解析旧 final-r7 TRX 为 **195/195**（含24新例），核 `restore.json` 及纯准备补丁仅两文件、当前 reverse-check 通过。最终四 TRX 为 **7789＝7771通过＋2原失败＋16原跳过**；与 Task46 逐 display／status／重数比较为 App N24/D0、Protocol N5/D0、其余0/0。两失败仍为 DimensionDungeonTests 两项、7100／maze0／(1,0)／Left 缺图。四轮 run 记录串行；App r1 的旧入口嵌套 guard 失败不计入最终口径，适配仅测试入口与 driver，App r2 已重新编译覆盖。
- 核对 Release build 0警告0错误、四份 DFO 62/1/7/1及独立control／无外围记录、runtime 路由原始登记、strict＋八坏例结果。独立比较迁移账确认仅六目标变化，original／行序及其他行保持；diff-check 通过。本次未重跑无新疑点的全量测试。

限制：Altar／Mirror 为受控 collaborator 的调用边界，physical／故障注入不是 A21 实机或完整引擎验收；原两失败与跳过仍在，T-007／T-008／B 未闭合。本批准不代替 A＋B seal、用户另告外部 Astra 通过及显式分支收尾门。仅追加本节，未修改产品、测试、Spec、Plan、台账或 index，未提交；现已停止写入，交主控消费。


## Controller Consumption R1

主控接受唯一 fresh Astra 的 R1 approved，无阻塞项或追加产品修改。已独立读实际实现、原测试适配、最终旧侧195例与restore记录、最终四份TRX及逐display／status／重数差集；App N24／D0、Protocol N5／D0，其余N0／D0，原两Dimension与skip保持。实际runtime两唯一route／Direct单binding、四fine清零及六行之外／original／行序均核对；主控重跑原DFO parser为71、四control齐全且outside／exe／orphan0，Result后的strict与八坏例再次通过。正式build为0警告0错误，后续仅App fixture入口适配由App r2重新编译覆盖。

按既定授权先提交经精确旧源验证的两文件纯准备，再提交whole与VC-B21／22及六行台账。保持四fresh轮次来源与受控physical／Altar／Mirror限制，不将历史四项目冒成新八full；本Task闭合，继续T-007结算与翻牌。T-007／B及外部收尾门仍未闭合，不推送、PR、合并、tag或清理。
