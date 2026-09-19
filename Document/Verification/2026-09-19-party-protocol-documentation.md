# 委任队长与踢出队员协议文档验证

```yaml
kind: rks-verification
decision: pass
```

## 验证目标

把 2026-09-19 A21 双客户端的委任队长与城镇踢人成功路径写入协议资料库，同时保持拒绝分支、
地下城路径和单帧效应隔离为未验证，不把服务端实现或自动化测试冒充客户端线级证据。

## 验证结果

- 新增实机证据记录与协议编排流程；记录服务端发送日志哈希、真实顺序、完整帧长度、客户端结果及抓包缺口。
- `CMD 0x0079`、`CMD 0x000E`、`NOTI 0x001A`、`NOTI 0x0009 type=2` 的页面与 catalog 状态已同步。
- 生成输入 `meanings/*.tsv` 与 `index-overrides.tsv` 已修正，重新生成 `protocol/game/index.md` 后结果稳定。
- `check-server-documentation-sync.py --repo-root .`：`failures: 0`。
- `check-controls.py protocol-layouts.json`：`controls failed: 0 / 14`。
- `git diff --check` 通过；新增和修改页面的相对链接目标均存在。

本次明确不生成伪造的 `samples/*.hex`：两个客户端未加载 `0100_NetworkHook`，因此结论标为
`ClientVerified` 而不是 `WireCaptured`。
