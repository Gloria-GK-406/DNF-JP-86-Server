---
kind: rks-review
verdict: approved
---

# 牛头怪乐园完整变更审查

## 审查目标与范围

- 目标：`task/next-round-20261013` 分支上，以 `3ad1467bf287d06986e37760fceba2c2b09164b0` 为基线的未提交完整工作树变更；审查时生产源码、测试与交付文档的最后修改均早于正确 server PVF 全量回归完成时间（2026-10-15 18:25:07 +08:00）。
- 治理来源：`TaskContract.md` revision 1、`spec.md`、`plan.md`，以及仓库 `AGENTS.md`。
- 范围：完整代码变更和 `Document/TauKingdom-20261015.md`、`Document/DailyRefillSettings.md` 两份交付文档；同时核对条件部署边界。
- 方法：独立阅读完整 diff、新增文件、关键调用链及回归测试；按照 reviewing-work 的 Code 与 Deliverable 标准判断。没有提供或要求独立挑战集。

## 结论

当前目标在本次审查范围内没有未解决的阻断缺陷，批准进入完成验证阶段。本结论批准的是已识别工作树中的实现与文档，不等同于任务整体完成、已部署或 `ClientVerified`。

## 决定性证据

### 入场、地图与掉落

- `EnterDungeonCommandHandler` 在通用/镜像/循环路由之前识别 DGN3700；`TauKingdomDomainService` 明确拒绝多人、非普通、练习、循环和镜像入口，并把门票消费、失败回滚和运行态发布交给公共 admission/visit 事务链。
- `PvfTauKingdomContentSource` 从实际 DGN3700 声明确认单人、角色等级替换、不耗疲劳和一张 690001556 门票；业务只冻结入场角色等级到公共 layout，不重写三房间路线、经验权重或 Boss 奖励。
- 真 PVF 回归覆盖 27 级门槛、无票拒绝、镜像拒绝、回滚不扣票、50 个首房怪物、三房间、等级冻结、疲劳不变，以及既有 62040 掉落表的 3 本/86 级不掉边界。

### 06:00 补票与既有普通补给

- `DailyRefillDomainService` 分离普通首次入场批次与独立周期规则：登录路径仍在规则为空或仅含牛头票时领取普通 `daily_refill_attempt`，在线路径只接受独立周期规则；两条路径继续共用每物品持久领取记录和同一派发事务。
- `DailyRefillRuleService` 只让 690001556 使用 `IDailyResetRuleService` 的 06:00 周期，并保守保留部署日可能领先的新旧记录。针对性测试覆盖 05:59/06:00、重复调用、事务回滚、离线跨日不累补、在线牛头票不抢普通批次、当日新增普通规则不重发，以及空/Tau-only 登录仍领取普通批次。
- 06:00 调度通过排他账号命令执行并重验 selected/published/active 代次；选角在发布后重新发送同一幂等补票命令，覆盖调度快照看见未发布会话的竞态。测试同时覆盖真实 command bus 的 unpublished → published 边界和选角工作流顺序。

### 兵书原子性与客户端投影

- PVF 内容源仅识别 `[expUp]` / `[expPercentUp]`，并把已识别但畸形的声明保留为不可用，避免退回旧的“只扣物品”路径。
- 经验规则实现固定值及当前等级升级所需经验百分比向下取整；普通物品规则继续负责等级、到期和消费计划。消费与 `CharacterExperienceEffect` 在同一个 `ApplyEffects` 事务中提交，随后读取技能点；写经验失败的回归证明物品和经验共同回滚。
- USE_STACKABLE 成功响应继续使用既有 0x002C，并追加既有 0x0025 经验/技能点投影；effect 重放测试证明重复入站只回放绝对库存与进度状态，不重复扣书或给经验。普通非经验物品仍走原路径。
- 新生成兵书的次日 06:00 到期和在线/登录清理复用现有 `DailyDelete` 生命周期；门票没有被赋予每日到期。

### 活动、分层与交付文档

- 活动 2215 由既有 managed-event owner 管理，默认启用，显式 GM 禁用持久保留；NOTI006C 的链接为客户端静态解析支持的 `taukingdom/0/0`。实现没有引入 0482 或 TauLevel 次数语义。
- Application 只依赖 Abstractions；PVF 读取保留在 Infrastructure，协议编码与通知投影保留在 Protocol。新增命令已纳入事务注册和 effect-settlement 保护清单。
- 两份文档与代码一致，并明确区分 `CurrentImplementation`、`ClientStatic`、PVF、历史参考和尚未取得的 `ClientVerified`，没有把构建或真 PVF 自动化测试写成实机结论。

