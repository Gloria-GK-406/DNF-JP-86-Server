---
kind: rks-review
verdict: approved
---

# Quest 归拢独立结果审查

## 目标与范围

- 基线/当前HEAD：`424da80417f020d5e852e2807d3526dec54fc392`；分支 `task/despair-tower`，审查对象为其未提交完整实现及新增文件。
- 服务端变更身份：42个修改、新增或删除文件。按仓库相对路径排序，将 `路径\t文件SHA256或DELETED\n` 拼接后的SHA256为 `75e605c3efad36415c930e3c19305152a10746758b2d96d7e1ea3282cd496e97`。文件集合取基线diff与Server下未跟踪文件的并集。
- 依据：TaskContract revision 1、spec、plan、query-result、completion-result；应用 `reviewing-work/references/code.md`，核对仓库AGENTS架构约束。
- 审查覆盖SC-1..5：实际diff、新接口与实现、调用方、相关测试，以及既有事务、预演、effect足迹、事件和协议投影的衔接。SC-6部署不属于本次代码批准。
- 审查者独立于实现者；只读产品代码，未运行构建/测试，未改产品或其他文档。没有配置挑战集。

## 判定依据

| 边界 | 核查结论 |
|---|---|
| 三查询只读与授权 | Query/ReadAccepted明确校验账号所有权，ReadAvailable经Facts.Subject同样校验；Subject的当前宠物使用AmbientStoreAccess只读会话。查询没有进度写回、资产写入或完成事件。当前已接优先于历史已完成；CanComplete与生命周期分开。 |
| 同一状态公式 | 登录、运行时持有刷新、材料需求、完成前/后预演均调用QuestStateRuleService。共享数量缺失为0且排除Main旧副本；金币单独入facts；只替换低通道，保留其他目标。Clear依赖初值、运行时推进和完成券复用ApplyClearedRequirements；普通完成后的cleared事实先包含本次任务再统一Evaluate，原本地第二套公式已删除。 |
| 可接资格与特殊路径 | 资格规则从World迁至Quest，World/Cycle/Replay保留同一规则的适配。Circle继续检查当前intent，Crack按RunId和参与者授权，Title等不误入普通列表。Acceptance/GiveUp的类型路由是薄协议适配，没有借重构新增普通Accept等级/前置门禁。 |
| Prepare与统一结算 | 普通Prepare仅读事实、生成cost/reward/任务effects和预演；转职技能读取没有materialize写入。镜像Prepare不apply、不更新quota/领奖标志。Settlement统一应用原effects；普通完成事件在apply后同事务入队；镜像奖励、Direct quota和本局记录Commit在同一个既有CMD事务，异常继续向外传播以回滚。重复镜像领奖为空effects且不增加quota。 |
| 完成券 | QuestClearancePlanRuleService只生成任务记录和依赖变化，Inventory仅拼接券消费；外部执行迁至Settlement。保留免条件、不发普通奖励的语义，没有普通完成领奖旁路。 |
| 同步、重放与满包 | 没有新增effect种类；库存/任务/经验/技能等继续经过既有ApplyEffects和EffectOperationContext足迹。Finish CMD仅消费receipt模式，ACK/NOTI编码未变；真实物品消费投影与镜像奖励投影仍沿用既有路径。GrantBasicCharacterItemEffect继续使用InventoryThenMail。接口后缀满足命令注入约束，未新增Application跨层引用。 |

## 已解决发现

早期发现镜像Hunt/Locations的目标进度可先归零，但尚未通关；仅根据trigger判断可提交会与真正完成规则矛盾。当前 `AcceptedQuestState.CompletionAllowed` 已分离该语义，镜像Query取 `CanFinish`，保留原始进度。新增镜像查询回归覆盖trigger=0、ClearOpen=false时两类查询不可提交，通关后可提交。该发现已解决。

## 证据与限制

- 检查新增查询用例及普通/镜像Prepare提交后仍无写入、完整结算、重复提交、事务回滚、完成券纯计划、协议模式投影等测试；没有删除相关已有断言。
- 读取现有TRX：`checks/quest-completion-focused.trx` 88通过；`checks/quest-settlement-protocol.trx` 71通过，均0失败/跳过。此为实现阶段保存的结果，审查者没有重新执行，不能替代controller的当前全量验证。
- 没有未解决的阻断发现。兼容构造使用可选依赖和同一规则的缺省适配，不存在另一套条件计算公式。
- 普通Accept原有接取门禁范围继续保留；本次只读CanAccept不会自动成为新的强制门禁。这是批准的兼容边界。
- 本批准仅针对上述身份的代码结果。全量检查、备份部署和实机验收仍需controller完成；登录即显示完成、普通材料结算、镜像完成/放弃、循环任务及完成券客户端行为尚不能标为ClientVerified。
