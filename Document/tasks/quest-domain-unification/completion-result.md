# T-002 完成计划与统一结算结果

基线424da804；工作区task/despair-tower。未提交、未部署；T-001既有修改和调查目录保留。

## 接口与执行职责

- `IQuestCompletionDomainService.PrepareAsync`只读外部事实和任务记录，生成`QuestCompletionPreparation`，成功计划显式携带`QuestCompletionRewardPlan`（ConsumedItems、Items、Gold、Experience、GrowType）及完整Effects/后续任务投影。不执行apply、不发完成事件、不提前写完成标记。
- `IQuestCompletionSettlementDomainService.FinishAsync`位于Application/Settlement，统一分流普通/镜像。普通计划由现有ApplyEffects一次执行，随后同事务入队QuestCompleted；原QuestCompletionReceipt保持协议适配形状。
- `IMirrorAradQuestDomainService.PrepareCompletionAsync`只读校验RunId/目标/本局记录、每日权益，预演并返回奖励与本局记录变动计划；`CommitCompletionAsync`仅维护任务自己的本局记录和MirrorQuestChanged事件。Settlement执行现有奖励effects、同步Direct投递MirrorRewardRecorded，再提交本局记录；任何异常由同一个CMD事务回滚。
- 镜像重复领奖返回既有成功/空变更，不再apply、不增quota。镜像不写普通30槽日志。
- `QuestCompletionReceipt.MirrorCompletion`把已选择的完成模式提供给CMD；FinishQuestCommand不再查定义或自行选择完成执行路线，只按receipt生成协议投影。
- `IQuestClearancePlanRuleService.BuildAsync`位于Quest，生成免条件完成的任务记录变化和依赖刷新，不生成cost或普通任务奖励。Inventory的CouponAsync仅拼接券消费效果。
- 完成券UseAsync的OpeningInventory.Apply执行迁至Application/Settlement/QuestClearCouponSettlementService；接口为`IQuestClearCouponSettlementDomainService`。原结果/选取规则保留于QuestClearCouponContracts。

## 统一状态与兼容性

普通CompleteAsync在remaining事实中加入本次Cleared mark，然后一次调用统一Effective状态计算；已删除本地第二轮clearquest缺口公式。完成券依赖推进也复用QuestStateRuleService.ApplyClearedRequirements。

补充独立审查发现：镜像Hunt/Locations目标trigger可提前为0，而地下城尚未ClearOpen。AcceptedQuestState新增可选CompletionAllowed覆盖；镜像Query使用CanFinish结果，保留原wire trigger，不伪造非零进度。普通任务默认继续按完整PackedTrigger为0判断。

接取/放弃仍为协议层薄类型适配，调用各领域既有规则。普通Accept没有新增等级/前置门禁；不能把新增只读CanAccept查询暗中变成旧Accept的强制规则。任务授予物品在CMD编排侧调用统一派发，未侵入Quest；已清理HandBack附近过时的部分授予失败注释。

没有新增Effect种类，原EffectOperationContext槽位/Quest/经验/技能足迹分支继续覆盖。ACK/NOTI仍在CMD提交后由协议handler发送。检查、Prepare和应用仍要求使用同一CMD事务和角色执行边界，计划不跨请求缓存。

## 调用点迁移

| 原调用点 | 新路径 |
|---|---|
| FinishQuestCommandHandler | QuestCompletionSettlementDomainService→普通Prepare/镜像Prepare→Apply→receipt |
| 普通Quest完成领域 | 只读Prepare，现有纯规则预演保留 |
| 镜像Finish | Prepare + 自身记录Commit；外部奖励/权益由Settlement编排 |
| QuestClearCouponCommandHandler | QuestClearCouponSettlementDomainService |
| SpecialConsumableEffectPlanRuleService.CouponAsync | QuestClearancePlanRuleService生成任务变化，Inventory仅拼券cost |
| Ordinary/Title/Achievement/Career完成测试 | 调用统一Settlement；新增Prepare只读断言 |

静态扫描Application/Quests没有IApplyEffects、OpeningInventory.Apply、DeliverAsync等外部资产执行调用。

## 验证

- Application专项88通过，0失败/跳过。包含CommandHandlerArchitectureTests、生产注册图解析、普通/镜像Prepare提交只读、显式cost/reward、真实统一完成、共享材料/金币/其他任务刷新、重复提交、满包邮寄、事务回滚、完成券免条件无普通奖励及只读计划。
- Protocol专项71通过，0失败/跳过。包含完成/接取/放弃/完成券、CMD注册与receipt.MirrorCompletion模式投影。
- 镜像Query回归覆盖Hunt目标trigger=0但未ClearOpen时CanComplete=false，ClearOpen后true；虚拟任务不落普通日志。
- TRX：checks/quest-completion-focused.trx、checks/quest-settlement-protocol.trx。
- PVF_ARCHIVE_PATH指向artifacts/avatar-socket-text-20261011/Script.server.pvf。
- `git -c core.whitespace=cr-at-eol diff --check`通过。AchievementTitleBookTests.cs在HEAD原为无BOM/CRLF，保留该文件原换行；其他既有文件保持原BOM/LF，未全文件规范化。

## 交接

尚未运行全Application/Protocol/Persistence与Release整体构建，未执行独立完整结果审查、未部署或实机验收，由集成阶段完成。本工作者所有写入/构建已结束，解除写占用。
