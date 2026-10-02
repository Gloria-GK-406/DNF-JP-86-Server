# Workspace evidence

- 2026-10-02：原生工作区工具因当前会话 cwd 非仓库返回 Not a git repository，使用已有 `.worktrees/` 和 Git CLI 建立同一任务工作区。
- 源仓库 `E:/Project/DNF/DNF-JP-86-Server`：切回 main，HEAD 未改变，干净。
- 代码根 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin`：git-dir 指向源仓库 `.git/worktrees/gm-web-admin`，common-dir 为源仓库 `.git`，没有 superproject。
- 分支 `feature/gm-web-admin`，HEAD `5fbe2dad35bf871a048ce4667055751e973bd844`，创建后状态为空。
- `.worktrees/gm-web-admin` 由本地排除规则忽略；产物/测试与证据只在工作区和其 Document/tasks/gm-web-admin 内生成。所有绝对目标先解析并确认该包含关系。
- 已有 AGENTS.md 适用；SDK/Node/npm/Python 可用。当前正在运行的服务、数据库、PVF 和客户端不作切换。

## spec-001 acceptance

- approved review + fresh verification exit0 后一次验收提交 0630348d2bace7a35887b43b8aaa0c145b0b66a2，父提交为原基线5fbe2dad，35个完整范围路径。
- 提交后 `git status --short` 为空；源 main 仍5fbe2dad且干净。成功验收后更新 controller/child cursor，剩余变更仅为声明的 Document/tasks/gm-web-admin durable artifacts。

## spec-002 acceptance

- 独立 approved/aligned 与最终 approved Round2、fresh verification pass 后接受为 `416f26c9919383b58b0bd0e7179a99734fe53b48`，父提交0630348d；52路径包含完整产品、测试与证据。
- 提交后worktree状态为空、分支feature/gm-web-admin，source main仍5fbe2dad且干净。child结果封存，游标转completed；随后root child-sealed→child-accepted推进基线。
- 验收时unstaged diff check exit0；首次暂存完整新增文件后的 diff check exit1，仅由execution YAML混合CRLF产生行尾空白提示，未形成语义或产品失败，提交已成功且身份可证。游标completed正常重写时统一LF，不改封存spec-result或提交历史。后续完整暂存检查应按退出码顺序消费，不能让前一检查失效被后续命令掩盖。
