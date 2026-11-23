---
kind: rks-verification
decision: pass
---

# 四副职业服务端交付验证

本结论覆盖第 3 版契约的服务端实现、开发期自动验证和实机验收准备。使用独立工作树、临时 SQLite 和当前只读 dev PVF，比较基线为 `cada01a0e`。 [任务契约](../TaskContract.md) · [独立评审](../reviews/change-review.md) · [构建和测试](../../../BuildAndTest.md)

| 契约 | 开发验证证据 |
| --- | --- |
| SC-1、SC-2 | 四个真实 PVF 就职任务经正常的接取和领奖命令授职，检查技能两页、职业状态、专属服务、设备初值与在线刷新。覆盖放弃、费用、后续任务清除及重新接取。晚期 SQLite 写失败回滚材料、任务、职业和技能。旧档状态及放弃关摊由持久化和生命周期实现复核。 [正常入口用例](../../../../Server/tests/Dfo.Server.Application.Tests/Quests/ExpertJobProtocolFlowTests.cs) · [实现记录](../tasks/TASK-01.md) |
| SC-3 | 双人分解覆盖顾客装备和产物、双方金币、摊主经验、耐久、耗尽关摊、晚期失败回滚和重开后的旧访客拒绝。NPC 和便携分解保留原入口，维护与升级表和结算路径已复核。 [双人用例](../../../../Server/tests/Dfo.Server.Application.Tests/Inventory/ExpertStoreProtocolFlowTests.cs) · [实现记录](../tasks/TASK-03.md) |
| SC-4 | 炼金和控偶的批量制造覆盖尝试数、扣料、金币、产物、经验和跨级解锁。真实后续任务可接取，三职的提取配置和保留提取器的实现已复核。 [生产用例](../../../../Server/tests/Dfo.Server.Application.Tests/Inventory/ExpertJobCraftProtocolTests.cs) · [实现记录](../tasks/TASK-04.md) |
| SC-5 | 最高档真实附魔图纸验证职业经验门槛。付费附魔的成功和失败覆盖卡、金币、装备写入、经验、耐久及关摊。普通宝珠保留目标档位，可识别的职业宝珠覆盖 0 档和非零档。制珠和维修读表与结算路径已复核。 [宝珠用例](../../../../Server/tests/Dfo.Server.Application.Tests/Inventory/EquipmentEnchantDomainTests.cs) · [实现记录](../tasks/TASK-05.md) |
| SC-6 | 完整解决方案构建、协议注册、完整 Protocol 回归、隔离资产集成测试及 Host TCP 引导。协议页记录来源和证据边界。 [测试汇总](../tasks/TASK-06.md) · [协议清单](../../../../protocol/catalog.md) |

## 最终回归

以提交 `1ec580af8` 对应的源码执行整套构建，0 警告、0 错误。使用 `dotnet test <项目> --no-build --no-restore` 运行所列项目，PVF 取 dev 通道的 `Script.pvf`。

| 范围 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| Application 专项及周边回归 | 170 | 0 | 0 |
| Protocol 全量 | 2,390 | 0 | 0 |
| Host 生产接线与打包 TCP 引导 | 14 | 0 | 0 |
| Persistence 私人商店与物品存档 | 33 | 0 | 0 |
| Infrastructure PVF 分解、附魔与任务读取 | 58 | 1 | 1 |
| Application 全量 | 7,017 | 41 | 8 |
| 同 PVF 的 Application 基线全量 | 7,004 | 41 | 8 |

Application 全量的 41 项失败在原主线基线已存在。逐条对照了此次受影响的状态写入审查、组队入口分类、结算登记和选角夹具。新增 13 例全部通过。Infrastructure 的失败是原任务数量快照预期 3,295、实际 3,255，在同 PVF 的未改动基线复现。该筛选另跳过了仅在缺少 PVF 时才执行的用例。 [验证命令与记录](../tasks/TASK-06.md)

## A21 实机阶段

计划中的 A21 玩家端验收尚未执行。关摊包尾、就职和放弃的刷新顺序、附魔随机失败的界面反馈及提取器制作后的界面重扫，均保留为实机取证项。 [A21 验收顺序](../A21验收顺序.md)
