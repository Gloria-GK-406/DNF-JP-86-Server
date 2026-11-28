---
kind: rks-review
verdict: approved
---

# 玩法权益归拢完整改动评审

评审目标为 `baa1fe099`，产品基线为 `f4a413bcf`。依据任务契约第 1 版、规格、实施计划与仓库的架构及数据归属约定，检查整体改动，重点复核首轮发现、相邻调用及验收结果。

## 当前裁决

- v49 → v50 新建独立时效表，按角色及道具存储绝对截止时间。旧表和仓储接口相对基线保持一致，入场按原值输出历史效果，并用时效表计算新增记录的剩余秒。同道具已有历史投影时保留历史项，服务端仍从时效表结算。迁移测试确认同道具旧值 180 保留，新增表初始为空。真实 PVF 用例确认旧值 77 透传，服药与扣除一并回滚或提交，重登后用同一掷点验证加成，到期后恢复原概率。[迁移测试](../../../../Tool/DbMigrate.Tests/SchemaVersionTests.cs#L9) · [强化药测试](../../../../Server/tests/Dfo.Server.Application.Tests/Inventory/EquipmentReinforcementDomainTests.cs#L19) · [入场投影](../../../../Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs#L295)
- 强化权益通过既有物品使用规则取得加成，普通强化在原成功判定中读取时效权益。独立权益相加，互斥分组取最大值，再按概率上限约束。强化券、增幅和勋章各自判定。[强化药声明](../../../../Server/src/Dfo.Server.Application/Inventory/ReinforcementPotionBenefits.cs#L10)
- 注册表按来源和范围整批替换，拒绝旧版本并对当前权益稳定排序。队伍共享神器沿用原随机序列，平均额外 1.5 轮，产物仍为共享地面掉落。角色经验、全服掉率、成长契约 Boss 掉落及任务助手的可用加成均可追踪到对应权益入口。复扫记录列出了基础规则、宠物成长和现有掉落调整扩展点。[完成后复扫](../verification/benefit-audit.md)
- 已核对当前目标的测试结果。应用领域套件 376、协议 209、持久化 59、迁移 74，均无失败。非空 `0x00AE` 的客户端显示时机仍待 A21 实机核对。

## 首轮发现

目标 `4b07b8c99` 的裁决为 `changes_required`。道具期限写入旧表的效果数值列，可能抹掉存量效果。强化声明和入场投影各自通过原始 PVF 定义猜测该列的含义。两项均已在当前目标修正。
