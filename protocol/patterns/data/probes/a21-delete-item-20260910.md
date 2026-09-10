# A21 `CMD 0x0012 DELETE_ITEM` 指令与实现边界（2026-09-10）

## 结论

本页只闭合玩家执行普通“丢弃”时的 19 字节路径。结论按
[`protocol/evidence.md`](../../../evidence.md) 的等级记录；没有把同 type 的 18 字节路径外推成同一业务。

| 结论 | 置信度 | 证据 |
|---|---|---|
| 普通构包器写 `container + entryCount + N×12B entry + reservedZero + safeModeSkipIndex` | Confirmed | A21 `0x012FDFC0` 构包函数及 `CNRDItemManager::requestDeleteItems` 日志路径逐条写入 |
| 普通单条丢弃的 `operationType` 为 1 | Confirmed | 三份实机帧均为 1，且 `0x012FE12D` 调用路径写常量 1 |
| offset 14 的 `u32` 是保留零 | Confirmed | `0x012FE3D8` 压入常量 0，`0x012FE3E1` 调用 `WriteU32` |
| offset 18 是 `safeModeSkipIndex`，表示安全检查例外上下文 | High | `0x012FE400` 从单例 `+0x46` 写 `u8`，随后 `0x012FE420` 以 0 复位；技能物品检查可写 `SAFE_MODE_SKIP_MOOCU_SKILL` |
| 18 字节、`operationType=0` 的 `0x00B70700` 路径不是已确认的普通丢弃 | Confirmed（形状）；Hypothesis（业务） | 该路径写同一 14 字节前缀与 `u32 0`，但不写安全上下文字节；尚无对应实机动作样本 |

## 普通请求样本

```text
000101004100B4B39800010000000000000000
000101000900B06A0000010000000000000000
```

解码：

| Offset | Size | 字段 | 第一帧 | 第二帧 |
|---:|---:|---|---:|---:|
| 0 | 1 | `container` | 0 | 0 |
| 1 | 1 | `entryCount` | 1 | 1 |
| 2 | 2 | `operationType` | 1 | 1 |
| 4 | 2 | `slotIndex` | 65 | 9 |
| 6 | 4 | `itemId` | 10007476 | 27312 |
| 10 | 4 | `deleteCount` | 1 | 1 |
| 14 | 4 | `reservedZero` | 0 | 0 |
| 18 | 1 | `safeModeSkipIndex` | 0 | 0 |

## 回包

A21 客户端 handler `0x00D303E0` 先读结果字节。成功分支继续读
`container:u8, entryCount:u8, slotIndex:i16, appliedCount:i32, trailingZero:i16`；失败分支读
`errorCode:u8, container:u8`。成功 ACK 会让客户端释放待删除状态并应用数量变化，因此服务端只在事务
提交后通过不可重试 Event 发送一次。

## 当前实现边界

- 仅接收恰好 19 字节、单条、`operationType=1` 的普通主背包丢弃。
- `reservedZero` 与 `safeModeSkipIndex` 在协议对象中保留，但不传入 CMD，不参与当前领域判断。
- CMD 调用 `IItemDiscardDomainService`；领域服务用 `IItemDiscardRuleService` 判断请求与物品规则。
- 服务端槽位内容是权威值：请求 `itemId` 不一致、数量越界、装备锁定或不支持的物品类别均不写库存。
- 18 字节路径、批量条目、时装、宠物、穿戴栏与收集箱语义不在本次接受边界内。
