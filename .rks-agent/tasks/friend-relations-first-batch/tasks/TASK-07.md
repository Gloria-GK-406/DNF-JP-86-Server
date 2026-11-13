# TASK-07 — 测试服与编号测试流程

## Task Unit Contract

- Revision: 1
- Outcome and references: Plan T-007。在本分支上起一个用户可以连接的测试服，使用用户确认的数据库的一致副本（已备份，已迁移到 v5），并提供编号测试流程，交给用户实测。覆盖 RQ-071、AC-071、TaskContract SC-10；用户的反馈是 SC-1 至 SC-7 实机部分的证据。
- Owned boundary:
  - 测试流程文档 `Document/tasks/friend-relations/用户测试流程.md`（新建，跟随分支提交）；
  - 测试服产物：启动脚本、数据库副本、日志，放在工作区 `artifacts/friend-relations/` 下，不提交。`artifacts/` 是否被忽略要先确认，没被忽略就只放在 scratchpad 或主检出的 `artifacts/` 下；
  - 测试期间临时修改 `DNFClient-A21-protocol`、`DNFClient-A21-protocol2` 的 `Launcher.ini` 中的 `BaseUrl`，测完改回。
- Required checks:
  - 测试服进程启动，Game、Channel、启动器端口都在监听；
  - 启动器状态端点返回 200；
  - 数据库副本的 `user_version` 为 5，迁移前的备份文件存在；
  - 测试流程文档覆盖 AC-071 列出的全部场景，每步写明预期。
- Dependencies: TASK-06
- Result review: not-required — 交付物是运行中的测试服和一份流程文档，用户实测会直接暴露问题；不改产品代码。
- Task-specific authorization: 用哪一份数据库，开始前用提问工具请用户确认。不停止、不替换用户正在运行的任何服务；只使用 agent 端口段 41000–48999。

## Detailed Plan

1. **确认数据库来源**（提问用户）。推荐：主检出 `artifacts/chat-routing/db/dfo-server.db`，即最近一次用户测试用的 v4 库，含 11-12 的进度。备选：用户指定的其他库。
2. **准备数据库副本**：用 SQLite backup API 或 `VACUUM INTO` 生成一致副本，带上 WAL 中的内容；再用 `dotnet run --project Tool/DbMigrate -- <副本>` 迁移到 v5。迁移工具会自动生成 `.v4-…bak` 备份。原库不动。
3. **构建与启动**：
   - 本分支 Debug 构建；
   - 启动脚本照搬 `artifacts/chat-routing/run-test-server.sh`：端口 41001/41010/41011/41100/41080，`Channel__UseScriptCatalog=false`，GmWeb 关闭；
   - gate token 从客户端 `Launcher.ini` 读取，不打印；
   - `PVF_ARCHIVE_PATH` 用 `DNFClient-A21-protocol/Script.pvf`；
   - 后台运行。
4. **客户端**：把 `DNFClient-A21-protocol`、`protocol2` 的 `Launcher.ini` 中 `BaseUrl` 临时改为 `http://127.0.0.1:41080/`，流程文档写明测完改回。
5. **测试流程文档**：格式参照 `Document/tasks/chat-routing/用户测试流程.md`。两个角色 A、B。前置条件写明两个账号、两个角色，最好放在不同频道或不同区域。步骤按以下顺序：
   1. A 加离线的 B：成功，列表出现 B（灰色）；B 的列表没有 A。
   2. B 上线：A 聊天框出现「B 进入 N 频道」，列表中 B 转为在线，显示频道号（AM-2：同频道也显示频道号）。
   3. B 换到另一个频道：A 先看到下线，再看到进入新频道。
   4. 同区域与不同区域：A 的列表都显示在线。
   5. B 升级（可以用 GM 或打怪），或转职：A 的列表等级或职业随之更新，A 的黑名单不受影响。
   6. 重复加 B：没有第二条；加不存在的名字：提示「角色不存在」；加自己：无提示。
   7. A 删除 B：列表移除；再删除：提示「列表中不存在，重连后刷新」。
   8. A 拉黑 B：黑名单出现 B 和日期。B 对 A 说区域话、私聊、喇叭，A 都收不到，其他人能收到。B 对 A 发起 1:1：B 看到黑名单提示，A 没有窗口。
   9. A 解除拉黑 B：恢复收到消息，可以建立 1:1。
   10. B 拉黑 A 后，A 发起 1:1：A 看到黑名单提示。
   11. 好友与黑名单同时存在时，A 重登：两个列表都完整。服务端重启后再登录：都还在。
   12. B 下线：A 的列表中 B 转为离线（灰色），聊天框出现下线提示。
   13. 回归：普通聊天、组队、1:1 对话照常。
   - 每步列出要观察的客户端表现，以及表现异常时要记录的内容。
6. 在 Result 中写清测试服就绪信息：分支与提交、端口、数据库路径、备份位置、启动方式，然后用提问工具请用户按流程测试并反馈。

## Result

- Outcome: done
- Round: 1，主代理执行。
- Changed:
  - 新建 `Document/tasks/friend-relations/用户测试流程.md`，16 个编号步骤，加上已知限制和测完的收尾说明。
  - 测试服（不提交，`artifacts/` 已被 .gitignore 忽略）：
    - `artifacts/friend-relations/run-test-server.sh`
    - `artifacts/friend-relations/db/dfo-server.db`
    - `artifacts/friend-relations/test-host.log`
  - 临时修改：`DNFClient-A21-protocol`、`protocol2` 的 `Launcher.ini`，BaseUrl 从 18080 改为 41080，测完改回。
- Why:
  - 数据库来源由用户在提问中选定：主检出 `artifacts/chat-routing/db/dfo-server.db`，即聊天测试库的副本。
  - 副本用 SQLite backup API 以只读方式做一致副本，含 WAL 内容。
  - 黑名单测试只用 85、86 级角色：客户端本地限制 20 级以下不能用黑名单（client-evidence RQ-002）。
- Checks:
  - 副本 integrity_check 为 ok，原库 user_version 4。
  - `DbMigrate`：4→5，自动备份 `dfo-server.db.v4-20261112151025.bak`，迁移后 user_version 5，`character_relations` 存在。
  - 测试服基于提交 ef5096477 的 Debug 构建，以下端口都在监听：41001/41010/41011/41100（0.0.0.0）、41080（127.0.0.1）。内容预热 77 项，0 失败。
  - `GET /api/gate/v1/status`：带 gate token 返回 200，不带返回 401。
- Remaining:
  - 等用户按流程实测并反馈；
  - 测完把 Launcher.ini 改回、停掉测试服。
