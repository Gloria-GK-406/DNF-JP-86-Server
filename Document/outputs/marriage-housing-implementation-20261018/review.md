---
kind: rks-review
verdict: approved
---

# 婚房完整实现独立代码审核

日期：2026-10-18。审核目标：`255bbada8501d7e3a330072b91f2f5f306e01905` 之上的当前婚房工作树，包括新增 Server 文件及审核过程中修复；并非只检查 tracked diff。审核者未修改生产代码、未启动客户端/生产服务、未操作 live DB。

依据：仓库 `AGENTS.md`、`Document/MarriageHousing-FinalDesign-20261018.md`、`wire/README.md`，以及 `reviewing-work/SKILL.md` 的 `references/code.md`。流程为 Direct，没有额外制造 Spec/Plan 审批条件。既存 Opus 报告、旧 outputs、缓存文件不属于交付差异。

结论：当前审核范围内没有尚未解决的实质阻塞。该结论覆盖代码及下述自动化证据，不建立 A21 实机、完整玩法验收或部署成功结论。

## 已发现并复核修复的问题

| 问题 | 原代码的可复现后果 | 当前修复与证据 |
|---|---|---|
| P1：婚房 CMD 回包缺通用结果信封；03F4 业务失败缺等级尾字节 | 打开保管箱成功首先被接收层当成 `ok=0`，原生窗口不打开；无条件读取 status 的树协议读取不足。03FF 空正文同样不满足通用接收层。 | `Server/src/Dfo.Server.Protocol/Game/MarriageHousing/HousingSlice.cs:69` 起统一发送 accepted=1；03FD/03F4 包含完整 status+value。与 `GameCmdEnvelope.cs`、原生 00880B60/00892FF0 的消费路径核对；codec 13/13。 |
| P1：正常业务拒绝使用 rollback-only 返回，与请求结算 Freeze 不兼容 | 背包满购买戒指券时，点数/金币写后返回 NoRoom，但外层把缺 Freeze 当持久化结果不确定，封锁当前会话，无法正常发购买失败。 | `HousingOperations.Shop.cs` 改抛 `NpcShopPurchaseRejectedException`，由事务确认回滚后 `BuyItemHandler` 映射原生失败；戒指 CAS 失败同样经 `HousingRejectedException`。真实 dispatcher 用例验证金币/点数回滚及后续命令仍可执行。 |
| P1：新婚当次会话未创建婚房 | 婚礼完成后 `InstanceAsync` 返回 null，入房 CMD0024 被拒；必须重选角色才可进入。 | `Server/src/Dfo.Server.Application/Marriage/MarriageDomainService.Venue.cs:101` 在成功婚礼同事务 Ensure 房间；测试在任何 Initialize/重选角之前断言当前会话已得到有效 Instance。 |
| P2：两处队伍投影丢失私房 Instance | 婚房内一方创建/修改/离开队伍，旁观配偶的队伍目录/外观刷新找不到成员。 | `PartyTownProjectionDelivery.SessionsInScope` 和 `PartyNotificationRecipientCapture.Capture` 均保留 `scope.Instance`。 |
| 修复过程中的住房购买包装 DI 回归 | 注入未注册的 `BuyItemCommandHandler` 导致真实 dispatcher 无法解析住房购买命令，并违反命令处理器依赖约束。 | 同一 `BuyItemCommandHandler` 实现普通与住房两种命令接口；无 handler 注入 handler、无新豁免。真实 dispatcher 与架构回归均通过。 |

## 关键不变量检查

- 个人点数使用有版本标记的一次迁移，夫妻分配与审计保存；初始化完成后旧婚礼余额不再作为回填源。日奖励有角色/周期唯一键，余额范围 0—999；消费修改个人余额。
- 共享写命令获取双方排序账号锁，进入领域后重新核对精确会话、婚姻阶段和账号集合；普通 NPC 购买未被强行纳入住房专用重放身份。新增 ReplayProjectionNeeds 已进入 Validate 白名单。
- 婚房场景键包括 channel、town、area、持久房间 Instance；town101 的 Instance0 被拒绝。入房以当前婚姻对应的房间解析实例，家具操作仍检查当前角色位于该实例。编辑租约在事务提交后生效，释放检查精确会话。
- 家具购买、钱包/金币变化、种子/水壶消耗和戒指更新归同一命令事务。婚房位置不覆盖持久普通返回位置。家具槽位、允许布局、窗帘/窗户互斥与数量均在服务端检查。
- 树收获按 planting_id/harvest_round 唯一记录奖励，双方邮件与轮次/成长更新使用同一事务；收获保留种植到期日和当日用水计数。邮件是实际资产投递，03DE 仅展示。
- DI 通过真实 dispatcher 和架构测试覆盖；读投影使用独立 HousingReadView，未引入婚房业务服务与选角/婚姻查询的显式构造循环。

## 验证证据

独立审核实际执行：

- [Application 筛选日志](review-Dfo.Server.Application.Tests.log)：55/55，含婚房、两条真实 dispatcher、重放及架构测试，真实 PVF。
- [Protocol 筛选日志](review-Dfo.Server.Protocol.Tests.log)：13/13，含 accepted 信封、03F4 失败完整形状、不透明请求前缀、家具购买 ACK。
- [Persistence 筛选日志](review-Dfo.Server.Persistence.Tests.log)：3/3，迁移重编译后验证一次分配与余额不复活。初次 `--no-build` 命中旧 DLL 的失败单独保存在 `review-persistence-stale-dll.log`；该旧结果已由重编译通过取代。
- 主代理更广回归日志 `artifacts/wedding-six-fix-20261017/housing-app-regression6.log` 已读取：真实 PVF，411/411；本项为生产者提供的证据，不冒称审核者完整重跑。

复核工具为 [review_tests.py](review_tests.py)，使用测试临时数据库。它的 `--no-build` 前提是测试 DLL 与当前源码一致；迁移测试遇到旧 DLL 时另行重编译后才采信结果。

## 保留的验收边界

- 本次没有新增 WireCaptured/ClientVerified；原生结构判断依赖固定 A21 ClientStatic 证据，服务端单测不升级其证据等级。
- 树末 i8 暂投影已用次数，仍为 Hypothesis；需实机观察 0→1→2、跨日恢复及 UI，不能宣称原服字段语义已确认。
- 已占用壁纸/地板槽位拒绝替换以保留旧资产，旧件最终去向仍未闭合。
- Benefits 为只读派生值；当前没有完整服务端战斗属性消费者。未把属性写回基础数值，也未宣称服务器战斗已应用。客户端属性显示、原生刷新、进出副本与决斗场效果仍需实机验收。
- 审核不替代部署前在线一致性备份、库副本迁移演练、运行身份核对及用户实机验收。
