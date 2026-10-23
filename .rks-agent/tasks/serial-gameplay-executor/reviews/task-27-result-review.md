---
kind: rks-review
target: tasks/TASK-27.md
verdict: approved
---

# TASK-27 结果快速审查（F-115 队伍标题上限，独立审查者）

- 审查对象：`tasks/TASK-27.md` Result（合同 Revision 2：执行前裁定 1～5 加「主控方处置（执行中断后）」）；HEAD `8c531c9b` 上的未提交差异：生产 1 个文件、测试 3 个文件（1 个新建）、`spec.md`、`protocol/` 3 个文件、收口与日志。`git diff -- Server/src` 与 `-- Server/tests` 的 sha1（`22119eaf…`、`e187aa79…`）与变异日志前后值一致。评审者只读，本文件由主控方按评审回报落盘。
- 适用标准：reviewing-work 的 Task Unit、Code、Deliverable 准则。
- 评审者自跑：Protocol 小过滤 21 条全部通过。

## 发现（均为低严重度，不阻断）

1. `t006-closure.md` §12 第 3 条（F-116 引用同一句）「客户端能控制的单帧扇出最大 336 B」说宽了：0x0002 在场快照也含客户端提供的字节（角色名 ≤ 18 B、宠物名 ≤ 12 B），单帧约 539 B，已在每人上界内，不影响结论。
2. §12 第 6 条只说 F-113、F-116、F-117 不阻断 M-2，漏了 F-114、F-118。
3. `protocol/game/client-to-server/0x000C.md` 新节写「1 B（上节抓包）」，实际在「2026-09-13」一节。
4. 合同正文 `Revision: 1` 与 Required check 2、停止条件 2 仍是旧 W1 写法，实质已由「执行中断后」处置取代。

## 重点项核实

| 项 | 结论 |
|---|---|
| 解码器修复 | 检查在 `titleLength < 0` 之后、自洽检查之前（`PartyCommandSlice.cs:245-249`），截断请求也按超长拒绝，MT4 守住次序；抛 `InvalidDataException` 与同一解码器其它拒绝一致，`NetworkPlugins.cs:239-262` 记 `threw` 后重抛、连接关闭；不截断；remarks 符合注释约定；`new PartyTitle(` 生产只此一处（`:271`） |
| 普查 | `ProtocolModule.cs` 的 `Add*MessageDecoder<` 恰为 184 处，无其它注册点；另以 `ToArray()`、`GetString`、`new PartyTitle` 机械交叉检查，命中类都在 36 类内、去向一致；抽查开放类 9 个判读正确；有界扇出 5 类全部核过（角色名 ≤ 18 B、宠物名两处 ≤ 12 B、邮件 1,024／19／512 B、婚礼留言 ≤ 20 B）；`WithRawBody` 只带 SHA-256 与长度；未注册帧丢弃成立；F-114 宠物文本更正成立 |
| 证据 | P2 基准变红为行为原因（257 与 1,048,400 两行「No exception was thrown」，第 3 行原因为 `requires '273'`）；P1 在 MT2、MT3 下变红；P3 钉住 80 ＋ 256；W1、W2 给出线上实测 336 B 与 649 B ≤ 978 B；MT1～MT4 各有指定红用例。不做线级复现的理由站得住：成因已在 TASK-26 评审确证，标题只有一个产出点，解码器挡住即进不了扇出。**F-115 可以关闭** |
| Spec 与协议记录 | VC-9 措辞与裁定一致；`protocol/` 三处只追加，0x000C.md 加在人工区；依据等级 CurrentImplementation，「客户端不超过 256 B」标为 Hypothesis，F-117 已登记 |
| 收口 §12 | 926；N_max 566（566 × 926 ＝ 524,116）；1,133；17,646 B、余 547 人；F-116 的 3,121 次；AC-013／RQ-036 条件只剩 F-113、F-114，§6、§9 原文保留并标作废；T-006 完成与 M-2 进入以 TASK-27 提交为准，成立；F-115 已关闭，F-116～F-118 未关闭且有归属 |
| 数字与日志 | 通过 8227 ＋ 失败 2 ＋ 跳过 18 ＝ 8247，N ＝ 8；取证 40 条；表征 10 过 0 跳过；「the drain tears it down」4 条判为停机时序可接受（Networking 隔离 0 条，新用例 ×10 为 0）；正式全量后只改注释换行，变异后重建与定向复跑在最终文本上，不重跑全量可接受 |
| 边界与行尾 | `Server/src` 只改 `SetPartyInfoDecoder`；测试只点名的 3 个文件；`Docs/`、`AGENTS.md`、`plan.md` 未改；`cr-at-eol` 口径 `diff --check` 无输出；CR 为 0 |

## 剩余风险（不阻断）

- 256 B 是推断，客户端合法上限若更大，合法玩家改标题会被断线；由 F-117 取证跟进。
- 普查分类是人工判读的表，今后新接线的协议没有判据守着，由 F-118 跟进。

## 主控方处置

4 条发现在提交前处理，见 `tasks/TASK-27.md`「主控方处置（评审后，提交前）」。
