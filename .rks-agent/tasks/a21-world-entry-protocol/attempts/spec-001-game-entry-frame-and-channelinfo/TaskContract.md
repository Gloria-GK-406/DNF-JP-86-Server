---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-1, SC-4, SC-5]
---

# TaskContract: A21 游戏端点入站帧边界与初始连接通知

## Context

### Source Request

根控制器 `a21-world-entry-protocol` 在 `successor_decision` 定出的第一个有界成果：
让 A21 客户端在连上游戏端点之后能走到发出 `CMD 0x0001 LOGIN`，而不是断开。

### Current Situation

实机取证（本仓工作树的 Host 通过记录型中转对 A21 客户端）确认了三件事：

1. **频道段已经能用。** A21 客户端渲染出「选择频道」界面并正确列出我们的 `ch11.厄运之城
   (Lv.17~26) 顺畅`；`0x0C`/`0x0A`/`0x12` 三条响应客户端都接受。客户端的频道请求用
   classification `0x00`、消息号 `0x0B`/`0x09`/`0x11`，与 `ChannelProtocol` 声明一致。
2. **入站 Game 帧头是 14 字节，我们按 13 切。** 客户端在游戏口的首条消息为
   `01 DD 04 0E 00 00 00 B5 9F 81 BD 00 00 00`——selector `0x01`、type `0x04DD`、
   len 14、checksum 4 字节，其后 3 字节。我们的 `GameClientFrameRequestInfo.HeaderLength = 13`
   把最后一字节当成 body，日志实测为 `type=0x04DD seq=0 body(1B)=00`，且 `0x04DD`
   没有已注册解包器，帧被传输层静默丢弃，客户端每约 10 秒重发一次。
3. **NOTI `0x0001` CHANNELINFO 的 body 我们发 60 字节，A21 需要 68 字节。**
   逐字段比对（我们 / A21 已知良好会话）：`extraAddressCount` i32=0 缺失；
   两个标记字节应为 `'0','0'`(`0x30`) 而我们写 `0x00,0x00`；CMD 表大小应为 1271
   而我们写 1086；NOTI 表大小应为 1218 而我们写 1036；末尾 i32=0 缺失。
   客户端收到我们这版后只回一条 `0x02B3` 便主动断开连接。

已知良好基线为 `ServerS4A21` 跑通一整局所留下的 458 帧抓包，已留存副本。
上游源码只作线索，不作规范。

## Goal

A21 客户端连上本仓游戏端点后不再断开，并发出 `CMD 0x0001 LOGIN`；沿途每一处线级
改动都以实测字节为依据，并同步落进 `protocol/` 知识库。

## Scope

### In

- `GameClientFrameRequestInfo` 的入站帧头长度与字段切分（含最后三字节的归属判定）。
- 传输层因帧头变化而受影响的拆帧、长度换算与相关用例。
- `GameInitialConnectionPayloadCodec` 的 A21 body 布局与其常量取值来源。
- `CMD 0x04DD` 的接纳方式：使客户端不再重试即可，不承载任何领域语义。
- 上述三处的 `protocol/` 记录与 `protocol/catalog.md` 状态、
  `Docs/NetworkingFrameProtocol.md` 的 Game 帧表、`AGENTS.md` 中「13B Game 头」的表述。

### Out

- `CMD 0x0001 LOGIN` 本身的请求解包与响应布局（下一个成果）。
- 花名册、选人与入场序列。
- Channel 端点的任何改动（实测已可用）。
- 服务架构变更。

## Constraints

- 不得修改服务架构：分层与项目引用图、协议切片边界、handler 四类允许行为、
  命令总线与管线次序、注册表接线方式均不变。
- 线级结论必须以实测字节为依据。**最后三字节的内部切分（`seq(2)+extra(1)` 还是
  `extra(1)+seq(2)`）在证据不足时不得凭上游源码断言**——上游写作前者，而实测三帧的
  末三字节为 `00 00 00`/`00 01 00`/`00 02 00`，按后者读才是 0/1/2 的自然计数。
  需要更多样本或客户端读序来判定；判不出就如实记为未判定，并选择不依赖该判定的实现。
- `protocol/` 不得含凭据、令牌或个人可识别信息；C→S `0x0001` LOGIN 样本不得入库。
- 测试 `-m:1`；C# 与文档 CRLF、无 BOM UTF-8；`TreatWarningsAsErrors` 保持开启。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 入站 Game 帧按 14 字节头解析，`0x04DD` 不再被误读为 1 字节 body | 用实测帧字节做输入的解包用例；服务端日志不再出现该帧的 `body(1B)` |
| SC-2 | NOTI `0x0001` 的 body 与已知良好会话逐字节一致（除频道 id、名称、host、时间戳等本机取值外） | 以已知良好帧为期望值的编码用例，逐字段列出允许不同的项与理由 |
| SC-3 | A21 客户端连上游戏端点后不再断开，并发出 `CMD 0x0001 LOGIN` | 中转记录中出现 `C->S ... type=0x0001`；此前的 `0x02B3` 后立即断开不再发生 |
| SC-4 | 末三字节的切分要么有证据判定，要么如实记为未判定且实现不依赖它 | 判定证据，或记录了未判定并说明实现为何与之无关 |
| SC-5 | 三处结论已落进 `protocol/` 与帧基线文档，`AGENTS.md` 的 13B 表述已更正 | 相应文件最终内容；被推翻的旧结论保留了原因 |
| SC-6 | 构建零错误零警告，全量测试通过 | 最终基线上的构建与 `-m:1` 全量输出（`Host.Tests` 满载假红以隔离复跑判定） |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 继承根契约 `a21-world-entry-protocol` 的 `task` 行：「那就处理掉登录进游戏世界前的全部协议问题……不过不能修改服务架构。对了，最好用sdd来渐进重构。并且协议文档要同步更新」 | 本成果的实现、测试、协议文档更新，以及为取证启动本仓服务端与 A21 客户端 |

## Assumptions

- 已留存的 458 帧抓包代表 A21 客户端接受的行为；与本仓既有结论冲突时以抓包为准。
- 频道段无需改动即可让客户端到达游戏端点（本轮实测成立）。
- 取证用的中转把广播端口与监听端口分开，因此目录里配了两条同 id 的频道条目；
  这是取证装置，不是产品配置。
