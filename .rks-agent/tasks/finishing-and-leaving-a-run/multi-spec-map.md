---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: in_progress
active_attempt: spec-001-boss-die-check
last_transition: child-created
transition_evidence: >-
  workspace-ready: git worktree add .worktrees/finishing-and-leaving-a-run -b
  finishing-and-leaving-a-run c5a36a1d; in-worktree toplevel, branch and HEAD verified; source
  branch main clean at c5a36a1d with the previous task merged and its worktree removed.
  root_evaluation at c5a36a1d: SC-1..SC-8 all not_started, every constraint aligned ->
  gaps-remain. successor_decision: the smallest stable outcome is answering BOSS_DIE_CHECK,
  which nothing else depends on and which is what currently strands the player at the end of a
  run -> safe-successor. child_modeling: spec-001-boss-die-check contract ready and contained,
  routing decision complete, no other active row.
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/finishing-and-leaving-a-run/.rks-agent/tasks/finishing-and-leaving-a-run
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/finishing-and-leaving-a-run
  branch: finishing-and-leaving-a-run
  baseline: c5a36a1d898b5aeb7154f79489e25b7eb0625d7d
  baseline_evidence: >-
    c5a36a1d "Give the host's refusal long enough to happen on a busy machine"; source tree
    clean; suite at that commit 1584 passed / 2 skipped / 0 failed. Every test run needs
    PVF_ARCHIVE_PATH=/d/project/DNF/DNFClient-A21-protocol/Script.pvf -- without it
    ItemPathLiveTests and the archive-backed content tests fail for want of the archive, which
    reads as a regression and is not one.
---

# Multi-Spec Map

## Multi-Spec Boundary

四个成果分属三个互不相干的子系统，各自独立可验收、独立可回滚，彼此没有实现依赖：

1. **应答 `BOSS_DIE_CHECK`** —— 结算界面出得来。单独交付即可玩。
2. **离开副本回到城镇** —— 不再逼客户端重连。与 1 无代码交集。
3. **地面掉落的数量语义** —— 落在进入副本的帧上，与结算那一圈完全无关。
4. **翻牌奖励** —— 一整个新领域，最大的一块，且带真实阻塞风险：奖励内容的来源尚未取证。取不到就该单独阻塞，不拖掉前三个。

切法是按「哪一个失败了不该连累另外三个」，不是按文件数或工时。

## Live Evidence This Task Starts From

2026-08-29 实机验收 `dungeon-run-experience` 时的服务端日志（同一局，副本 144）：

```
Monster death ... : boss room 55611 of dungeon 144 is clear, opening the exit
warn: Unhandled Game frame ...: selector=0x01 type=0x0075 seq=126 body(39B)=0100E90E0000...
Set play result ... : dungeon 144 cleared in 136477ms at rank 99; clearBase=552 rankBonus=110 award=662
warn: Unhandled Game frame ...: selector=0x01 type=0x0045 seq=131 body(0B)=
```

以及中途的 `type=0x002F seq=82 body(12B)=2402EA0000790044CF703501`（`DROP_ITEM`，本任务范围外）。

第一局（频道 10）的 `Give up game ... run ended=True` 之后紧接着一整套重新登录握手，这就是"回城不好用"的样子。

