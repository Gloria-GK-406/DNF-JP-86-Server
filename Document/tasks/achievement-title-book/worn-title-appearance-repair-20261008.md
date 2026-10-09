# 称号簿穿戴与外观独立同步修复（2026-10-08）

> 历史候选，已被实机否定：space=18+category 跳过原生 pending 清理，导致无法再次佩戴。以下保留原设计与验证范围；当前修复恢复原生 ACK，见[原生确认修复](native-title-confirmation-repair-20261008.md)。不得将以下自动化与分支模拟记为 ClientVerified。

## 授权与边界

用户授权：“ok，先按你的想法完成修复吧。还不行我们再更细粒度的决策。”沿用 `task/abyss-research`，不修改客户端 patch。目标是称号簿保留原实例，穿戴位创建镜像，装备、切换、取消不经背包搬移；外观修改只属于当前穿戴实例。

## 故障与候选修复

- **ClientStatic / High**：A21 `CMD019C`（VA `0111CD40`）和 `CMD019D`（VA `0111CFF0`）成功分支，space=3/17 调用本地交换 `052583D2`。这会交换穿戴位与书槽；即使服务端书中原件没动，客户端界面也可能删掉书中内容。
- **ClientStatic / High**：穿戴位用 `NOTI0002 subtype1` 的 `01182850` 更新。先前用 `NOTI000D list3` 刷新穿戴位与客户端映射不匹配，不能当作有效修复。
- **CurrentImplementation / 候选**：提交后依序发送穿戴 `0002 subtype1`、外观 `0002 subtype0`、书槽 `000E list18..22`，最后成功 ACK 的 space=18+category。这条有效书容器分支只重建称号簿 UI，不走物品交换。只有旧真实穿戴称号被挤到背包时才同步背包。
- **ClientStatic + Emulated**：对上述两个真实 x86 handler、五类书槽共 10 例执行分支模拟，均调用分类 UI 重建，未调用本地交换/背包取出 helper。读取原语、容器映射、UI 查找和重建被替身隔离。结果在 `artifacts/title-appearance-checks/native-ack-emulation.json`。这不证明真实客户端引用生命周期、请求队列或渲染表现，也不是官方 ACK 样本。
- 书槽保留后，客户端再次右键仍可发装备请求。`ToggleEquippedAsync` 将“当前镜像来源正好是该书槽”的再次点击解释为取消穿戴；其他来源则替换镜像。领域的显式 `WithdrawAsync` 保留装备/取出语义。

## 数据与职责

`ItemId` 是实际属性称号；`AppearanceItemId` 为可空外观覆盖，`DisplayItemId=AppearanceItemId ?? ItemId`。两者默认一致，不改物品 82B 核心线格式。SQLite `items.appearance_item_id` 为新增可空列，旧记录默认无覆盖；镜像来源和原件 UID 不变。

`CMD0239` → 称号簿领域服务 `SetAppearanceAsync`：必须已穿戴合法称号；非零外观必须是已拥有的称号。0/实际物品 ID 清除覆盖。只修改穿戴实例，保存、重登可恢复，不影响属性与称号簿原件；镜像删除后覆盖一起删除，新镜像默认本体外观。

外观变更经 `TitleAppearanceChanged` → EventService → 通知投影；角色当前状态、登录、支援与角色列表均从实际穿戴实例读外观覆盖。取消镜像事件不额外触发背包全量刷新，其他通用镜像移动保留原行为。

## 验证与验收

完整业务回归 3286/3286 通过，包含真实 PVF，0 跳过。构建 0 警告、0 错误。协议回归 1397/1397、SQLite 持久化回归 466/466 通过，均 0 跳过。

扩大持久化回归发现测试自建 items 表未包含新列、全字段文本基线未列新属性，已更新夹具与基线，未改变旧 core 字节。另有此前 `13241b87` 已修正 A21 外观范围但未更新的测试断言（旧 0/12/23，实际 0/12/13/24），以及此前镜像列漏列的 schema 断言，一并对齐既有生产定义。当前状态为 **CurrentImplementation + ClientStatic + Emulated，ClientVerified 待验收**。

实机验收：原生称号与手动存入称号连续切换、再次点击取消；书中原件始终可见、背包不闪现镜像。Shift+右键换外观后属性称号不变，重登保持当前外观；取消再装备恢复本体外观。
