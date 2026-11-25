# TASK-09 — 用户测试服与编号实测步骤

## Task Unit Contract

- Revision: 1
- Outcome and references: 基于本分支 Release 构建与当前主干玩家库一致备份副本（迁到 v11）的测试服在 41000–48999 端口运行，不触碰用户正在运行的服务；`Document/tasks/pvp-duel/用户测试流程.md` 给出就绪说明与覆盖 SC-1–SC-10、重登、重启、停服及待实机抓包项的编号步骤。Plan T-008（A2 后为 TASK-09）；Spec RQ-081；TaskContract SC-12。用户反馈、修复复测与抓包结论回写属 SC-13，另起单元。
- Owned boundary: `artifacts/pvp-duel/`（被 gitignore 的测试服产物与脚本）、`Document/tasks/pvp-duel/用户测试流程.md`。
- Required checks: 测试服监听正常、网关 `GET /api/gate/v1/status` 返回 200 且 PVF 摘要与客户端一致；数据库副本完整性与版本核对；文档覆盖 SC-12 列出的项。
- Dependencies: TASK-08
- Result review: not-required — 运行与文档产物，检查即为实际服务状态与清单对照；错误会在用户测试第一步暴露，且不触碰主干库与原服务。

## Detailed Plan

用 SQLite backup API 只读复制 `DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Data\dfo-server.db`，查越界段位，DbMigrate 迁到 v11；Release 构建复制到 `artifacts/pvp-duel/server`；脚本以环境变量设置 42xxx 端口、显式频道（CH11 环境 1、CH50 环境 24、CH60 环境 8）、网关 token（从 Launcher.ini 读取不打印）与客户端一致的 PVF；启动后核对监听与网关状态；写测试流程。

## Result

- Outcome: done
- Changed: 测试服运行中（`artifacts/pvp-duel/run-test-server.ps1`，监听 42001/42011/42050/42060/42080）；`Document/tasks/pvp-duel/用户测试流程.md`。
- Why: 41xxx 已被另一测试服占用（PID 52636，主检出 Debug 构建），故用 42xxx；客户端目录 PVF 已换为 `revised-20261114`（a97132dd），启动器比对 PVF，测试服改用同一份；脚本须带 UTF-8 BOM，否则 Windows PowerShell 5.1 按 GBK 读中文注释会吞掉下一行。
- Checks: 备份 `pragma integrity_check` = ok，9 角色，`character_pvp` 越界段位 0；DbMigrate 0→11 成功，`character_pvp_record`/`character_pvp_skill_state` 各 9 行、`character_pvp_legacy` 0 行；网关 status 200，`pvfSha256` = a97132dd…cfcd2，gamePorts [42011, 42050, 42060]。
- Remaining: 用户按流程实测、反馈后修复复测并回写抓包结论（SC-13）。
