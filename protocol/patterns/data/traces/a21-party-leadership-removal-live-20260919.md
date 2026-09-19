# A21 委任队长与踢出队员实机闭环（2026-09-19）

## 范围与证据边界

- 客户端：同一 A21 固定客户端，两实例、两名角色、城镇两人队。
- 服务端：`main`，功能提交 `99db6063`，Release Host。
- 客户端操作：原队长把队长委任给 slot 1 成员；新队长随后踢出仍位于 slot 0 的原队长。
- 实机结果：用户确认委任与踢出功能正常；队长权限完成转移，被踢者退出队伍，幸存者保留队伍。
- 服务端发送日志源：`dfo-host-20260919-093922.out.log`，取证时 SHA-256
  `D72DB150C828DF752B60D25490C9520396A5D7E56596C4F29FAD965509E90576`。

本次两个客户端目录没有加载 `0100_NetworkHook`，因此没有客户端侧原始十六进制帧抓包。下面的
selector、type、完整帧长度与顺序来自服务端逐帧发送日志；客户端如何表现来自双端实机观察。
因此本记录支持 `ClientVerified`，不标记 `WireCaptured`，也不虚构 `samples/*.hex`。

## 委任队长

Host 先接受 `CHANGE_HOST`，日志明确记录原队长 character 2、目标 character 1、目标 slot 1：

```text
Command TransferPartyLeadershipCommand handled ...
S->C Game:01-00-00-00 selector=0x00 type=0x001A frame=16 UdpHostNotification
S->C Game:01-00-00-00 selector=0x00 type=0x0009 frame=64 PartyRosterMessage
S->C Game:02-00-00-00 selector=0x00 type=0x001A frame=16 UdpHostNotification
S->C Game:02-00-00-00 selector=0x00 type=0x0009 frame=64 PartyRosterMessage
CHANGE_HOST leader=2 target=1 slot=1 accepted=True failure=None
```

S2C Game header 为 15 字节，因此两种 body 分别为：

- `NOTI 0x001A`：1 字节；当前编码值为新队长 slot `1`；
- `NOTI 0x0009 type=2`：49 字节；不带信息块，固定八槽名册，offset 46 为新队长 slot `1`。

两个精确成员按各自的 `0x001A → 0x0009 type=2` 顺序收到通知。客户端最终保持原 party id 和原成员
槽位，slot 1 成员获得队长身份及权限。这里证明的是两帧组合的最终效果，未隔离单帧独立作用。

## 踢出原队长

委任完成后，character 1 作为新队长踢出 character 2；目标仍位于 slot 0：

```text
Command RemovePartyMemberCommand handled ...
S->C Game:01-00-00-00 selector=0x00 type=0x0009 frame=20 PartyRosterMessage
S->C Game:02-00-00-00 selector=0x00 type=0x0099 frame=21 PartyRealtimeMessage
S->C Game:02-00-00-00 selector=0x00 type=0x000B frame=38 PartyEndpointsMessage
S->C Game:02-00-00-00 selector=0x00 type=0x0009 frame=80 PartyRosterMessage
S->C Game:01-00-00-00 selector=0x00 type=0x0009 frame=80 PartyRosterMessage
WALKOUT_PARTY_MEMBER leader=1 target=2 slot=0 accepted=True failure=None
```

按 15 字节 S2C header 换算：

- 被踢者先收到 5 字节 `type=3` clear；
- 幸存者收到当前单人成员的 realtime、endpoint 与 65 字节 `type=0` 完整 refresh；
- 被踢者清除自己的成员身份后，又作为同区非成员收到幸存队伍的 65 字节目录投影。

成功路径没有出现 selector `0x01`、type `0x000E` 的 CMD ACK。客户端最终表现正常，同时证明队长
判断不能绑定 slot 0：发起踢人的新队长在 slot 1，被踢的原队长在 slot 0。

## 未覆盖

- 非队长、无效槽位或成员关系已变化时的 CMD `0x000E body=00 13`；
- `CHANGE_HOST` 的失败静默路径；
- 剧情地下城对委任按钮的客户端限制；
- 地下城内踢人及被踢者返回城镇；
- `NOTI 0x001A` 与 `0x0009 type=2` 的单变量效应隔离。
