# 完整代码与交付结果独立审查

```yaml
kind: rks-review
verdict: approved
```

## 审查对象与独立性

依据父 TaskContract revision 1、approved spec、task-units plan，审查基线 `b5506390a2e97f7254d67997510b0b8cca90a4aa` 之后 001—011 的完整当前实现、测试、文档及人工清单。目标为 `task/solo-play-roadmap` 工作区全部 tracked/new/deleted Server 路径，不限 TASK-011。使用 reviewing-work 的 code.md、deliverable.md；审查者独立于生产者与 change-challenges 生成者。

本轮重新枚举及独立重算冻结目标：230 个 Server 路径，digest `022A677546B6B07D5F887340B342F8E0DFE4F424A5CE20C64C16E7AF5D492A25`，与父指定目标一致。该标识仅用于确认审查对象，不作为质量证明。综合此前独立源检查与本轮完整要求核对，复读当前业务规则/执行器、事务与请求生命周期、投影发送接口、调用扫描和自动证据；没有将局部 approved、registry done、drift aligned 或零挑战直接当作完整质量批准。

本次没有运行 dotnet、修改生产/测试、提交、部署或接触用户运行库/PVF/client，仅写本文件。批准表示当前指定范围没有未解决的阻断缺陷；不替代父 fresh verification、本地完整提交或用户实机接受。

## 要求与实际结果

