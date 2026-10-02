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

## spec-003 acceptance

- 已封存completed result、approved/aligned独立审查及fresh verification pass；一次接受提交573be46b075d8363f0882c2f40a5a0135d1ada0c，父提交416f26c9。完整暂存diff检查exit0后才提交，未改sealed结果。
- 提交后status为空，任务分支feature/gm-web-admin；source main仍5fbe2dad且干净。全解决方案5317 passed / 0 failed / 2 deliberate skips，前端25 passed，构建与发布核对通过，实际浏览器查询检查及隔离夹具正常退出另有证据。
- root恢复依序消费child-sealed→child-accepted；基线推进573be46b。后续正常游标变化仅声明的durable task artifacts。

## spec-004 acceptance

- 当前Round2独立approved/aligned与fresh verification pass后一次验收提交07e1f607777c082a09e4614d5d81cf137dee41ce，父提交573be46b。完整60路径，cr-at-eol兼容历史CRLF的暂存检查exit0；提交后worktree干净、source main仍5fbe2dad且干净、分支feature/gm-web-admin。
- fresh全量5355 passed/0 failed/2 deliberate skips，前端41 passed，当前构建/发布及隔离夹具正常停止通过。sealed result保持不可变。child游标完成后，root依序child-sealed进入child_disposition、child-accepted进入root_evaluation，推进基线与SC-4。

## spec-005 acceptance

- 当前独立approved/aligned、fresh5373pass/0fail/2deliberate skip、前端54pass、真实Chrome和当前发布/夹具通过后，一次验收提交aa5b663672c1beaac82484e57c3bbbeff5429d44，父07e1f607；56完整路径。两份raw历史日志原样保留，暂存空白检查仅排除当前verification原始*.log，所有产品/文档/其它证据check exit0；没有失败commit或重试。
- 提交后status为空、branch feature/gm-web-admin，source main仍5fbe2dad且干净，accepted链只前进此一commit。sealed result不回填ownSHA；child cursor verification/completed。
- 消费terminal completed result与repo状态，child-sealed进入child_disposition/pending。

- child-disposition复核completed/currentpass与parent identity，child-accepted推进baseline aa5b6636，清除active child/封存第5行；仅根声明durable artifacts正常变化。
