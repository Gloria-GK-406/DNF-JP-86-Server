---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: A21 登录应答与花名册条目

## Delivered

- A21 客户端在「选择角色」界面上渲染出账号已有的角色。实机验证到三个角色，名字、职业、
  等级均正确，删除按钮由置灰转为可用。
- 登录应答由 53 字节对齐到 A21 的 54 字节：版本字节 18→20，环境标识之后补一个零字节。
- 花名册逐角色条目对齐到 A21：等级之后写 `0x0B 0x01`、补 23 字节外观条目、尾部 32→36 字节。
  一个角色带一条外观时 body 为 103 字节，与已知良好会话**逐字节相同**。
- 外观数据从 `characters.appearance_blob` 接到界面：花名册查询多读该列，
  `CharacterSummary` 带上解码后的条目。存储 blob 与线上条目同形，因此是回写而非翻译。
- 同一条线级事实的第二个编码器（入场路径）的尾部一并改正，并补上长度闭合守卫。
- `protocol/` 两条记录、catalog 两行状态、归档件三节（`[H]`/`[I]`/`[J]`）同步落地。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md`：以归档 `[I]` 节 54 字节为期望值的整段比对；实机 body 54 字节 |
| SC-2 | 同上：整段比对通过；实机 body 与参考 103/103 逐字节相同 |
| SC-3 | 同上：一/二/三个角色三次实机会话的渲染结果 |
| SC-4 | 同上：编码器 0/1/3 条外观的长度闭合用例；持久层真实 SQLite 的空与非空两分支；零外观形状已实机验证 |
| SC-5 | 同上：两条协议记录、catalog 两行、归档件三节的最终内容 |
| SC-6 | 同上：构建 0 警告 0 错误；全量 `-m:1` 1209 / 2 跳过 / 0 失败 |

## Repository Result

- Changed paths：22 个已跟踪文件 + 2 项新增（`AccountCharacterRosterCodecTests.cs`、
  本成果的 `.rks-agent/` 制品）。
- Baseline：`9bc75fa`。
- Parent rollback required：no。

## Successor Guidance

1. **「外观 23 / 尾部 36」已由实测判定**，判定构造是「零外观的条目后面还跟着条目」。
   单角色或零外观排在最后都不可判别——客户端读满 `count` 就停。**尾部内部的字段划分仍未定**
   （`0xFF` 落在第 27 字节只有上游支撑）。
2. **等级之后那两个字节 `0x0B 0x01` 是常量还是数据，仍未判定。** 角色列表按实测字节写，
   入场路径按两个进度值写——**同一段字节今天在本仓有两种写法**，入场路径发出的字节与
   已知良好会话不同（除非首个进度值恰为 267）。判定它最便宜的证据是第二个进度值不同的角色。
3. **建号路径不写 `appearance_blob`**，本仓自产角色的外观条目数恒为 0。要让新建角色带上
   初始外观，需要在建号或入场侧补数据。
4. **测试账号里有三个手工种入的探针角色**（槽位 0/1/2，名 `1122`/`3344`/`5566`），
   不是服务端建出来的。根任务收尾前须清除。
5. **`NOTI 0x0002` 在本仓有两个编码器**，本次只统一了尾部；头部之后的十字节仍不一致。
   合并它们属结构调整，记在 `Docs/ProtocolScaffoldDissonances.md` 第七节，未在本成果内动。
6. 入场序列还没碰：USERINFO 子类型 `0x00`（188 字节，开头有 38 字节零块）、`0x01`（519 字节）、
   `0x06`（25 字节，A21 新增）的实测字节都在已知良好会话里，尚未归档。

## Artifacts

- `spec.md`
- `reviews/change-review.md`（三轮：`changes_required` → `changes_required` → 最终判决）
- `verification/completion-evidence.md`
- `protocol/patterns/data/traces/a21-world-entry-20260823.log` 的 `[H]`/`[I]`/`[J]` 节