| 父验收要求 | 当前源码与结果核对 | 关键自动证据 |
|---|---|---|
| SC-1；AC-001；RQ-001/002 | DomainEffect/EffectChange 使用具名 record。ApplyEffects 先识别全部 handler、预览累计库存，再全部 Validate、顺序 Apply；明确账户/角色/可选 exact session。未知类型、物理空间/预期物品不符、累计超支先拒绝。生产计划以只读或不可变集合返回，合法不自耗与内容不支持分开。 | ApplyEffectsTests 的跨账户、跨栏同索引、累计扣除、换物/过期/未知及不自耗；RealConsumableIntegrationTests 动态枚举整个 Abstractions 的全部 concrete effect 与真实唯一 DI handler 精确集合。 |
| SC-2；AC-003；RQ-004 | ItemUseEffectRuleService 的源自耗、冷却、秘药及饲料变化均显式。Feed 业务 API/实现删除；宠物保留 AddStomach 和原时钟/待发经验/成长基础职责。消费与宠物身份/模板及其他字段没有被旧快照覆盖。 | 普通 Use/共享冷却/秘药既有回归；实际源24＋实际 pet 同库，晚 Prepare 确认 SQL flush 后失败、回滚、retry 与运行投影检查；旧宠物时钟/待发经验测试保留。 |
| SC-3；AC-004；RQ-005 | EquipmentEffectPlanRuleService 完整列出费用、成功或正常失败、保护券、升级/损毁与全部返材料；其他改造和分解也只构建具名效果。handler 调用基础 instance setter/Destroy，按 UID 读当前字段；连续变化及销毁不可复活保护保留。 | 各设备领域/handler/真实 PVF 回归；EquipmentEffectExecutionTests 的连续字段/UID、累计账户、晚 SQLite 提交失败全回滚、销毁并复用槽后旧身份拒绝；便携最后机器腾槽及损毁产物反例。 |
| SC-4；AC-005；RQ-006 | Opening/Recipe 明确源、钥匙、多堆/账户成本、金币、prepared/virtual 产物、期限、权益、luck、quota、冷却；共享 standing 预览消费腾槽与产物竞争。fatigue/coupon/water/rename 完整计划与基础 CAS 分开，没有旧 Settle/Refund/RecordUse/CompleteWithoutRewards 完整业务旁路。固定自耗的专用品保持既有语义。 | OpeningEffectExecutionTests、制作/开包/双倍/售货机既有容量与拒绝回归；SpecialConsumableEffectTests 实际源＋fatigue/usage/cooldown；011 卡/宠物、coupon/source/quest、water/source/mixed skills 晚失败及 retry 同库。 |
| SC-5；AC-006；RQ-007 | QuestCompletion 一次收集 facts，规则决定有效进度、完成/clear、物品金币、XP、career、全页技能和关联 trigger，再共享 Apply。奖励使用旧级事实，earned 明细与成长 cap 分开。ApplyAwardAsync 为共享 effects 入口；Kill/helper/canonical/party 各 recipient/Clear 均接入，旧 Award 委托、Quest Finish 与 Career 完整 writer 关闭。 | OrdinaryQuestEffectTests 多堆完整奖励、有效 Seeking、合法两次、cap、真实1776筛选、转职全页/locked flags/bonus 保真、late XP/skill/Prepare trigger 联合回滚；CommonExperienceEffectTests 实际 bus/SQLite 的归属、跨 TResult 串行、提交后估价与失败后恢复。 |
| SC-6；AC-002/007；RQ-003/008 | 45 exact recipient/生命周期 → 57 全局串行 → 58 结算/uncertainty → 60 UoW。各 SQLite facet 同参与者事务；内存效果 durable 后发布。60 在 Commit 前 Freeze；58 正常返回同步 Committed，普通失败仅在 Abort 确认后记 ConfirmedRollback；递归 uncertainty、postdurable/发布违约同步建立 exact-session/physical fence，不能被 handler 普通失败映射吞掉。 | 多 effect/late SQL/late Prepare/commit/rollback failure 及 typed uncertainty 测试；真实 source→pet/quest/skill 四组、真实 bus durable Probe、实际 registry/exact close 分层证据。开发 RED 与首次失败历史保留。 |
| SC-6/7；AC-007；RQ-008/009 | 全 18 request/20 wire/20 primary command 接 physical CWT ledger。完整 body hash/length 在 decoder 前生成；all-frame ordinal 和 fresh 区分同次重传与合法下一次，不靠参数相同。footprint 穷尽 actual effect/change，未知在提交前拒绝；第二 main 不付，后续 readonly 不覆盖，closed inherited flow 不降成 trusted。旧六 guard 与 Lottery/Vending 业务缓存删除。3 XP 使用自己的事件 claim，durable 后 claim 发布失败不回退成可重付。 | 实际 DI exact 集合；Use durable 后 specific sender failure RED/GREEN、当前 typed batch/原 context/无旧 ACK、nextFresh/fullushort/wrap/128窗口/raw冲突、确定等待 barrier 下 stale selected、second main/readonly/closed、NoSelected capture 后选角拒付；Lottery 并发 draw/pay 一次；008 XP claim/recipient 故障。 |
| SC-7/8；AC-008；RQ-009 | 全范围 active caller 分类与当前覆盖表对应规则、效果、handler、基础 writer；源码检查未发现未解释的范围内结算旁路。实现说明使用真实 API，顺序24项含主要业务闭环、连续合法操作及重登；全部 HumanPending，未声称已部署/ClientVerified。 | 全 Server/src tracked/untracked caller scan；最终完整8项目、动态注册、Host唯一绝对临时DB；coverage/implementation/human-test-list 与源及证据一致。 |

中央方案仍为 facts → 完整具名计划 → shared Apply → 基础 instance/container/store → 提交后协议投影。没有任意业务委托 effect 或“执行整段旧业务”包装；ledger 的 handler 委托属于网络流程接缝。范围内领域服务已经关闭直接完整结算写入，保留的底层函数不是平行业务实现。

普通 no-refund Learn 的豁免有 TASK-004 边界：任一 Refund!=0 在首写前整批进入 effects，mixed 更新/新增同行也一并纳入；仅 all Refund0 普通学习保留原写入。其他具名范围外 caller（selection/quest accept-drop-trigger、shop/mail/repair/move/交易、combat 材料耐久、ground/card、tutorial/recovery、pet clock/hatch/growth）与父范围相符，没有扩大为范围内完整业务旁路。固定等级券/升级宝珠等仍未新增，不被人工清单虚构为成功能力。

## 当前重放与首次通知边界

