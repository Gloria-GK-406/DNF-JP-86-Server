# 队伍委任队长与踢出队员流程

本页只描述协议编排。单帧字段与证据分别见
[`CMD 0x0079`](../game/client-to-server/0x0079.md)、
[`CMD 0x000E`](../game/client-to-server/0x000E.md)、
[`NOTI 0x001A`](../game/server-to-client/NOTI-0x001A.md) 与
[`NOTI 0x0009`](../game/server-to-client/NOTI-0x0009.md)。2026-09-19 双客户端证据见
[`a21-party-leadership-removal-live-20260919.md`](data/traces/a21-party-leadership-removal-live-20260919.md)。

## 委任队长

```text
当前队长 C2S CMD 0x0079 { targetSlot }
  → 服务端校验发起者仍是当前队长、目标槽仍属于同一队伍且不是发起者
  → 保持 party id、成员顺序与 SlotIndex，只改变 leader
  → 对每个当前精确成员，依次发送：
      S2C NOTI 0x001A { hostSlot }
      S2C NOTI 0x0009 type=2 { roster, leaderSlot }
  → 成功路径无 CMD ACK
```

通知 audience 是 mutation 提交时的当前精确成员；replacement session 或已经离队的旧连接不应收到。
2026-09-19 A21 两端确认 slot 1 接任后权限立即生效。

## 踢出队员

```text
当前队长 C2S CMD 0x000E { targetSlot }
  → 服务端按稳定 SlotIndex 找目标，并校验发起者仍为队长
  → 从队伍移除目标；若目标属于地下城实例，同时解除其参与关系
  → 被踢目标：S2C NOTI 0x0009 type=3 clear
  → 幸存成员：当前 realtime → endpoint → S2C NOTI 0x0009 type=0 refresh
  → 同区非成员：按城镇目录规则得到幸存队伍的 type=0 投影
  → 成功路径无 CMD ACK
```

无效槽位、非队长或竞态下成员关系已经改变时，服务端返回同 type CMD `0x000E body=00 13`，不执行
领域变更。该拒绝路径和地下城返回路径尚未由 A21 实机覆盖，不能沿用城镇成功路径的
`ClientVerified` 状态。
