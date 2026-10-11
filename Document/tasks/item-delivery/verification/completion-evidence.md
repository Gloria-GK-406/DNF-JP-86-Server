---
kind: rks-verification
decision: pass
contract: ../TaskContract.md
contract_revision: 1
---

# 完成验证：统一奖励交付

结论范围：本次实现、系统物理奖励迁移完整性及自动化回归达到 TaskContract SC-1..6；**不宣称全测试套件零失败，不宣称 ClientVerified**。未部署，未启动正式服务，未修改运行数据库、1123或傻瓜包。

工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/despair-tower`；分支 `task/despair-tower`；改动前基线 `4aec17baa083271f322012043511f9f2845112db`。证据采集日期：2026-10-11。最终证据对应本地结果提交中的代码；提交号由 Git 历史记录，避免在提交内容中自引用。

## 新鲜结果

| 检查 | 最终结果 | 证据 |
|---|---|---|
| `dotnet build Server/86JP.Server.slnx --no-restore -v:q` | exit0，0警告、0错误 | 当前完整解决方案构建 |
| Application 项目全测 | 3652通过、3失败、0跳过，总3655 | `artifacts/item-delivery-tests/item-delivery-application.trx` |
| Protocol 项目全测 | 1443通过、0失败、0跳过 | `artifacts/item-delivery-tests/item-delivery-protocol.trx` |
| Persistence 项目全测 | 468通过、0失败、0跳过 | `artifacts/item-delivery-tests/item-delivery-persistence.trx` |
| HostCompositionArchitectureTests | 1通过、0失败 | `artifacts/item-delivery-tests/host-composition.trx` |
| 奖励反向扫描 | exit0；710源码、64命中文件、0未分类 | [扫描表](../migration-scan.md)、[可复跑脚本](../tools/scan-reward-routes.py) |
| 独立审查 | round2 approved，无未解决阻断项 | [保留两轮证据的审查记录](../reviews/change-review.md) |
| `git diff --check` | exit0 | 当前待提交改动 |

表中 `artifacts/` 为 `E:/Project/DNF/DNF-JP-86-Server/artifacts/`，本机忽略产物，不纳入提交。三个项目依次执行，避免共享输出目录并行编译。PVF环境变量：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf`。

复跑各项目命令：`dotnet test Server/tests/Dfo.Server.{Application|Protocol|Persistence}.Tests --no-restore -v:q`，并设置上述 PVF 环境变量。扫描脚本可从任意当前目录运行。

## 失败对照与限制

最终全测仍失败的三项为：

- `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`
- `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`
- `WorldContentArchiveTests.Historical_mirror_rooms_override_old_absolute_levels_without_mutating_PVF(dungeonId: 60)`

前两项均为地下城7100迷宫0的(1,0)没有兼容左入口地图；后一项为地下城60迷宫4存在占用格之外的地图声明。将 exact baseline 的 `Server` 和 `global.json` 单独导出、使用相同PVF执行对应范围，得到相同三项失败（另15通过，总18），证据 `artifacts/item-delivery-baseline/baseline.trx`。这些文件未在本任务修改，因此认定为既有内容/测试环境失败；没有通过忽略或修改断言掩盖。

中间广域运行还出现过一次既有 `SelectCharacterLifecycleTests.ObserverQueuedBeforeRollbackClose_CompletesAfterGateRelease` 的2秒等待超时；隔离重跑该类29项全过（`lifecycle-recheck.trx`），最终广域运行也通过。另一次中间运行出现测试fixture删除SQLite文件时占用异常，后续运行未再出现。保留为测试运行稳定性限制，不对登录或奖励生产代码做无证据修补。

## 成功条件对应

| 条件 | 证明范围 |
|---|---|
| SC-1 | 全生产来源与底层写入双向扫描、逐入口矩阵及独立遗漏复查；包括未修改的称号簿/显式GM邮件真实制造者，明确其专属语义例外 |
| SC-2 | 真实DI `ItemDeliveryTests`：普通材料8+7入2寄5；共享材料int.MaxValue-2+5入2寄3；23装备满包拆3邮件，数量及UID无重复 |
| SC-3 | 同测试真实SQLite保存/领取装备、装扮、宠物，UID及完整core/details一致；真实邮件handler投影编码覆盖avatar/creature分支及宠物运行态 |
| SC-4 | 邮件写入失败回滚背包和来源金币、后续无效物品使整批回滚；既有翻牌/礼包/双倍罐/任务/塔幂等和支付回归通过 |
| SC-5 | 真实Mailbox/礼包投影保留类型扩展，罐子及售货机全邮件ACK可编码，塔邮寄奖励展示seed等于实际实例；现有提交后邮件EventBus通知保留 |
| SC-6 | 任务/教程/礼包满包不再被旧预检查拒绝；制作显式InventoryOnly，满包邮件领取保留原附件；每日补充真实DI跨天计入未领邮件，指定栏位和堆叠限制不外溢 |

`ItemDeliveryTests` 最终12项均在Application广域运行中通过。老命令fixture的 `BagRewardDelivery` 仅用于来源流程回归，不能当作真实邮件持久化证据；上表SC-2..4由真实DI/SQLite新增测试证明。

实机奖励展示、邮件提示、装扮/宠物领取仍需部署后验证，建议顺序见 [结果及测试清单](../spec-result.md)。这是后续客户端验收边界，不在本次“实现但不部署”的完成声明内。
