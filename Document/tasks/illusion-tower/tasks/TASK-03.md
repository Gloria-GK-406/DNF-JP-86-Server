# TASK-03 — 塔经验、结算规则与一次资产提交

## Task Unit Contract

- Revision: 1
- Outcome and references: T-004后端部分；RQ-006、007、009。真实击杀即时经验、成功/失败结算经验与奖励生成进入领域事务；结果凭据可供后续网络投影，重复请求不重新发奖。
- Owned boundary: Abstractions/Application的IllusionTower与必要经验计算接缝、Application注册及相关测试；可扩展已完成TASK02的领域契约/服务，但保持其行为检查通过。不写Protocol、运行目录或玩家DB。任务物品地面/拾取及网络返回由后续单元接入。
- Dependencies: TASK01规则内容、TASK02事务/精确身份/动作与receipt；`reward-and-projection-evidence.md` 的公式与证据边界。
- Required checks: 普通/APC即时XP、每成员角色等级、无普通人数/等级差倍率；结算层与最终经验cap；确定种子候选/类型/金币/空项；击杀/结算重复、真实资产回滚和已结算receipt不重发资产；满包候选与实际交付区分；既有XP/塔生命周期回归。

## Detailed Plan

1. 实现迷妄自己的奖励规则服务，显式角色等级、奖励层、演员类型与种子输入。复用只读PVF奖励/金币/候选内容和无状态基础算法，不调用普通整套WorthOfKill/FaceFor/CardGoldOf。参数与原生随机边界详见补证文档；采用本地确定性PRNG并明确不宣称原服随机序列等价。
2. 击杀按角色等级mob-worth/2、Normal rank与difficulty、APC=5专属倍率和monster倍率逐次截断；不用普通等级差或队伍除法。结算按F*M*表[F-1]，失败F由dgn提供。CurrentImplementation的MonsterExperienceWorth曲线代替70数据库，保留该证据界限。
3. 经验使用独立Tower经验上下文，经现有CharacterExperienceDomainService→计算→DecideAward→Effects写入，不能伪装Quest/Clear普通公式。补证确认70塔kill明确跳过成长契约且无现有频道kill加成；clear自建参数中成长契约和SpiritPotion为0。独立黑钻身份映射未知，暂不自动继承普通BlackDiamond加成。先交付有依据的塔基础经验，运营/avatar/creature/未恢复premium加成保持未实现边界；最终结算cap1,100,000时保持分解各项总和一致。
4. 候选数min(10,DgnMax,CardCount[F-1])，纪录加成=0是本任务排行榜排除边界。分类按P=min(10,F)、阈值50P/99P；第三参数100不作分母。金币按补证截断链和现有内容；物品用角色等级default阈值、drop-kind/type、稀有度0..1000000与候选池；无可用时装池保持该分支为空，不重分配给装备。70随机装备覆盖支路未恢复时标注当前候选池边界。
5. 在现有IllusionTowerDomainService动作事务内接入资产：只在新Kill事实后发XP/任务；末层Kill、Die及剩余全死的Leave产生结果时结算并附着一次receipt。奖励领域服务可单独封装发奖能力，但不另持挑战状态、不反向调用挑战造成DI环；CMD只驱动塔用例。域外调用也必须保持状态+资产同一事务。后续通知失败只能重放receipt。
6. 通过既有Character/AddGold、ItemGrant/业务容器和物品生命周期发资产，创建装备带正确耐久。receipt分别保留候选展示与实际交付（库存更新、经验Award、金币余额），包括队伍稀疏槽。0091金币显示行=(0,金币数)，空行=(0xFFFFFFFF,0)且保留在count中；装备第二word为实例add_info，不能写发放Count=1，当前实例默认0时才显示0。满包按70参考该件不入包后继续，不新增邮件；异常失败回滚整次事务，不将异常当满包吞掉。
7. 新击杀事实报告现有QuestEnemyDefeated；APC的wire类型5映射到任务AiCharacter=2，普通怪映射到Monster。仅整塔成功报告QuestDungeonCleared，失败/每层清除不冒充整塔通关。活跃任务950的任务物品掉落是后续独立接入工作，本单元不把普通掉落开关当成永久关闭任务物品。
8. 已离线/替换session不影响精确旧成员清理；由离开触发的剩余全死结算只处理仍有效成员，不能因其他过期身份使退出永久失败。最终成员主动离开/断线无奖励补发。
9. 增量维护行为测试：优先RED/GREEN关键资产与幂等场景，串行运行有代表性的规则、真实SQLite事务、现有经验与塔领域回归。测试夹具可显式关闭本单元奖励以继续隔离TASK02状态测试，生产DI必须注册并在集成测试命中真实奖励。记录结果与实际命令，不提交Git。


