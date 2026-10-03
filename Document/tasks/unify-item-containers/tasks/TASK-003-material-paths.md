# TASK-003 — 全部材料入口及协议投影切换

## Task Unit Contract

- Revision: 1
- Outcome: T-003；RQ-002/RQ-003/RQ-006，AC-001/AC-002/AC-003/AC-006。所有共享材料读写使用普通容器，删除旧余额写源及结果分叉，完成 A21 合法存取和已有玩法接入。
- Dependency: TASK-002 的 SharedMaterial 内部种类 240、业务槽 0..10、Game Main/354..364；`GetStore(ContainerOwner, BusinessInventoryStoreKind)`；`PlacementForAsync(itemId,destinationSpace,ct)`；共享单槽发放容量不足整笔 NoRoom；迁移只负责旧数据。
- Owned boundary: 所有必要 Application/Protocol/Abstractions/Data 接入与 DI 清理，旧 Persistence 材料 store 删除；对应测试及 helper。允许修复经验证的底层遗漏，不另建共享材料结算模型。
- Checks: Application/Protocol/Persistence 编译与材料、金库、effect、邮件、任务/教程奖励、入场/重放相关测试；完成后整体项目回归留 T-004 统筹。先为新可观察行为保留有效 RED；接口替换用原有行为回归。
- Restrictions: 独占生产写入与 dotnet；不提交、不部署、不动线上库、不改父级执行工件。保留用户已有教程/金库实机验收事实，新行为不标 ClientVerified。

## Detailed Plan

1. 统一接口：GrantAsync 返回现有 `BusinessItemAdditionResult`，删除 ItemGrantOutcome；ContainerAddition 只保留 Items；MaterialCost 直接携带 BusinessSlot，MaterialDebit 保留 RemainingCore 而删除 AccountChange。删除 IAccountMaterialBag/store/state、AccountMaterialChange 和全部专用 effect/handler/DI。Catalog/Kind 只作物品、固定槽规则映射。
2. 统一发放与整笔 Add：默认放置空间决定账号或角色所有者；离线发放可读账号容器；在线注册容器必须验证实际 owner/type。保留事务、实际落点、完整数量循环，不能将单步部分放置当整笔成功。CharacterContainerAddition 不得反向注入依赖 registry 的 inventories 造成 DI 环；复用底层 Persistent/Business 容器构造即可。
3. 效果、装备及消费全部使用既有 ConsumeFromContainerEffect/ContainerConsumptionChange、GrantStackableEffect/StackableGrantEffectChange/InventorySlotEffectChange。预演 standing 统一按业务槽读取；删除余额缓存及专用 reward 计数。所有候选规则排除 SharedMaterial 的 Quick fallback。消费保持整批校验后写入、会话鉴权、重复槽拒绝及原本 carried+Shared 来源，不扩到金库。
4. Recipe/Opening 按真实业务槽收集成本、生成消费和刷新最终结果；共享材料不在 Main.Items，不能按旧角色快照查空。保留现有 recipe 产物资格与邮件显式材料附件选择限制；邮件无指定槽的材料来源/领取/系统入口统一读写真实账号容器，保留物品交易、绑定和锁规则。整笔邮件失败必须与费用和领取状态共同回滚。
5. CargoTransfer 显式支持 SharedMaterial，校验账号归属与匹配 storage type；目的金库 PVF 准入不放宽。自动存入 StackLimit==1 用目的空间 placement。11 种材料双向存取、拆分/合并、错误固定槽及上限正常拒绝，数量守恒；已知材料槽不抛未映射异常。
6. 协议归一化 ItemGrantWireProjection：按真实 Container+local slot 生成 GameSlot 和最终更新；GroundPickup、CardReward（含黑钻）、MonsterDeath/ObjectDestruction、AcceptQuest、FinishQuest、Tutorial、DeleteItem、装备、Compound、Mailbox 全部替换旧材料结果。保留 ACK 增量、绝对校正与私有投递顺序；普通拾取不用多发帧。耗尽材料不能发正 itemId/0 的获得物品通知，已清槽 ACK 后省略零值校正；无 ACK 的绝对清除走既有 ClearedSlotNotification。
7. 入场 11 项 VirtualResources 可保留为线级形状，但真源只读 SharedMaterial 容器；不要重复并入 Main.Items。Replay touched SharedMaterial 必须检查 account.VirtualResources 或统一投影值，不能在快照后错误清空非零材料。直接任务掉落 GrantLimit 按真实持有容器统计；既有 seeking 任务资格不顺手扩大。
8. 测试 seed 新增统一 helper，按账号/Shared storage kind/slot 通过 store 精确 seed，独立 SQLite 连接 count 保留提交先于 ACK 的检验。原有 UPDATE accounts 材料种子改为 helper；迁移测试的旧列输入保留。不要更改普通 ShopFixture.Seed/Count 的原含义，也不要保留旧 Bag 服务让旧测试通过。
9. 验证：材料发放/消费/分解/丢弃/邮件/任务/教程、混合成本不足/故障回滚、max 附近拒绝不落 Quick、换角色/离线发放、入场和 replay 非零槽、11 材料金库存取及拒绝后守恒。通过编译找齐所有旧类型引用；生产搜索旧账号列仅允许 Schema/迁移。最终写本单元 Changed/Why/Checks/Remaining 交接。

