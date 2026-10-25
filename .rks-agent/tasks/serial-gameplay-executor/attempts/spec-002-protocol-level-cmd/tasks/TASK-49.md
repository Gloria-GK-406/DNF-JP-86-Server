# TASK-49 — 放弃副本与结果界面退出归并

本Task承接T-007，迁移GiveUpGame（65578）、EplpCommand（65608）及LeaveDungeon、SelectBossTowerResult、SelectIllusionTowerReturn三条fine，共五行。TASK-48已由同一Astra批准、主控消费，以d5f0c4e1／b0c4343e分段提交，工作区干净；固定卡牌取消能力与TASK-39真实共享退队／返城算法可直接复用。目标未偏离，只消除退出入口的实际编排；两塔Leave／Start／Synchronize、AltarAction及各Timer仍有其它消费者，不提前闭合。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-48；承接RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 两个唯一Protocol whole薄handler；三条fine及零生产调用的旧退出编排清零，保普通／两塔／祭坛的原事务段、取消与投递次序。精确五行，不增Main／raw／Self，不改共享runner／bus或注入能力清单。
- Nature/Gates: 以保持行为归并；whole scope／即时编码失败的实际差异追加VC随唯一fresh Astra Result快审。Spec B8／Plan P10与根／子／中心不变；一个sol6.1执行后一个Astra独立快审，无挑战。先旧正式表征，再产品；主控验收后分段本地提交。

## Detailed Plan

1. **先钉旧行为。** 精确b0c4343e跑现有LeaveDungeon、DungeonClearProjection、EndOfRunWireKey、BossPublicHandler、两塔ProjectionConcurrency／AppPublicFlow与Networking EplpRechallenge强例。补真实decoder→dispatcher／scope／outbox／encoder最小旧新oracle：普通退出无事务、原Leave fine scope在End之后而ACK之前；Boss结果选择Tx与随后多次Leave；Illusion结果选择Tx的空保护；祭坛结算／发布失败。实际状态、奖励／事务回滚后重试、原cause／关闭精度与原帧顺序一起断言，不能只验假bus或帧数；需要的资产用例在旧侧阶段补齐。
2. **普通退出原序。** GiveUp stale先行→已发布→两塔分支→当前视图／位置／InWorld→祭坛单人结算→取消card→End→移除精确delivery participant→原party离队／通知→ACK→weakness一次／Town。无run不离队。Eplp先两塔、再在异步视图前捕获run；保leader／cohort／账号／精确session／entry intent／town ownership各门，普通state1 options0..2退出、story3确认、option2无run兼容；逐人取消→原Direct End循环（每人原取消点，无新Write／ReadOnly）→移除→ordinary option0 retry intent→XO-2 peer BestEffort ACK先于caller ACK→Town。
3. **两塔原边界。** Boss lookup先于stale、Exact、state2仅ACK、state1有效option才SelectResult一次ordinary Write（原全selected保护／无footprint）；变化后冻结成员并原序ACK，option2逐成员Leave仍各独立ordinary Write，CancellationToken.None及返城隔离／关闭保持。Leave采用已批准P8实际owners及独立保护集合，不把每人事务合并。Illusion stale先于lookup、仅state1、SelectReturn ordinary `[]`／None、仅Changed ACK及原非取消失败关闭；GiveUp复用TASK-39真实两塔／队伍／返城算法，原剩余legacy适配器保留至实际最后消费者迁完。
4. **祭坛与共享归属。** 两出口原门之后按当前registry／InWorld／同run重新取cohort；原AltarAction一段改直接领域ordinary Write，保原multiselected／无footprint及全队或仅本人结算参数。投影仍重取收件人，失败仍按原PostCommitPublicationFailure逐精确session保护并保cause，成功仍Wake。提取真正共享的参与者／投影算法供新whole和未迁legacy使用，不注入旧Coordinator／Delivery、service locator、callback执行器或伪DomainService，不调用fine／另一CMD；零调用算法归本CMD partial。同卡牌control只Cancel，不新增调度或改变timer。
5. **原接口与失败核验。** ProtocolModule两消息各唯一whole Direct；三个目标fine注册／类型／生产caller归零，所有其它fine与source按真实剩余caller保留。保旧强例断言；必要fixture改同算法driver，不能冒充production scope证据。旧新实际声明／root／source／Completion／cause、selected与physical替换、后阶段失败保前段提交／响应及不会重复奖励均有正式断言；可见差异具名VC，禁止默默扩大raw／Self或缩围栏。
6. **冻结检查与交付。** Release solution build0/0＋聚焦＋全部受影响项目full（至少Application／Protocol／Networking／Host；新跨层变化再按实际扩大），严格项目串行、默认类并行、5m／detailed／独立DFO，首次dotnet require_escalated。逐display／status／重数对48有效库存比，只保原两Dimension与18skip。若需最后旧源复验，必须全runner结束、固定路径manifest与完整当前binary recovery patch／HEAD／branch／fresh等价预检、逐项finally还原验证；不复制旧源码当证据。最终旧绿纯准备patch独立；runtime两唯一route／三fine0、五行外／original／顺序、Result后strict／八坏例及含新文件diff-check。Result和快审各一页，执行者停写交唯一fresh Astra；不封存整簇或执行外部操作。

## Result

