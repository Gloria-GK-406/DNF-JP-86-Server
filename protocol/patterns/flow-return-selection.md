# 返回角色选择界面（A21）

状态：服务端已实现；实机闭环待验证（2026-09-08）。

用户点击“选择频道”时，本地服务端实际收到 C2S CMD `0x0007` 空体，详见
[证据定位](data/probes/a21-return-selection-20260908.md)。该动作目前按“返回选人”处理；
是否完整覆盖客户端后续频道菜单切换，尚未通过实机验证。

## 本次采用的发送顺序

```text
C2S CMD  0x0007  空 body
S2C NOTI 0x0245  五个 i32 零（仅当前角色为守护者 job=12）
S2C CMD  0x0007  01
S2C NOTI 0x0002  subtype=02，最新角色名单；header byte 7=0
S2C NOTI 0x0289  账号荣誉信息
```

发送顺序来自固定远端提交 `c58cd2b479b0357cb6ca78887c9d638b080f4638` 的
`Handle_ENUM_CMDPACKET_RETURN_SELECT_CHARACTER` / `SendCharacterListAsync`，**High，参照实现，尚未实机确认**。
客户端对 CMD 成功信封和 NOTI 名单 subtype 2 的消费有独立指令证据，但这不能单独证明整批已被接受。

返回时使用普通名单刷新帧，`header byte 7=0`。GET_USERINFO 的查询应答使用另一路标志，不能直接把它的
整个 handler 当作返回流程复用；这里也不额外发送 `CMD 0x0286` 隐藏角色信息。
荣誉子系统仍沿用当前服务端的初始值投影。

## 实机验收

1. 进入角色，换一件装扮，点击本次触发 `0x0007` 的“选择频道”入口。
2. 确认回到选人界面，角色名单外观反映当前穿戴。
3. 再次选择角色进入，确认不会断线、卡住或残留上一会话状态。
4. 记录实际出站顺序和客户端可观察结果后，才能把阶段升级为“双向验证结束”。

[字段与信封](../game/client-to-server/0x0007.md) · [实现与自动验证](../../Document/ReturnToSelection-20260908.md)
