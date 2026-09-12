# TASK-04 当前 A21 队长离队语义纠正

## 证据范围

- 最终定向帧：`live-final-targeted-frames.jsonl`；仅保留 C2S/CMD `0x000C/0x000D` 与直接相关的
  S2C `NOTI 0x0009/0x000B/0x0099`，同号 S2C `NOTI 0x000D ITEM_LIST` 已排除。
- 环境与哈希：`live-preparation.md`。
- Host 使用 spec-002 当时的最终实现：队长离队先清旧 party，再为幸存者创建并投影新 party。
- 用户 UI 观察：队长离队后显示“队伍已解散”；用户确认当前版本应直接解散，不沿用旧版自动转移。

## 可归因序列

两名当前 A21 角色在同一 `10010` 游戏频道组成 Normal 队伍后，队长角色 2 离队：

```text
2026-09-12T10:45:01Z  game-10010-0004  C2S 0x000D  bodyLength=0
2026-09-12T10:45:01Z  game-10010-0004  S2C NOTI 0x0009  01 00 01 00 03
2026-09-12T10:45:01Z  game-10010-0003  S2C NOTI 0x0009  01 00 01 00 03
2026-09-12T10:45:01Z  game-10010-0003  S2C NOTI 0x0009  type-0 partyId=2 ...
```

Host 同轮记录 `LEAVE_PARTY character=2 accepted=True failure=None`，随后角色 1 的实时查询仍返回
`inParty=True`，证明服务端确实执行了 replacement，而不是只发 clear。客户端 UI 仍显示旧队已解散，
因此“服务端继续建新队即可实现队长转移”被当前 A21 直接反驳。

角色 1 随后离开 replacement：

```text
2026-09-12T10:45:56Z  game-10010-0003  C2S 0x000D  bodyLength=0
2026-09-12T10:45:56Z  game-10010-0003  S2C NOTI 0x0009  01 00 02 00 03
```

同角色重新创建 Normal 预设标题队伍：

```text
2026-09-12T10:46:03Z  C2S 0x000C  00 03 04 00 00 00 00 05 00 02 00 00
2026-09-12T10:46:03Z  S2C NOTI 0x0009  type-0, partyId=3, titleIndex=3, target=0
```

这同时复核了独立的字段交换、预设标题索引和 Normal target `0` 修复，不因生命周期纠正而撤销。

## 裁决与实现边界

- 当前 A21 队长离队：整队解散，向全部旧成员投影旧 party id 的 type-3 clear；`ClientVerified`。
- 当前 A21 普通成员离队：保留同 generation 幸存队并刷新；沿用既有 ClientVerified 生命周期。
- 旧版上游的自动转移：冲突参考，不属于当前 A21 规则。
- 删除仅为自动转移增加的 `PartyRetirement`、replacement party 和退休通知流；保留 Normal target、
  C2S→canonical 字段交换与标题编码。

## TDD 纠正证据

保护结果是“队长离队后所有原成员均无队且只收到旧队清除”。现实回归风险是旧版兼容逻辑再次创建
replacement，导致服务端状态与当前 A21 UI 分裂；领域快照/通知计划测试直接观察该行为，且不依赖内部
helper 或类结构。

- RED：在 transfer 实现仍存在时运行两个队长离队/会话清理用例，`2 Failed / 0 Passed`；失败值均为
  不应存在的 replacement `PartySnapshot`。
- GREEN：移除 replacement/retirement 后同一命令 `2 Passed / 0 Failed`。

## 运行收尾

本轮 Host、中转和两份 DNF 客户端均已结束；`7001/10010/10011/17001/18080/20010/20011` 无本轮监听。
