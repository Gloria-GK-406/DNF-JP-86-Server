---
kind: rks-task-contract
revision: 2
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-1
  - SC-7
  - SC-8
---

# TaskContract: 打死 boss 之后，结算界面出得来

## Context

### Source Request

根契约 `.rks-agent/tasks/finishing-and-leaving-a-run/TaskContract.md` 的第一个稳定成果。用户实机报告：「杀死boss后应该有修理商出现，并且展示回城UI」。

### Current Situation

2026-08-29 实机日志里，清完 boss 房之后是这样的：

```
Monster death ...: boss room 55611 of dungeon 144 is clear, opening the exit
warn: Unhandled Game frame ...: selector=0x01 type=0x0075 seq=126 body(39B)=0100E90E0000...
```

我们在检测到 boss 房清空的那一刻主动发出 `NOTI 0x001F ENABLE_CLEAR_DUNGEON` 与 `NOTI 0x0117 SECRET_SHOP_NPC(1000)`；随后客户端发来 `CMD 0x0075`，我们没有处理器。结算能走到（`SET_PLAY_RESULT` 收到了、经验也付了），但玩家说修理商与回城入口没出现。

### 一条必须先解开的线索

`ServerS4A21` 把 `0x0075` 叫 `BOSS_DIE_CHECK`，而它的处理器 **不发任何应答**：解析出 boss 的 running number，判断本局是否该清关，然后**由它去触发**清关流程（`SubmitClearIntentAsync`）。也就是说在那个服务端里，`ENABLE_CLEAR_DUNGEON` 是这一帧的**后果**，不是它的前提。

我们的顺序正好相反：先自己判定清关并开门，客户端才发来这一帧。

**所以本成果不能从"补一个应答"开始。** 要先弄清楚：客户端在什么条件下才摆出结算界面（修理商与回城入口）；缺的究竟是对这一帧的应答、是某个我们没发的帧、还是我们发得太早。上游只是线索——这个仓库已经两次因为照抄上游的命名与字节而错。

## Goal

打死 boss 之后，玩家在客户端上看得到结算界面：修理商在场，回城入口可用。并且弄清楚 `CMD 0x0075` 在这条链路里到底是什么，而不是让它继续挂在 `Unhandled` 上。

## Scope

### In

- 取证：客户端摆出结算界面的条件（客户端反汇编、实机试探，或两者）。
- `CMD 0x0075` 的解码与其在链路中的角色——无论结论是"要应答"、"是触发器"还是"可以忽略"，都要有依据并写下来。
- 使结算界面在实机上出得来所需的服务端行为。
- 该链路的失败与边界：不是 boss、没有 run、重复发来、run 已结算。

### Out

- 离开副本回到城镇（第二个成果）。
- 翻牌（第四个成果）。
- 结算屏上的数字（已在 `dungeon-run-experience` 完成）。
- 副本机关（上游的 `DungeonMechanismCoordinator` 处理机关型清关条件）——我们的清关判定是"boss 房的怪都死光"，本成果不改这条判定本身。

## Constraints

- 权威是客户端与实机。上游的 `BOSS_DIE_CHECK` 处理器只是线索，其应答字节不可照抄。
- 已验证的线级字节不得改变。
- 结论必须写下来：这一帧是什么、为什么这样应对。下一个人不该重新推一遍。
- 不得因为"日志里没有 warn 了"就认为成功——判据是实机界面。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 客户端在结算处所等的那一帧得到应答，且「等的是哪一帧」有客户端依据 | 应答的编解码测试；`protocol/game/client-to-server/` 中写明的反汇编依据 |
| SC-2 | `CMD 0x0075` 被解码，其在链路里的角色有明确结论与依据 | 解码测试；协议记录中写明依据 |
| SC-3 | 没有 run、没有会话、帧不可解、或重复发来时不会卡死也不会重复触发 | 各情形的测试 |
| SC-4 | 既有的清关判定与结算路径行为不变 | 既有测试未经修改仍通过 |
| SC-5 | 全套测试通过，构建零警告零错误 | `dotnet build` 与 `dotnet test` 输出 |

### SC-1 为什么在第 2 版里改了措辞

第 1 版写的是「打死 boss 后结算界面出现，修理商在场、回城入口可用 / 实机截图」。那是**根契约
SC-1 的原文**，连同它的实机判据一起抄进了子契约——在还不知道谁挡路的时候，这看起来只是把根的
条件落到第一个子成果上。

取证之后有两点变了：

1. **挡路的不是本帧。** 实机日志里 `0x0075` 未被应答，客户端照样发出了 `SET_PLAY_RESULT`、
   收下了结算帧、画完了结算卷轴。卡住的是再往后的 `0x0045`。
2. **实机判据拿不到。** 用户 2026-08-29 的指示是「先完成sdd，所有功能完成后再叫我」，一局实机
   要用户在场。子成果里没有它。

实机确认没有被放弃，它回到了它本来的位置：**根契约的 SC-1**，由四件全部做完之后那一次实机会话
给出。子契约只留它自己能证的东西。这样根任务在那次会话之前无论如何封不了口。

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根契约 `finishing-and-leaving-a-run` 的 `task` 行：用户 2026-08-29「起一个新的sdd来实现。可以用multispec，写完任务契约和spec等待吧。我压缩一下上下文再一次性跑完。」 | 在本任务工作树内实现本成果并请求一次验收提交 |

## Assumptions

- 客户端在 `39` 字节的 `0x0075` 体里报的是 boss 的 running number 与一批附带信息。捕获样本：`0100E90E000000000000000000010000002F0200000000000071020D0192013E010A0008001100`。解码要自己核，不照抄上游的 `BossDieCheckRequest.TryParse`。
