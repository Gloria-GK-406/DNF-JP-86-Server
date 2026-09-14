# A21 UDP 端口字节序实机对照（2026-09-15）

## 结论

`CMD 0x0002 SET_UDP_IP_PORT` 的端口字段是 `u16 BE`（网络序）。置信度：
**Confirmed**。

## 环境

- 服务端：`main`，实现提交 `c242e8b5`，Release Host PID `75228`。
- 客户端：`DNFClient-A21-protocol` 与 `DNFClient-A21-protocol2`，同机双开。
- 两端均在选角前成功上报 `SET_UDP_IP_PORT`；组队邀请及接受成功，随后客户端 P2P 连接失败。

## 运行时对照

当时服务端仍用 `ReadUInt16LittleEndian`，日志记录两个端口为 `31477`、`44504`。同一时刻
Windows UDP endpoint 表中两个活跃客户端实际监听 `62842`、`55469`：

| 线上两字节 | 错误 LE 结果 | 正确 BE 结果 | 操作系统监听 |
|---|---:|---:|---:|
| `F5 7A` | 31477 | 62842 | 62842 |
| `D8 AD` | 44504 | 55469 | 55469 |

两个线上值按 BE 解码后与两个活跃 DNF 进程的唯一 UDP socket 完全一致。另有旧 DNF PID
`64932` 占用默认 UDP 5063，促使本轮两个客户端使用随机高端口；它暴露了错误，但不是协议
应依赖固定端口的理由。

## 静态证据与旧结论修正

- `Docs/a21-units/unit-030.md` 的客户端路径显示，构造上报记录时对 mapped port 调用 `htons`；
  写入线上记录后应解释为网络序。
- `NOTI 0x000B USER_UDP_IP_PORT` 的已确认客户端消费布局同样要求端口按大端发送；现有编码器
  已如此实现。
- 2026-09-11 的两份旧样本均为 NAT type 5，未同步采集操作系统 socket。它们仍证明端口字段
  位于 offset 9，但不能证明先前标注的 LE 数值。

## 修复边界

只把 `SetUdpIpPortDecoder` 的 offset 9 端口读取改为 `ReadUInt16BigEndian`。Session 归属、
STUN/UDP discovery 的 host-order 响应，以及 `NOTI 0x000B` 的 BE 端口编码均不改变。
