# TASK-04 自动化与构建证据（审查前）

## 静态探针与捕获器

- spec-001 A21 设置探针：`byte_anchors=14/14 PASS`。
- Normal 专项探针：`byte_anchors=5/5 PASS`。首次按系统默认 GBK 输出长文本时遇到
  `UnicodeEncodeError`，改用 Python `-X utf8` 后完整通过；输入哈希与锚点未变化。
- 定向捕获器：基础 split/coalesced/allowlist/create 顺序 `SELF_TEST_PASS`；Normal 空 body `0x000D`
  allowlist `NORMAL_SELF_TEST_PASS`。

## TDD 生命周期纠正

- RED：transfer 实现仍存在时，队长显式离队与 leader session cleanup 两个行为测试均因得到非空
  replacement `PartySnapshot` 而失败，`2 Failed / 0 Passed`。
- GREEN：移除 replacement/retirement 后同一筛选命令 `2 Passed / 0 Failed`。
- 产品代码和测试中 `PartyRetirement` / `Retirement` 引用为 0。

## Release 构建

`dotnet build Server/86JP.Server.slnx -c Release --no-restore --no-incremental -m:1`：
`0 warning / 0 error`。

## 全量测试

固定 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf` 后，以 `-m:1` 串行项目运行
Release 全套：

| 项目 | Passed | Skipped | Failed |
|---|---:|---:|---:|
| Launcher | 63 | 0 | 0 |
| Pvf | 60 | 0 | 0 |
| Application | 1379 | 0 | 0 |
| Host | 17 | 0 | 0 |
| Infrastructure | 301 | 2 | 0 |
| LauncherApi | 50 | 0 | 0 |
| Networking | 62 | 0 | 0 |
| Persistence | 353 | 0 | 0 |
| Protocol | 800 | 0 | 0 |
| **合计** | **3085** | **2** | **0** |

两项 skip 是专门验证“缺失 PVF 档案时失败”的反例，在提供真实 PVF 的完整运行中按设计跳过。

首次以解决方案默认并行项目运行时，唯一失败是既有
`NotificationDeliveryTests.The_deadline_a_stalled_recipient_runs_into_is_the_configured_one`：两次 50ms
deadline 在并行负载下测得 3099ms，超过既有 2.5s wall-clock 上界。该测试独立连续 5 次均通过
（171–180ms），Application 全项目独立运行通过，解决方案串行运行也通过。证据支持“并行项目造成
timer 调度饥饿”，不支持队伍代码回归；未扩大时间阈值或修改无关通知代码。

## 资料与仓库检查

- 新增 12B C2S create、61B S2C preset-title roster、5B clear 三份最小样本，字节数分别核对为
  `12 / 61 / 5`。
- `0x000C`、NOTI `0x0009`、trace、game index 与 catalog 已同步当前证据等级。
- `git diff --check`：Passed。
- 本轮 Host、中转、DNF 与任务端口已在实机结束后确认无残留。
