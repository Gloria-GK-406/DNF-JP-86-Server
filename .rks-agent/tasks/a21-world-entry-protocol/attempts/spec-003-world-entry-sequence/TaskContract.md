---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-3, SC-4, SC-5]
---

# TaskContract: A21 入场序列

## Context

### Source Request

根控制器 `a21-world-entry-protocol` 的最后一个有界成果：让 A21 客户端选中角色后**留在游戏
世界里**，而不是在收完入场批次后断开。

### Current Situation

spec-002 之后，客户端已能到达「选择角色」并正确渲染角色。用服务端自己的建号路径造出角色
后（此前手工种入的角色缺五张入场必需表，在选人时被判 `incomplete required profile state`
而拒绝——那是数据问题不是协议问题），实机链路推进到：

```
C→S CMD/0004 SELECT_CHARACTER        → S→C 261 字节应答（与参考一致）
S→C 入场批次 56 帧（含 ENTER_GAMEWORLD_COMPLETE、USER_POSITION）
C→S INFORM_NOTICE_2ND / CERA / MERCENARY_INFO     （与参考一致）
C→S CMD/02B3 STATICS_RUNTIME_TING → 关闭连接
```

「一条 `0x02B3` 之后断开」与 spec-001 里 `CHANNELINFO` 发错时是同一形状。

逐条对照已归档为 `protocol/patterns/data/traces/a21-world-entry-20260823.log` 的 `[K]` 节：
**22 条协议与参考完全一致**（含 `CLEAR_QUEST_LIST` 30004 字节与选人应答 261 字节）。差异：

| 类别 | 内容 |
|---|---|
| 布局可疑（定长或半定长） | USERINFO 子类型 `0x00`（本仓 134 / 参考 188）、子类型 `0x01`（153 / 519）、子类型 `0x06`（**本仓不发** / 25，本仓改发第二遍 `0x00`）、`ENTER_GAMEWORLD_COMPLETE`（0 / 8） |
| 大小不同但依赖数据 | `ITEM_LIST`、`SKILLINFO`、`EVENT_INFO`、`GAME_OPTION`、`HOTKEY_OPTION`——**大小不同不等于布局不同** |
| 本仓缺发 | `USER_STATE`、`PARTY_INFO`、`VILLAGE_OBJECT_LIST`、`SET_USABLE_COUNT_INFO`、`BUY_RESTRICT_ITEM_LIST`、`CONTRACT_OF_CUBE_INFO`、`EQUIPMENT_RENTAL_LIST`、`EPIC_BOOK_INFO`，以及 CMD `MERCENARY_INFO` |
| 本仓多发 | `BOOSTER_GAGE`、`TAG_CHARACTER_INFO`、`YUT_GAME_EVENT_INFO`、`LUKE_RAID_KILLED_NAMED_INFO`、`TOURNAMENT_END`，以及 CMD `CHANGE_GUILD_MARK`（参考实现在 A21 迁移里删掉了这条分支） |

另一件已确认的事实：**A21 客户端自带的 `Script.pvf` 与 A12 那份不是同一个归档**
（72 MB / 2017 对 58 MB / 2015）。建号与入场都要读内容，服务 A21 必须读 A21 那份。

## Goal

A21 客户端选中角色后进入并留在游戏世界；入场批次中每一处与已知良好会话不一致的线级布局
都以实测字节为依据改正或如实记录；相关结论同步落进协议知识库。

## Scope

### In

- NOTI `00/0002` USERINFO 的入场子类型 `0x00`、`0x01`、`0x06` 的报文体布局。
- NOTI `00/007C` `ENTER_GAMEWORLD_COMPLETE` 的报文体。
- 入场批次里本仓缺发与多发的通知集合。
- 上述各处的 `protocol/` 记录与 `protocol/catalog.md` 状态。
- 为使实机可复现而需要的数据或配置（含服务端读取 A21 归档）。

### Out

- 进入世界之后的玩法协议（移动、副本、战斗、交易等）。
- 大小差异纯由角色/账号数据决定的协议（`ITEM_LIST`、`SKILLINFO`、`GAME_OPTION`、
  `HOTKEY_OPTION`、`EVENT_INFO`）——除非有证据表明其布局也变了。
- 服务架构变更。
- `ClientHack/` 的代码改动。
- 合并 `NOTI 0x0002` 的两个编码器（已记在 `Docs/ProtocolScaffoldDissonances.md` 第七节）。

## Constraints

- 不得修改服务架构：分层与项目引用图、协议切片边界、handler 四类允许行为、命令总线与
  管线次序、注册表接线方式均不变。
- 线级结论必须以实测字节为依据；上游实现只作线索，采纳前须用实测字节验证。
  **大小不同不足以断定布局不同**，须分辨数据差异与布局差异。
- 开发阶段擦库不迁移。
- `protocol/` 不得含凭据；引用必须能解析，整段会话须先归档再引用。
- 测试 `-m:1`；`protocol/**` 为 LF、其余 CRLF、无 BOM；`TreatWarningsAsErrors` 保持开启。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | A21 客户端选中角色后进入游戏世界并保持连接 | 实机会话的双向帧记录：入场批次之后不再出现「一条 `0x02B3` 随即关闭」；连接在其后仍有往来 |
| SC-2 | USERINFO 三个入场子类型的报文体与已知良好会话一致（除随角色数据变化的取值外） | 以归档帧为期望值的编码用例，逐字段列出允许不同的项与理由 |
| SC-3 | `ENTER_GAMEWORLD_COMPLETE` 的报文体与已知良好会话一致 | 同上 |
| SC-4 | 缺发与多发两组，每一条都已改正或有记录在案的理由 | 逐条清单与其处置；未改的须说明依据 |
| SC-5 | 每处改动都分辨了「数据差异」与「布局差异」，没有把前者当后者改 | 每条改动指向的实测字节，以及被判为数据差异的那些的理由 |
| SC-6 | 相关结论已落进 `protocol/` 记录与 catalog 状态，被推翻的旧结论保留原因 | 相应文件最终内容 |
| SC-7 | 构建零错误零警告，全量测试通过 | 最终基线上的构建与 `-m:1` 全量输出 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 继承根契约 `a21-world-entry-protocol` 的 `task` 行：「那就处理掉登录进游戏世界前的全部协议问题……不过不能修改服务架构。对了，最好用sdd来渐进重构。并且协议文档要同步更新」 | 本成果的实现、测试、数据与配置调整、协议文档更新，以及为取证启动本仓服务端与 A21 客户端 |

## Assumptions

- 已归档的 A21 已知良好会话代表该客户端接受的行为。
- 断开的直接原因尚未定位到具体某一条协议；上表的「布局可疑」四项是首选嫌疑，
  但**这是待验的假设而非结论**。
- 该会话的角色是一个已有装备与技能的角色，本仓用的是新建的一级角色，因此依赖数据的
  协议大小本就不同；把它们排除在外是有意的判断，不是遗漏。

## Requires Confirmation

- 若定位到的原因需要改动服务架构才能表达，停下并回到用户确认。
