---
kind: rks-verification
decision: manual_required
---
# 采样部署验证（2026-09-24）

范围是受控采样候选，不是TaskContract完成。

- 完整解决方案构建：0警告、0错误，见../build.log。
- Protocol全量1107通过；Application邮箱/CharacterEntry定向85通过；Persistence邮箱6通过。见../test-*.log。
- 协议第一次全量1106通过1失败：非空邮箱登录fixture的0061 hash因静态证据要求补字段而改变。逐字段消费测试及差异定位确认仅第47帧改变，更新该hash并标记为本地回归基线（不是实帧），随后1107通过。
- Release发布成功，git diff --check通过。独立review只批准采样边界。
- 重启前10010/10011无Established连接。停止原进程30840，仅启动新进程19216。
- 发布目录：E:/Project/DNF/DNF-JP-86-Server/artifacts/mailbox-protocol-20260924/runtime。
- 备份：同级../before-mailbox-20260924-210253.db，SQLite backup API生成，quick_check=ok。
- 原Database.Path保持E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db，未修改任何角色资产。
- 启动完成；进程19216监听7001、10010、10011、18080，stderr为空。
- 旧二进制回滚目录：artifacts/object-party-20260924/runtime。不得自动回滚数据库，避免覆盖部署后新资产。

## 尚未验证

005E/013B与0060请求体等待用户实帧；暂不注册，仅记录Unhandled。发送及在线新邮件通知尚未接入。0061非空邮箱、0144 UI效果、领取/已读/删除仍需A21实机，不因本地测试升级ClientVerified。主任务保持implementation/in_progress。
