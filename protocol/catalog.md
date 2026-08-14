# 协议目录

状态含义：

- `Observed`：仅有实机报文。
- `Reversing`：正在定位客户端构包/解包逻辑。
- `LayoutKnown`：布局和行为已有直接证据。
- `Implemented`：服务端已实现且自动验证通过。
- `ClientVerified`：真实客户端功能验证通过。

| Endpoint | Direction | Command | Type | Name | Role | Status | Record |
|---|---|---:|---:|---|---|---|---|
| Game | Client → Server | `0x01` | `0x00AB` | `CLIENT_SPEC_STATISTIC` | Report / no response | ClientVerified | [记录](game/client-to-server/0x00AB-client-spec-statistic/README.md) |

当同一 type 在另一方向或端点出现时，必须新增独立行和独立记录，不能覆盖已有含义。
