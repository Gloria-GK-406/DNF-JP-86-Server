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
| Game | Client → Server | `0x01` | `0x00C2` | `CLIENT_SPEC_PROFILE`（描述性命名） | Report / no response | ClientVerified | [记录](game/client-to-server/0x00C2-client-spec-profile/README.md) |

当同一 type 在另一方向或端点出现时，必须新增独立行和独立记录，不能覆盖已有含义。

名称来源不同必须写明：`CLIENT_SPEC_STATISTIC` 出自旧服枚举；`CLIENT_HARDWARE_DETAIL` 与
`CLIENT_SPEC_PROFILE` 是本资料库的描述性命名，取得枚举证据后应改名并保留旧名映射。

三条已建记录的协议都携带同一批硬件数据的不同编码（分桶 / 原始 / 归一化+表），读自同一个
上报对象；但触发时机不同：`0x00AB` 与 `0x01DD` 属登录握手，`0x00C2` 属**客户端退出 flush**。
详见各记录的交叉对照表。

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
| Game | Client → Server | `0x01` | `0x00FA` | 50 | 形如 8 × 6 字节的索引表（`00…00`、`01…00`… `07…00`）+ 2 字节尾，疑似槽位/选项枚举 |
| Game | Client → Server | `0x01` | `0x0117` | 12 | `01 06 A1C98A48 E12A0A49 0500`，含两个疑似时间戳/浮点的 4 字节量 |
| Game | Client → Server | `0x01` | `0x0003` | 1 | EXIT 请求（`00`），服务端已实现 |
| Game | Server → Client | — | `0x0003` | 1 | EXIT 响应（`01`），服务端已实现；本表首个已观察到的**有响应**对 |
| Game | Client → Server | `0x01` | `0x008F` | 5 | `1F000000 01`，EXIT 之后仍发出 |

采样场景：2026-08-14 三次实机会话（登录 → 角色列表 → 退出）。`0x00FA`/`0x0117`/`0x008F`
与已建记录的 `0x00C2` 同属**客户端退出 flush**（Confirmed，两次会话逐帧一致）：

```text
0x0002(31B) → 0x00C2(361B) → 0x00FA(50B) → 0x0117(12B) → 0x0003 EXIT ⇄ → 0x008F(5B)
```

只进频道并停留在角色列表的会话到 `0x0002`（seq 7）就结束；强制结束进程也不会触发 flush。
要采样退出序列的协议，必须走**游戏内正常退出**。