## 审查中发现并已复核的修复

1. 早期版本移除整批门控后会让在线牛头补票改变普通补给的“当天只处理一次”语义。最终版本以独立周期接口和普通批次领取分离修复，并补了空配置、Tau-only、在线后普通补给及当日新增规则测试。
2. 05:59 开始选角、06:00 调度快照略过未发布会话时可能漏票。最终版本在发布后用同一持久领取记录补检，并在新的异步命令之后再次执行 `EnsureCurrent`。
3. Tau 路由优先于镜像路由曾可能让带 `MirrorQuestId` 的 3700 请求进入 Tau admission。最终领域入口显式拒绝镜像，并有真 PVF 回归。
4. 新的 `RefillOnlineTauTicketCommand` 最初遗漏 effect-settlement 注册清单。最终测试期望已加入该命令，正确 server PVF 全量回归通过。

以上问题在本次目标中均已解决，没有遗留为 blocking finding。

## 验证证据

- `artifacts/tau-kingdom-20261015/build.log`：Release 全解决方案构建成功，0 警告、0 错误。
- `artifacts/tau-kingdom-20261015/targeted.log` 与 `tests/tau-targeted.trx`：针对性回归 11/11 通过，0 跳过。
- `artifacts/tau-kingdom-20261015/application-server-pvf.log` 与 `tests/application-server-pvf.trx`：使用运行时所需的 `artifacts/sao-ticket-20261014/Script.server.pvf`，Application 全量 3918/3918 通过，0 失败、0 跳过；TRX 计数与日志一致。
- 审查者复跑 `git diff --check`，无输出。

先前使用客户端 PVF 的全量运行不作为最终证据：其中 3 项失败来自该档案缺少服务端迷宫修补 DGN60/7100，另 1 项确实暴露并促成了上述命令注册清单修复。最终证据已改用生产运行配置指定的 server PVF。

## 非阻断边界

- `taukingdom/0/0`、DGN3700 入口可点击、实际 0x002C/0x0025 UI 刷新和完整 50 + 50 + Boss 游玩闭环仍需 A21 实机验收；当前证据只能分别支持 `ClientStatic`、PVF 和 `CurrentImplementation`。
- 最终进程检查发现游戏客户端 PID 6536 仍在运行，因此按用户授权条件没有停服、替换运行文件或修改线上配置/数据库/PVF。实现处于待部署状态，这符合 SC-6 的条件分支；本批准不声称已部署。
- 完成验证仍需由控制器对 TaskContract 的全部 SC 做新鲜证据汇总，并记录待部署状态；本审查不能替代该阶段。

## 复审记录：RQ-004 结果投影归一（2026-10-15）

```yaml
kind: rks-review
verdict: approved
```

复审目标是 `spec.md` 的 RQ-004/AC-004 末句，由“主客户端用普通 USE_STACKABLE 响应并通过 Event 投影刷新经验/等级/技能点”精确改为：

> 主客户端用普通USE_STACKABLE响应及命令提交后的绝对库存/经验/技能点结果投影刷新状态；在线补票通知走EventBus。

这是对既有成功条件和实现同步模型的合理归一澄清，不需要生产改动：

- TaskContract SC-4 要求扣书与经验原子提交并让客户端刷新进度，没有指定兵书主动操作必须经过 EventBus。
- `UseStackableHandler` 是客户端主动请求路径；它在事务命令返回后同批发送普通 ACK、绝对库存状态和 0x0025 经验/技能点状态。这满足 SC-4 的可观察结果，也保留 effect 重放的绝对状态语义。
- 牛头票在线补发是服务端主动发生的状态变化，仍由 `TauKingdomTicketRefilled` 经 EventBus/通知投影刷新库存。新句明确保留了这条要求。
- 该修改没有放宽原子性、等级/到期拒绝、异常不扣书、客户端刷新或补票通知要求，只修正了把两种不同触发模型都称为“Event 投影”的不准确描述。

控制器随后提供的无 PVF 全量结果为 3440 通过、400 跳过、0 失败；这与本次仅规格澄清一致。原完整变更的 `approved` 结论保持有效。