- Outcome: done
- Changed: 两消息各唯一 Protocol／Direct whole 薄入口；普通退出零事务，Boss 选择与逐成员 Leave 保原独立 ordinary 写段／None 取消及全 selected、各单元报告者 physical／独立 declared owners，Illusion 选择保 `[]`／None。复用 TASK-39 真实两塔／party／Town 算法，祭坛结算直达既有领域并共享实际参与者／投影／typed／日志／Wake；三 fine 和零生产调用旧退出方法删除，其余命令／Timer不闭行。仅五行台账与一行 AGENTS route。
- Why: whole scope 的实际阶段差异及祭坛默认 physical 保护写 VC-B25～27，未扩 Main／raw／Self；这些生命周期差异与 whole 归并不可分。最终纯准备 patch 独立，可先作测试检查点。原直接 fixture 用同一算法 driver，生产 scope／SQL／encoder 证据由16正式 oracle提供，未把假 bus 冒 production scope。
- Checks: 最终旧 b0c4343e＋同一最终纯准备 App202/202（186＋16）、Protocol119/119、Net2/2；31项完整恢复0错误／0不匹配，恢复后当前 App full与Protocol119重编译通过适用范围。Release非增量0警告0错误；四 fresh full **7829＝7811pass＋2原Dimension＋16skip**，App r3 N16/D0，Protocol r1／NetHost r2各N0/D0；与未影响四项目48r2有效总库存 **9426＝9406pass＋2原失败＋18skip**。原DFO四fresh71＋三旧control＝74，outside／exe／orphan0；actual runtime两唯一Protocol Direct／三fine类型和注册及生产0。五行外／original／行序保持，Result后strict／八坏例及含新文件diff-check通过，见[核验摘要](../verification/task49-verification.md)。
- Remaining: 原两Dimension仍7100／maze0／(1,0)／Left缺图。首轮Net端口占用和Host首Game连接拒绝保留诊断，原例聚焦及无源码变化的Net／Host full复跑均绿；Host同根未证实，不称修复端口问题。最终纯准备 `/private/tmp/task49-pure-preparation.patch` 与 `-r2.patch` 同内容；恢复材料保留。受控规则／故障／physical不等于A21，T-007／T-008和Spec未关闭，无外部操作。产品／测试与全部runner已停，待唯一fresh Astra快审、主控接受及本地提交。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

**结论：本 Task 局部结果通过，无阻塞发现。** 对象为 `task/refactor-session-20261021`、HEAD `b0c4343e713149966a760b361814ebbfb75c38ee` 上当前未提交 Result（Contract r1），含实际 tracked diff 与六个新增 Server 文件。按 `reviewing-work` 的 Task Unit／Code 判据，遵循 process-reset、工作区 AGENTS、Spec B8／VC-B25～27、Plan P10 的 T-007 与 P8 边界；未生成挑战，未重跑测试。

**依据。** 两消息确为唯一 Protocol／Direct whole，三目标 fine 类型、注册与生产引用清零。普通 End 保零事务、逐人取消点、registry 清理和 XO-2 peers→caller ACK→Town；GiveUp 复用 TASK-39 的真实共享退队、两塔与返城阶段，不回调 fine 或注入旧协调器。Boss 保选择一次写、option2 ACK 后逐成员独立 Leave／None，原 cohort 保护与各单元 reporter physical、DeclaredNonRequest owners 分开；Illusion 保空 selected／None。祭坛结算与投影共享真实参与者筛选，legacy 用同一 captured recipients 逐 null-physical Block，时钟仍在非空收件人的受保护 try 内。

VC-B25 的旧 End 后 scope／新末尾 scope、VC-B26 的旧一次 Committed 无 ACK／新 Boss 三次 Committed 与 ACK 及 Illusion 已 ACK 但无 Fence／Block、VC-B27 的原合成 InvalidOperationException cause 与来源 physical 默认 Block，均与代码和正式 oracle 相符；没有新增 raw／Main／Self。实际读取 runtime／types JSON 并逐行比较 HEAD 台账：仅指定五行改变，original、顺序及其余分组不变。

直接解析最终旧 `task49-old-final-r3` TRX：App202、Protocol119、Net2 全通过；16 个最终 oracle 同在旧／新通过，覆盖实际 decoder／dispatcher、DI Dispose、生产 outbox、真实 SQLite 奖励回滚与换 physical 重试。核对当前四 fresh TRX 为 **7829＝7811通过＋2原Dimension失败＋16跳过**；有效八项目库存 **9426＝9406通过＋2原失败＋18跳过**，后四项目沿48证据，不称八 fresh。非增量构建日志0警告0错误；恢复记录 all_restored=true／errors与mismatches为空，纯准备两个文件及别名一致；strict、八坏例、最终检查记录相符。

**遗留／remaining。** 两条7100／maze0／(1,0)／Left内容失败继续保留。首轮Net有相邻 bind 地址占用证据；Host只证实首Game连接拒绝，不能据此断言同根。后续两项目full各203通过，不宣称修复端口问题。受控规则、故障与physical不等于A21验收；两塔剩余Leave／Start／Sync、AltarAction／Timer及T-007／T-008／Spec仍未闭合。交主控消费并按既定纯准备／整体结果分段提交；本审查仅追加此节，随后STOP。

## Controller Consumption R1

主控接受唯一 Astra 的 approved，无待处理阻塞项。已独立读取产品与三条 VC、最终旧／新 TRX 和逐用例差集、原 DFO 分类、实际注册／类型、精确五行台账及严格坏例结果；最终旧源复验由主控执行，31项恢复与最终纯准备检查通过。接受四 fresh 与未受影响四项目的明确证据边界及两次连接失败诊断，不将其写成八 fresh 或已修端口问题。按已授权流程先提交最终旧绿测试准备，再提交 whole／VC／台账／结果；继续 T-007，T-008及其余来源仍保持未闭合，不执行外部收尾。
