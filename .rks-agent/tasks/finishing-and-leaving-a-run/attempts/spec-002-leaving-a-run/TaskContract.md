---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-2
  - SC-7
  - SC-8
---

# TaskContract: 打完之后走得回城镇

## Context

### Source Request

根契约的第二个稳定成果。用户实机报告：「在副本回到城镇不好用」。

### Current Situation

2026-08-29 实机日志里，`Give up game ... run ended=True` 之后紧接着是一整套重新登录握手——客户端
把整个会话从头重建了一遍。

**取证推翻了根契约对这一条的判读。** 上游一次完整通关的抓包
（`ServerS4A21/Server/DfoServer/bin/Debug/packet_log.upstream-killexp-20260827.txt`）里
**根本没有 `CMD 0x002A GIVEUP_GAME`**。客户端离开副本用的是 **`CMD 0x0048 EPLP_COMMAND`**，
两字节 `state option`，服务端回 `[01 state option]`，随后主动推城镇状态
`NOTI 0x0003 / 0x0017 / 0x0018 / 0x00CA / 0x0002`。

**本仓完全没有 `0x0048` 这个 type。** 按钮按下去，落到一个没有这一 type 的服务端上。

`Docs/ProtocolScaffoldDissonances.md` 第十四条（回城时不推城镇状态）记的是 `0x002A` 之后的事，
仍然成立，但**不是这次的成因**。

## Goal

打完一局之后按下离开，角色回到它进副本前站着的地方，客户端不必重连。

## Scope

### In

- `CMD 0x0048` 的解码与应答。
- `state == 1`（离开）时结束本局、并把城镇状态推给客户端。
- 与「取消副本选择界面」共用同一份城镇投影，不留第二份实现。
- 该链路的失败与边界：没有会话、没有 run、非离开的 state、体长不足。

### Out

- `NOTI 0x00CA VILLAGE_OBJECT_LIST` 与游戏端的 `NOTI 0x0002 USERINFO`：上游在城镇状态之后还推
  这两帧，本仓两者都没有；`BACK_2_VILLAGE` 这条早已在用的路子也不推，实机上回城正常。加它们要
  各自的取证，不在本成果内。
- `CMD 0x002A GIVEUP_GAME`：仍然登记着、仍然应答，本成果不动它。
- 翻牌（第四个成果）。

## Constraints

- 权威是客户端反汇编与实机；上游抓包只有线索价值。
- 已验证的线级字节不得改变。
- 城镇投影不得出现第二份实现——写两遍会漂，漂出来的样子正是「从副本选择界面回城好用、从副本里
  回城不好用」。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | `CMD 0x0048` 被解码，应答的形状与客户端读取它的方式相符 | 编解码测试，判据钉在抓包的字节与客户端应答 handler 的读取序列上 |
| SC-2 | `state == 1` 时本局结束，且城镇状态随应答一起发出 | 处理器测试 |
| SC-3 | 其余 state 被应答但不结束本局、不推城镇 | 各值的测试 |
| SC-4 | 城镇投影只有一份实现，两条路走的是同一份 | 代码结构；两条路各自的测试断言同样的落点 |
| SC-5 | 没有会话、没有 run、体长不足时不卡死也不误发 | 各情形测试 |
| SC-6 | 全套测试通过，构建零警告零错误 | `dotnet build` 与 `dotnet test` 输出 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根契约 `finishing-and-leaving-a-run` 的 `task` 行 | 在本任务工作树内实现本成果并请求一次验收提交 |

## Assumptions

- `state == 1` 即「离开」：客户端自己的应答处理器（RVA `0x00D06C00`）比较返回的 state 与 1，
  只有相等才走离开那一支；上游读同一字节。两边一致，且抓包里只有第二次 `01 02` 之后才推城镇。
- 城镇状态推三帧（`0x0003 / 0x0017 / 0x0018`）就够：`BACK_2_VILLAGE` 这条路一直只推这三帧，
  实机上回城正常。
