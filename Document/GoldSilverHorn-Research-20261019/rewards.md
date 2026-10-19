# 新·金角银角奖励购买与开启语义

调查日期：2026-10-19
代码基线：`task/next-round-20261019` / `ccc0e845e300`
范围：`490001565`、`490001561`、`490001562`、`490001563`、`490001564` 的购买成本、开启成本和当前实现差距。本文只记录研究与修复设计，不修改生产代码或现服数据。

## 结论

五件商品的 `[need material] 490001570 N` 都是 **NPC 商店购买成本**，不是开启成本。四个袖珍罐的开启协议 `0x001B USE_LOTTERY_ITEM` 只有 `mode + sourceSlot` 四字节，复活币包的普通 `0x00A0` 请求只有 `sourceSlot` 两字节；两种请求都没有碎片槽位或碎片物品 ID。因此服务端不能把 `[need material]` 解释成开启时再次消费。

`490001563`、`490001564` 的 `[lottery use cost] 7000000` 才是开启费用。碎片在向吟荷购买罐子时扣除，700 万金币在开启传说罐时扣除；普通罐 `490001561/562` 和复活币包 `490001565` 没有额外开启费用。

当前实现并不会完成“双扣”：它会更早失败。内容解析器把 `[need material]` 同时投影为商店材料价和开盒材料价，随后：

- `490001561/562/563/564` 被 `LotteryOpeningDomainService` 的非零 `MaterialCount` 门槛拒绝，700 万金币逻辑尚未执行；
- `490001565` 被 `PackageOpeningDomainService` 以“请求没有材料”拒绝。

这五件商品因此都需要先修正内容字段作用域，才能形成购买后开启的闭环。

## PVF 事实

吟荷 `20152` 的 `itemshop/chn_dayofdungeon_shop.shp` 在“新·金角银角”页列出以下商品：

| 商品 | 名称 | 类型 | 购买成本 `[need material]` | 开启成本 |
|---:|---|---|---:|---:|
| 490001565 | 复活币 | `[usable cera package]` | 490001570 × 5 | 无；开启后给复活币 × 1 |
| 490001561 | 银角袖珍罐 | `[upgradable legacy]` | 490001570 × 10 | 无 |
| 490001562 | 金角袖珍罐 | `[upgradable legacy]` | 490001570 × 10 | 无 |
| 490001563 | 传说的银角袖珍罐 | `[upgradable legacy]` | 490001570 × 99 | 金币 7,000,000 |
| 490001564 | 传说的金角袖珍罐 | `[upgradable legacy]` | 490001570 × 99 | 金币 7,000,000 |

PVF 文案只在 `490001563/564` 明说“开启袖珍罐时，会消耗700万金币”，并与独立字段 `[lottery use cost] 7000000` 一致。五个文件的 `[need material]` 都位于可购买物品的顶层，吟荷商店解析器按购买材料价读取它。

同一 A21 PVF 中已有实机对照：预约硬币商品 `490003532/490003534` 和复活币礼盒 `490003531` 也在顶层声明 `[need material]`。`0x0015 BUY_ITEM` 实机购买后，数据库里的预约硬币减少量与该字段一致；这证明当前 A21 的 `[need material]` 至少在该同类内容上是商店兑换成本。证据见 `protocol/patterns/data/traces/a21-shop-live-20260909.md`。

真正的抽奖开启材料字段是 `[lottery use need item]`。参照读取器将它与 `[need material]` 分开保存；参照 `LotteryItemDefinitionProvider` 也只把 `LotteryUseNeedItemId/Count` 投影为开罐所需物品。金银角四个罐子均没有 `[lottery use need item]`。

## 客户端与协议行为

### 碎片不会由开罐请求提供

`0x001B` 的 A21 请求固定为四字节：

```text
u16 mode
i16 sourceSlot
```

四个已知构包点 `VA 00999959`、`009B502B`、`024E730E`、`024F3D8D` 均只写 `mode` 和来源槽位。其紧邻发送分支检查的是 UI/请求状态和来源槽位有效性，没有写入碎片 ID、碎片槽位或碎片数量。协议记录见 `protocol/game/client-to-server/0x001B.md`。

`490001565` 的普通包开启走 `0x00A0` 两字节请求，只含来源槽位。当前 `PackageOpeningDecoder` 也按这个布局解码。

因此客户端开罐流程没有把 `[need material]` 作为待发送的开启材料。不能依赖客户端替服务端完成或证明碎片扣除；正确兼容行为是购买事务扣碎片，开启事务不再扣碎片。

### 客户端是否会提前阻止开启

- **碎片不足：不会形成开罐材料校验。** `[need material]` 由商店购买 UI 使用；买不到罐子时自然没有新罐子，但已经持有或副本掉落的罐子不应因碎片不足而禁止开启。四个 `0x001B` 构包点也没有碎片字段。
- **金币不足：不得依赖客户端。** `[lottery use cost]` 属于开罐费用，客户端可用它展示价格或提示；本轮未取得“金币不足时是否完全不发 `0x001B`”的实帧，故这一点保持 Unknown。服务端必须继续把金币余额校验与 700 万扣除放在同一开启事务内。
- **服务端失败仍需保守处理。** 即使客户端本地显示或预检费用，背包与金币的权威状态仍在服务端；客户端预检不能替代服务端校验。

