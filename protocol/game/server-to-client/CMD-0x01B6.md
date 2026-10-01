# CMD `0x01B6` — 随机词条变更结果

## 2026-09-30 静态读取证据

- `ClientStatic`：本次反汇编文件为 A21 `DNF.unpacked-b1-debug.exe`，SHA-256 `D7618A102251F1DB724F2514C74A4CC855DF26E6A4BC308F8EE9F2397DB0A99A`；与它对应的原版壳 `DNF.exe` SHA-256 为 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`（[请求记录](../client-to-server/0x01B6.md) 绑定此身份）。客户端回调 RVA `0x00C45060`，PE 基址 `0x00400000`，VA `0x01045060`。注册 forwarder VA `0x02F2B600–0x02F2B613` 把 type `0x01B6` 与此回调绑定。
- VA `0x01045063–0x01045067` 比较 `[ebp+0x0c]` 的成功标志；成功分支 `0x0104506D–0x010450FF` 不从消息流读取额外字段，只触发客户端刷新/提示调用。错误分支 `0x01045100–0x01045111` 读取 `[ebp+0x10]` 单字节 reason；不同值进入提示分支。相同通用 CMD 状态/错误首字节形态已在 [CMD `0x0110`](CMD-0x0110.md) 的 A21 静态读取路径核对。
- 因此本服最小结果体：成功 `01`；失败 `00 reason`。这是客户端静态可读的下界，不证明原服从不附加其他字段。具体 reason 值到 UI 文案的映射以及通知顺序无 A21 实帧，待 [Human Test List](../../../Document/tasks/equipment-development/attempts/spec-003-magic-seal/Human-Test-List.md) 验收。

当前仅 `ClientStatic + CurrentImplementation`，不标 `ClientVerified`。不能用上游或 70 的回包替代 A21 实帧。


## 2026-10-01 S02 转换窗口与背包结果不一致

`ClientStatic / Confirmed`：上述同SHA客户端在成功分支VA `0x010450B1–0x010450C1` 取得窗口对象，读取全局所选装备指针`0x03A2B21C`，调用`0x01044EB0`。该函数VA `0x01044FF0`调用`0x010448B0`重建选项；后者VA `0x01044917–0x0104492E`从此全局装备取得数据，再读取词条集合。ACK本身不携带新词条，窗口刷新依赖当时已更新的装备。

`CurrentImplementation`：成功顺序改为装备NOTI `0x000E` → 金币NOTI `0x000E` → CMD `0x01B6 01`。原先ACK在最前，窗口先读旧结果，背包随后更新，符合用户当场窗口“武力B”而tooltip“活力C”的反馈。请求格式、成功／失败body及品质抽取不变。

`AutomatedVerified`：经注册encoder核对通知序列与实际装备词条字节；丢弃角色容器、从SQLite重建后，生产快照中的101字节装备记录与刚发送的delta一致。该检查不代替客户端显示验收；S02a／S02b仍待[用户复验](../../../Document/tasks/item-effects-unification/human-test-list-20261001-round4.md)。


## 2026-10-01 0.2.0验收边界

29d2b100部署后用户确认S02符合预期并验收完成。成功通知先装备／金币后ACK的GUI效果已被接受；未新增原服raw帧。 统一结果与遗留Issue见[版本收口](../../../Document/releases/0.2.0.md)；跨消息编排见[effect流程](../../patterns/flow-item-effects.md)。
