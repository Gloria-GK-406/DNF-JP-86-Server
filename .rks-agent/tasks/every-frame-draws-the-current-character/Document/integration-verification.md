---
kind: rks-verification
decision: pass
---

# 合并与工作树清理

2026-09-08。用户授权：「好，先合并到主干和清理工作区吧」。本记录是此前接手结果之后的收尾状态。

原任务成果 `0b6ee257b9131f812e97ab60698589ec43cafa21` 已普通合并到本地主干，合并提交为 `b5c5e750a06e6aefc061f7b72eceb139f8f3d866`。另一父提交为 `4e4791c46379464e50102e01d196c729a1c9fa03`，包含其他任务已合入的 CRAP 工具。合并无冲突，未重写五个任务提交。接受依据仍为 [根审查](root-review.md) 与 [根验证](root-verification.md)，其限定和未实机事项继续适用。

## 当前验证

运行环境提供 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。

| 检查对象 | 命令 | 退出与结果 | 原始输出 |
|---|---|---|---|
| 合并前任务 HEAD 0b6ee257 | `dotnet test Server/86JP.Server.slnx --nologo --logger "console;verbosity=normal"` | 0；2417 通过、2 跳过、0 失败 | [合并前](integration-pre-merge-server.txt) |
| 合并后 main b5c5e750 | 同上 | 0；2417 通过、2 跳过、0 失败 | [合并后](integration-post-merge-server.txt) |
| 合并后 CRAP 工具 | `dotnet test Tool/CrapMeter.Tests/CrapMeter.Tests.csproj --nologo --logger "console;verbosity=normal"` | 0；81 通过、0 跳过、0 失败 | [工具测试](integration-post-merge-crap-tests.txt) |

服务端七程序集通过数：Pvf 60 + Networking 52 + Protocol 631 + Persistence 333 + Infrastructure 259 + Application 1076 + Host 6 = 2417。两次服务端运行均同此分布。两项跳过是 Infrastructure 的 missing-archive 检查，不是缺少实际 PVF 而跳过内容验收。日志中 Hosting failed to start 的异常身份为 NotificationHandlerRegistry 的 InvalidOperationException，属于注册拒绝判据；未出现测试失败、error CS 或 error MSB。

`git diff --check` 通过。合并后的 Server/src 与任务 HEAD 完全一致；CRAP 工具、其测试、中央包版本及测试 Directory.Build.props 与主干父提交完全一致。AGENTS.md 自动合并保留两批说明。所有原始输出通过二进制复制及 SHA-256 比对保存。

## 清理结果

- 从主仓执行清理前，确认任务路径位于本仓 `.worktrees/`、与 Git 注册路径一致、HEAD 未变且无未提交文件；被忽略内容仅为 Server 构建和测试输出，其中数据库位于测试项目 bin 目录。
- `git worktree remove` 成功，目标目录 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/every-frame-draws-the-current-character` 已不存在。
- `git branch -d every-frame-draws-the-current-character` 成功；分支成果均已是 main 的祖先。
- `client-debug-lab` 与 `entries-are-what-the-client-reads` 两个工作树保留，HEAD 分别为 c8f7497e 与 f5c00fb8。CRAP 工作树在本轮开始前已由其他操作清理，本轮未再操作它。
- `git worktree prune --dry-run` 无待清理记录。清理后 main 工作区干净；随后只增加本记录和三个验证日志，作为一次证据提交保存。审计提交 HEAD 在提交后单独报告。
- 本轮未推送或修改 GitHub issue。远端状态不由本地合并自动改变。