本轮独立复读 ReplayStateDomainService、IReplayStateProjection、ReplayStateProjectionDelivery 与 TouchSocketNetworkMessageSender：当前读取无 selection 维护/LegacyNormalizer/Seeking 同步/宠物 Advance，按 needs 在同 SQLite snapshot 收集；one At、数组冻结与 outbound copy 保持。Exclusive 覆盖读到发送并核对 exact recipient；batch 核对原 physical context，整组 actual encoder/endpoint/frame≤1MiB 校验并复制完才 first send，后续始终用捕获 transport。

重放投影当前绝对状态：六 inventory 与零虚拟余额、空物理槽 -1（金ID0另列）、当前 pet/skill/quest/XP/fatigue/usage/premium 与必要 appearance。不重发旧成功 ACK、奖励增量或 earned popup。GoldProbe 的真实 bus/SQLite/pre-send barrier 是 read→send 门证据，PartyXP 的适用性另外来自实际 recipient 调用链；没有把 Probe 标作实际 Party wire race。

普通首次通知继续保留既有 best-effort Send-ID/Explicit 路径。设备/opening Compose 会核对完整 receipt.Session，但 FinishQuest 与共享 SendAsync 没有新增全组原 physical context/exact-session batch 保证。挑战文档对此描述准确，本审查接受为既有非阻断边界：状态结算已经先提交/记账，重复请求只读当前；本轮未形成这条首次投影边界导致重复付款或部分资产提交的具体缺陷。**009 的强化保证仅适用于当前 replay batch，不能外推为所有首次通知。** 不要求为批准本重构增加新的全通知架构或原子网络提交承诺。

I/O 仍可能仅送出部分消息；已 Committed 不因此重新抽签/支付，后续当前刷新或正常重登承担恢复显示。客户端实际 sequence reset、没有 nonce 的古老 raw 碰撞、TouchSocket callback 调度外部证据仍有明确 Unknown；24项人工清单包含返回选角后连续使用取证，不先虚构 reset 策略。它们未被写成已证明能力，也没有实际伤害证据要求本轮扩大 scope。

## 独立挑战裁定

已消费冻结 `change-challenges.md`：保留 CH 集合为零，没有逐项未裁定假设、阻断输入或待举证项。其反查记录作为源码/证据索引，零项本身不推导批准。本次再次核对原子提交、raw 身份/合法 next、旧 writers、current batch 与首次通知边界后，没有确认 material 缺陷；首次 legacy delivery 观察按上节明确裁定为声明准确的非阻断边界。

## 自动证据与交付说明

本轮重新直接解析8个正式 TASK-011 TRX 的 UnitTestResult outcome：Application2599、Protocol1226、Networking86、Persistence392、Infrastructure473、HttpApi50、Host26、Pvf60 共 **4912 Passed / 0 Failed / 2 NotExecuted**（total4914）。两条 skip 为真实 PVF 可达时的 missing_archive 负例；不使用 Infrastructure Counters.notExecuted=0 覆盖实际两条 skip，不将专项9与完整Application重复累加。

复读 TASK-011-build-final.txt 为0warning/0error，当前 git diff --check clean。runner 的 effective settings/实际 collections off/区间证据保留；不是仅凭配置声称 adapter 执行串行，也不外推内部异步没有并发。真实 fixture采用唯一绝对临时DB，Host composition不接默认运行库。

四组实际组合中晚 Prepare 读取使用提交前捕获的原 SqliteDatabaseParticipant.CommandAsync，确认 source1 与目标新值已 flush 后注入失败；不是 DomainTransaction.IsSettled 后另开连接读旧值。首5/4失败、其他开发诊断、007—010 RED均保留。domain+scope、真实 bus/SQL、TCP metadata dispatcher seam、actual encoder/registry/exact close 与 postdurable Probe 的证据层次没有互相冒充。

coverage 当前总表与历史段落区分明确，Water/XP 当前状态正确；implementation 与当前 API/顺序相符，人工24项准备/操作/期望可用于用户后续按序接受。CurrentImplementation 与 HumanPending、范围外及未授权操作声明准确。当前完整代码与非代码交付可以通过本审查；此阶段现停止，父控制器继续独立 fresh verification 后按已授权范围完成一个完整本地结果提交。
