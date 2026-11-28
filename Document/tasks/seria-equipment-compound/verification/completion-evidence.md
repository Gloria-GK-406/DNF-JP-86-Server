---
kind: rks-verification
decision: pass
---

# 赛利亚物品合成候选验证

交付范围为可连接、可供用户实测的服务端候选。功能提交 `67cb3f931b0f1a122e2148cdbd8152e1056c9092` 已通过[独立审查](../reviews/change-review.md)。

| 契约条件 | 结果 |
|---|---|
| SC-1 | [请求](../../../../protocol/game/client-to-server/0x0197.md)、[响应](../../../../protocol/game/server-to-client/CMD-0x0197.md)及[证据](../evidence.md)分别记录了客户端静态、PVF、上游和 70 参考。 |
| SC-2 | 真实 dev PVF 的四档配方、两种模式倍率、传承产物和同级／升 5 级池通过验证。随机模式的 part 16 通过实际解码及结算测试，定向继续按 PVF 校验。 |
| SC-3、SC-4 | SQLite 测试验证原物品销毁、新 UID、角色与账号材料数量、同槽清除再填充、回滚、原帧重放及后续合法请求。八字节、截断、无效模式、空池、装备锁和缺料均未写入部分资产。 |
| SC-5 | 候选 Release Host 已监听 `55201/55211/55212/55213/55280`，HTTP gate 返回 `running` 和正确 PVF 摘要。用任务账号完成取票、Game 登录、选角和六种预置模板 INVENTORY_LIST 下发校验。实际 A21 GUI 测试由用户执行。见[实机清单](../Human-Test-List.md)。 |

## 执行记录

- 端点注册用例在基线由 `Assert.NotNull(decoder)` 失败，增加切片后通过。命令为 `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --filter FullyQualifiedName~SeriaEquipmentCompoundRegistrationTests`。独立复审另验证了两个 Networking 用例。
- 在 dev PVF 下运行 Application 的 `SeriaEquipmentCompoundTests|EffectSettlementRegistrationTests|EpicCraftTests|InventorySlotBoundaryArchitectureTests`，33 通过，退出 0。
- Protocol.Tests 全量 2607 通过，1 失败。选角快照 `AcceptedEntryTranscript_StaticBytesExceptReferenceOnlyTitleExtensionMatchAcceptedBaseline` 的第 32 帧在本分支和独立导出的 `818cbeed7` 都得到相同差异，期望 `5FC962E0…`、实际 `390445D4…`。
- `python -B Tool/ProjectReferences/check.py` 通过，检查 13 个生产项目及 32 项直接引用。Release Host 发布通过。
- 用户授权在 DNF 内就地选择可用 SQLite。采用工作区默认发布 Host 下历史已用于玩家的本地库。源只读，通过 SQLite backup API 取得一致副本，原始复制时为 4 账号、9 角色、687 物品及 679 槽位。副本完整性检查通过。仅副本运行 `DbMigrate`，从 schema 0 升至 49 后启动。凭据、数据库和运行日志留在本地忽略目录。
- `ClientStore client verify Client-03` 通过。该 trace 客户端来自 dev 通道，启动器连接 `http://127.0.0.1:55280/`。下发已验证 `100050238, 10165, 10100115, 10100116, 10099773, 3037`，素材保留供用户实测。
- 变基到主干 `bfbece062` 后，Release 解决方案构建通过，0 警告、0 错误。上列 33 项 Application 测试及 1 项 Networking 合成注册测试均通过，项目引用检查通过。变基冲突只涉及注册测试的预期协议 ID 清单。

## A21 反馈

2026-11-28，用户在 A21 合成窗口中放入普通上衣，观察到左侧三个产物图标。核对候选的含义和测试服的加权抽取规则后，用户确认实机试用符合预期，同意进入 PR 收尾。

上述界面行为记为有限范围 `ClientVerified`。反馈未逐项确认候选列表与服务端抽取池完全一致、各模式的结算与扣料、成功和失败提示、封装状态或重登后的显示。扣料、持久化、失败保护与重放由自动测试覆盖，官方日服的传承出率仍待验证。
