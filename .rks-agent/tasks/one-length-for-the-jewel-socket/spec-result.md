---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 珠宝槽在线上只有一个长度

## Delivered

时装珠宝槽存 30 字节而 A21 只发前 18。本仓有**三条**上线路径，其中两条在发 30。
本轮把那两条改成 18，并让三条路读同一处常量、走同一段截断写法。

| 路径 | 帧 | 之前 | 之后 |
|---|---|---|---|
| `GameInventoryItemEntry.WriteAppearanceBlock` | NOTI `0x000D` / `0x000E` | 18 | 18（改为调用共用写法，逐字节等价） |
| `StateSynchronizationPayloadCodec.WriteNoti2EquippedItem` | NOTI `0x0002` 子类型 1；消息 `0x019F` | **30** | **18**，每条缩短 12 字节 |
| `StateSynchronizationPayloadCodec.WriteMailboxAvatarDetail` | 消息 `0x0061` | **30** | **18**，每条缩短 12 字节 |

新增 `Dfo.Server.Protocol.Game.AvatarJewelSocketOnTheWire`：一个 `WireJewelBytes = 18`
与一个 `WriteJewelRun`，三条路都调它。产线里线上长度**只有一处定义**、截断写法**只有一处实现**。

判据新增 8 条、修改既有 1 条（`SelectCharacterPayloadCodecTests` 里那个唯一受影响的用例，
每处改动都对应授权的 12 字节位移）。协议文档三份：`NOTI-0x0002.md` 补上时装槽外观块的形状与位置、
`NOTI-0x000D.md` 为「那条路本轮未改动」加了指名补记、`NOTI-0x0061.md` 新增 A21 小节记下附件里的这一段。

## Verification

`./verification/completion-evidence.md`（`decision: pass`）。
条件逐条的证据与四次独立变异（M-A..M-D）都在那里，不在此重复。

评审：`./reviews/change-review.md`（四轮独立评审，终判 `verdict: approved`）。

## Repository Result

- Changed paths：已跟踪 6 个（`Server/src` 2、`Server/tests` 1、`protocol/` 3），
  未跟踪 2 个新代码文件加 `.rks-agent/tasks/one-length-for-the-jewel-socket/` 下的流程产物
- Baseline or accepted result：`d94e184108c8ee07f889b0e2ac98dd510a0668b0`
- Parent rollback required：no

## Successor Guidance

- **18 这个数没有实机证据，且在原理上得不到。** 依据只有一条：上游三个写入方共用
  `A21AvatarJewelBytes = 18`（源码读出）。珠宝运行自带长度，发 30 也对齐也不崩；
  测过的珠宝槽全是零，18 个零与 30 个零无从分辨。**任何人不得把「不崩」写成这个长度的证据。**
- **判据有两类，缺一不可。** 四条逐帧判据钉「今天是 18」；
  `EveryFrameDeclaresTheSameJewelLength` 不指名任何数值，只钉「四个发送方彼此相等」。
  将来若查明 A21 其实发别的长度，改共用常量一处即可，四条逐帧判据会红、收敛判据保持绿——
  这是设计意图，不是漏洞。
- **穿戴那两条帧今天还发不出带时装的条目**，因为时装还穿不上（issue #11）。
  #11 一旦落地，这段立刻上线。邮件那条（`0x0061`）今天就在入场序列里。
- **字节锚本轮不变，不是因为它钉不住。** 变异 M-B 证明只改共用常量就会让它在第 18 帧变红。
  它本轮不变，是因为参照会话里既没人穿时装、也没有带时装附件的邮件。
- **一处已知的记载缺口（不阻塞）**：变化清单「剩下的 `30` 共 8 处」漏列 `Entry.cs:300`
  ——那是 `StoredJewelBytes` 自己的 XML 注释行，也是存储长度。正面主张
  「剩下的每个 `30` 都说存储长度」经验证方独立复扫为真。
- **`Docs/GM工具_新背包表结构与ItemCore语义.md:539` 有一句已过期的记载**（说 `0x000D`/`0x000E`
  仍写 `len=30`），由验证方发现。它是**上一轮**（`308ac02c`）推翻的，不在本轮 SC-8 的范围内，
  **留给后续处置**。
- **本轮最贵的教训在契约里，不在这份结果里。** 见 `TaskContract.md` 的
  `Contract Amendment C-1` 及其两节补充：记账层的四层错法，第四层是
  「`grep` 一个词只命中散文——表格、代码块、行号里的同一事实不含那个词，永远不会被命中」。
  这条是在写下它的同一次整改里又被自己绕过的。

## Artifacts

- `./behavior-change-list.md`（正向 3 条、反向 1 条、非行为改动 5 条、被推翻的说法 3 条，含搜索集与搜法）
- `./spec.md`（RQ-001..RQ-010 / AC-001..AC-011 / EV-001..EV-008 / AS-001..AS-004 / NA-001..NA-002）
