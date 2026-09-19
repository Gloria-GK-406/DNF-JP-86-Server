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
| 持久化 | `InventoryStorageSlot` / `BoundInventorySlot` | 在容器绑定处把业务槽转换为既有容器及存储索引；数据库结构和已有行不迁移。 |

## 适配方向

```text
A21 body -> GameSlot -> 唯一映射规则 -> BusinessSlot -> 领域行为
领域结果 -> BusinessSlot -> 只读协议投影 -> GameSlot -> A21 body

PVF 绝对区段 -> 内容源适配 -> BusinessSlotRange -> 到达/放置规则
BusinessSlot -> 容器绑定适配 -> InventoryStorageSlot -> 既有持久化
```

映射是强转换：未知 list、保留槽、越界索引和无法反向表达的业务槽抛出
`InventorySlotMappingException`。普通业务拒绝不得吞掉结构映射异常。

## 非普通位置

- `Main/0..2` 是客户端状态投影使用的虚拟资源，不是六条普通物品 CMD 的合法目标。
- 修理全部使用 `index = -1` 哨兵。它不构造 `BusinessSlot`，但请求的 wire `space` 会保留到成功回包。
- PVF 开包中的复活币等虚拟奖励使用 `VirtualResource` 内容目的地，由开包领域的专用虚拟资源分支处理，不进入普通槽位映射。
- 当前未建模的 title、独立 quick-slot list、保留区段和未知 list 均显式失败。

## 行为约束与结构映射

“能映射”只表示地址结构有效，不表示某个行为允许该空间。出售、丢弃、修理、分解、使用和
移动分别保留自己的允许空间规则。例如 Pet 的 `CreatureEquipment` 可以结构映射，但普通 NPC
修理仍拒绝它；不得用公共映射表代替行为授权。

## 单一真源约束

- 协议绝对偏移表只存在于 `InventorySlotMappingRuleService`。
- Application 的放置、到达、商城、开包、补充和分解规则只消费业务空间及局部索引。
- `IBusinessInventorySlotDomainService` 只负责业务槽与已绑定容器之间的关系，不暴露协议坐标。
- 通知阶段的反向转换通过独立只读投影服务完成，不能读取或修改库存。
