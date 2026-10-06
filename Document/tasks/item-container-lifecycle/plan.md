---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 容器物品生命周期

## Planning Choice

一个详细计划统一决定数据形状、生命周期和调用迁移。存储实现作为有界工作委派，共享工作区写入串行；主代理负责集成与消费者迁移。无 Task Unit 注册表。

## Shared Interfaces and Decisions

- 唯一属性值 ItemSnapshot 放在 Data/Inventory：ItemId、Count、Type（已有 ItemTraits）、IsPile（由 PVF 身份分类得出）、ItemUid?、全部实例字段及 Avatar／Creature 细节。字段扁平表达，未知字段保留；LegacyKind、InstanceValue 仅服务边界编码，Count 不再与旧 Core.Value 混用。不增加新的 ItemType/WorldClass 业务分类。PVF 内部原有分类可用于构造／验证 IsPile。
- InventoryItemCoreState 只作为现有数据库／协议编码边界形状，ItemSnapshot 的双向转换覆盖全部字段。业务规则和行为改读写 Snapshot；不以 Core 包装伪装为统一模型。嵌套字节数组在输出／写入边界复制，避免绕过 Update。
- ItemRecord 是会话发行的 detached 物品能力，包装上述 Snapshot，不包含位置；不允许普通读取快照转换为可 Put 的记录。Put/Delete 按会话登记验证具体对象仍未被接收／销毁。
- ItemReference 是容器内位置及占用代次的能力，包装同一 Snapshot。包括会话身份、真实存储地址和可选业务坐标视图；业务 SlotIndex 投影为局部槽，底层始终用原存储地址。取出后旧引用失效，即使原 UID 放回。Get 不移除；Update 以引用定位并对最新 Snapshot 应用变更，拒绝换 UID／身份形态。宠物合法变形保留 UID，模板及状态由既有规则验证。
- IItemDomainStoreSession 改为 Get/GetAll、Take(reference,count)、Put(container,slot,record)、Update(reference,change)、Create(snapshot)、DeleteItem(record)、ApplyDecidedWrites；去掉通用裸 Write/Clear。容器提供 Get、GetAll、Take（按槽或引用，可指定数量）、Put、Update；生命周期领域服务提供 Create/DeleteItem，取代旧新建包装。普通业务不提供 Save(Core)。
- 部分材料 Take 减源数量并返回 detached 堆；Put 到空槽或兼容材料堆，后者经容量与堆叠限制检查后合并并消费 record。消耗使用 Take(count)／DeleteItem，跨容器不凭复制 Snapshot 生成材料。
- SQLite 保留现有事务参与者：Take 只删除位置，保留物品行；Put 按 UID 建位置，Update 更新同一行，DeleteItem 明确删除。内部材料行键不外露为业务 UID。禁止来源推断、按 Core 内容猜身份和隐式空槽删除世界行。
- 中途 Flush 只物化当前操作，不能判定未安置 record 为错误或清掉生命周期登记；提交前独立验证所有 detached 已安置／销毁，无论 dirty 是否已清空。事务结束使全部引用和 record 失效。独立操作在事务内完成，易失测试 store 遵循同一约束。
- 原有同账号归属检查保留，不以 AccountShared 特性充当授权。移动与属性规则、协议编码和数据库 schema 不变。
- CreatureFacet 变为 Item 会话上的状态适配，避免两个完整状态写者互相覆盖；入场条件复活保留原守卫。在线小数饥饿缓存与完整记录整数更新协调，不能覆盖已更新的记录。

- 同一事务的 SQLite／易失容器允许材料跨后端 Take/Put；发行后端在接收完成时结束原 backing 行，双方回滚一致。独立物品不跨后端，仍保留现有单可失败提交参与者约束。
- 邮件附件是现有持久托管位置：由同一个 item session 接收 detached record，并从验证了收件人、附件未领取及 UID 实际持有的附件取出 record。GM 先 Create 再交给邮件托管，移除仅凭无槽位 UID 寄送的入口；沿用现有表、领取事务及堆叠附件规范化规则。

## Work and Dependencies

### T-001 — 数据与存储生命周期（RQ-001—004、006—007）

新增 Snapshot、Record、Reference 和兼容转换；替换 store 接口及 SQLite 结算，提交校验与中途 Flush 分离。迁移易失 store。优先真实 SQLite 测试：Take 保留行、Get 不可 Put、重复接收、同 UID 回槽旧引用失效、未安置且中途 Flush 拒绝提交、回滚完整状态。测试先观察行为失败；接口编译失败不算 RED。

### T-002 — 容器与普通业务迁移（依赖 T-001；RQ-001—008）

实现物理／业务容器同一 lifecycle 和局部槽视图；注册生命周期工厂／删除服务。移动交换先 Take 双方再 Put；整理先取出再放入；材料分拆合并／消耗通过数量 Take/Put/Delete。迁移商店、邮件、效果、奖励、拾取、仓库、虚拟资源边界和所有生产写入点。纯规则只接收 Snapshot 或位置引用的只读数据。

### T-003 — 实例与加载边界（依赖 T-002；RQ-003—008）

装备实例绑定取得时的位置引用，属性操作通过 Update，移出后不按 UID 追踪到新槽继续修改；销毁通过 Take/Delete。容器按 slot 提供装备行为入口。宠物同样使用引用和统一完整状态，协调 CreatureFacet 与在线计时。加载读取完整扩展状态，直接组装容器数据／必要客户端投影，清理 InventoryStorageItem 等重复数据模型。所有旧类型与 Core 业务输入进行反向审计。

### T-004 — 回归与收口（依赖 T-003；全部 RQ）

按真实语义迁移测试夹具：先创建 detached record 再 Put；修改使用引用；移动先 Take，不能保留旧覆盖槽即销毁的隐式 API。既有协议 golden 不改写。更新 AGENTS 当前架构段落，独立完整结果审查，修复发现后新鲜验证，再一次本地结果提交。

## Verification

所有 .NET 构建／测试串行。基线解决方案构建成功，0 警告错误，日志 artifacts/item-container-lifecycle/baseline-build.log。新行为测试的 oracle 观察真实槽位、UID、物品行和回滚，不固定内部集合结构；它们防止现有测试未覆盖的 detached／引用误用。最终 Application、Persistence、Protocol、Infrastructure（真实 PVF E:/Project/DNF/DNFClient-A21-protocol/Script.pvf）全项目测试，解决方案构建及必要 Host/Networking 集成检查。审计普通业务不再通过 Core、StoredItem、ContainerSlotItem 和旧 Put* 写入。原始日志留 artifacts/item-container-lifecycle，最终证据逐条件记录。
