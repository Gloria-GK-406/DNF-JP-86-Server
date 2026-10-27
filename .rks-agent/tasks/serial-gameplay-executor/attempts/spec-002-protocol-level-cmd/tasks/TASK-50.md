# TASK-50 — 技能与角色成长入口归并

本Task承接T-007，迁移ChangeSkillSlot（65564）、LearnSkill（65565）、IncreaseStatus（65566）、SwitchSkillBuild（65796）、LevelUpTicket（65954）、TutorialLevelUp（66020）及其六条fine，共十二行。TASK-49已获唯一Astra批准并由主控接受，以cab97aa3／27e91df9分段本地提交，工作区干净；现有Main、CommittedReplayCurrentStateBatch及具名WriteResult边界足够承接本组。目标未偏离，本组保持技能／成长事务与响应语义，不扩大raw、恢复白名单或跨簇能力。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-49；承接RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 六唯一Protocol／Direct whole及薄handler；ChangeSkillSlot、LearnSkill、IncreaseStatus、SwitchSkillBuild、UseLevelUpTicket、GrantTutorialLevel六fine及实际调用清零。三个既有效果入口各保单次Main与原重放需要，三个普通入口各保原空selected写事务；不新增来源或保护项闭合。
- Nature/Gates: 保持行为迁移；实际whole scope／投递失败差异以旧新正式证据写VC，随唯一fresh Astra Result快审。只消费已批准机制，不修订Spec B8／Plan P10；发现需改核心契约先交主控。一个sol6.1执行、一个Astra快审，无挑战；测试准备与迁移分段本地提交。

## Detailed Plan

1. **旧侧一次补齐。** 在27e91df9先跑SkillLearningCommand／CharacterSkillLearning／SkillBuildReset／SkillSlot、IncreaseStatusIntegration、LevelUpTicket、TutorialLevelUp、ConsumedReset与Networking SkillLearning现有强例。最小正式oracle须在产品改动前覆盖真实decoder→dispatcher／bus／SQLite／encoder／outbox、实际DI scope、事务Completion、精确physical及请求root／source／close cause；已有强例覆盖的健康业务不重复堆矩阵。三个Main实际成功后同帧重放及其真实encoder失败均核原entry／Ordinal、资产一次性与关闭／保护，不能只核注册许可。普通代表例核空selected／None及scope／投递失败，技能与升级的真实SQL回滚／合法重试、旧错误ACK映射也在旧侧完成；不把假支付或假bus当资产／生产scope证据。
2. **技能三入口。** Learn保page／session／online门、原当前学习／refund／SP／TP／槽位规则；只将原Learn fine改单次Main，原普通拒绝的提交语义不改。仅OpeningRejected映ResetItemRequired，其它SQL／取消／typed失败保持原传播；同原位置检查InWorld，库存receipt仍OpeningCompleted投影，其余直接Learn ACK。ChangeSlot／Switch原仅IAccount标记，故ordinary Write为`[]`／None，不能凭Session字段扩保护；Swap／Switch及无session失败ACK、已退场静默均保留。真实业务继续由现有领域服务承担，命令只拥有原阶段，不引入转发服务或跨协议bus。
3. **道具成长两个Main。** Increase保槽位映射、InWorld／空间／当前物品读取、reviveCoin优先再experience、写后当前槽和时钟；原普通动作／原生Commit存储失败仍映ACK2，映射异常仍NotUsable。LevelTicket保完整包／reserved／slot／精确会话门、原OpeningRejected的11／16／17／1及普通失败99；票消耗、进度、任务写同Main。仅在既有WriteResult内用原窄action catch＋RequireRollback(cause)，实际确认回滚后映原失败；不捕获runner之外的guard／sticky／cleanup／取消／不确定来伪成功。
4. **原投影与教程。** Increase仍ACK→库存→经验→虚拟资源更新；Level仍经验、已完成／在做任务、当前视图／offers、城镇CurrentUser、Extended／Skills／Housing及OpeningCompleted原序，aux查询异常仅原范围记录后继续。Learn与Level原通知投影返回Failed但调用者忽略时保持该语义，不自行改成抛错或授首成功raw。Tutorial仍先published view／run门，再一段普通`[]`／None事务现读角色等级并按原2级目标写入，已有升级角色不倒退；随后offers／当前点数→经验（有点数时）→任务列表→ACK，保缺帧现状，不借迁移补协议或改规则。
5. **显式请求与清零。** 三Main保原MinimumReplayNeeds及精确selected／physical、一个原结算段；三个普通不获Main／raw／Self。新注册仅为已证旧重放owner声明CommittedReplayCurrentStateBatch，不增加其它出口；各actual owner的失败Oracle先旧后新。六条唯一binding与fine类型／注册／生产caller零逐一核；保既有断言与类并行，必要fixture改同一算法driver，明确其证据边界。所有needed oracle／fixture在冻结前收齐；可见变化具名VC。
6. **冻结交付。** Release solution非增量0警告0错误；聚焦及受影响项目full至少Application／Protocol／Networking，若触Host或共享能力则按真实影响扩大。严格项目串行、原类并行／5m／detailed／独立DFO，首次dotnet需require_escalated。对49有效库存逐display／outcome／重数闭合，保原两Dimension和18skip，不将新失败并入基线；已过且源码未变的项目不重复刷。若最终需旧源复验，先完整CURRENT binary recovery patch＋固定owned manifest／HEAD／branch／runner结束预检，主控读脚本后执行，逐项finally恢复；不存旧源码快照。只闭本十二行，保original／顺序／其它行；Result后strict／8坏例／含新文件diffcheck，停写交唯一fresh Astra，主控接受后提交并继续T-007，不封存整簇或外部收尾。

