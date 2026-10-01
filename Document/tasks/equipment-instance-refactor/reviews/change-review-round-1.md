---
kind: rks-review
verdict: approved
---
# 装备实例重构：独立全结果审查

## 目标及范围

审查时间：2026-09-30。审查者为生产者之外的新鲜独立 reviewer；直接执行审查，没有再委派。依据 `reviewing-work` 的 `references/code.md` 和 `references/deliverable.md`，同时检查代码、文档与部署证据。

目标工作树：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap`，HEAD 为 `6e60c4c67417ce06b88e9e99277fb1e4c0239726`，当前结果未提交。治理来源为本仓 AGENTS.md、[TaskContract](../TaskContract.md)、[Spec](../spec.md)、[Plan](../plan.md)、三份 Task Unit 计划/结果及 `sdd-execution.yaml`。仓库没有 `.codegraph/`，本轮使用实际源码及 Git 差异定位。

检查了所有已追踪差异、全部未追踪 Server 文件及本任务 Document 文件，包含旧 `EquipmentInstance.cs` 的删除、新实例/规则/测试、原强化准备文档中的历史版本提示。Server 文件共 22 个，与部署 manifest 的完整集合一致；逐文件 SHA256 和删除标记均无 mismatch，目标源集合指纹为 `6af2b4b5ed52615a84b3c99693b215d2f4ce102cd2943bf648fea36bee467333`。文档范围包含契约、规格、计划、路由/执行控制、基线边界、三个 Task 文件、实现示例、Human-Test-List 和自动/部署证据索引。

## 结论

**approved**：在本次完整结果审查范围内，没有发现尚未解决的、具有具体后果的阻断缺陷。当前产物可以进入 SC-6 的人工最后门；该 verdict 不代表 TaskContract 已完成，也不授权提前提交。

**当前未决项仅为明确约定的人工验收**：E-01 结构确认、R-01 本重构版本一次普通强化、R-02 正常退出重登。用户反馈前，最终 verification 应保持 `manual_required`，不将以前版本的结果代入，不封存最终完成结果。其他养成完整 GUI 清单仍沿原任务继续，不扩大本轮人工门。

## 决定性检查

| 检查面 | 当前代码及证据 | 判断 |
|---|---|---|
| UID、拥有者与最新状态 | `Application/Inventory/EquipmentInstances.cs:40`、`:60`、`:173` 每次操作验证原事务并在当前 owner 库存按 UID 定位；没有独立 core 缓存。`EquipmentInstanceTests` 检查两句柄保留不同字段、同模板不同 UID、连续换槽、越权、真实 SQLite 读回和持久化。 | 满足 SC-1/2、AC-001。slot 为当前地址，修改的是 UID 指向的装备。 |
| 销毁与寿命 | 同文件 `:24` 的事务共享 tombstone，`:165` 销毁后所有集合/句柄拒绝该 UID；事务提交/回滚后 accessor 不再交出原事务。九项新实例测试覆盖销毁后另一句柄、同 core 重放、回滚原状态及提交/回滚后的失效。 | 没有误把耐久归零当销毁，也没有旧句柄跨新事务继续写入的路径。 |
| 暂存身份映射 | `Persistence/Inventory/SqliteItemDomainStore.cs:115`、`:139`、`:200` 的读取投影复用结算 `Survivors`；`:311` 补读合法 transfer source，`:367` 仍按显式转移、原位相同内容、相同内容移动、原位同一物品顺序匹配，并按 UID 防重用。写入不接受调用方 UID。 | 暂存更新与转移继续保留原世界身份；未分配行的新物品不会被猜出 UID。实际 move 在 `ItemMovementDomainService.cs:74` Flush 后继续使用实例，连续换槽测试使用同一生产语义。未要求扩张裸 staged 多跳操作。 |
| 规则、领域、实例职责 | `EquipmentReinforcementRuleService.cs:10/:65/:82/:102` 接管报价、决定、保护和销毁产物公式；Enchant/Repair 规则接管内容映射与价格。实例只进行字段组修改与本地约束，没有内容源、费用、概率或角色资源查询。领域服务继续管理会话、锁、资产数量/期限、产物空间及共同事务。 | 满足 SC-1、AC-002/003。旧快照及其 Repair 公式已删除。 |
| 调用覆盖与兼容 | 强化、附魔、异次元、魔法封印、锻造，以及普通/自动修理、耐久均经新实例；分解通过 facts 与 Destroy 适配。强化残余 `core with` 是空间模拟，分解 Put/Clear 是工具/产物。 | 没有留下这些业务的装备字段平行 writer；没有引入增幅新玩法或扩张分解旁支。 |
| 原规则与结算 | 对照基线差异，保持强化 +30 准入、rarity 上限、缺 level weight 取 1、100000 随机范围、自动保护优先级、失败才扣券、销毁产物空间先验；原资源准入后才抽 draw。附魔升级珠子拒绝、锻造失败耗材、魔法封印暂态清理、异次元 type/value、修理 float32/整数 grade/截断、无耐久零费不写和自动修理资格/额度均保持。 | SC-3/AC-004 有实际代码与既有行为 oracle 支撑，没有因结构迁移改变错误码、概率或扣费次序。 |
| 事务、通知、重放与 wire | 原 ProtocolModule 仍以 Transactional/Exclusive 注册相关命令；强化 handler 在 CommandBus 返回后才缓存成功并发布通知。实际 handler/domain 测试使用 SQLite 触发器强制提交失败，并检查回滚和无通知。Protocol/Networking/Data wire/schema 无差异。 | SC-4/AC-005 的兼容性证据成立；没有把自动回归升格为 WireCaptured/ClientVerified。 |
| 架构判据 | `ItemIdentityArchitectureTests.cs:97` 的可空身份扫描和行网关许可未移除；`:137` 只允许具名世界实例 facts/API 及其实现；`:146` 明确否定通用 ContainerSlotItem/InventoryItemState/ItemPile 的豁免。 | 没有以全命名空间或所有库存类型放宽判据。修正已有宠物实例 UID 的基线误判符合本次世界实例边界。 |

## 自动与部署证据交叉核对

本轮读取并核对原始 `artifacts/equipment-instance-20260930/full-test.log` 和八份 `equipment-instance-final.trx`，结果为 Application 2309、Protocol 1202、Persistence 385、Infrastructure 408、Networking 72、Host 24、HttpApi 50、Pvf 60，合计 **4510 passed、0 failed**。TRX 的两个 `NotExecuted` 分别为 Town/Quest 内容源 missing-archive 负向测试，与真实 PVF 配置下的原 skip 条件相符。没有机械重复整套 solution 测试；Release build 的零警告/错误按本任务原始验证记录保留，reviewer 另执行的 `git diff --check` 通过。

读取了实际模块副本 probe 的源码和日志：其 `Database:Path` 指向 `probe.db`，生产库不是实例写入目标；生产 Infrastructure/Persistence/Application/Networking/Protocol 组合完成选角消息构建与编码校验，真实 PVF 对 UID339 报价 450 金币、3037×10，固定 draw 成功至 +1。公开两句柄调用保存强化和耐久字段，事务后旧句柄拒绝，新事务读取相同 UID 与修改。该 probe 没有执行玩家业务扣费，文档已准确限制其证明范围。

读取在线 backup 脚本、验收基线、发布配置和启动日志；主数据库路径仍为原生产库，新 runtime 保留原 PVF 路径，备份 `before-deploy.db` 保留。reviewer 当前只读核对：PID42104 的实际可执行文件在新 runtime，7001/10010/10011/18080 监听均属该 PID，bootstrap 返回 apiVersion1/status ready。发布的 Application、Abstractions、Persistence 三个关键 DLL 与当前工作树 Release 构建 DLL 的 SHA256 相同。未修改或重新启动服务，未修改任何生产数据。

## 交付物与最后门

实现例子对应当前公开 API，正确说明报价用于准入、TryReinforce 返回决定、领域结算资源、实例应用字段组；新建无 UID、事务失效、移动跟随与销毁失效均有实际代码或测试支持。Human-Test-List 给出当前 runtime/PID、UID339、原资产、一次强化报价及正常退出重登顺序；原强化准备记录已经标注历史 PID 与当前入口。任务结果清楚区分自动完成、可供验收和最终用户认可，没有声明本轮实机已通过。

**阻断 finding：无。缺失 review 前置输入：无。** SC-6 尚未得到当前版本用户反馈，属于下一阶段约定的人工门，不是本次可验收产出的拒绝理由。后续由控制器收集 E-01/R-01/R-02 反馈，并结合当前日志和只读数据库核对，再作最终完成判断。
