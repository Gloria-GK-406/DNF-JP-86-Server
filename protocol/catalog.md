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
| Game | Client → Server | `0x01` | `0x01DD` | `CLIENT_HARDWARE_DETAIL`（描述性命名） | Report / no response | ClientVerified | [记录](game/client-to-server/0x01DD-client-hardware-detail/README.md) |

当同一 type 在另一方向或端点出现时，必须新增独立行和独立记录，不能覆盖已有含义。

名称来源不同必须写明：`CLIENT_SPEC_STATISTIC` 出自旧服枚举，`CLIENT_HARDWARE_DETAIL`
是本资料库的描述性命名，取得枚举证据后应改名并保留旧名映射。

## 已观察但未建记录

下列 type 已在 2026-08-14 全量抓包中直接观察到，尚未逆向；列出以免重复发现，
并作为后续排序依据。**方向是实测的**，不是推断：

| Endpoint | Direction | Command | Type | Body | 场景 |
|---|---|---:|---:|---:|---|
| Game | Client → Server | `0x01` | `0x0001` | 73 | LOGIN（服务端已实现，未建记录） |
| Game | Server → Client | — | `0x0001` | 54 | LOGIN response（服务端已实现，字段含魔数，未建记录） |
| Game | Client → Server | `0x01` | `0x0008` | 3 | GET_USERINFO（服务端已实现，前 2 字节未解释） |
| Game | Server → Client | — | `0x0002` | 18 | 角色列表（空名单样本） |
| Game | Client → Server | `0x01` | `0x02A8` | 2 | `FFFF`，角色列表之后 |
| Game | Client → Server | `0x01` | `0x0002` | 31 | 与 S→C `0x0002` 同 type 反方向，须独立建记录 |
| Game | Client → Server | `0x01` | `0x00C2` | 361 | 角色列表界面停留期间。body 前 12 字节是 `330A0000 0C00 8680 A7A8`，即与 `0x00AB`/`0x01DD` 相同的 MHz/核心数/GPU PCI ID，**疑似第三份（详细版）硬件上报**（Hypothesis）；其余多为 0 与定长块，未见字符串 |
| Game | Client → Server | `0x01` | `0x00FA` | 50 | 形如 8 × 6 字节的索引表（`00…00`、`01…00`… `07…00`）+ 2 字节尾，疑似槽位/选项枚举 |
| Game | Client → Server | `0x01` | `0x0117` | 12 | `01 06 A1C98A48 E12A0A49 0500`，含两个疑似时间戳/浮点的 4 字节量 |
| Game | Client → Server | `0x01` | `0x0003` | 1 | EXIT 请求（`00`），服务端已实现 |
| Game | Server → Client | — | `0x0003` | 1 | EXIT 响应（`01`），服务端已实现；本表首个已观察到的**有响应**对 |
| Game | Client → Server | `0x01` | `0x008F` | 5 | `1F000000 01`，EXIT 之后仍发出 |

采样场景：2026-08-14 两次实机会话（登录 → 角色列表 → 关闭客户端）。`0x00C2` 之后的四条
出现在客户端退出流程中，尚未确认它们属于退出序列还是与退出无关的定时上报。