## Result

- Outcome: done（执行轮1；基线27e91df9；等待唯一fresh Astra快审／主控接受，未封T-007或Spec）。
- Changed: 六入口各唯一Protocol／Direct whole与薄handler；六fine类型、handler、注册和生产caller清零。三个Main保原MinimumReplayNeeds及精确selected／physical、原重放owner；换槽／切页／教程各保ordinary `[]`／None。技能、消费／升级、任务与投影仍由原领域能力完成，未新增Domain转发／嵌套CMD／bus或共享核心。原范围错误ACK、动作与native Commit确认回滚重试、Level投影Failed被忽略和教程目标2／缺帧保持。
- Why: whole的实际Dispose时点与首次直接ACK／required batch默认保护不可同原fine拆分，实测差异仅追加Spec VC-B28／29。纯测试准备可独立提交：`/private/tmp/task50-pure-preparation.patch`；旧复验所用无格式清理版另保`task50-old-verified-preparation.patch`，两者只差尾空白。未授首次成功raw／Self或增加一般恢复。
- Checks: 最终旧源由主控安全执行，App115＝111pass＋4skip、Protocol35、Net1；22新oracle先旧后新均通过，含实际decoder／SQLite／DI Dispose／encoder／production outbox和受控physical、Main重放原entry／Ordinal／资产一次／替换隔离。新非增量Release solution 0警告0错误；有效full为App r1 **5659＝5641pass＋2原Dimension＋16skip**、Protocol r2 **1786pass**、Net r2 **203pass**。逐display／outcome／重数对49仅N22／D0，原失败与skip一致；原DFO parser精确70＝67反例＋3control，outside／exe／orphan0。详见`verification/task50-verification.md`及本机`logs/task50-final-r1/comparison.json`。runtime.actual直接核六唯一binding和六fine类型／handler／注册0，源码引用0；仅十二行台账闭合，original／顺序／其他行不变。Result后strict、八坏例及含新增文件diff-check通过。
- Remaining: 已披露一次调度错误：重复Protocol覆盖r1原Protocol产物并与Net重叠，二者均排除有效serial证据；全部自然结束后在独立r2串行重跑均绿，App早于重叠仍有效。保`logs/task50-final-r1/run-anomaly.md`，不伪造丢失记录；无产品改动迎合失败。未运行A21实机、部署、提交、推送、PR、tag、外部issue或清理。所有runner已结束，源码／测试停写，交唯一fresh Astra。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

**阻塞发现：none。** 本结论只批准 TASK-50 执行轮1的局部结果，交主控接受；不代表 T-007／Spec 完成。审查目标为本工作区 `task/refactor-session-20261021`、基线 `27e91df967db015378d842d9ebe34de48f1ff828` 上的冻结改动，合同 r1、六消息及六 fine。按 `reviewing-work` 的 Task Unit／Code 判据，读取本 Task、process-reset、工作区 AGENTS、架构约定、VC-B28／29、核验摘要、实际源码与恢复补丁；逐文件核对补丁与当前22个产品／测试路径一致，未执行 Git、测试或服务。

