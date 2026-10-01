# spec-005 实施结果（2026-09-30）

## Changed

- PVF 内容源读取 `etc/upgrade_separate.etc` 的上限、概率、材料 grade 表与 rarity 权重；规则用可控 roll 和真实样本计算成功/失败及消耗。
- 角色/槽/装备/材料校验后在命令事务中扣料并有条件写回原 UID 的 `GenuineUpgrade`。概率失败是已结算尝试，扣料但保留等级；业务拒绝与提交异常不扣。金币、普通强化及其它 core 字段不改。
- `0x01B7` 普通 variant0 的有界动态尾解码、结果 ACK、同连接重放和提交后目标/材料权威刷新；variant1/2 不进入事务。协议人工区、目录与 Human Test List 保留 A21 未实测边界。

## Why

PVF `upgrade_separate.etc` 没有金币费用；普通锻造不从普通强化表推断金币。动态名字不是资产身份，真正身份由当前角色槽位、模板 ID 与 PVF 材料校验。当前仅 70/上游/A21 静态交集支持普通 variant0；卷券变体先隔离。

## Checks

- TDD 规则 RED：`EquipmentForgingRulesTests` 4 失败；GREEN：4/4 通过。
- 真实 PVF RED：`PvfEquipmentForgingContentSourceTests` 1 失败；GREEN：在明确设置 `PVF_ARCHIVE_PATH` 下 1/1 通过。
- 领域 RED 首次因测试桩构造参数未使用被 TreatWarningsAsErrors 阻挡；修正桩后 10 个行为断言失败。GREEN：领域+规则 14/14 通过。
- 字节 RED：`EquipmentForgingWireTests` 3 失败；GREEN：3/3 通过。
- 复审前定向 Application 19/19、Protocol 字节 3/3、Networking 真实 TCP 1/1；复审后补规则服务分层与错槽资产不变测试，锻造定向 Application 21/21。完整回归、审查结论与已知基线失败见 `verification/completion-evidence.md` 和 `reviews/change-review.md`。

## Remaining

客户端实帧/GUI/重登由集中人测完成；卷券变体仍隔离，不称 `ClientVerified`。
