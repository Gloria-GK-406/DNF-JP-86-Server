---
kind: rks-verification
contract: ../TaskContract.md (revision 7)
spec: ../spec.md (revision 7)
verified_at: 2026-11-13
server_head: 5c6e66651 (feature/adventure-group)
clienthack_head: b41b33d (task/adventure-login-banner)
---

# 完成验证：冒险团

## 新鲜检查（2026-11-13，提交 5c6e66651 的工作区）

- **构建**：测试服停止后，`dotnet build Server/86JP.Server.slnx` 输出「已成功生成」，0 警告、0 错误。
- **全量测试**，`dotnet test Server/86JP.Server.slnx`：

  | 模式 | 失败 | 各项目通过数 |
  |---|---|---|
  | 设 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` | 0 | HttpApi 146、DbMigrate 27、Pvf 82、Protocol 2215、Infrastructure 1038（跳过 2）、Persistence 456、Application 6644、Host 243（跳过 1）、Networking 218 |
  | 不设 PVF | 0 | 依赖 PVF 的用例按约定跳过 |

- **ClientHack**：`ctest --test-dir ClientHack/out/win32-release -C Release` 17/17 通过，其中包括 `clienthack_adventure_levelup_fix_tests`。
- **列名搜索**：在 `Server/src` 与 `Server/tests` 中大小写敏感搜索 `manage_level`，命中 0 处。

## 逐条成功条件

| SC | 结论 | 证据 |
|---|---|---|
| SC-1 | 通过 | 测试覆盖 AC-001 列出的全部边界值，并保留两处 PVF 格式缺陷：`AdventureGroupTests`、`PvfCharacterManageContentSourceTests`。真实 PVF 用例在带 PVF 时实际运行并通过 |
| SC-2 | 通过 | `AdventureGroupTests` 覆盖删角、新建低等级角色、改等级；代码里没有冒险团值的持久化写入（独立审查已确认） |
| SC-3 | 通过 | `AdventureGroupWireTests` 覆盖：USERINFO1 尾部、subtype3 `+3`、`0x01BA` 两条路径、subtype2 写 0。`LightweightProtocolCommandFlowTests` 断言 `0x01BA` 按已登录账号回答（`01 03 42 54 00 00 00`）。特征化 golden 只在冒险团字节上有差异 |
| SC-4a | 自动化通过；实机部分见下 | 自动化：`ExperienceCalculationDomainServiceTests`、`ExperienceBonusDomainServiceTests`、`DungeonClearCodecTests`（body+45）。实机：用户没有逐项报告结算面板数值，整体结论为「没问题了」 |
| SC-4b | 通过 | `BossTowerKeepsItsCapAndCutsTheAdventureGroupLast` 覆盖未触顶、截短、触顶三种情况 |
| SC-5 | 通过 | 自动化：`AdventureGroupPushTests`、`AdventureGroupTests` 中的 `LevelRiseAsync` 用例。实机：用户明确反馈用升级券升级时出现冒险团升级提示。帧日志 13:48:40，账号 4 的点数 54055 → 54315（越过阈值 54250），冒险团 5 → 6 级，同一时刻发出恰好 1 帧 `0x01A0`，紧接着的 `0x01BA` 为 6 级。新建高等级角色时（4 → 5 级）按设计不推送 |
| SC-6 | 通过 | DbMigrate 测试（含 v4→v5 测试与结构一致性测试）通过；列名搜索 0 命中；主干库副本迁移后 116 张表行数一致（`artifacts/adventure-group/migrate.log`） |
| SC-7 | 通过 | 协议页 `0x01BA`、`NOTI-0x0002`、`NOTI-0x0023`、`NOTI-0x01A0` 与 `catalog.md` 已更新。证据标签为 ClientStatic、CurrentImplementation、用户实测，没有标 ClientVerified |
| SC-8 | 通过 | 见上「新鲜检查」 |
| SC-9 | 部分明确、整体通过 | 用户明确反馈：选人界面右侧冒险团、城内 buff 都显示。冒险团窗口、四维、第二客户端查看他人，用户没有逐项报告，整体结论为「没问题了，可以开始走 PR 流程」 |
| SC-10 | 通过 | 原因的取证记录在协议页 `NOTI-0x01A0` 与 `Document/tasks/adventure-group/re-login-banner/`。ClientHack 构建与测试通过。客户端日志确认 `0210_AdventureLevelUpFix` 已加载，且在首次绘制时把初始动画标为已结束。用户整体结论为「没问题了」，没有单独报告重登两次的情况 |

用户对实机验收的明确授权原话是：「我测试过了，在用升级券升级时会挑出来冒险团升级标志。所以没问题了。可以开始走pr流程了。」凡上表标注「没有逐项报告」的，PR 描述中如实写明。

## 用户测试中顺带发现

「升级券（Lv1～85）」使用后确认窗口不关闭。与本任务无关，已登记为 Gloria-GK-406/DNF-JP-86-Server#319。