## 当前实现定位

| 位置 | 当前行为 | 影响 |
|---|---|---|
| `PvfNpcShopDefinitionSource.cs:181-190` | 把顶层 `[need material]` 解析为 `NpcShopMaterialCost` | 正确，购买时扣碎片 |
| `PvfPackageOpeningDefinitionSource.cs:342` | 同时把 `[need material]` 解析为开启 `MaterialItemId/Count` | 字段作用域错误 |
| `PvfPackageOpeningDefinitionSource.cs:304` | `[lottery use need item]` 目前仅生成“不支持”诊断 | 保持失败关闭，不应拿 `[need material]` 替代 |
| `LotteryOpeningDomainService.cs:61` | 普通罐只要 `MaterialCount != 0` 就拒绝 | 四个金银角罐当前无法开启 |
| `LotteryOpeningDomainService.cs:89` | `GoldCost` 在开启事务中扣除 | 传说罐的 700 万路径方向正确 |
| `PackageOpeningDomainServices.cs:20` | 普通包定义若有材料成本就拒绝 | `490001565` 当前无法开启 |

## 最小兼容修复

只改内容解释，不改协议和领域事务：

1. 在 `PvfPackageOpeningDefinitionSource.Parse` 中停止把顶层 `[need material]` 投影为开启材料。
2. 保留 `PvfNpcShopDefinitionSource` 对 `[need material]` 的现有解析和购买扣除。
3. 保留 `[random upgradable legacy]` 的 `[sealing removal item]` 开启材料解析。
4. 保留 `[lottery use need item]` 的当前失败关闭诊断；不要在本修复中顺带扩大尚未验证的抽奖材料功能。
5. 保留 `[lottery use cost]` 到 `GoldCost` 的映射和现有原子金币扣除。

代码层面的最小变化是把解析条件从：

```csharp
name == Fields.NeedMaterialField.Name || random && name == Fields.SealingRemovalItemField.Name
```

收窄为：

```csharp
random && name == Fields.SealingRemovalItemField.Name
```

这会让五件金银角商品的开启定义 `MaterialCount == 0`，同时不影响商店购买定义，也不影响魔盒的真正开盒材料。

## 回归目标

### 内容解析

- 吟荷 20152 的商店页仍包含五件商品。
- 五件商品的购买材料分别为 `5/10/10/99/99 × 490001570`，且 `IsReadable=true`。
- `490001561/562/563/564` 都解析为 `Lottery`，`MaterialCount=0`、诊断为空。
- `490001563/564` 的 `GoldCost=7000000`；`490001561/562` 的 `GoldCost=0`。
- `490001565` 最终解析为普通 `Package`，`MaterialCount=0`，固定奖励为复活币 × 1。
- 一个带 `[need material]` 的既有商店礼盒（建议 `490003531`）在购买源仍保留材料价，在开启源不再产生材料要求。
- 一个带 `[sealing removal item]` 的真实魔盒仍解析出原有开启材料，防止误伤魔盒链路。
- 一个带 `[lottery use need item]` 的合成定义仍产生明确诊断并失败关闭。

### 领域事务

- 购买五件商品分别只扣一次对应碎片并增加商品，不产生开启动作。
- 开启 `490001561/562`：扣一个罐子、发一个池中结果、不扣碎片、不扣金币。
- 开启 `490001563/564`：扣一个罐子与 7,000,000 金币、发一个池中结果、不扣碎片。
- 金币不足时，传说罐、金币和奖励均不变化；满足余额时原子提交。
- 开启 `490001565`：扣一个包、增加一个复活币、不扣碎片。
- 满包、存储失败和重复请求仍维持现有回滚与幂等边界。

### 线协议与实机验收

- 不改变 `0x0015`、`0x001B`、`0x00A0` 的请求或响应布局。
- 实机最低闭环：碎片刚好购买一件商品，购买后碎片归零；随后仍能开启普通罐/复活币包，证明客户端不会把商店碎片价重复当开启门槛。
- 传说罐分别以不足 700 万和足够 700 万测试；确认不足时无消耗，足够时只扣 700 万与罐子，不再扣碎片。
- 逐项核对客户端弹窗、奖励展示、背包结果和重登持久化。完成前只能标记 `ServerImplemented`，不能标记 `ClientVerified`。

## 证据等级与剩余未知

- **ContentStatic / Confirmed**：五件商品的类型、`[need material]`、`[lottery use cost]` 和奖励表。
- **WireCaptured / ClientStatic**：`0x001B`、`0x00A0` 请求没有开启材料字段；同类 `[need material]` 商品的 `0x0015` 购买扣币已有 A21 实机记录。
- **CurrentImplementation / Confirmed**：当前错误投影及两个领域拒绝门。
- **Unknown**：A21 客户端金币不足时是否在发送 `0x001B` 前本地拦截；这不影响服务端必须权威校验与原子扣款的设计。