拾取那一次：`Get item ... : drop 1 is item 10088621, into slot 121`。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 打死 boss 后结算界面出得来，修理商在场 | in_progress | 服务端已应答客户端在结算处等待的 `CMD 0x0045`；`CMD 0x0075` 的角色已定论 | **实机未验**。且上游对 `0x0045` 回两帧，第二帧 `0x0046` 是翻牌版式，属第四件 | aligned |
| SC-2 | 结算完成后能走回城镇，客户端不需要重连 | in_progress | `CMD 0x0048` 已应答并推城镇状态；四个变异均被判据抓住 | **实机未验** | aligned |
| SC-3 | 地上的堆叠物显示真实数量 | in_progress | 堆叠物改写档案声明的数量；副本 144 boss 房声明 3，读真档案的判据钉住 | **实机未验**；`+7` 的双重语义尚未从客户端反汇编直接确认 | aligned |
| SC-4 | 地上的装备仍能被逐件区分 | satisfied | 装备仍写实例判别值，判据断言它与拾取写进背包的是同一个数；既有 byte-for-byte 参照帧未改仍通过 | none | aligned |
| SC-5 | 能翻牌，翻到的进背包，一局只能翻一次 | not_started | none | 第四件未开工；奖励内容的规则来源待与用户共同确定 | aligned |
| SC-6 | 翻牌规则住在领域里，handler 只收发 | not_started | none | 同上 | aligned |
| SC-7 | 四件的失败路径不卡死、不重复发奖 | in_progress | 前三件各自的失败路径都有判据；三个新解码器都改成 decline 而不是抛（抛会掐断连接） | 第四件未开工 | aligned |
| SC-8 | 全套测试通过，构建零警告零错误 | in_progress | 1614 passed / 2 skipped / 0 failed，0 警告 0 错误 | 第四件落地后重跑 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001-boss-die-check` | `attempts/spec-001-boss-die-check/TaskContract.md` | 打死 boss 后结算界面出得来 | SC-1, SC-7, SC-8 | `sealed` | completed | 见下 |
| 2 | `spec-002-leaving-a-run` | `attempts/spec-002-leaving-a-run/TaskContract.md` | 打完之后走得回城镇 | SC-2, SC-7, SC-8 | `sealed` | completed | 见下 |
| 3 | `spec-003-floor-quantities` | `attempts/spec-003-floor-quantities/TaskContract.md` | 地上的东西显示真正的数量 | SC-3, SC-4, SC-7, SC-8 | `sealed` | completed | 见下 |

### 三个子成果同时开着，这是有意的偏离，理由记在这里

Multi Spec 要求一次只有一个 `running`。这里同时有三个，因为 spec-001 的独立评审跑了三十多分钟
（它要复核客户端反汇编、跑全套、并做变异测试），而用户明确要求把四件一次跑完。干等不是选项。

**保住的是这条规则真正保护的东西**：三个成果的文件集互不相交，因此各自仍可独立验收、独立回滚。

| 成果 | 独占的文件 |
|---|---|
| spec-001 | `Game/DungeonClear/ResultScreenSlice.cs`、`.../ResultScreenTests.cs`、`protocol/.../0x0075.md`、`protocol/.../0x0045.md` |
| spec-002 | `Game/DungeonClear/LeaveDungeonSlice.cs`、`Game/TownProjection/TownReturnProjection.cs`、`.../LeaveDungeonTests.cs`、`Game/DungeonSelection/DungeonSelectionSlice.cs` |
| spec-003 | `Game/DungeonEntry/FloorPieces.cs`、`Game/DungeonEntry/DungeonEntrySlice.cs`、`Infrastructure/Content/PvfDungeonRoomDropSource.cs`、`Abstractions/Content/DungeonRoomActorContent.cs`、`.../DungeonEntryCodecTests.cs` |

三处共用文件（`GameProtocol.cs`、`NetworkMessages.cs`、`ProtocolModule.cs`）各自只加了自己的常量
与登记行，互不覆盖。

**代价，如实记下**：评审在同一棵工作树里做变异测试，期间的构建与测试结果不可信——2026-08-29
有一次 `ScoreScrollStateType` 被改成 `0x0046`，我的判据当场把它抓住了（这正说明判据有力），
但那一次的全套结果是脏的。下一个成果的评审要么在独立工作树里跑，要么在评审期间停止本树的构建。

## 2026-08-29 取证：整条收尾链（推翻了根契约里那张上游对照表）

根契约的成因表来自 `ServerS4A21` 的**命名**，两处是错的。以下取自**客户端普查 + 客户端反汇编 +
上游一次完整通关抓包**（`ServerS4A21/Server/DfoServer/bin/Debug/packet_log.upstream-killexp-20260827.txt`
第 748 行起）。抓包是线索不是权威，但它给出的是一条**确实走通了的**收尾链：

```text
SEND NOTI 0x001F (1B) [00]                 ENABLE_CLEAR_DUNGEON     本仓已发
SEND NOTI 0x0117 (4B) [E8 03 00 00]        SECRET_SHOP_NPC = 1000   本仓已发，值一致
SEND CMD  0x03D0 (13B)                     未知                      本仓不发
SEND NOTI 0x03C1 (8B)                      未知                      本仓不发
RECV CMD  0x002E                           SET_PLAY_RESULT
SEND NOTI 0x0022 / 0x0025 / 0x0023         结果、经验、奖励          本仓已发
SEND NOTI 0x0015 / 0x0002 / 0x0005         任务表、角色投影          本仓部分
RECV CMD  0x0075 (39B)                     BOSS_DIE_CHECK  —— 不应答（客户端不为它登记应答 handler）
RECV CMD  0x0045 (0B)                      SCORE_SCROLL_STATE
SEND CMD  0x0045 (1B) [01]                 ─┐ 上游 HandleCardStartRequest 一次发两帧
SEND CMD  0x0046 (17B) [01 01 00 FF×14]    ─┘ = SendLayoutAsync，翻牌面板的版式
RECV CMD  0x0047 (2B) [00 00]              SELECT_CARD（免费卡、第 0 张）
SEND CMD  0x0047 (33B)                     翻牌结果
SEND NOTI 0x000E (205B)                    背包变化
RECV CMD  0x0048 (2B) [02 02] -> SEND [01 02 02]   EPLP_COMMAND
RECV CMD  0x0048 (2B) [01 02] -> SEND [01 01 02]   state==1 即"离开"
SEND NOTI 0x0003 (4B) [01 03 00 00]        USER_STATE   ─┐
SEND NOTI 0x0017 (10B)                     USER_AREA     ├ 城镇状态，上游主动推
SEND NOTI 0x0018 (12B)                     AREA_USERS    │
SEND NOTI 0x00CA (1B) [00]                              │
SEND NOTI 0x0002 (189B)                    角色投影     ─┘
```

**由此定下的三条结论：**

1. **`CMD 0x0075` 不是任何一件的成因。** 实机里它未被应答，客户端照样走完了结算。见
   `protocol/game/client-to-server/0x0075.md`。
2. **「回城不好用」走的不是 `GIVEUP_GAME 0x002A`。** 整段抓包里根本没有 `0x002A`；客户端用
   **`CMD 0x0048 EPLP_COMMAND`** 离开，本仓**完全没有这个 type**。`Docs/ProtocolScaffoldDissonances.md`
   第十四条记的是 `0x002A` 之后不推城镇状态——那条仍然成立，但**不是这次的成因**。
3. **结算界面要走完，`CMD 0x0045` 的应答是两帧**：`0x0045 [01]` 加 `0x0046` 版式。只发前者能不能
   走到回城 UI，静态断不了（客户端那处分支有一个查表 id 未定），要一次实机。

`item 10088621` 是用户捡到的那件材料，数量显示成几十万——`START_MAP` 掉落记录 **+7 的 u32**
（19 字节一条，布局见 `protocol/game/server-to-client/NOTI-0x001D.md` 的第二则 A21 补记）本仓写的
是实例判别值。上游那一位写的是 `DropInfo.PacketValue`：

```csharp
public uint PacketValue => Core == null ? StackCount : unchecked((uint)Core.Value);
```

即**有物品实例就写实例的 `value`，没有实例就写堆叠数量**——与本仓库里 `value` 这一列本身的双重
语义（装备是实例判别值、堆叠物是数量）是同一件事，见 `Docs/GM工具_新背包表结构与ItemCore语义.md`。
本仓无条件写实例判别值，于是材料的数量位上是个 31 位伪随机数。

## Planned Successors

模型化留给控制器逐个进行（Multi Spec 不预建未来的 Spec），此处只记顺序与理由：

2. **离开副本回到城镇** —— `CMD 0x0048 EPLP_COMMAND` 的应答，以及其后的城镇状态推送。与翻牌
   无代码交集：抓包里 `0x0048` 在没有 run 时也照答。
3. **地面掉落的数量语义** —— `DungeonEntrySlice.cs:279` 那个 u32 的双重语义。最孤立。
4. **翻牌奖励** —— `0x0046` 版式 + `0x0047` 翻牌，按用户要求实现为领域服务。最大的一块，
   放最后。根 SC-1「回城入口可用」的实机确认要等它一起。

每个子成果按 `single-spec-workflow` + `sdd-lite` / `code-feature` 执行，评审用独立子代理。
