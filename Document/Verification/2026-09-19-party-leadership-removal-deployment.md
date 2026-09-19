# 委任队长与踢出队员：部署验证

```yaml
kind: rks-verification
decision: manual_required
```

## 验证目标

确认 A21 `0x0079` 委任队长与 `0x000E` 踢出队员已接入领域行为、协议处理和通知投影，主干 Release 构建可运行，并已部署到本机测试服务。

## 已验证条件

- 领域和协议回归：完整解决方案串行测试通过，`3443 passed`、`2 skipped`、`0 failed`。
- Release 构建：`dotnet build Server/86JP.Server.slnx -c Release --no-restore` 成功，`0 warning`、`0 error`。
- 运行进程：`Dfo.Server.Host.exe` 从主干 Release 输出目录启动，PID `1844`。
- 网络监听：HTTP `18080`、Channel `7001`、Game `10010/10011` 均由 PID `1844` 监听；UDP discovery 启动在 `12311/12312`。
- HTTP 探针：`GET /api/launcher/v1/bootstrap` 返回 `200`，正文状态为 `ready`。
- 运行日志：`C:\Users\Gloria\AppData\Local\Temp\dfo-host-20260919-093922.out.log`；错误日志为空。

## Unverified Conditions

- 尚需 A21 双客户端确认：委任后成员槽位不重排、新队长权限立即生效、旧队长权限撤销。
- 尚需 A21 双客户端确认：踢人后被踢客户端清空队伍投影；若在地下城中则只移除被踢成员并返回城镇，剩余队伍保持。
