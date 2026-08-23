---
kind: rks-verification
decision: pass
---

# 终验：spec-001 A21 游戏端点入站帧边界与初始连接通知

## 被评估的主张

A21 客户端连上本仓游戏端点后不再断开，并发出 `CMD 0x0001 LOGIN`；入站帧按 14 字节头切分、
`NOTI 0x0001` 的 body 与已知良好会话一致、`CMD 0x04DD` 得到应答；三处结论已落进协议知识库、
帧基线文档与仓库说明。

证据全部取自整改后的当前工作树（基线 `0d37e9a` + 未提交改动），不复用评审前的任何结果。

## 条件与证据

| SC | 条件 | 新鲜证据 | 结果 |
|---|---|---|---|
| SC-1 | 入站按 14 字节头解析，`0x04DD` 不再被误读为 1 字节 body | `The_clients_empty_connection_probe_parses_with_no_body` 以抄录帧为输入解出 `BodyLength = 0`；本轮实机服务端日志中 `Unhandled Game frame` 仅剩 `0x04D7`/`0x04F1` 两条遥测（本成果范围外），**无 `0x04DD`**，其 body 亦不再出现 `body(1B)` | pass |
| SC-2 | `NOTI 0x0001` 的 body 与已知良好会话逐字节一致 | `The_notice_reproduces_the_body_the_client_accepted` 以归档件 `[D]` 节的 68 字节为期望值整段比对；实机下发帧 `body=68` | pass |
| SC-3 | 客户端不再断开并发出 `CMD 0x0001 LOGIN` | 本轮实机双向记录：`t=100.19 S->C NOTI/0001 body=68` → `t=100.23 C->S CMD/0001 LOGIN body=73` → `t=102.12 S->C CMD/0001 body=53`，其后 `GET_USERINFO`、`SAVE_GAME_OPTION_1`、`USERINFO` 等继续往来；**GAME 腿全程无 CLOSE、无 ERROR**，最后一条记录在 `t=141.99` | pass |
| SC-4 | 末三字节的切分有证据判定 | 190 条入站帧统计：`byte[11]` 取值集合 `{0}`、`byte[13]` 取值集合 `{0}`、`u16@12` = 0..189 且 **189/189 步为加一**；按上游读法 `u16@11` 为 **0/189**。归档于 `[A]` 节。**高位字节是否参与计数未证实**（样本从未越过 255），该限制已如实写进代码注释、`game-frame.md` 与规格 Assumptions | pass |
| SC-5 | 结论已落进协议知识库与两处文档 | `protocol/patterns/game-frame.md`（入站按版本分节 + 证据链接 + A21 客户端哈希）、`protocol/game/server-to-client/NOTI-0x0001.md`（68 字节逐字段 + 推翻说明）、新增 `protocol/game/client-to-server/0x04DD.md`、`protocol/catalog.md` 两行、`Docs/NetworkingFrameProtocol.md`（入站表、出站说明、当前实现描述三处）、`AGENTS.md`（两处）。全仓 grep 确认已无"当前入站为 13 字节"的表述 | pass |
| SC-6 | 构建零错误零警告，全量测试通过 | `dotnet build Server/86JP.Server.slnx` → **0 警告 0 错误**；`dotnet test Server/86JP.Server.slnx -m:1` → **1194 通过 / 2 跳过 / 0 失败**（退出码 0，`Host.Tests` 本轮满载亦未假红）。按名复跑：ConnectionCheck + InitialConnection 13/13，FrameRequestInfo + GameLoginIntegration 16/16 | pass |

## 守卫的可证伪性

| 改坏 | 期望 | 实测 |
|---|---|---|
| `ConnectionCheckType` `0x04DD` → `0x04DE` | 红 | **红 1**（`The_answer_carries_the_wire_type_the_probe_arrived_on`）。恢复后该文件 sha256 与改动前一致 |

评审方另行做过四次改坏（seq 读回 offset 11、头长回 13、删 `ExtraAddressCount`、标记字节与表大小回 A12），四次全部变红。

## 独立评审

`reviews/change-review.md`：第一轮 `changes_required`（3 条 A 级、5 条 B 级），整改后复核
`passed`，八条全部关闭、无 A 级残留。复核方另提四条 C 级措辞问题，已一并改完。

其中两条由我方回到原始字节独立复核后**采纳并更正了自己的结论**：`0x04DD` 不门控登录
（LOGIN 是入站第 0 帧、首条探测在 seq 13），以及"CMD 表数差一"是虚构（两值均为各自
`END` 哨兵的取值）。另有一条复核方的依据不成立（NOTI 枚举并非 1219 且并非连续，实为 1218
个成员、`0x018A` 空缺），我方按实测写了更精确的表述，而非照单接受。

## 约束

| 约束 | 状态 |
|---|---|
| 不得修改服务架构 | aligned。无 `.csproj` 改动；新切片只含五类制品，与既有 `ContentsPlayInfoSlice` 同形；handler 仅落在 AGENTS.md 允许的第 3/4 类；`ProtocolModule` 仍是唯一接线点 |
| 线级结论须有实测依据 | aligned。每条具名取值均指向归档件 `patterns/data/traces/a21-world-entry-20260823.log` 的具体小节 |
| `protocol/` 不得含凭据 | aligned。归档件只保留 LOGIN 的帧头；全仓 grep 确认口令哈希未入库 |
| 行尾与编码 | aligned。`protocol/**` 为 LF（`.gitattributes` 要求），其余 CRLF；无 BOM 变更；git 无换行警告 |
