---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 在线身份一条记录、一把锁；在线角色状态一个无锁运行时

## Planning Choice

选 direct。这是同一套类型在一次编译边界内的迁移：被删除的类型一旦删掉，编译器会列出全部剩余调用点，迁移进度由“能否编译、测试是否通过”直接衡量。拆成 Task Unit，每个单元在中途都编译不过，无法单独验收，交接只会增加成本。整个工作放在一个 Plan 里，最后一次实现提交。

## Approach and Shared Decisions

- **DP-001 在线身份的落点**：在 `Dfo.Server.Application/Users/OnlineStateRegistry.cs` 的同一个 `sync` 内，新增按账号的选中会话记录 `Selection`（`SelectedSessionIdentity`、`Phase`、`Fenced`、`InWorldOwner: OnlineUserId?`）。按角色、按连接、按 `WireUserId` 的查找，在锁内用小字典索引或遍历都可以；记录数等于在线人数，遍历是可以接受的，先按现有 `SelectedSessionDomainService` 的做法遍历。删除 `activeByAccount`、`ActiveCharacterRegistration` 与 `OnlineUser.CurrentCharacterId` 字段；`OnlineUser.Snapshot` 改为由注册表根据 `Selection` 计算出来（RQ-007）。
- **DP-002 接口形状**：`ISelectedSessionDomainService` 增加 `EnterWorld`、`Fence`、`StandingOf`、`IsInWorld`。在 `Dfo.Server.Data/World/SelectedSessionModels.cs` 新增 `SelectedSessionPhase { Selected, InWorld }` 与 `SelectedSessionStanding(Phase, Fenced, ConnectionRevoked)`。`SelectedSessionDomainService` 改为 `internal sealed class SelectedSessionDomainService(OnlineStateRegistry state)`，同时实现 `ISelectedSessionDomainService` 与 `ISelectedSessionSnapshotQuery`，方法体直接调用注册表的同名方法（与现有 `OnlineUserDomainService` 门面写法一致：同一组件的不同职责面，不另存状态）。
- **DP-003 运行时服务**：Abstractions 新增 `Characters/IOnlineCharacterRuntimeDomainService.cs`，Application 新增 `Characters/OnlineCharacterRuntimeDomainService.cs`，内部用 `ConcurrentDictionary<CharacterId, Entry>`，`Entry` 是不可变的 sealed class（引用相等），包含会话、位置、血蓝、`ImmutableDictionary<ItemCooldownKey,long>` 冷却、灵药（倍率、到期）。所有更新走 `TryGetValue` 加 `TryUpdate(key, next, current)` 循环；`EndExact` 用 `TryRemove(KeyValuePair)`；`Begin` 用索引器赋值。不提供会凭空插入记录的更新路径，从而满足 RQ-013 的“不复活”。
- **DP-004 拆除时的频道**：`SelectedSessionCleanupService` 注入 `IChannelEntryPolicy`，按 RQ-014 处理。
- **DP-005 测试夹具**：测试中原来的 `new SelectedSessionDomainService()` 改为基于同一个 `OnlineStateRegistry` 构造。原来只调 `runtimes.Publish` 的测试改为先登记在线用户再 `EnterWorld`；也可以在 `tests/Support` 加一个小工具，一步完成“登记连接、认证、选中、进世界”，只在出现大量重复时才加。

## Work and Dependencies

| ID | Work | Covers | Depends on | Check |
|---|---|---|---|---|
| T-001 | 先写在线身份的单元测试，覆盖 AC-002～AC-006（新文件 `tests/Dfo.Server.Application.Tests/Sessions/OnlineIdentityTests.cs`），确认测试为红 | RQ-002～RQ-010 | — | 测试编译不过或失败 |
| T-002 | 实现 DP-001/DP-002：模型、接口、注册表、门面；删除 `SelectedSessionRuntimeDirectory`、`ActiveCharacterDomainService` 与接口；`SelectedSessionSafety` 改用 `Fence`/`StandingOf`；更新 `ApplicationModule` 注册 | RQ-001～RQ-010, RQ-016 | T-001 | T-001 的测试转绿 |
| T-003 | 迁移身份相关的生产调用点（Spec 调用点映射前四行，以及 `PublishSelectedCharacterSessionCommandHandler`） | RQ-003, RQ-004, RQ-015 | T-002 | `Dfo.Server.Application`、`Dfo.Server.Protocol` 编译通过（运行时部分可以暂时保留旧服务） |
| T-004 | 先写运行时单元测试与并发测试，覆盖 AC-007、AC-008（`tests/Dfo.Server.Application.Tests/Characters/OnlineCharacterRuntimeTests.cs`），确认为红 | RQ-011～RQ-013 | — | 测试为红 |
| T-005 | 实现 DP-003；删除四个旧服务和接口；按映射迁移全部调用方（发布、拆除、两塔、副本、城镇、婚姻、效果、队伍、GM、PlayingCharacter 等）；更新 DI | RQ-011～RQ-013, RQ-015, RQ-016 | T-003, T-004 | T-004 转绿；生产代码全部编译通过 |
| T-006 | 拆除流程按 RQ-014 改用频道推导；写 AC-009 测试；补 AC-010 的“协议拒绝后由断线回调清理”流程测试 | RQ-014, RQ-015 | T-005 | 新测试通过 |
| T-007 | 迁移全部测试项目的编译错误与夹具（DP-005）；不改动断言语义，若必须改断言则逐条记录原因 | RQ-017 | T-005 | 全部测试项目编译通过 |
| T-008 | 跑完整构建与测试，与基线对照；失败逐个定位，属于本次引入的就修复 | RQ-017 | T-006, T-007 | 只剩基线的 2 个失败 |
| T-009 | 更新文档（RQ-018 列出的位置），并检索 Docs 中残留的旧类型名 | RQ-018 | T-005 | 检索结果为零或已说明 |
| T-010 | 运行 AC-001、AC-008 的检索，把结果写入验证证据 | RQ-001, RQ-011, RQ-013, RQ-016 | T-008 | 检索结果为零 |

T-001 与 T-004 互不依赖，可以先后连着写。T-003 完成后，生产代码在“旧运行时服务仍在”的中间状态下可以编译，作为一个安全的停靠点；最终只做一次实现提交。

## Verification

集成验证就是 T-008 与 T-010：完整构建；放有 `Script.pvf` 时的完整测试，与 `verification/baseline.md` 逐项目对照；AC-001、AC-008 的代码检索；新测试按 AC 编号可追溯。之后进行独立的完整结果审查，再按 TaskContract SC-1～SC-7 做最终验证。
