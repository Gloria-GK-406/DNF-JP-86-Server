# 默认容器替换与死亡塔快捷栏

日期：2026-10-05；实施基线：`82294d45`。服务端 `11abf74c` 与客户端 `32cbcad` 已部署，2026-10-05 用户实机验收通过。

## 容器契约

`FindContainer` 只查询已注册容器，缺失返回 null；`GetContainer` 缺失抛错。普通 `AddAsync` 和物品发放也不补建容器。选角负责明确注册角色各容器；离线整量发放使用 `AddToPersistentAsync`，入场加载和邮件使用 `GetPersistentStore` 明确访问持久化存储，不制造在线注册。

`ReplaceDefault(character, expectedDefaultId, replacement)` 只替换已经存在的默认容器：同类型、replacement 必须为不可移除的 default、ID 不得与另一注册容器冲突。旧 default 正是 current 时，current 一起换到新容器；另有 current 时保留它，日后移除 current 会回到新 default。替换加入领域事务，回滚恢复原绑定；它不会删除持久化物品。`IsDefault` 是绑定角色，`IsPersistent` 是存储生命周期，两者独立。

## 死亡之塔

- 选角显式注册一个空的默认内存容器，物理类型沿用 `InventoryContainerType.QuickSlot = 29`，业务空间为 `TowerQuickSlot`，容量六格。
- 入塔通过 `ReplaceDefault` 换成本局新容器，跨层保留；退塔或断线换回新的空内存容器。原 Main、SharedMaterial 绑定不变。没有“找不到就创建”的恢复路径。
- 塔无色 `6515` 和塔药剂 `6516..6525` 获得 `Tower` 标志；当前 45 层实际掉落是 `6515/6518/6519/6520/6521/6523/6524` 七种，竞争六格。
- 准入规则：Allowed = Tower | Consumable | Material，All = Tower，Any = Consumable | Material。普通栏位不允许 Tower；塔栏位也不允许普通物品。保留堆叠上限、真实模板分类、槽位和容量检查。
- 相同物品可以合堆；无可容纳位置时拒绝拾取，地面物仍可领取，不溢出到普通背包。新物品由内存 store 创建，不先插入再删除数据库记录。
- 塔药剂使用与塔无色扣料走既有物品/材料行为，映射到类型 29。普通奖励进入原持久化容器并正常投影。
- 已移除 `Diagnostics:TowerKeepInventory` 探测开关及 Main/SharedMaterial 临时切换逻辑。

## 客户端与报文

[类型 29 静态证据](../protocol/patterns/tower-quickslots.md)。线级槽号是 3..8，业务索引是 0..5；类型 29 在客户端仍绘制于 Main 的六个快捷格。

ClientHack 的 `0180_TowerInventory` 对死亡塔 11000 恢复原生备份、清空、塔标志和离塔恢复，对迷妄 11001 保持原有跳过逻辑。不能继续配合旧版“对两塔均跳过”的 DLL 使用本实现。客户端配套提交为 `32cbcad`，新 DLL 的 SHA-256 为 `EECFDFA96C658E280F81494A1DED429EAE2FAA82D31155D966174C4BFAA169AF`；已于 2026-10-05 部署到 protocol2，用户实机验收通过。

拾取顺序为 CMD GET_ITEM 成功 → NOTI 0027 地面物入包/移除 → NOTI 000E 绝对数量刷新。0027 本身会入包或增加堆数量，不能在它之前发最终数量。塔内重复操作重放的 Main 全量快照使用当前塔六格内容覆盖普通快捷格；离塔后旧塔操作重放不清除恢复的普通快捷格。退塔仍重新发送普通 Main 快照，校正原生恢复可能带回的旧数量。

## 2026-10-05 实机验收：通过

证据等级：ClientVerified（用户实机测试与验收确认）。测试对象为已部署服务端 `task/warehouse-storage` / `11abf74c`，配套 ClientHack `32cbcad`，客户端 `DNFClient-A21-protocol2`；0180 DLL SHA-256：`EECFDFA96C658E280F81494A1DED429EAE2FAA82D31155D966174C4BFAA169AF`。

用户反馈：“ok，测试成功。在满了的情况下拾取新的会发物品栏已满。符合预期。可以当验收了。”据此将本次死亡塔六格快捷栏交付记为验收通过；明确观察到的满格新种类拾取提示为“物品栏已满”。下列清单保留为回归参考，不把未逐项描述的操作补记成独立实机证据。

## 实机回归清单

1. 城镇记录普通六格、背包、共享材料；死亡塔入场只切换快捷栏，普通背包保留，邀请函扣一次。
2. 拾取无色和六种药剂，验证六格满后第七种拒绝、已有堆可继续拾取，地面物不被提前领取。
3. 调换塔六格位置，使用药剂、释放耗无色技能；观察类型 29、槽号 3..8、数量和清空结果。
4. 换层保留，退塔/断线重登/再次挑战不带出旧物品；普通快捷栏、背包和账号材料恢复正确，奖励仍在。
5. 迷妄之塔继续使用普通快捷栏；组队补查拾取广播和成员离开。

ClientVerified 依据上面的用户实机反馈；静态地址与自动化分支测试仍按各自证据来源记录。

## 自动化验证

Release 完整解决方案构建：0 警告、0 错误。真实 A21 PVF 配置下，8 个测试项目共 5,776 通过、0 失败、2 跳过（原有缺少 PVF 场景用例）。TRX：`artifacts/tower-default-container/regression-final/`。

覆盖默认替换/回滚/独立 current 回退、拒绝隐式补建与离线显式访问、45 层推进、七种物品竞争六格、满格合堆与拒绝、内存换位与禁止移出、药剂/技能扣料、退塔/断线/新局隔离、重放和真实 TCP 拾取的 0027 → 类型 29 的 000E 顺序。客户端 Win32 Release 构建和 CTest 11/11 通过。自动化验证完成后，服务端与客户端已配套部署并由用户验收；运行及备份记录见 `E:/Project/DNF/Document/tower-quickslots-deployment-20261005.md`。
