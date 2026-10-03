---
kind: rks-review
verdict: approved
---

# 统一物品容器与共享材料：完整结果独立审查

审查目标为 `task/warehouse-storage` 工作树相对基线 `fd14e7172b0b6a1b39d194b342e4421d470e69bf` 的完整未提交结果，包含生产代码、测试、协议记录、说明文档及任务工件。审查依据为本目录 `TaskContract.md` revision 1、`spec.md`、`plan.md`，并采用 `reviewing-work` 的 Code 与 Deliverable 准则。`artifacts/unify-item-containers/final-source-manifest.json` 所列 143 个 Server 路径（98 个生产路径、45 个测试路径）已逐项重新计算 SHA-256，当前结果为 143/143 匹配。

本轮未发现需要修改的实质缺陷，批准当前代码与交付物通过独立结果评审。该判定不替代主控的最终完成核验、本地结果提交或客户端实机验收。

## 要求与实现核对

| 要求 | 审查结论与直接依据 |
|---|---|
| RQ-001 / AC-001：统一身份、所有权与隔离 | `ContainerId` 能解析角色/账号所有者及容器种类；持久化允许角色的 Main、Avatar、PersonalCargo、Equipment、Pet，以及账号的 AccountCargo、SharedMaterial。账号槽唯一约束包含容器种类，账号金库查询固定为种类 12，不会和共享材料种类 240 同槽混淆。在线角色经已登记账号容器访问共享空间，离线发放、邮件等路径显式解析账号所有者；越权和不同账号隔离有容器及重载测试覆盖。 |
| RQ-002 / AC-002：11 种材料成为普通物品 | `AccountMaterialCatalog` 定义 6 种晶体和 5 种品级灵魂、固定局部槽 0..10；共享容器保存普通 `InventoryItemCoreState` 和 `item_slots` 记录，数量上限为 `int.MaxValue`。默认发放按目录落到固定共享槽，但材料进入个人/账号金库后仍按目标容器的普通 PVF 准入与堆叠规则处理。旧 `IAccountMaterialBag`、余额 store、专用 effect/result 与 DI 已删除；生产代码对旧余额列的引用只剩 Schema 声明和一次性迁移读取。 |
| RQ-003 / AC-003：材料存取与守恒 | Main/354..364 在协议边界映射为 SharedMaterial/0..10，内部种类 240 不暴露为 wire list。`ItemMovementDomainService` 对移动两端分别执行目标准入、数量、容量和反向交换检查；非堆叠实例仍使用 `PutTransferredAsync` 保留 UID。`SharedMaterialPvfTests` 使用真实 A21 PVF 覆盖全部 11 种材料在个人金库和账号金库的拆分、合并、取回；满仓、错误槽和故障回滚主要由既有合成规则、领域及协议回归覆盖。普通容器中的合法材料实例没有被全局禁止，符合计划中“按目标容器规则处理”的边界。 |
| RQ-004 / AC-004：旧数据迁移 | `SharedMaterialMigration` 在独立 SQLite 事务内先检测任何已有共享容器或旧材料实体冲突，再读取旧账号余额；逐账号、逐材料验证非负且可容纳于 `int`，写入普通 item/item_slot 后才记录 `schema_migrations` 标记。异常回滚整次迁移，重复运行由标记保证不增发；冲突数据明确拒绝而不猜测相加。迁移测试覆盖数量保持、合法高数量、幂等、冲突和失败回滚。 |
| RQ-005 / AC-005：既有容器与实例行为 | 角色背包、穿戴、时装、宠物、个人金库和账号金库仍通过相同业务容器接口与旧存储坐标边界工作。账号金库与共享材料分别读取自己的容器种类。既有移动、仓库存取、UID、附加事实和重载回归均纳入最终全项目测试，未见共享材料改造向其他容器泄漏固定槽或无限堆叠规则。 |
| RQ-006 / AC-006：生产、消费及通知闭环 | 通用 Grant、Consume 和 effect 现在携带真实 `BusinessSlot`，发放、技能/特效消费、强化、分解、制作、开包、NPC/CERA 商店、丢弃、任务/教程/副本奖励、拾取和邮件均落到容器状态。预检与应用读取相同容器，完整数量通过循环放置/消费，事务异常由现有结算边界回滚。邮件的共享材料读写显式使用账号所有者，领取后的投影重新读取最终容器。强化/分解/开包等回执从 effect 最终槽生成，不再构造余额分支。 |
| RQ-006 通知细节：ACK、绝对值、0 与 replay | Main/354..364 的非零材料增量使用实际共享容器 core 投影绝对值。消费/丢弃 ACK 已让客户端扣除最后一份时，不再补发正 itemId/count=0，避免“获得 0 个”提示；没有消费 ACK 的清除仍发空槽通知。replay 的空槽判断读取 `Account.VirtualResources`，不会在完整快照后误清除非零共享材料。强化、分解、邮件、任务及开包的已触及槽均按最终值或清除状态投递。 |
| RQ-007 / AC-007：文档与证据边界 | `Document/AccountMaterialBag.md` 已说明旧余额模型被替代；`Document/SharedMaterialHumanTest-20261003.md` 集中列出存取、换角色、重登、数量刷新和拒绝场景。协议 0004、0013、000D、000E 及 catalog 的更新区分 CurrentImplementation、既有 ClientVerified 与本次尚待实机的效果，没有把自动测试升级为 WireCaptured 或 ClientVerified。 |

## 验证证据判读

审查者未运行 `dotnet`，独立读取了控制器保存的 TRX、汇总和稳定源清单。最终证据为：Application 2805/2805、Persistence 406/406、Protocol 1246/1246、Infrastructure 在真实 PVF 环境 766/768（2 项因归档存在而按设计未执行）、Networking 86/86、Host 118/118；两项“归档不存在”场景在去除 PVF 环境后定向运行 2/2 通过。按互斥环境合并为 **5429 个独立场景通过、0 失败**。

证据目录仍保留修复前的 `Persistence.trx`（404/406）和 `Host.trx`（115/118）。前者暴露新增 `tutorial_completed` 列及 `schema_migrations` 表计数未同步；后者暴露 GM 邮件测试内容把晶体虚构为 kind 10，以及断言仍读取旧 `cube_clear` 余额。当前 143 路径清单已包含对应测试修正，`Persistence-final.trx` 与 `Host-final.trx` 分别全通过；历史失败没有被改写成首次全绿。`git diff --check fd14e7172...` 无输出。

## 保留边界

本次没有重启或部署现有运行服务、操作生产数据库或运行客户端；Host 测试只启动了隔离测试 Host。共享材料的新普通容器模型仍需按集中清单完成 A21 客户端的存取、换角、重登和刷新验收，因此不能据此将新模型标记为 `ClientVerified`。邮件附件整体容器化、未完成物品玩法、钱包物品化和分布式事务均在合同范围外，本评审未将它们作为当前交付的缺陷或完成条件。

## 提交前格式归一化复核

提交前仅对 9 个新增 Server 文件和 `sdd-execution.yaml` 做了换行与文件尾归一化。审查者依据 `eol-normalization.json` 在内存中把当前文本追加记录的末尾 LF，再按 `originalNewline` 恢复 LF/CRLF；10/10 均重建出记录的原 SHA-256。9 个 Server 文件的原哈希同时与 `review-source-manifest.json` 匹配，当前哈希与 `final-source-manifest.json` 匹配；后者全部 143 个路径也重新计算为 143/143 匹配。该变化只涉及换行和多余文件尾空行，不改变已审查语义，`verdict: approved` 保持不变；未因此重跑测试或重开代码审查。`git diff --cached --check` 通过。
