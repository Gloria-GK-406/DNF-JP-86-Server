# 物品与容器

修改物品状态、容器、移动或生命周期前阅读。地址转换另见
[库存槽位坐标边界](inventory-slot-coordinate-boundaries.md)。

## 属性与生命周期

**业务属性只定义在 `ItemSnapshot`。** 模板号、数量、`ItemTraits Type`、PVF 身份分类形成的
`IsPile`、独立物品 `ItemUid`、全部兼容字段及 Avatar/Creature 细节都在这份数据中。
`Count` 是数量；`InstanceValue` 是旧编码里的实例值，不能用它代替独立物品 UID。
`InventoryItemCoreState` 和 `InventoryItemState` 只用于既有数据库、报文和入场投影边界。

容器的读取、移动、修改与销毁有不同入口：

- `Get/GetAll` 返回 `ItemReference`，绑定事务、真实槽位和占用代次；它不能直接被 `Put`。
- `Take(reference, count?)` 返回 detached `ItemRecord`；整件取出腾空槽位，部分取出减少原堆。
  记录携带完整属性，但没有容器位置。
- `Put(slot, record)` 接收一次记录；兼容材料可合堆。新物品通过生命周期服务 `Create` 创建。
- `Update(reference, change)` 对当前占用的最新属性应用变更，不移动物品，不替换 UID。
  属性修改不使同一占用的其他引用失效；整件 Take 后即使同 UID 放回，旧引用仍失效。
- 销毁先 `Take`，再 `IItemLifecycleDomainService.DeleteItem(record)`；取出本身不销毁。
  装备与宠物实例绑定这类引用，移位后须重新取得行为接口，不再按 UID 跟随旧句柄。

所有写入处于同一领域事务。`ItemLifecycleScope` 保证 detached 记录只被接收或删除一次，
未安置记录不能提交；中途读取/Flush 不结束其生命周期。回滚恢复物品、数量、状态和位置。
邮件附件由同一 item session 接收/取出记录，作为持久托管；同事务材料可在 SQLite 与易失后端间
转移，独立物品不跨后端。装备交换先取出双方，再分别放入。

`items` 继续保存 82 字节兼容 core 及时装六项、宠物八项扩展状态，`item_slots` 保存归属和位置。
整件 Take 只删位置，Put 保留原 UID，Update 更新原行，显式 Delete 才结束物品行。
材料的内部 backing 行键不充当业务 UID。`InventoryEntryAssembly` 读取完整引用并投影入场状态，
不得以会话库存副本或整份回写替代当前物品事实。

新建和落位根据 PVF 校验类型、是否堆叠和目标规则；既存行的身份形态以实际 `item_slots.kind`
为准。容器 flag 准入统一使用 `ItemAdmissionRule` 的 Allowed/All/Any 匹配，语义见
[物品准入规则](ItemAdmissionRules.md)。容量、固定槽、堆叠上限及同账号归属仍由对应业务边界检查。容量缩小后的旧堆可取出或减量，
不能借 Update 增长或改写其他状态。普通补货沿用补货规则决定的数量，与普通发放的堆叠规划不同。
账号仓库无时装细节的历史行仅保留读取兼容，不放宽新建/重新落位的完整性要求。
角色 Main/1 的零复活币逻辑数量为 0，位置表的物理 count 保持正数。

`IsDefault` 标记默认绑定角色，每种类型至多一个且不可移除；`IsPersistent` 独立描述存储生命周期。
`GetContainer(..., isDefault: false)` 使用当前绑定，`true` 显式取默认容器；Find/Get 不补建缺失容器。
`ReplaceDefault` 事务性替换已有默认容器；离线操作显式使用 `GetPersistentStore` / `IItemGrantDomainService.GrantAllPersistentAsync`。
容器注册服务只管理绑定；整批物品赋予归属 `IItemGrantDomainService`。钱包、成长和容量的修改入口见
[资产赋予领域服务](AssetGrantServices.md)。

