# 设置blob交付记录

基线：314bd463；分支：task/next-round-20261013；日期：2026-10-13。

## Changed

- `IClientSettingsDomainService`承载账号主设置、两组快捷聊天与按账号/profile的完整热键文本；C0沿用角色设置领域能力。
- 上传通过协议切片及事务命令；精确连接身份、generation和可选selected session在入口捕获，执行门内复核。profile token防止排队后串类别。
- 登录普通profile、选角普通／缔造者profile复用原发送顺序。空文本走客户端默认入口；旧二进制槽数据不回放为文本。
- 独立审查发现的断线激活交错已补末次精确清理；历史数据保留，新增表，无字段语义修改。

## Checks

最终证据见[完成验证](verification/completion-evidence.md)。单元、真实SQLite及真实TCP检查分别保护字节外壳、持久化和接线。
曾尝试测试先行，但本机沙箱testhost无法建立通信，未取得可靠behavioral RED；后续在沙箱外完成回归。
这些测试是实现后的验证证据，不报告为成功的TDD周期。

## Remaining

客户端恢复、普通／缔造者切换及实际字段上传归属等待用户实测，见[验收列表](acceptance.md)。
C6没有profile关联字段，无法辨认新profile激活后才收到的旧profile上传；只保证已捕获上下文不因队列延迟串写。
