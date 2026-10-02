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

## spec-006 acceptance and clean candidate

- 当前完整独立approved及fresh verification pass后，一次接受提交a4195c310a3b11eb5e8c1798cc0680ccdec35a44，父aa5b6636；23完整分类路径、全暂存空白检查exit0。提交后任务分支feature/gm-web-admin且clean，source main仍5fbe2dad且clean。
- 在尚未写游标metadata的干净接受HEAD运行标准package exit0，release.commit明确a4195c31。Windows候选53211031字节，SHA256 2f0de0350a5056b6ad8fd30beac17061f7bb5ade8e618bfed5332260237cfdd6；3资源/CRC/校验文件/默认3关闭/无部署数据、全部8文档链接及7源文档字节核对通过，HTTP3资源/未初始化/私有401，子进程正常结束。artifacts/gm-tests/spec006/accepted-package.log及accepted-package-proof.json。
- sealed result保持不变；child verification/completed，root消费child-sealed进入child_disposition，再以接受提交/clean/parent事实执行child-accepted，清除active并推进baseline a4195c31。

## Root review rework acceptance

- 原完整根审查唯一F-001邮件未知失权恢复已局部修正。当前独立重审approved、56前端/11工具freshpass及新资源原生候选pass后，一次接受f63b7c67473f2f4d8f7593f704c9e15d4f1c5e38，父a4195c31；10完整分类路径、暂存空白检查exit0，接受后任务clean、source main5fbe2dadclean。六childsealed结果和接受链未改写。
- 在游标metadata尚未写回的clean f63b7c67运行standard package exit0，release.commit精确f63b7c67；资源已经包含当前F-001修复，HTTP3资产/未初始化/401及ownprocess cleanup通过。日志artifacts/gm-tests/root/accepted-package.log；最终archive/文档校验由根verification记录。
- root workspace controller以approved+freshpass+完整diff接受root审查rework，推进accepted baseline f63b7c67；Root Review current目标与其2产品字节一致，约束恢复aligned，才能review-approved进入根verification。没有新child、没有重开sealedSpec或root terminal。
