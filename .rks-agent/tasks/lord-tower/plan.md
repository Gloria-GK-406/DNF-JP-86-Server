---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 领主之塔

## Planning Choice

依赖已闭合，当前方案可在一份顺序计划中维护，采用direct详细计划。限定单个worker写工作区，主代理保留集成、评审和部署；不创建Task Unit文件。每段完成不代表单独交付，完整实现检查通过后做一次implementation checkpoint。

## Approach and Shared Decisions

公共准入header从PVF原DGN解析取得，独立于Maze；普通路径继续原行为，难度规则消费同一视图。Boss静态池和纯规则独立，运行态参考IllusionTower的事务克隆，真实Prepare→Consume→BeginEntry扣票，不伪造普通DungeonPartyEntry。跨模式占用使用下层共享事务性服务，避免Boss→Admission→Boss环。

规则不查询世界，领域不直接读PVF；协议handler只驱动领域命令和投递。奖励复用经验、钱包、物品生命周期和InventoryThenMail，独立掉落只用可证来源。NPC购买只加内部精确run/map/session授权，不接受客户端自报授权、不放宽普通地下城。

## Work and Dependencies

### T-001 公共准入与静态规则基础（RQ-001～RQ-003、RQ-005、RQ-008）

定位PvfDungeonDefinitionSource的公共DGN读取，提取DungeonAdmissionDefinition/source和对应规则接缝，使DungeonAdmissionDomainService和DungeonDifficultyRuleService都能使用无maze头部。普通定义映射兼容既有测试/调用者。增加PvfBossTowerContentSource及不可变Boss内容（8池、29MAP、NPC、global_bosstower参数），启动路径预热/验证配置。增加活动2329受管定义及EventPlayerSnapshotProjection中的3505，开关两态一致。

将2749110纳入ItemArrivalRuleService独立周期，增加Boss在线日补给作业与选角补给路径，复用SouthernDale/Tau的事务/调度/有效配置机制。保留旧claim保守比较避免迁移重发。先补行为测试（关闭拒绝/投影、补票时间/原子性），看到真实行为缺失后实现；结构拆分只运行既有行为回归，不为依赖形状制造测试。

### T-002 专用领域闭环（RQ-002、RQ-004～RQ-009）

在Abstractions/World/BossTower、Application/World/BossTower实现规则/运行/结算接口与实现。聚合含run/session、成员slot/leader/dead、CurrentStage/LastClearedStage、roomVersion、seed/map/actors/drops、Ready、完成与发奖标记。全队Prepare/Consume/BeginEntry与建run同一事务；公共参与占用防止普通/Illusion/Boss互入且无DI环。失败必须RequireRollback或抛异常，不以正常拒绝提交已扣票。

非最终清层只标记Cleared，MoveMap校验当前阶段对应合法parity坐标再进下一层；第8层和全灭产生一次结算。门票不退、离队不发终奖；重试先释放旧run再走新全队准入，同事务失败保护原结果。死亡不进入普通复活/虚弱路径。塔内拾取归属run/room/UID，复用正常Inventory/材料/任务事实；禁APC人偶/复活币从实际消耗品/复活路径生效。断线清理匹配旧session，存量资产保持。

塔RewardRule读取Boss参数，逐怪与终局各自经验上下文，终局最终封顶；生成一次候选，以真实物品/金币交付结果投影，空格不发放。独立/指定怪物掉落不调用普通总生成器，碎片自动拾取按当前资源规则执行。NpcShopDomainService按当前Boss域签发的内部许可复用报价/扣材/交付核心，实际map必须有1010；购买/开启及相关任务事实贯通。先加可观察原子回滚/重复请求/合法跨层/奖励结果测试，再实现对应行为。

### T-003 协议与生命周期接线（RQ-004～RQ-010）

增加Boss专用消息切片0333/4/5/6和codec/注册、命令及投递服务。既有SelectDungeon、Ready、MonsterDeath、MoveMap、Death、Pickup、0048、GiveUp/Leave、SelectCharacter cleanup按当前领域模式分流，不能普通fallback误结算或发虚弱。复用IllusionTowerProjectionDelivery的成员投递/会话核验/库存经验通知方式；业务决策留领域。

0333为8B；0334为12+14M+18I；0335为经验u32+固定8组count及item/addInfo；0336为1B。阶段坐标parity、未占用成员0组；0048只处理可证按钮/合法state，必要时继续现有dump取证而非把event1无条件回城。不篡改021F卡册。测试实际handler分流、事务后通知、重放无新收益和旧session隔离；协议文档同步CurrentImplementation与仍待原生验证项。

### T-004 集成、独立复核及部署（RQ-011，全部AC）

完成no-PVF合成测试和真实PVF测试；修正手工DI fixture只注入真实所需double，不把Script.pvf变成隐藏必需项。Release build/test串行，保存完整输出/TRX；若失败先取身份/根因再修复。CI要求的PythonWireSmoke/Release以及frontend（只有需覆盖变化/CI要求）正常检查，避免并行Host端口争抢。达到通过后implementation提交。

新独立reviewing-work上下文对整个diff和全部AC审阅，主代理处理缺陷并做remediation checkpoint，重新评审。fresh verification覆盖契约SC-1～SC-8，包括部署。部署脚本从artifacts/gold-silver-quota-fix-20261019已有prepare/deploy/database_ops受控调整；先核对当前运行PID/路径/监听/DB/PVF，再SQLite backup+integrity/FK验证，候选clone无监听演练；复制现服配置/experience到新runtime，不打印密钥。使用新实现提交publish、源身份manifest。只停止核实后的旧PID，停止后备份，Start-Process Hidden启动新版本；查bootstrap/GM health、12程序集哈希/版本、监听归属和DB一致性。失败恢复旧runtime并保留当前DB，不自动恢复备份覆盖玩家资产。发布验收清单覆盖入口启停、扣票、八层/全灭/退出/重试、多人与NPC交易、掉落经验和06:00补票，标记仍待用户实机。

## Verification

每段针对实际可观察行为回归，不为低价值结构变化增镜像测试。测试原子扣票/发奖、重复/旧会话、跨层条件为高收益保护；编码精确字节及实际handler接线防止静态字段正确却未接入。完整独立结果review与部署后fresh evidence决定seal；原生验收明确由用户执行。部署前保留回退材料，关闭网络的clone不能冒充真实服务可用性。