账号注册与创角通过 `IInitializationDomainService` 在创建事务内应用当前配置；GM 中账号、角色各有一份独立版本的生效配置，见 [初始化配置](InitializationSettings.md)。
每日补给通过配置 / 发放领域服务在角色入场时执行，每日尽可能补给一次，空间不足也记录完成；GM 区分 PVF 与自定义规则，见 [每日补给](DailyRefillSettings.md)。
选角显式注册空的默认内存类型 29 容器。死亡塔入场换成本局六格，跨层保留，退塔/断线换回空容器；
Main 与 SharedMaterial 不切换。实现及实机边界见 [两塔快捷栏](TowerQuickSlots-20261005.md)。

边界限制：`core_uid` 没有数据库唯一约束，普通写入由记录接收机制限制；入场仍检查重复行身份。
`items.item_uid` 的 wire 范围 CHECK 不能靠给既存表 ADD COLUMN 补上，编码仍使用 checked 转换。
物品写入统一通过生命周期接口；数据库结构约束由直接 SQL 测试覆盖。

## 普通装备数量与实例值

独立装备的 `ItemSnapshot.Count` 恒为 1；旧 core.Value 由 `InstanceValue` 保留。
起始装备和地面掉落的这个兼容值不再参与数量判断，也不等同于 `ItemUid`。
移动和对调通过 Take/Put 保留原 UID，耐久等属性通过 Update 修改。

## 装备修理与修理券

修理业务归 `IEquipmentRepairDomainService`，NPC收费、契约自动修理及修理券各有明确入口。
券修理沿用原生CMD0017，材料槽由唯一槽位映射转换为角色当前背包／快捷栏位置；不支持券与全身、
自动或快速修理模式混用。普通消耗品使用入口拒绝`[repair coupon]`，避免只扣券却不修复装备。

券身份、最低等级、职业、场所、冷却及`[action usable dungeon]`来自物品使用内容及修理规则服务。
遗忘之地修理券10093456声明地下城4000；在城镇、其他地下城、加载中或探索已到期时拒绝使用。
只修复角色持有或穿戴的单件可修装备；目标须有正的耐久上限且当前未满，过期、锁定或镜像物品不作为券修理输入。

券扣量复用材料消费领域服务，耐久修改复用装备实例。一次修复扣1张券，金币不变，二者在同一显式事务提交；
失败不扣券也不修复。成功后在原修理ACK之前投影券的剩余数量或清空槽位，客户端依原生ACK更新目标耐久。
同一物理帧的重放不再次消费；新的请求遇到已满耐久的目标也不扣券。
客户端成功实测与自动验证边界见[修理券验收](tasks/forgotten-land/RepairCoupon.md)。

## 时装与槽位诊断

- 时装的 wire kind 为 8，完整装扮细节必须随物品生命周期保存；装扮栏条目形状见
  [装扮栏条目](avatar-container-entry-shape.md)。
- `PvfEquipmentSlots.BusinessByName` 是业务位置，`ByName` 是兼容槽号；不要把它们与 SQLite
  物理槽号混用。具体边界与未实机确认的宠物／神器偏移见 [槽位坐标边界](inventory-slot-coordinate-boundaries.md)。
- 时装名字对应业务装扮槽 0..10；槽 11 没有本档案模板声明的键。更换档案需重新检查，不能由上游枚举补猜。
  PVF `[equipment type]` 名字后面的数字不是可直接采用的穿戴槽号；槽号需协议／实机证据。
- `GameMoveItemSpaceCommandHandler` 的日志包含模板、源／目标列表、槽号和拒绝状态。
  `from/to` 描述请求两端，不能仅凭这行或模板号判断实际移动方向；应检查两端移动前后的占用。
  模板尚未读出时拒绝日志可能没有有效物品 ID，不能据此认定客户端请求的是模板 0。
- 永久、限时与到期的存储／wire 规则见 [物品期限](Avatar-Permanent-20260909.md)。
  永久标记不能用某个固定年份换算出的剩余秒数代替。

实现、自动回归与 A21 实机验收是不同证据；不能由文档或单元测试推导 ClientVerified。
