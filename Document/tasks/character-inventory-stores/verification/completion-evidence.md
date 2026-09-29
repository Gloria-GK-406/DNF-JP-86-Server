---
kind: rks-verification
decision: pass
---

# 角色物品业务容器：代码交付验证

验证范围为 TaskContract revision 1 的本地代码、测试、资料与验收步骤交付，不包含部署或 A21 实机认证。产品源码及测试由唯一实施者冻结，独立完整审查为 [approved](../reviews/change-review.md)。

## 最终执行证据

- 工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap`，分支 `task/solo-play-roadmap`，接受前 HEAD `9c63059147ebccac99bcfb9ca0bfec63f64ddb1f`。
- 环境：Windows、仓库 .NET 10 SDK，`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`；只使用隔离测试库。
- 主控命令：`dotnet test Server/86JP.Server.slnx -m:1 -v quiet --nologo --logger "trx;LogFileName=inventory-controller-final.trx"`。
- 退出码 **0**；八个项目 **4241 通过、0 失败、2 跳过**。完整输出：[controller-final.log](controller-final.log)。各项目通过数：Pvf 60、Application 2134、Host 24、HttpApi 50、Infrastructure 388、Networking 66、Persistence 378、Protocol 1141。
- 两个跳过项为 Infrastructure 的缺少 PVF 负向测试；它们要求档案不可访问，与本轮真实 PVF 条件互斥，非本次新增跳过。
- `git diff --check` 通过。Application 的业务操作不再通过 `ToGameSlot` 回转读写；剩余调用位于只读投影及入场快照组装。生产 `IItemContainer.MoveToAsync` 已移除。
- `main` 仍为 `10ccbad248495abc80dc0ce8df86b3d552808e86`，主仓库状态干净；本轮未操作运行服务和生产数据库，未推送或部署。

## 成功条件

| 条件 | 结果和证据 |
|---|---|
| SC-1：业务 store、局部索引 | 通过。Item/Avatar/Creature 分组及 Medal 预留；两个业务容器的局部0不互相覆盖。`ContainerAdditionTests`、业务接口及独立审查 RQ-001。 |
| SC-2：边界转换与未知拒绝 | 通过代码交付要求。协议映射、旧库存储适配独立；往返和越界测试通过。A21 宠物候选映射依据及尚未实机的边界见 [槽位证据](../evidence/slot-evidence.md)，没有冒充已验证字段。 |
| SC-3：统一发放目标 | 通过。奖励、商城、邮件、开包、拾取使用同一模板归属；宠物事实创建、离线发放、首格、装扮与宠物装备分类测试通过。 |
| SC-4：交换准入、原子性和身份 | 通过。双向准入、拒绝无写入、堆叠拆合、同 core 双 UID 交换、连续穿脱及移动后耐久更新均有回归；旧宠物归位保留 UID 且幂等。详见 `BusinessEquipmentSwapTests`、`CreatureGoesBackOnTests` 及审查关闭记录。 |
| SC-5：既有行为和范围 | 通过本地自动验证。八项目全量通过；保留勋章、技能快捷键及仓库范围限制，明确 [实机清单](../client-acceptance.md) 尚待后续部署执行。 |

## 非一次全绿的验证过程

并行全量 [full-final-v2.log](full-final-v2.log) 曾出现未改动通知测试的墙钟上限失败（4730ms > 2.5s）；该测试独立复测通过，见 [notification-focused.log](notification-focused.log)。未修改通知实现、测试断言或跳过条件。主控随后以 `-m:1` 逐项目完整执行，同一测试及全部项目通过；该证据支持当前通过结论，不能把中间失败解释成一次全绿。

## 后续实机边界

宠物穿戴25、宠物装备穿戴26..28及宠物装备背包140仍为上游候选。首次部署应优先核对这些地址，以及普通装备、装扮移动与重登。历史错放 Main 的宠物装备不在自动修复范围。以上不阻塞本契约的本地交付，但尚不能宣称客户端验收完成。
