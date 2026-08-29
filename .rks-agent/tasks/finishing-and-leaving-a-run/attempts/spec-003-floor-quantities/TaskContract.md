---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-3
  - SC-4
  - SC-7
  - SC-8
---

# TaskContract: 地上躺着的东西显示它真正的数量

## Context

### Source Request

根契约的第三个稳定成果。用户实机报告：「在副本掉落了巨多数量的材料。可能数十万，完全不正确。」
捡起来的那件是 `item 10088621`。

### Current Situation

房间描述帧 `NOTI 0x001D START_MAP` 的掉落块每条 19 字节，`+7` 是一个 `u32`。
`Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs` 无条件往那里写
`DropInstance(...)`——一个 31 位伪随机判别值。

**那一位有两种含义。** 上游把它写成 `DropInfo.PacketValue`：

```csharp
public uint PacketValue => Core == null ? StackCount : unchecked((uint)Core.Value);
```

即有物品实例就写实例的 `value`，没有就写堆叠数量。这与本仓库里 `value` 这一列本身的双重语义是
同一件事——`Docs/GM工具_新背包表结构与ItemCore语义.md` 写着「装备品级/实例值；堆叠数量；
时装 AvatarUid；宠物 CreatureUid」，一列多义，按物品种类分。

**对装备是对的**（`GroundPickupSlice.CarryOverAsync` 靠它把地上那件与背包那件对上），
**对堆叠物就是错的**：客户端把那个位置读成数量。

档案里还有一个一直没被读的字段：房间 `.map` 的 `[special passive object]` 行是
`code x y z ? [item] group count -1 -1 -1`，`count` 就在组号后面，本仓的解析跳过了它。

## Goal

地上的材料显示它真正的数量；地上的装备仍能被逐件区分，捡起来之后与地上那件对得上。

## Scope

### In

- 从档案读出可破坏物声明的数量。
- 按物品种类决定 `+7` 那一位写什么：堆叠物写数量，装备写实例判别值。
- 判别所需的物品种类查询。
- 该路径的边界：档案不认识的物品、数量声明为零或负。

### Out

- 击杀掉落（`NOTI 0x0026` 的掉落条数仍恒为 0）——那是另一帧、另一种记录形状。
- 掉落**内容**的生成规则（掉什么、按什么概率）。一个组声明多条时仍只取第一条。
- 金币掉落。

## Constraints

- 装备的实例判别值不得改变，也不得与拾取那一侧写进背包的值分家。
- 已验证的线级字节不得改变：装备那一条的 19 字节必须与既有的参照帧逐字节一致。
- 权威是客户端与实机；上游的 `PacketValue` 只有线索价值，但它与本仓库自己的 `value` 语义互相印证。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 堆叠物在地上显示的数量就是档案声明的数量 | 编解码测试；实机截图 |
| SC-2 | 装备那一位仍是实例判别值，且与拾取写进背包的是同一个数 | 编解码测试断言两者相等；既有拾取测试不改仍通过 |
| SC-3 | 既有的参照帧仍逐字节复现 | 既有的 byte-for-byte 测试未经修改仍通过 |
| SC-4 | 档案不认识的物品、数量声明为零或负时不会把荒谬的数字放到地上 | 各情形测试 |
| SC-5 | 全套测试通过，构建零警告零错误 | `dotnet build` 与 `dotnet test` 输出 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根契约 `finishing-and-leaving-a-run` 的 `task` 行 | 在本任务工作树内实现本成果并请求一次验收提交 |

## Assumptions

- `+7` 的双重语义取自两处互相印证的证据（上游的 `PacketValue`、本仓库 `value` 列的语义），
  **尚未从客户端反汇编直接确认**。`START_MAP` 的 handler 很大，掉落块的读取落在跟不进去的子调用
  里。若反汇编显示是别的结构，以反汇编为准。判据里因此留了实机截图。
- `.map` 那一行组号之后的整数是「这个物件产出多少」。教学副本每一条都写 1，所以本假设在现有数据
  上不可证伪；实机上一件数量大于一的材料即可确认。
