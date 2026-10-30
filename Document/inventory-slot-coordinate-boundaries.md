# 库存槽位坐标边界

## 目的

库存槽位只在边界上使用外部坐标。普通物品业务统一使用 `BusinessSlot`，避免把客户端的
`space + index`、PVF 的绝对区段或数据库的容器地址带入命令与领域行为。

## 四种坐标

| 层 | 值 | 责任 |
|---|---|---|
| 网络协议 | `GameSlot` | 原子保存 A21 的 `InventoryContainerType + index`；decoder 创建，协议 handler 立即转为业务槽；响应编码前再反向投影。 |
| 内容 | `BusinessSlotRange` | PVF 读取器把绝对主背包区段转换成一个业务空间内的局部闭区间；Application 不再读取 PVF 绝对槽号。 |
| 业务 | `BusinessSlot` | ICommand、领域服务、规则服务和领域结果唯一使用的地址；空间和局部索引不可拆分传递。 |
| 持久化 | `LegacyInventoryStorageCoordinate` | `BusinessItemContainer` 在旧 SQLite 后端前做局部索引与物理 list/index 转换；不修改 schema。旧穿戴宠物/神器可在入场事务中按模板安全归位。 |

## 适配方向

```text
A21 body -> GameSlot -> A21InventoryLayout -> BusinessSlot -> 领域行为
领域结果 -> BusinessSlot -> A21InventoryLayout -> GameSlot -> A21 body

PVF 绝对区段 -> 内容源适配 -> BusinessSlotRange -> 到达/放置规则
BusinessSlot -> BusinessItemContainer -> LegacyInventoryStorageCoordinates -> 既有持久化
```

映射是强转换：未知 list、保留槽、越界索引和无法反向表达的业务槽抛出
`InventorySlotMappingException`。普通业务拒绝不得吞掉结构映射异常。

## 非普通位置

- `Main/0..2` 是客户端状态投影使用的虚拟资源，不是六条普通物品 CMD 的合法目标。
- 修理全部使用 `index = -1` 哨兵。它不构造 `BusinessSlot`，但请求的 wire `space` 会保留到成功回包。
- PVF 开包中的复活币等虚拟奖励使用 `VirtualResource` 内容目的地，由开包领域的专用虚拟资源分支处理，不进入普通槽位映射。
- 当前未建模的 title、独立 quick-slot list、保留区段和未知 list 均显式失败。
- 旧 `Equipment/24` 只有结合 kind 与模板才能区分普通穿戴和旧宠物；入场归位先检查全量目标碰撞，错误数据明确失败。普通穿戴物理 `12..24` 保持原位。历史错放 `Main` 的宠物装备不自动归位。

## 行为约束与结构映射

“能映射”只表示地址结构有效，不表示某个行为允许该空间。出售、丢弃、修理、分解、使用和
移动分别保留自己的允许空间规则。例如 Pet 的 `CreatureEquipment` 可以结构映射，但普通 NPC
修理仍拒绝它；不得用公共映射表代替行为授权。

## 单一真源约束

- 候选 A21 wire 偏移只存在于 `Dfo.Server.Data` 的静态 `A21InventoryLayout`，旧 SQLite 偏移独立位于 `LegacyInventoryStorageCoordinates`。两表数字可能相同，但证据来源和职责不同。宠物 `25`、神器 `26..28` 仅有上游参考，尚待 A21 实机裁决。
- Application 的放置、到达、商城、开包、补充和分解规则只消费业务空间及局部索引。
- 角色先按 Item、Avatar、Creature、Medal 取得业务 store，再按业务空间取得局部 container；共享底层 `IItemDomainStore` 不改变归属。旧 `IItemContainer.MoveToAsync` 物理移动接口已移除。
- `IBusinessInventorySlotDomainService` 只负责业务槽与已绑定容器之间的关系，不暴露协议坐标。读取槽位得到当前 `ItemReference`，`Take` 校验该次占用并返回 `ItemRecord`，`Put` 消费该记录放入目标槽；实例与堆叠物统一使用这条链路。旧引用在占用变化后失效，不能用裸来源槽重新解析来替代身份校验。详见 [物品生命周期规格](tasks/item-container-lifecycle/spec.md)。
- `A21InventoryLayout` 是 A21 客户端布局这一固定事实，不读内容与配置、没有状态，因此不是规则服务，也不经 DI 注入；
  协议切片、通知阶段、内容源适配和测试都直接调用这同一张表，不另建包装服务或替身。
