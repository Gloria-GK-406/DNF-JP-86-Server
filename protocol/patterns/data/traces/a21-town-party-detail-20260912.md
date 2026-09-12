# A21 城镇组队与角色详情实机闭环（2026-09-12）

## 环境与证据边界

- 客户端：当前 A21 双客户端，分别登录两个账号并进入同一 Game listener。
- 服务端：`town-party-lifecycle` worktree 的 Release Host；A21 `Script.pvf`；现有生产形状 SQLite 数据库。
- 本轮 ClientHack 未部署 `0100_NetworkHook`，因此没有可归档的真实 S2C body。本文只把服务端已接受的
  C2S 语义、用户可见 UI 结果和当前实现/自动化已固定的发送顺序关联起来；不把 UI 成功冒充逐字节抓包。

## 用户可见结果

用户在执行完整 live oracle 后确认“测试 ok”：

1. 两个角色在 PVF `[gate]` 赛丽亚到达房间互不可见，进入同 listener 的共享 `[normal]` 区域后互相出现。
2. 目标收到正确的组队邀请；接受后两端都保持存活并显示同一两人队伍。
3. 两端头像在完整名册建立后立即显示 HP，不依赖每个客户端各自先发送 `0x00A6`。
4. 对在线队友点击“查看角色详情”后窗口打开并显示目标角色信息。
5. 普通成员主动离队后本人恢复无队；队长侧保留单人队，并出现“保留队伍/移除队伍”的原生选择。

最后一项同时揭示了后续独立问题：单人队不是自动销毁；客户端把后续处置暴露成两个动作，而且队伍还有
剧情、循环、普通地下城三种类型。本记录只证明当前普通成员离队闭环，不把这些范围外设置解释成已实现。

## 同一会话窗口的服务端关联

```text
SET_UDP_IP_PORT character=1 accepted=True failure=None
SET_UDP_IP_PORT character=2 accepted=True failure=None
REQUEST_PEER inviter=1 target=2 clientReportedReviveCoins=64 accepted=True failure=None
RESPONSE_PEER invitee=2 inviter=1 accepted=True failure=None
GET_USERINFO mode=3 requester=2 target=1 accepted=True failure=None
LEAVE_PARTY character=2 accepted=True failure=None
CALL_PARTY_MEMBER_REALTIME_INFO character=1 inParty=True
```

当前 Information 日志不逐帧打印 S2C body。S2C 的类型、长度和顺序由同一 Release 二进制中的确定性
encoder/delivery 与本轮 3050/3050 自动化结果约束；客户端 UI 证明当前 A21 实际消费了该结果。
