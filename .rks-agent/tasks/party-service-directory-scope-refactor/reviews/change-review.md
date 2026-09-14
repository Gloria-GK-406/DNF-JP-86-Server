---
kind: rks-review
verdict: approved
---

# Change Review

## Verdict

**APPROVED**

基线：`623054bae44820c456adbec72b7f90f3868bb305`
目标：当前未提交工作树，忽略 `verification/test-results*` 下生成的 TRX。

## 阻塞项

无。

## 审查结论

- `PartyService` 独占队伍、成员关系、端点、邀请和目录索引状态；公开契约只使用角色身份、
  角色镜像和 Party 值对象。
- `Abstractions.Parties` 全部公开类型均被架构测试扫描；未发现 Session、连接身份、代际或
  ListenerPort 泄漏。
- 目录使用逻辑 `ChannelId + TownId + AreaId`；private、未共享和非城镇 presence 不进入公共目录。
- 队长移动、队长转移、离队和解散均同步维护索引，没有发现陈旧目录项。
- 跨连接选角先清理旧角色，再注册 replacement；stale teardown 只在 exact owner 条件成立时
  清理 Party。
- 通知接收者在 lifecycle gate 内捕获为 exact session，旧通知不会被重定向到 replacement。
- presence 写入和 Party scope callback 由每角色顺序锁覆盖；确定性交错测试验证回调不倒序。
- `SetUserAreaHandler` 使用同一账号 transition gate，并在取锁后重查 exact selected session，关闭
  区域移动与 Party 角色镜像写入之间的范围回退竞态。
- `PartyTownProjectionDelivery` 通过 `IPartyService.Query` 发现队伍，不通过 Session 列表反推。
- 延迟旧 observer plan 只在当前 Party revision 更高且队长范围确实变化时被抑制。
- 既有通知顺序和 codec 黄金测试继续通过。

## 独立验证

- Solution build：0 warning，0 error。
- Application：1420/1420 passed。
- Protocol：814/814 passed。
- `git diff --check`：passed。
- Party 禁止类型扫描：无匹配。

## 非阻塞观察

- `SetUserAreaHandler` 为正确性把账号 transition gate 保持到网络投递结束；慢速投递可能延长同账号
  重连等待，未来可在完整捕获投影计划后提前释放。
- `SessionPresenceDomainService` 的每角色顺序锁不回收，会为每个曾出现角色保留一个小对象；这是
  长期运行内存优化项，不影响本次正确性。
- 本轮未执行双客户端实机回归；继续标记为待复测，不提升 ClientVerified 状态。
