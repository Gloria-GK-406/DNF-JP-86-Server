---
kind: rks-integration-closeout
decision: integrated_with_manual_evidence_remaining
---

# `town-party-lifecycle` 集成收尾

## 仓库结果

- 任务结果提交：`7aec0c18e3c1d21c26e99e2a24bf6d327c94dbec`。
- 主分支合并提交：`06c6a12a`；采用普通 merge，与主分支已有的
  `3d859b3f fix(client): remove facebar compatibility patch` 无冲突合并。
- 本记录不把缺失的 S2C 逐帧抓包补写为完成；根任务与 TASK-08 继续保持
  `in_progress/manual_required`。

## 合并后验证

- `dotnet build Server/86JP.Server.slnx -c Release --no-restore --nologo`：退出 0，
  0 warning、0 error。
- `.rks-agent/tasks/town-party-lifecycle/verification/postmerge-final-20260912`：十份 TRX
  逐条解析为 3050 Passed、0 Failed、0 Other/NotExecuted。九个测试项目在真实
  `PVF_ARCHIVE_PATH` 下逐项目串行运行；Infrastructure 的 301 条 archive-present 用例
  与清空环境变量后的 2 条 `A_missing_archive` 用例分别执行。
- `git diff --check` 在提交本记录前通过。

## 执行期异常

首次以解决方案级并行方式重建证据时，既有测试
`NotificationDeliveryTests.The_deadline_a_stalled_recipient_runs_into_is_the_configured_one`
把两次 50ms deadline 的墙钟上限测得 8563ms，超过其 2.5s 断言。该测试及
`NotificationBus` 均未被任务分支改动；单独复跑为 166ms 通过，随后 Application 全项目
串行复跑为 1369/1369。证据支持跨测试项目并行造成线程调度延迟，而非本次合并的业务回归。
测试源码本身已注明再次出现时不应继续扩大墙钟阈值；本次没有借收尾之机修改该旧测试语义。

## 清理边界

失败尝试生成的未跟踪 `postmerge-20260912` 目录已在保留上述诊断摘要后删除；可验收的
`postmerge-final-20260912` 原始 TRX 被保留。合并前根目录重复生成的 `Logs/` 与
`TestResults/` 也已删除，其同等验收结果保留在任务内的持久化验证目录。
