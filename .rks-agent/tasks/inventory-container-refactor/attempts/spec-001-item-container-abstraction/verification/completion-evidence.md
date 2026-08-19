---
kind: rks-verification
decision: pass
---

# 完成验证：容器抽象与角色绑定

## 被验证的主张

子契约 `TaskContract.md` revision 2 的 AC-1..AC-5 已达成：道具读写经由可替换容器完成，
角色对「当前使用哪个容器」有显式绑定，且对外可观察行为一字未变。

证据于评审通过后重新采集，未沿用实施阶段的旧结果。

## 条件与证据

| 条件 | 证据 | 结论 |
|---|---|---|
| AC-1 道具移动与道具使用两条协议不再直接依赖持久化仓储 | `MoveItemSpaceHandler` 与 `UseStackableHandler` 的构造依赖为 `ICharacterContainerDomainService`；`CharacterContainerDomainServiceTests.The_item_protocols_reach_storage_only_through_a_container` 以反射断言两者的构造参数中不含 `IEntryInventoryMutationRepository` | pass |
| AC-2 当前容器可作为背包投影内容的来源 | `ContainerInventoryProjectionTests` 在同一份 SQLite 存储上，分别经容器与入场加载路径生成 Main 的容器状态并编码 `0x000D` / `0x000E`，断言字节一致。行数与列表过滤均被覆盖：去掉 `ReadMainItemsAsync` 的 `AND list_type=0` 后该用例失败（已实测） | pass |
| AC-3 同类型下可注册多个容器，可列出、可切换、可移除，首个注册者为初始当前容器 | `CharacterContainerDomainServiceTests` 的 `The_first_container_registered_is_the_one_in_use`、`Switching_changes_the_binding_and_nothing_else`、`Removing_the_container_in_use_falls_back_to_the_one_that_cannot_be_removed` | pass |
| AC-4 移除当前容器后角色仍绑定到一个可用容器；无可回落容器时以异常暴露 | 同上的回落用例，以及 `Removing_the_only_container_leaves_the_character_bound_where_it_was`——异常抛出后列表与绑定均未被破坏 | pass |
| AC-5 对外行为一字未变（自动化部分） | `dotnet test Server/86JP.Server.slnx -m:1`，`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 均指向 `D:/project/DNF/DNFClient-debug/Script.pvf`：Pvf 60、Application 339、Host 4、Infrastructure 154（跳过 2）、Networking 46、Protocol 188，合计 **791 通过 / 2 跳过 / 0 失败**，即基线 780/2/0 加本次新增的 11 个用例。既有用例的改动经三轮评审确认仅为构造参数与接口成员的机械适配 | pass |
| AC-5 对外行为一字未变（实机部分） | 2026-08-18 由用户在教学关走通：药水拖到快捷栏落位、按键使用后回血且数量减一。服务端日志：`Move item space character=5: item 8474 Relocate 3->4 count=2`，`Use stackable character=5: consumed 1 of item 8474 from slot 4, 1 left`。两条都经由当前容器完成 | pass |

## 实机走查记录

- 环境：worktree 构建的 `Dfo.Server.Host`，配置覆盖为
  `--Networking:Enabled=true`、`--Database:Path=<主检出的 dfo-server.db>`、
  `--Pvf:ArchivePath=D:/project/DNF/DNFClient-debug/Script.pvf`，即代码取自本分支、
  数据与归档取自既有环境。
- 首次启动漏配 `Pvf:ArchivePath`，选角准备阶段抛 `FileNotFoundException` 致连接中断；
  补配后复测通过。该次中断源于启动配置，与本次改动无关。
- 结果：拖动与使用的表现与改动前一致，无异常帧、无额外推送。

## 补充记录

- 三轮独立评审的完整记录见 `../reviews/change-review.md`，最终 `decision: approved`，无未决阻断项。
- 评审第二轮指出契约与交付物不符，用户于 2026-08-18 授权将根契约 SC-2 与子契约 AC-2
  重述为「当前容器可作为背包投影内容的来源」，两份契约升至 revision 2；
  「投影真正由容器驱动」与「入场快照改由容器供给」一并记入
  `../../multi-spec-map.md` 的 `## Deferred Follow-up`，随多容器切换那一刀落地。
- 已知缺陷「`unlimited*` 道具被扣减」按约束未在本次修复，属根任务成果二。