## Result — round 1, 2026-10-03

- Changed: 新增 `IIllusionTowerRewardRuleService` / `IllusionTowerRewardRuleService` 与无挑战状态的奖励领域服务；生产 DI 注册完整。`IllusionTowerDomainService.Kill/Die/Leave` 在已有事务中调用，状态与真实 SQLite 经验、金币、装备同提交，只有新事实发奖；重复动作返回既有结算 receipt。独立 TowerKill/TowerClear 上下文经既有经验计算、DecideAward、Effects 写入。
- Changed: 即时XP与最终clear任务事实均进既有任务领域；actorType5→AiCharacter2。实际任务64/65的递减、只在末层推进整塔clear均有真实任务行测试。奖励层使用dgn的完成事实，失败1层按1、5层失败按4；结算总XP与分解同时封顶1,100,000。
- Changed: 候选保持稀疏成员Slot；空候选-1/0、金币0/金额、装备id/实际InstanceValue，`Delivered`区分实际交付。`Items`仅入包装备，`InventoryUpdates`携带BusinessSlot与完整ItemSnapshot（正确耐久）；Gold为实际余额增加，GoldBalance为提交余额。新增只读 `CanContinueAsync(context)` 检查所有有效成员下一次所需材料，不扣材料；`TotalLevels`由dgn投影。
- Why: 当前非堆叠业务创建会分配InstanceValue，并非固定0；成功装备展示改取创建后的实例值，未入包候选维持0且Delivered=false。无可用时装池该分支为空；普通副本成长契约/SpiritPotion/频道/黑钻加成不会通过普通bonus收集器进入塔。金币PartyGold乘数当前读ClearRewardTable.PartyMemberRate(1)，未声称存在独立gold专表；物品机会使用default角色等级band.Rate + DifficultyRate(0)。
- Checks: [TASK-03 bounded verification](../verification/TASK-03-verification.md)，最终204通过、0失败、0跳过；`git diff --check`通过。追踪文件恢复原HEAD行尾，保留TASK01/02改动，无Git提交。
- Remaining: 本单元后端已交付；协议通知/网络重放、返回timer、任务950的任务物品掉落/拾取由后续单元接入。未恢复排行榜加成、运营/avatar/creature/premium映射、70随机装备覆盖分支；本地SplitMix64和当前MonsterExperienceWorth曲线不宣称原服随机/数据库等价。未运行或修改用户服务、部署目录、玩家DB；非ClientVerified。

### Stable consumer contract

- `TowerActionResult.KillRewards`：每次新击杀的成员Session、Slot、ExperienceAward、QuestProgressSnapshot；重复Kill为空且Changed=false。
- `Run.Completion.Receipt.Members`：每成员Slot、Session、Award、GoldBalance、Candidates、InventoryUpdates、QuestProgress；成员旧四参保留兼容但生产完整填充。重复Die/Kill保留同一已结算receipt，不重新发奖。
- `Candidates`: 行顺序和空行全部保留。ItemId=-1应转wire FFFFFFFF；DisplayValue是0091第二word，不使用Items.Count代替。Gold为实际发放（金币cap后），展示候选可大于实际到账。
- `CanContinueAsync(TowerActionContext, CancellationToken)`：返回继续资格的只读判断；新入场仍通过Enter重新检查并消费。
- `IllusionTowerSnapshot.TotalLevels`：当前dgn总层数，不从Protocol读规则。CMD继续只调用塔owner的原Kill/Die/Leave等动作。