六条 thin handler 均只派发所属 whole；ProtocolModule 与实际 DI 注册证据各有唯一 Protocol／Direct binding，六 fine 类型／handler／注册／生产引用为零。三个 Main 保原精确 Recipient 与重放需求，三个 ordinary 保 `[]`／None；仅三条原 owner 获 CommittedReplayCurrentStateBatch。构造依赖沿用领域接口及既有批准技术能力，无新增核心、白名单、转发服务或嵌套派发。

核对旧新 diff：Learn 仅动作内 OpeningRejected 确认回滚后映 ResetItemRequired；Increase／Level 的 action catch 与既有 WriteResult 原生 Commit 确认回滚出口保持 ACK2／99，不包住 runner、guard、cleanup 或 outbox，取消与递归 typed uncertainty 继续传播。Level 原辅助查询 catch、OpeningCompleted Failed 被忽略及帧序保留；换槽／切页无会话 ACK 和退场静默、教程现查进度且只设2级及缺帧边界保持。实际 scope／首次 encoder 差异与 VC-B28／29 一致，没有新增首次成功 raw 或 Self 出口。

正式22例确实经过注册 decoder、SQLite、实际 DI Dispose、encoder 与 production outbox，覆盖每个 Main 的原 entry／Ordinal 重放、资产一次性、精确 physical／替换隔离、动作及 native Commit 回滚重试、scope 和首次投递失败；内容、视图与 physical 仍为受控 fixture。既有学习强例当前路径使用合法序列化 body／checksum 与 dispatcher，两合法请求仍断言等级3／4；Level 强例仍核85→86、库存9、任务 flags2和失败回滚。两个 direct driver 仅守原算法／投影，未冒充实际 scope 证据，原强断言未削弱。

直接读取旧源 TRX 与恢复记录：App115＝111pass＋4skip、Protocol35、Net1，22新例旧新均通过，22路径恢复完整；两份 pure preparation 仅两处尾空白及对应补丁索引差异。当前非增量构建成功；独立逐 display／outcome／重数复算有效 App r1 5659＝5641pass＋2原 Dimension＋16skip、Protocol r2 1786pass、Net r2 203pass，相对49仅新增22例、删除0，失败身份及消息一致。读取原 DFO 解析逻辑并重算有效日志得70＝67反例＋3control，未见额外来源／exe／orphan。runtime、strict、八坏例及新增文件 diff-check 证据与十二行闭合记录一致。

调度异常已充分披露：r1 Protocol 记录被覆盖且与 Net 重叠，两者不作为有效串行证据；App r1 早于重叠，后续独立 r2 Protocol→Net 的时间链有效。不存在“全部历史串行”或“八项目 fresh 全绿”的结论；两项旧 Dimension 失败、受控测试非 A21 实机及其余五项目沿历史有效结果的边界继续保留。无新增取证任务。STOP。

## Controller Consumption R1

主控已读取唯一 fresh Astra 的 approved／none 判定并接受本 Task。独立读取实际旧新 TRX、恢复记录与当前代码，逐 display／outcome／重数核得 N22／D0，原两项 Dimension 失败及 skip 不变；原 DFO parser 对三个有效 fresh 项目精确通过，runtime 六路与六 fine 清零、仅十二行台账变更及全部 original／顺序／非本项不变均已复核。调度重叠及原 Protocol 日志覆盖如实保留，正式采用 App r1／Protocol r2／Networking r2 的无重叠时间链；其余五项目为历史有效结果，不冒称八项新跑。

最终旧复验由主控执行并确认22文件完整恢复，纯准备补丁可单独应用于27e91df9；随后仅格式清理，当前构建／全量覆盖最终源码。接受 VC-B28／29 的有界生命周期差异及测试边界，按纯测试准备、whole 与记录分两次本地检查点，署名 Codex。继续 T-007 的普通任务协议；不封存功能簇／Spec，不推送、PR、合并、tag、外部 issue 或清理。
