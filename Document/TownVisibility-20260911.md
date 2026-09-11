# 城镇同屏玩家同步（2026-09-11）

## 协议结论

城镇里让两个玩家真正互相出现，不是一条协议完成，而是一个最小生命周期：

1. `NOTI 0x0002 USERINFO` subtype `0`：创建远端角色所需的身份与公开外观。
2. `NOTI 0x0165 CHARAC_INVISIBLE_FALGS`：同步可见性位。
3. `NOTI 0x0017 USER_AREA`：把角色放进指定城镇区域与坐标。
4. `NOTI 0x0018 AREA_USERS`：发送完整同区名册；每个接收者的名册都以自己开头。
5. 客户端 `CMD 0x0023 SET_USER_POSITION` 后，服务端以 `NOTI 0x0016 USER_POSITION`
   回给本人并广播给同区其他角色。
6. 换区或断线时，以 `NOTI 0x0006 USER_LEAVE` 从旧区域其他客户端移除角色。

只发送 `0x0017 + 0x0018` 不足以创建远端角色对象；`0x0002` subtype `0` 必须先到。

## 服务端映射

- 选角发布与 `0x0024 SET_USER_AREA` 换区都会从在线会话、实时位置和区域 presence
  生成同区快照。
- 新增 `TownParticipantPublished`、`TownPositionChanged`、`TownParticipantDeparted`
  三种 Event。Handler 只发布区域事实与角色受众；通知总线负责把角色解析到当前连接、
  隔离单个接收者的发送失败并执行消息组合。
- 同区只有本人时保留原来的 `0x0017 + count=1 的 0x0018` 行为。
- `RelocateExact` 只允许当前会话 generation 迁移 presence，避免旧连接改动替代会话。

## 验证记录

```yaml
kind: rks-verification
decision: manual_required
claim: 城镇同屏服务端最小生命周期已经实现并通过自动化验证
```

- 自动化覆盖：选角进入已占用区域、跨区离开旧受众并加入新受众、同区移动广播、
  generation-safe presence 迁移、双方消息顺序与个性化完整名册。
- 完整解决方案测试需使用实际 `Script.pvf`；结果及构建状态以本次提交前的终端验证为准。
- 未验证条件：A21 双客户端是否按该序列正确创建、移动和移除远端角色对象。因此协议目录
  只标“实现服务端功能”，不能标记为 `ClientVerified`。

## 实机验收动作

1. 两个客户端分别登录不同账号角色并进入同一 town/area；双方都应看到对方外观。
2. 任一角色移动；另一端应连续更新位置。
3. 任一角色跨区；旧区域端应移除该角色，新区域端应创建该角色。
4. 任一角色正常退出或断线；另一端应移除该角色。

若任一步失败，保留两个客户端各自的 `clienthack-frame-capture.log`，按接收方向核对上述
NOTI 的实际顺序与 body。
