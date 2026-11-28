---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# A21 加百利秘密商店实现方案

基线为 `fe6c55321045f7099f14821759ac1ee625b8b43f`。清场、货单与资产消费可在一套详细计划中按依赖顺序完成。

## 实现顺序

1. **T-001 内容与规则（RQ-001）。** Infrastructure 用 Dfo.Pvf 的令牌区段读取 `etc/secretshop.etc` 的副本权重、等级段、人数权重及普通池。Application 规则按 `DungeonLayout.DungeonId`、`BasisLevel` 和入场人数抽一次 NPC。分角色按对应池权重抽条目，每个物品 ID 在货单中只有一行。解析缺失走现有德利拉分支。用真实 dev PVF 和确定性规则输入验证。
2. **T-002 副本和清场投影（RQ-001、RQ-002、RQ-004）。** 现有 `DungeonRunStore` 在成功清场后记录当局 NPC 与按角色分配的货单，查询和购买扣库存都通过所属 run。普通布局按 PVF 准入，特殊布局沿用既有清场投影。清场结果提供冻结的个人商品快照，Protocol 在 `0x001F` 和 `0x0117` 后发 `0x0118`。离场使用原副本成员状态，清场重复不重新抽货。断线、新场次和已离开成员不可买旧货单。
3. **T-003 交易与回包（RQ-003、RQ-004）。** `0x0129` 按单字节开关上报处理。 `0x0128` 进入现有精确会话效果命令主工作单元。加百利按运行时货单报价，复用普通 NPC 的容器、支付、落位和发放计划。扣库存由副本 owner 登记为同一个领域事务的内存参与者，准备阶段复核，提交阶段生效。成功后按参照服 ACK 与绝对背包／金币状态投影，重复效果帧用仓库的当前状态重放。失败映射以已知客户端码和参照服默认拒绝为依据。
4. **T-004 验证与交付（RQ-001 至 RQ-005）。** 增加清场、购买、回滚、重复和离图回归，覆盖协议注册及结构检查。使用隔离测试配置构建候选并整理当前 PVF、启动参数、测试顺序与待抓包事项。更新协议页及 catalog，分别写明 ClientStatic、CurrentImplementation 和用户验收状态。

## 证据

- ClientStatic 以 `0x0117`、`0x0118`、`0x0128`、`0x0129` 协议记录和 A21 b1 地址为准。WireCaptured 已有德利拉分支，ClientVerified 加百利仍待用户测试。
- 参照服 `1870c85` 的 `Game/SecretShop`、`SecretShopClearPacketBuilder` 和 `SecretShopHandler` 支持候选约定。
- 70Reference 在工作区 `Research/1995chen-dnf/other/等级补丁/70/df_game_r` 的符号表看到 `CParty::GetSecretShopData`、`IBuyRule::CheckLimit`、金币与配方购买处理，以及购买和开关分发器。仅佐证早期业务分类，A21 行为仍以本版证据为准。
- 工作区调查文档 `E:/Project/DNF/Document/A21-加百利秘密商店-初步调查.md` 汇总了 PVF 版本、配置实例和日服公告。
