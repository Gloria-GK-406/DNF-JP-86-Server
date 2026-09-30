# CMD `0x01B6` — 随机词条变更结果

## 2026-09-30 静态读取证据

- `ClientStatic`：本次反汇编文件为 A21 `DNF.unpacked-b1-debug.exe`，SHA-256 `D7618A102251F1DB724F2514C74A4CC855DF26E6A4BC308F8EE9F2397DB0A99A`；与它对应的原版壳 `DNF.exe` SHA-256 为 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`（[请求记录](../client-to-server/0x01B6.md) 绑定此身份）。客户端回调 RVA `0x00C45060`，PE 基址 `0x00400000`，VA `0x01045060`。注册 forwarder VA `0x02F2B600–0x02F2B613` 把 type `0x01B6` 与此回调绑定。
- VA `0x01045063–0x01045067` 比较 `[ebp+0x0c]` 的成功标志；成功分支 `0x0104506D–0x010450FF` 不从消息流读取额外字段，只触发客户端刷新/提示调用。错误分支 `0x01045100–0x01045111` 读取 `[ebp+0x10]` 单字节 reason；不同值进入提示分支。相同通用 CMD 状态/错误首字节形态已在 [CMD `0x0110`](CMD-0x0110.md) 的 A21 静态读取路径核对。
- 因此本服最小结果体：成功 `01`；失败 `00 reason`。这是客户端静态可读的下界，不证明原服从不附加其他字段。具体 reason 值到 UI 文案的映射以及通知顺序无 A21 实帧，待 [Human Test List](../../../Document/tasks/equipment-development/attempts/spec-003-magic-seal/Human-Test-List.md) 验收。

当前仅 `ClientStatic + CurrentImplementation`，不标 `ClientVerified`。不能用上游或 70 的回包替代 A21 实帧。
