---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: The session copy keeps up

## Approach

`MoveItemSpaceHandler` already holds both collaborators this needs -- the runtime directory and the
inventory service -- because the previous round gave it them to build the appearance frame. That frame's
method already fetches the copy and reloads the inventory. So the work is to make that one reload serve two
purposes and to write the reloaded inventory back onto the copy, replacing one member.

Nothing new is injected, no interface changes, and the six existing refresh sites are untouched.

## Decisions Before Work

**DP-001 -- Replace `CharacterInventory` on the copy, not the copy.**
Settled by the enumeration rather than by preference: fourteen of sixteen readers take members a move
cannot change, and a `with` expression hands them the same instances. Rebuilding the whole result would
mean re-preparing entry state on every drag and would put those fourteen readers behind a re-read they do
not need.

**DP-002 -- Refresh on every successful move, not only on moves that change the drawn appearance.**
The two stale readers take the whole Equipment container, and `ChangesWhatTheCharacterLooksLike` answers a
narrower question -- appearance slots `0..12` and `23`, plus creature slot `24`. A move onto worn slot 13 changes equipment
without changing the drawn appearance, and the copy would stay stale under the narrower rule. The wider
rule is also the simpler sentence: what a move changed, the copy follows.

**DP-003 -- The refresh shares the appearance frame's failure isolation, and needs it for a reason of its
own.** `ReplaceExact` throws `InvalidOperationException` when the session generation has moved on
(`SelectedSessionRuntimeDirectory.cs:26`). This can occur if the entry is removed or a different generation is published between
`TryGet` and `ReplaceExact`, across the inventory load. A same-generation replacement does not throw. The move is already committed by this point and the client holds the inventory
shut until it is acknowledged, so the refresh must not be able to take the acknowledgement with it.

## Work and Validation

| ID | Covers | Work and boundary | Check and expected result |
|---|---|---|---|
| T-001 | RQ-001, RQ-002, RQ-005 | In `MoveItemSpaceSlice.cs`, fold the reload and the write-back together: fetch the session copy once, reload the inventory once, `ReplaceExact` the copy with `state with { CharacterInventory = reloaded }`, and build the appearance frame from the same reloaded equipment when it is wanted. | Builds; existing move checks stay green. |
| T-002 | RQ-004 | The refresh runs only where `result?.Status is ItemMoveStatus.Moved` already gates the appearance frame. | AC-004. |
| T-003 | RQ-003 | The refresh sits inside the existing `try`/`catch` that protects the acknowledgement, and the log line says the move stands. | AC-003. |
| T-004 | RQ-001 | A check that moves a piece of equipment and then reads what the dungeon selection frame carries. | AC-001. Fails before T-001. |
| T-005 | RQ-002, RQ-006 | A check reading the resident copy after a move: `CharacterInventory` changed, the other eight members reference-equal, the position flag unchanged. | AC-002, AC-006. |
| T-006 | RQ-003 | A check whose `ReplaceExact` raises, asserting the acknowledgement is still sent. | AC-003. |
| T-007 | RQ-005 | A check counting the inventory loads a single appearance-changing move performs. | AC-005. |
| T-008 | all | Mutations: strike the `with { CharacterInventory = ... }` clause (clone the CharacterEntry member instead of retaining its reference) so T-005 reddens; remove the refresh so T-004 reddens; move the refresh outside the `try` so T-006 reddens. At least one must redden by value mismatch. | AC-007. |
| T-009 | all | The forward-and-reverse change list, derived from the code, and the records this falsifies -- including the prose count at `MoveItemSpaceSlice.cs:814`, which this work makes wrong in two ways at once. | C-6, C-8. |
| T-010 | all | Full suite, output kept and searched. | AC-008. |

T-001 is consumed by everything. T-008 consumes T-004..T-007.

## Choices During Implementation

- Whether the refresh and the appearance frame stay one method or become two. One method reads the copy
  once and cannot forget to reload; two separate the concerns. The live shape of `AppearanceAsync` decides.
- How T-007 counts loads. A counting stand-in for `ICharacterInventoryEntryDomainService` is the obvious
  way; whether the existing test harness already has one decides whether it is worth adding.

## Risks

- **The one-member replacement is the whole correctness argument, and it is easy to write as a whole-result
  replacement by accident.** T-008's first mutation exists to make sure a check would notice.
- **`ReplaceExact` throwing is not hypothetical.** It is how the directory refuses a stale generation, and
  removal or generation change during the load is one such case; a generation already missing at TryGet returns quietly.


## 2026-09-08 评审整改决定

- BF-1/BF-5：调用真实 EnterSelectDungeonHandler，在穿上与脱下之后读取 ExtendedUserStateMessage；库存加载计数同时检查外观与后续界面。运行态测试封装真实 SelectedSessionRuntimeDirectory，并检验位置持久化标志 true/false。
- BF-2：保留更新被拒时只发送成功确认的行为，明确记为外观发送的新增抑制条件。读到已经失效的会话时不构造外观是安全选择。
- BF-7：按评审的“bound it or list it”记录现有整体覆盖风险。当前边界明确不改其他六个写点；不宣称八成员保留等于并发安全。此处未新增接口或全局调度策略。
- P1 精确修改为 `state with { CharacterInventory = reloaded, CharacterEntry = state.CharacterEntry with { } }`，预期 Assert.Same 身份不等；按字面重建整个 result 但复用所有成员不会被此判据拒绝，也不应拒绝。
