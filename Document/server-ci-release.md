# 服务端 CI 与版本发布

## 约定与触发

适用仓库：DNF-JP-86-Server。客户端补丁和启动器由各自仓库管理。

- 开发使用任务分支，经 PR 合并到 `main`；用户明确指定直接修改主干并推送的任务按其授权执行。
- 创建 PR 前先按 [创建 PR 前同步主干](#创建-pr-前同步主干) 拉取主干，有更新则变基并处理冲突。
- 所有 PR 都必须通过下面的 [Codex 审核闭环](#codex-审核闭环) 及当前提交 CI 后才合并，合并后按 [合并与清理](#合并与清理) 收尾。新功能另须先完成 [新功能开发流程](FeatureDevelopment.md) 中的用户测试。
- PR 面向 main 时运行 CI；合并产生 main push 后再次运行 CI。
- 当前版本的唯一声明位于 `Server/Directory.Build.props` 的 `VersionPrefix`，修改权限见下面的版本规则。
- 日常构建及 CI 候选包使用源码版本，程序集信息保留提交标识。修改版本本身不打 tag、不创建 Release；只有用户明确要求发布时才走下面的 tag 流程。
- PR、main push 和手动 Server CI 只在 Linux 上构建并运行显式标记 `Category=Unit` 的服务端单元测试、GM 前端单元测试及单元报告判据，不生成候选包。完整集成与跨平台测试只在 Release 执行。
- `vMAJOR.MINOR.PATCH` 或 `vMAJOR.MINOR.PATCH-rc.1` tag 推送后触发 Release。
- Release 必须先通过 tag 校验、两个平台的测试、自包含发布与启动冒烟测试，再创建 GitHub Release。预发行 tag 自动标记为 prerelease。
- tag 必须指向 main 历史上的提交。校验使用完整 Git 历史和 `merge-base --is-ancestor`，不要求恰好等于最新 main，避免并行合并造成误拒。
- 当前私人仓库没有强制分支/tag 保护，开发和 tag 管理依赖约定；workflow 校验无法阻止有人改写 workflow 或直接推主干。

## 版本规则

- 版本采用 `MAJOR.MINOR.PATCH`，唯一声明为 `VersionPrefix`。
- 版本号只由用户明确授权后在 `main` 上修改，修改的值以用户指定为准。
- 新功能、修复、重构、文档、测试与 CI 等任何开发分支和 PR 都不修改版本号。PR 中出现 `VersionPrefix` 变动即属越界，须撤回。
- 版本号不由 Actions 自动提交；修改版本号不自动授权 tag 或 Release。

## 创建 PR 前同步主干

创建 PR 前先拉取主干，确认任务分支基于最新 `main`：

1. `git fetch origin`。若 `git merge-base --is-ancestor origin/main HEAD` 成立，说明主干没有新提交，可以直接创建 PR。
2. 主干有更新时，先把任务分支变基到最新主干（`git rebase origin/main`），逐个提交解决冲突。不要把主干合并进任务分支。
3. 变基后重新构建，并跑与改动及冲突范围相称的测试。冲突解决若涉及行为，按原流程重新验证；新功能还须请用户复测受影响步骤。
4. 分支此前已推送时，用 `git push --force-with-lease` 更新远端，不用 `--force`。
5. 变基会改写本分支的提交哈希。任务记录与 PR 说明里引用的提交以变基后的为准。

确认分支已基于最新主干后，再创建 PR。

## Codex 审核闭环

仓库由 `chatgpt-codex-connector[bot]` 审核 PR，打开 PR 或草稿转为待审时自动触发。适用于所有 PR：

1. 推送并创建 PR 后，等待 Codex 对 PR **当前提交**给出审核回执。
2. 有意见时逐条处理：修复并推送，或在该条意见下回复不修改的理由。每条意见二者择一即为处理完毕。
   涉及行为的修复重新完成相应自动验证；新功能还须让用户复测受影响步骤。
3. 全部处理完毕后，在 PR 留言纯文本 `@codex review` 请求复审。
   不要选输入框自动弹出的 `Codex [agent]` 候选：它会召来 GitHub Copilot 的合作代理，账号没有 Copilot 订阅时只会报错，不产生审核。
4. 复审再有意见时回到第 2 步。意见与架构根本冲突或处理存在争议时，按根 `AGENTS.md` 的审核规则交人工裁定。

**通过**指：最新回执对应 PR 当前提交，结论为未发现重要问题（或 Codex 只以 👍 回应），且没有未处理的意见。
PR 摘要评论中的 `codex-review-state` 记录了审核的 `commit_sha` 和 `has_unresolved_findings`，可用于核对。
审核仍在运行、回执针对旧提交、没有回执、只有 Copilot 代理的报错，都不算通过。
任何新提交都使旧回执失效，须对新提交重新取得通过。

## 合并与清理

Codex 审核通过且当前提交的必需 CI 通过后，使用 merge commit 合并，并核实合并提交。
每次合并后都必须清理：

- 删除已合并的远端任务分支和本地任务分支；
- 删除该任务的 worktree；
- 主检出目录切回 `main` 并同步，而不是删除；
- 更新关联 issue，只关闭已完成的范围。

正在运行的服务、数据库、其他任务的工作区与有价值的取证资料保留。

## 发布操作

用户明确要求“分支收尾”即授权以下完整流程，无需逐步重复询问：
更新文档和验收记录 → 同步主干（有更新则变基并处理冲突）→ 推送并创建 PR → 完成 Codex 审核闭环 → CI 通过 → 合并 → 清理。新功能须先完成用户测试。
收尾不等于修改版本号、打 tag 或发布 Release。

确认目标 main 提交的 CI 已通过，更新本地主干；从主干创建带注释的版本 tag，再单独推送该 tag。例子中的版本必须替换为本次真实版本：

```text
git switch main
git pull --ff-only
git tag -a v1.2.3 -m "Release 1.2.3"
git push origin v1.2.3
```

不移动、不覆盖已经发布的 tag。失败时先看 Actions；测试/打包失败不会创建 Release。资产上传或发布失败可能留下 draft，核对后删除这个 draft 再重跑原工作流；不要删除已公开使用的发布版本来覆盖产物。

Actions 中手动运行 **Server CI** 与 PR/main 使用相同的 Linux 单元测试门，只上传单元测试报告。tag 触发的 Release 才运行 Windows/Linux 完整测试、发布包构建和启动冒烟，并上传候选包。产物和测试报告保留 7 天；正式 Release 附件长期保留，除非人工删除。

## 发布包与运行

附件为 Windows `.zip`、Linux `.tar.gz`，各有 `.sha256` 校验文件。压缩包带单一顶层目录，包含 .NET 10 自包含运行时、服务端程序、默认配置、频道资源、经验配置、GM 页面资源与模块说明/验收表，及 `release.json`（版本、提交、SDK、目标平台和GM资源SHA256）。无需另装 .NET 或 Node；Linux 仍需要系统的 glibc、ICU、OpenSSL 等原生运行库。CI 平台为 Ubuntu 24.04 x64 / Windows Server 2022 x64；具体版本的双平台测试及发布包检查以该版本的 Server Release 验证结果为准，本机浏览器验证不替代 Linux CI。

运行入口：Windows `Dfo.Server.Host.exe`；Linux `./Dfo.Server.Host`。Linux tar 包保留可执行权限。

Host 内组合 HttpApi、GmWeb 与 GameServer，仍为一份发布包和一个进程；组件边界、启动顺序和 HTTP 单开行为见 [host-component-composition.md](host-component-composition.md)。原 LauncherAuth 配置键与 HTTP 路径保持兼容。

默认 `Networking:Enabled`、`LauncherAuth:Enabled` 与 `GmWeb:Enabled` 均为 false。实际部署需配置地址、端口、数据库与 PVF，再开启所需服务。GM默认监听本机18081，管理员由部署秘密首次初始化，见 [gm-web-admin.md](gm-web-admin.md)。数据资源不随包发布：

- 使用合法持有的 `Script.pvf`，通过环境变量 `PVF_ARCHIVE_PATH` 指定，或放入 `Data/Pvf/Script.pvf`。
- 默认数据库为程序目录下 `Data/dfo-server.db`。可以用环境变量 `Database__Path` 指定外部数据位置。
- 环境变量配置键使用双下划线，例如 `Networking__Enabled=true`。
- 升级时保留现有数据库、PVF 和部署配置。新版本服务端只打开当前结构版本的数据库，升级前需停服并运行 `Tool/DbMigrate`（会先备份），见 [数据库结构版本与迁移](DatabaseSchemaMigration.md)。本工作流只产出附件，不自动部署，不复制开发机数据库，不修改正在运行的服务。

启动冒烟在发布副本、独立临时数据库与本机临时端口启用GM，关闭游戏/Launcher，清除继承的管理员初始化配置。验证GM入口及全部资源的HTTP内容/类型、未初始化状态和私有API401，再结束自己的进程。数据库和日志均留在临时目录，不进入压缩包；这不代表游戏客户端验收。

## 从源码构建 GM 与候选包

构建机使用 Node 22.19.0/npm、固定 .NET SDK 和 Python；PR CI 按 lockfile 执行 `npm ci` 和前端单元测试，并构建服务端、运行显式 Unit 分类。Release 另外执行 TypeScript/Vite 构建、完整服务端测试及发布包验证。所有步骤正常失败即停止，不忽略错误。

```powershell
npm.cmd --prefix Web/gm-admin ci
npm.cmd --prefix Web/gm-admin test
npm.cmd --prefix Web/gm-admin run build
dotnet build Server/86JP.Server.slnx -c Release
```

Windows本地候选（不创建tag或外部Release）：

```powershell
$env:RELEASE_RID = 'win-x64'
python Tool/Release/release.py package
```

Linux原生构建使用 `RELEASE_RID=linux-x64`。package入口每次重新 `npm ci` 和build，不依赖遗留dist；前端失败、HTML引用缺资源、发布资源与新dist不同、默认开关错误或环境数据进入包均明确失败。候选生成在 `artifacts/release`。开发者单独运行dotnet publish时也应先构建前端；标准交付入口以package为准。

最终人工验收按 [gm-acceptance-list.md](gm-acceptance-list.md) 顺序执行，后台浏览器检查与A21真实邮件/余额展示分开记录。

## 测试范围与维护

PR 使用正向筛选 `Category=Unit`。只有经源代码依赖检查确认的纯规则、协议字节编解码及内存替身机制测试才标记 Unit；真实 SQLite、socket、完整 Host、外部 PVF 及混合类留在 Release 全量测试中。新增单元测试须显式添加该 Trait，不用名称规则或 `Category!=Integration` 猜测。报告要求整个筛选至少执行一项测试，零匹配会失败；未纳入 PR 的集成测试不算作已通过或条件跳过。

Release 云端不分发 PVF，已有内容依赖测试及新增 PvfFact/PvfTheory 会明确显示跳过；其余业务、协议、持久化、网络和 Host 测试完整执行。TRX 和 Actions Summary 都保留通过、失败、跳过数量，不能把绿灯解读为全部内容或客户端已验证。

GM 业务测试固定使用显式内容夹具，事务回滚、幂等、附件配置与领取判据不依赖开发机档案。真实 PVF 的物品目录、装备领取和商城消费竞争另有内容集成测试；有档案时必须执行，缺档案时明确跳过。邮件浏览器夹具仍使用真实 PVF。

本机完整内容验证：设置 `PVF_ARCHIVE_PATH` 后运行 `dotnet test Server/86JP.Server.slnx -c Release`。不得用 continue-on-error 或宽泛过滤来掩盖失败。

CI 使用 .NET 10 最新稳定维护版（setup-dotnet 10.0.x，符合 global.json），实际 SDK 写入发布包清单。Actions 依赖固定提交 SHA；更新 SDK/Action 时仍应经过 PR 单元门，发布前由 Release 双平台验证。

PR Linux 单元任务预算 15 分钟，Release Windows 总任务预算 90 分钟、Linux 30 分钟，具体以
`validate.yml` 为准。完整测试和发布共用任务预算，测试结果保存成功不代表发布冒烟已完成。
单项测试保留 3 分钟无进展挂起检测，不过滤失败或跳过发布步骤。合并等待当前提交的 Linux 单元 CI
成功；Release 必须等待两个平台含发布冒烟的任务全部成功后才能发布。

工作流只读权限为默认值，只有最后发布 job 获得 contents:write。未配置额外 PAT、PVF 下载凭证或生产部署密钥。

## 传输兼容与验证边界

TouchSocket 的监听启动、快速重连及有序关闭存在版本相关边界；实现约束和升级验证要求统一见
[玩法执行与传输兼容](GameplayExecution.md#物理关闭与框架兼容)。
启动冒烟不替代实际客户端对 Linux 部署的验收。

Windows checkout 通过进程级 Git 配置启用 `core.longpaths`，以保留较深的验收资料路径。
该设置只作用于 checkout，不过滤检出内容或测试。
