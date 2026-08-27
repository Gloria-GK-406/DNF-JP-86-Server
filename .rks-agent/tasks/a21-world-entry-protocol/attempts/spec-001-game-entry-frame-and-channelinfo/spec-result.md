---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: A21 游戏端点入站帧边界与初始连接通知

## Delivered

- A21 客户端连上本仓游戏端点后不再断开，并发出 `CMD 0x0001 LOGIN`；服务端应答后客户端继续
  推进到花名册往来（`GET_USERINFO`、`SAVE_GAME_OPTION_1`、`USERINFO` 等）。
- 入站 Game 帧按 14 字节头切分：`selector(1) + type(2) + totalLength(4) + checksum(4) +
  保留(1) + seq(2)`。保留位与计数的边界由 190 帧实测判定，**与上游参考实现的读法相反**。
- `NOTI 0x0001` 初始连接通知的 body 由 60 字节改为 A21 的 68 字节：补 `extraAddressCount`
  与末尾 `i32`，两个标记字节由 `0x00` 改为 ASCII `'0'`，两个派发表大小由 1086/1036 改为
  1271/1218。
- 新增 `CMD 0x04DD` 协议切片：空体请求，应答为同 type + 单字节 `0x01`。
- 协议知识库与两处文档同步：`game-frame.md` 按客户端版本分节、`NOTI-0x0001.md` 补齐 68 字节
  逐字段、新增 `0x04DD.md`、`catalog.md` 两行、`Docs/NetworkingFrameProtocol.md` 三处、
  `AGENTS.md` 两处；实测证据归档为 `protocol/patterns/data/traces/a21-world-entry-20260823.log`。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md`：实机日志中 `Unhandled Game frame` 已无 `0x04DD`；解析用例以抄录帧解出 `BodyLength = 0` |
| SC-2 | 同上：以归档件 `[D]` 节 68 字节为期望值的整段比对通过；实机下发 `body=68` |
| SC-3 | 同上：`NOTI/0001` → `CMD/0001 LOGIN` 的实机时间线，GAME 腿全程无 CLOSE 无 ERROR |
| SC-4 | 同上：`u16@12` 189/189 步加一，上游读法 0/189；高位字节未证实一点如实记录 |
| SC-5 | 同上：六个文件的最终内容；全仓 grep 确认无"当前入站 13 字节"表述 |
| SC-6 | 同上：构建 0 警告 0 错误；全量 `-m:1` 1194 / 2 跳过 / 0 失败 |

## Repository Result

- Changed paths：12 个已跟踪文件改动 + 4 项新增（`Server/src/Dfo.Server.Protocol/Game/ConnectionCheck/`、
  两个测试目录、`protocol/game/client-to-server/0x04DD.md`、
  `protocol/patterns/data/traces/a21-world-entry-20260823.log`），另有本任务的 `.rks-agent/` 制品。
- Baseline：`0d37e9a`。
- Parent rollback required：no。

## Successor Guidance

后继成果必须保留或采纳的事实：

1. **`0x04DD` 不门控登录。** 客户端在首条探测之前就发 LOGIN（已知良好会话里 LOGIN 是入站
   第 0 帧、seq 0，首条探测在 seq 13）。若后继看到"客户端不发某条命令"，不要归因于探测。
2. **入站头是 14 字节，且末三字节是 `保留(1) + seq(2)`。** 上游 `Packet.cs` 读作
   `seq(2) + extra(1)`，那是错的（189 帧计数比对 0/189）。高位字节是否参与计数仍未证实。
3. **两个派发表大小是各自枚举 `END` 哨兵的取值**，不是"条目数"——CMD 枚举 1272 个成员而线上
   报 1271；NOTI 枚举 1218 个成员、取值 `0..1218` 且 `0x018A` 空缺，线上报 1218。
4. **本仓的 LOGIN 应答目前仍是 A12 形状**：`GameLoginResponseEncoder` 写 `body[1] = 18`
   且偏移 4 处少一个字节，而 A21 已知良好会话的应答 body 为 54 字节、`body[1] = 20`。
   客户端当前**接受**了我们这版（实机继续推进），因此这不是阻塞项，但下一个成果应以实测
   字节重新判定，不要因为"客户端没崩"就认定它已经对。
5. **取证装置**：`ClientHack` 的 `0100_NetworkHook` 在 A21 上**无法加载**——它与
   `0050_A21RuntimeFix` 争抢同三个 hook 地址（`0x022B7D90` 等），且其失败是致命的，会让
   客户端起不来。本轮的做法是把该插件移出 `plugins/`，改用记录型 TCP 中转在服务端侧抓帧。
   客户端侧的 `[FrameCapture]` 与 `[ReadTrace]` 目前都不可用；入场序列若需要字段级切分证据，
   要先解决这个冲突。
6. **频道段无需改动**：A21 客户端已能正确渲染频道列表并进入游戏端点。

## Artifacts

- `spec.md`
- `reviews/change-review.md`（两轮：`changes_required` → `passed`）
- `verification/completion-evidence.md`
- `protocol/patterns/data/traces/a21-world-entry-20260823.log`