## Research anchors

只读交接 `artifacts/t003-material-effects-mail-audit.md`；协议逐入口审计由本单元执行者先前完成。以当前代码为准，不机械按旧行号替换。最终禁止用“兼容包装”掩盖第二套结算；线级投影特殊性则应保留。


## Result — 2026-10-03

### Changed / Why

- 所有材料发放、整笔 Add、消费、effect、强化/分解、任务/教程、掉落/卡牌、商店、开包/配方与邮件接入普通容器。`GrantAsync` 返回 `BusinessItemAdditionResult`；成本和回执携带真实 `BusinessSlot` / `RemainingCore`；删除旧 Bag/store、余额变更结果和材料专用 effect/命令/DI，避免两套结算。
- 账号共享容器沿用 kind 240、local 0..10；默认入袋与金库目的空间分别取 placement，Shared 不落 Quick。发放与邮件支持显式账号 owner；注册容器验证 owner/type。11 种材料均补个人/账号金库双向存取、错误固定槽和单槽上限拒绝守恒回归。
- 依据父级提供的真实 PVF 调查修正规则与种子：六种晶体归类 11，五种灵魂归类 3。迁移生成 canonical kind；金库准入保留绑定、交易、锁和期限限制。
- 协议只在投影边界保留 Main/354..364 特殊形状；ACK 增量、随后绝对校正和私有投递顺序保持。耗尽消费在 ACK 清槽后省略正 itemId/0 通知；Replay 不再把非零共享槽误清空。入场加载只返回非零真实共享行，维持旧加载快照，固定 11 项由既有线级投影负责。
- 开包 prepared reward 经过默认 placement 归一化并保留声明 expiry；配方的共享材料产物仍按既有资格拒绝。邮件显式材料槽限制与 seeking 任务资格未扩大。
- 测试 seed 改为真实账号 item_slots；独立连接计数继续检验提交先于 ACK。加载 characterization 的预期内容保持，种子从旧账号列迁至真实共享行。生产旧材料列搜索仅剩 Schema 与一次性迁移；旧 Bag/change/outcome/location/effect 类型无生产引用。修复既有架构扫描使用已迁到 Data 的类型作 Application 程序集标记的遗漏，仍扫描五个程序集。

### Checks

以下均为服务端实现/自动化证据，不代表新增 A21 ClientVerified：

| 检查 | 结果 / 日志 |
| --- | --- |
| 公共 Grant 与整笔 Add 读写同一现存共享容器 | 有效 RED：期望 10、实际仍 7；之后绿色，`artifacts/t003-shared-entry-red.log` / `t003-boundaries.log` |
| Prepared 开包奖励落真实共享槽且保留 expiry | 有效 RED：普通角色槽意外多出 2；修复后绿色，`artifacts/t003-prepared-red.log` / `t003-prepared-green.log` |
| 材料/effect/强化/分解/配方/邮件/任务/快照聚焦组 | 266 总计，253 通过、13 跳过，`artifacts/t003-focused.log` |
| Protocol 全项目（当时状态） | 1239 总计，1180 通过、59 跳过，`artifacts/t003-protocol-tests.log` |
| 新边界与 44 个金库存取/拒绝用例、共享容器、架构、邮件 | 94/94 通过，`artifacts/t003-boundaries.log` |
| 开包/配方/NPC/CERA 聚焦组 | 134 总计，91 通过、42 跳过、1 环境依赖失败：CERA product 900003 缺 Script.pvf；`artifacts/t003-prepared-green.log` |
| Persistence inventory 初轮 | 162 总计，160 通过、2 失败；两处为 canonical kind 预期和旧余额 characterization 种子，已修正，`artifacts/t003-persistence.log` |
| Persistence migration + characterization 最终补跑 | 11/11 通过，保留原 characterization 非零值与顺序；`artifacts/t003-persistence-final.log` |
| Replay 新边界及分解/强化/合成协议最终补跑 | 36/36 通过，`artifacts/t003-protocol-final.log` |
| 最终 solution build | 0 warning / 0 error，`artifacts/t003-final-build.log` |
| 最终 whitespace 检查 | `git diff --check` 通过；恢复原有已跟踪文件行尾/BOM，减少全文件噪声 |

### Remaining / Handoff

- T-003 实现与本单元相关验证完成；释放生产写入与 dotnet 独占权给父级。没有运行中的测试，没有提交、部署或修改线上数据库。
- T-004 由父级使用 `E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf` 执行真实 PVF、全量回归、独立审查、整体文档与本地提交。上表是分阶段检查，不能相加冒充一次最终全量通过；CERA 缺 PVF 的失败留该阶段复核。
- 已有用户实机事实继续保留；新增 11 种材料存取、开包与 Replay 行为只具备本次自动化证据，等待集中 Human Test List 验收。
