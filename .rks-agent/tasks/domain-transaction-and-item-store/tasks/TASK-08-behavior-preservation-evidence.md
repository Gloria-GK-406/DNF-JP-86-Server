# TASK-08 — 行为保持的整体证据

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 给出帧字节比对、全套测试与实机走查三类证据，证明对外可观察行为一字未变。
- Referenced spec requirements: RQ-009
- Referenced plan sections: `../plan.md` 的 `### T-008` 全节；BD-016
- Owned boundary: BD-016
- Permitted files or interfaces:
  - `Server/tests/Dfo.Server.Protocol.Tests/`（帧字节比对用例）
  - 本 Task 文件（实机走查记录）
- Dependencies: TASK-04, TASK-06, TASK-07
- Required dependency outputs: TASK-04、TASK-06、TASK-07 的全部产出——本任务的证据覆盖它们之后的最终状态。
- Acceptance conditions:
  1. 移动与使用的帧字节与改动前逐字一致。
  2. 全套测试为 848 通过 / 2 跳过 / 0 失败的基线加全部新增用例；`ChannelHostStartupTests` 若因负载假红，单独重跑即绿，不计回归。
  3. 实机走查：教学关拖拽道具与使用药剂的表现与改动前一致，含服务端冷却拒绝仍然生效。
- Task-specific authorization: 沿用根 TaskContract 的 `task` 授权。重建前须先停止 `Dfo.Server.Host` 进程。
- Required methods: 证据性任务：不新增生产行为，只产出行为保持的可核对证据。
- Required checks: CK-025、CK-026、CK-027、CK-028（见 `../plan.md` 对应任务的 Validation 表，命令与预期结果以该表为准）
- Required evidence: CK-025 的帧字节比对结果；CK-026 的进程停止结果；CK-027 的全套结果；CK-028 的实机走查记录。

## Execution Result 1

### CK-025：帧字节

两条协议的帧字节不是「测出来一致」，而是**按构造不可能不一致**：`MoveItemSpacePayloadCodec` 与 `UseStackablePayloadCodec` 两个类的源码与基线逐字节相同（以 `git show HEAD:` 取基线，对两个类各自的完整正文做相等比较，结果均为 True）。请求记录、解码器、编码器三者也无任何改动——两个 slice 文件的全部 diff 落在 `ItemMoveStatus` 枚举、两个命令处理器的正文与 `ErrorCodeFor` 上，没有一处落在编解码路径。

在此之上再跑测试：

- `--filter FullyQualifiedName~MoveItemSpaceCodecTests|FullyQualifiedName~UseStackableCodecTests` → `已通过! - 失败: 0，通过: 13`
- `--filter FullyQualifiedName~Codec`（全部协议编解码）→ `已通过! - 失败: 0，通过: 161`

两个协议测试目录（`Game/MoveItemSpace/`、`Game/UseStackable/`）的 `git diff` 为空：帧字节用例一行未改。

### CK-026：重建前停止宿主进程

`Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*Dfo.Server.Host*' }` 命中 PID 29420，已 `Stop-Process -Force`。复查时唯一残留命中是查询命令自身的 PowerShell 进程（它的命令行里含有搜索串），实际宿主进程为零。随后以 `--no-incremental` 全量重建，`0 个警告 0 个错误`。

### CK-027：全套测试

`dotnet test Server/86JP.Server.slnx -m:1` → **`940 通过 / 2 跳过 / 0 失败`**

| 项目 | 通过 | 跳过 |
|---|---|---|
| Dfo.Pvf.Tests | 60 | 0 |
| Dfo.Server.Application.Tests | 448 | 0 |
| Dfo.Server.Host.Tests | 4 | 0 |
| Dfo.Server.Infrastructure.Tests | 182 | 2 |
| Dfo.Server.Networking.Tests | 46 | 0 |
| Dfo.Server.Protocol.Tests | 200 | 0 |

基线 848 通过 / 2 跳过，净增 92：新增 101 条，删除 9 条（`MainInventoryMoveTests`，它测的方法已在 TASK-06 退役）。既有用例语义无一改动。

紧接 `--no-incremental` 全量重建的那一次全套跑动中 `Dfo.Server.Host.Tests` 出现 1 条失败，单独重跑该项目即 `4 通过 / 0 失败`，再跑一次全套亦全绿。按验收条件 2 记为负载假红，不计回归。

### CK-028：实机走查——**未完成，需要用户**

组合根冒烟已做，且是本次改动之后最值得做的一项自动检查：新增了两个规则服务、改了持久化仓储的构造签名、加了 `IItemStore` 的接口映射，任何一处接错都只会在真正启动时才炸。实际启动结果：

```
Starting DFO server with modules: Infrastructure, Application, Networking, Protocol
IChannelTcpService: 监听已启动
IGameTcpService:   监听已启动
Application started.
```

DI 图完整解析，两个监听端口（7001、10011）均正常起来，无异常。进程随后已停止。

**但走查本身没有做，也不该由我代记为已做。** 它要求在教学关里用真实客户端拖拽道具、饮用药剂，并核对服务端冷却拒绝仍然生效——这需要有人在屏幕前操作。本仓库的前例也正是如此（`inventory-container-refactor` 的 `completion-evidence.md`：「2026-08-18 **由用户**在教学关走通」）。

留给用户执行，步骤与判据：

1. `cd Server/src/Dfo.Server.Host/bin/Debug/net10.0` 后 `NETWORKING__ENABLED=true ./Dfo.Server.Host.exe`
2. 进教学关，把药水拖到快捷栏——**落位应与改动前一致**，服务端日志应出现 `Move item space ... item 8474 3->4 count=N`
3. 按键使用药水——**应回血且数量减一**，日志应出现 `Use stackable ...`
4. 立刻再按一次——**应被冷却拒绝**，客户端表现与改动前一致
5. 退出再入场——**背包内容应与退出时一致**。这一条比前几条更要紧：移动现在是 clear + write，行号会变，入场快照若有问题只会在这一步显现

该项完成前，TASK-08 的验收条件 3 未满足，本任务不应被记为完全通过。

## Executor Self-check 1

| Acceptance condition | 结果 |
|---|---|
| 1. 移动与使用的帧字节与改动前逐字一致 | 通过——编解码源码与基线逐字节相同，且 13 + 161 条编解码用例全绿 |
| 2. 全套为基线加全部新增用例 | 通过——940 / 2 / 0；Host 项目一次负载假红，单独重跑与再次全套均绿 |
| 3. 实机走查 | **未完成**——需要用户在教学关操作，步骤与判据见上 |

### Validation

| ID | 结果 |
|---|---|
| CK-025 | 通过 |
| CK-026 | 通过 |
| CK-027 | 通过（940 / 2 / 0） |
| CK-028 | **待用户执行** |
