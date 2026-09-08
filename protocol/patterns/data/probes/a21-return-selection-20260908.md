# A21 RETURN_SELECT_CHARACTER 证据定位（2026-09-08）

客户端：`E:/Project/DNF/DNFClient-A21-protocol/DNF.unpacked-b1-debug.exe`。
SHA-256：`CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`。
ImageBase `0x00400000`，下列地址均为 RVA。使用 Python 3.11 + Capstone 5.0.7 读取 PE `.text`。

## 实机入站记录

源：`Server/src/Dfo.Server.Host/bin/Debug/net10.0/Logs/server-20260908-223228.stdout.log`。
归档摘录时源文件 SHA-256：`cb06a6a9627b398f2889943c4d0e603c36c4829667a1aa206f12cd6108eec181`。源文件仍由旧版 Host 写入，哈希只标识此时快照。
下面保留原行号；这是服务端解码后的入站记录，不是完整原始帧。

```text
196: Unhandled Game frame Game:01-00-00-00: selector=0x01 type=0x0007 seq=63 body(0B)=
492: Unhandled Game frame Game:04-00-00-00: selector=0x01 type=0x0007 seq=26 body(0B)=
```

用户在本次会话明确说明点击了“选择频道”；第二条记录与该动作相对应。
两个请求的 body 都是 0 字节。没有从日志还原未知 checksum 或伪造原始帧。

## 构包与 CMD 接收链

| 主张 | 定位 | 置信度 |
|---|---|---|
| C2S 请求为空体 | `FA9093` push 0 / push 7；`FA909D` MakeHeader；`FA90A2` Finalize；之间无字段写入 | Confirmed |
| type 7 注册到该 handler | `D18DA6` push `0x01118280`（VA），`D18DAB` push 7，`D18DAF` Register | Confirmed |
| 通用 CMD 信封先读 u8 成功标志 | `123BF30` 调 `ReadU8=23C5230`；`123BF63` cmp flag,0；`123BF6D` jne 成功路径 | Confirmed |
| 失败时再读 u8 原因 | 仅零标志路径在 `123BF73` 第二次调用 ReadU8 | Confirmed |
| 信封参数传给 type handler | `123C069`/`123C06C` 取 reason/ok；`123C078` 到 dispatch，`123770C` 间接调用 handler | Confirmed |
| type 7 成功分支门 | `D18283` cmp byte ptr [ebp+0xC],0；`D18287` je `D18415`（返回） | Confirmed |
| 返回名单走 subtype 2 | NOTI `0x0002` handler `FE91E0` 首读 subtype；`FE92AB..FE942A` 为 subtype 2 的名单解析路径 | Confirmed |

`0xD18280..0xD18417` 没有额外的直接载荷读取，但存在虚拟化子调用（例如 `0x4E4D0C7`），
不能据此声称完整 UI 状态机或所有潜在读取已经还原。`01` 作为完整成功 body 的选择同时参考下列固定版本上游；实机仍待验证。

## 与静态普查基准的代码一致性

对照旧内存转储 `E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260822-232754-263-26888/DNF-memory-rva.bin`，
以 RVA 索引；当前 PE 以节表换算文件偏移。以下范围逐字节相同，而不是仅函数名相同。
旧基准客户端身份见 [a21-census-anchors](a21-census-anchors.md)。

| RVA 起点 | 长度 | 两侧相同字节的 SHA-256 |
|---|---:|---|
| `D18280` | `0x197` | `936bed02fcda11f16160db14fc3557c97c36963e37f9938c5dd7d8f295996a1c` |
| `D18BC0` | `0x200` | `9f82c315406e0085baacb4289f7d6f84a18c58ed0bbfa6eec6f8d442db71cc87` |
| `1237690` | `0xC0` | `f3ce80664fb2cf463eb7c301864bf837dad1da971a9f75b0ccbedde973a18080` |
| `FA9093` | `0x14` | `d7fccf0ac67ae8240e0e04bdb14983ba9873c903da1b5021e8caeae3f3df690c` |

## 固定版本远端参照

直接读取远端提交 `c58cd2b479b0357cb6ca78887c9d638b080f4638`，没有以本地旧克隆的 HEAD 冒充远端。

- [CharacterSelectHandler.cs](https://gitgud.io/rewio/ServerS4A21/-/blob/c58cd2b479b0357cb6ca78887c9d638b080f4638/Server/DfoServer/Network/Handlers/CharacterSelectHandler.cs)：`Handle_ENUM_CMDPACKET_RETURN_SELECT_CHARACTER`、`SendCharacterListAsync`。
- [CharacterSessionLifecycleCoordinator.cs](https://gitgud.io/rewio/ServerS4A21/-/blob/c58cd2b479b0357cb6ca78887c9d638b080f4638/Server/DfoServer/Network/Handlers/CharacterSessionLifecycleCoordinator.cs)：返回选人生命周期，保存后释放角色并保留账号连接。

这是参照实现的行为证据，不是客户端实测。其发送顺序见 [返回选人流程](../../flow-return-selection.md)。
