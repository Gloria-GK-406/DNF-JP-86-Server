# 服务端 CI 与版本发布

## 约定与触发

适用仓库：DNF-JP-86-Server。客户端补丁和启动器由各自仓库管理。

- 开发使用功能分支，经 PR 合并到 `main`，不直接向主干推送开发提交。
- PR 面向 main 时运行 CI；合并产生 main push 后再次运行 CI。
- 当前版本为 `0.2.8`，唯一声明位于 `Server/Directory.Build.props` 的 `VersionPrefix`。每个 PR 默认将目标 main 的 PATCH 加一，同一 PR 追加提交不再加。用户明确指定版本时以用户要求为准；并行 PR 在合并前重新核对 main。该递增由开发流程执行，不由 Actions 额外向主干提交。
- 日常构建及 CI 候选包使用源码版本，程序集信息保留提交标识。版本递增本身不打 tag、不创建 Release；只有用户明确要求发布时才走下面的 tag 流程。
- Windows x64 / Linux x64 分别还原、Release 构建、运行测试、生成候选包并启动验证；任一失败均标红。CI 不创建 GitHub Release。
- `vMAJOR.MINOR.PATCH` 或 `vMAJOR.MINOR.PATCH-rc.1` tag 推送后触发 Release。
- Release 必须先通过 tag 校验、两个平台的测试、自包含发布与启动冒烟测试，再创建 GitHub Release。预发行 tag 自动标记为 prerelease。
- tag 必须指向 main 历史上的提交。校验使用完整 Git 历史和 `merge-base --is-ancestor`，不要求恰好等于最新 main，避免并行合并造成误拒。
- 当前私人仓库没有强制分支/tag 保护，开发和 tag 管理依赖约定；workflow 校验无法阻止有人改写 workflow 或直接推主干。

## 发布操作

日常分支收尾采用固定顺序：先更新文档和验收记录，再推送并创建 PR，等待 CI 通过后合并，清理任务分支及可清理的工作区，最后更新关联 issue。主检出目录切回 main 而非删除；正在运行的服务、数据库、其他任务工作区与有价值的取证资料保留。收尾不等于打 tag 或发布 Release。

确认目标 main 提交的 CI 已通过，更新本地主干；从主干创建带注释的版本 tag，再单独推送该 tag。例子中的版本必须替换为本次真实版本：

```text
git switch main
git pull --ff-only
git tag -a v1.2.3 -m "Release 1.2.3"
git push origin v1.2.3
```

不移动、不覆盖已经发布的 tag。失败时先看 Actions；测试/打包失败不会创建 Release。资产上传或发布失败可能留下 draft，核对后删除这个 draft 再重跑原工作流；不要删除已公开使用的发布版本来覆盖产物。

Actions 中手动运行 **Server CI**，可验证两个平台的发布包，不创建 tag 或 Release。常规 PR/main CI 只上传测试报告，手动 CI 和 tag 工作流另外上传候选包。产物和测试报告保留 7 天；正式 Release 附件长期保留，除非人工删除。

## 发布包与运行

附件为 Windows `.zip`、Linux `.tar.gz`，各有 `.sha256` 校验文件。压缩包带单一顶层目录，包含 .NET 10 自包含运行时、服务端程序、默认配置、频道资源、经验配置、GM 页面资源与模块说明/验收表，及 `release.json`（版本、提交、SDK、目标平台和GM资源SHA256）。无需另装 .NET 或 Node；Linux 仍需要系统的 glibc、ICU、OpenSSL 等原生运行库。CI 平台为 Ubuntu 24.04 x64 / Windows Server 2022 x64；具体版本的双平台测试及发布包检查以该提交的 Server CI 结果为准，本机浏览器验证不替代 Linux CI。

运行入口：Windows `Dfo.Server.Host.exe`；Linux `./Dfo.Server.Host`。Linux tar 包保留可执行权限。

Host 内组合 HttpApi、GmWeb 与 GameServer，仍为一份发布包和一个进程；组件边界、启动顺序和 HTTP 单开行为见 [host-component-composition.md](host-component-composition.md)。原 LauncherAuth 配置键与 HTTP 路径保持兼容。

默认 `Networking:Enabled`、`LauncherAuth:Enabled` 与 `GmWeb:Enabled` 均为 false。实际部署需配置地址、端口、数据库与 PVF，再开启所需服务。GM默认监听本机18081，管理员由部署秘密首次初始化，见 [gm-web-admin.md](gm-web-admin.md)。数据资源不随包发布：

- 使用合法持有的 `Script.pvf`，通过环境变量 `PVF_ARCHIVE_PATH` 指定，或放入 `Data/Pvf/Script.pvf`。
- 默认数据库为程序目录下 `Data/dfo-server.db`。可以用环境变量 `Database__Path` 指定外部数据位置。
- 环境变量配置键使用双下划线，例如 `Networking__Enabled=true`。
- 升级时保留现有数据库、PVF 和部署配置；先备份数据库。本工作流只产出附件，不自动部署，不复制开发机数据库，不修改正在运行的服务。

启动冒烟在发布副本、独立临时数据库与本机临时端口启用GM，关闭游戏/Launcher，清除继承的管理员初始化配置。验证GM入口及全部资源的HTTP内容/类型、未初始化状态和私有API401，再结束自己的进程。数据库和日志均留在临时目录，不进入压缩包；这不代表游戏客户端验收。

## 从源码构建 GM 与候选包

构建机使用 Node 22.19.0/npm、固定 .NET SDK 和 Python；标准CI按lockfile执行 `npm ci`、前端测试、TypeScript/Vite构建，然后运行原服务端构建/测试。所有步骤正常失败即停止，不忽略错误。

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

云端不分发 PVF，已有内容依赖测试及新增 PvfFact/PvfTheory 会明确显示跳过。普通业务、协议编解码、持久化、网络和 Host 测试继续执行。TRX 和 Actions Summary 都保留通过、失败、跳过数量，不能把绿灯解读为全部内容或客户端已验证。

GM 业务测试固定使用显式内容夹具，事务回滚、幂等、附件配置与领取判据不依赖开发机档案。真实 PVF 的物品目录、装备领取和商城消费竞争另有内容集成测试；有档案时必须执行，缺档案时明确跳过。邮件浏览器夹具仍使用真实 PVF。

本机完整内容验证：设置 `PVF_ARCHIVE_PATH` 后运行 `dotnet test Server/86JP.Server.slnx -c Release`。不得用 continue-on-error 或宽泛过滤来掩盖失败。

CI 使用 .NET 10 最新稳定维护版（setup-dotnet 10.0.x，符合 global.json），实际 SDK 写入发布包清单。Actions 依赖固定提交 SHA；更新 SDK/Action 时仍应经过 PR 和两个平台验证。

Windows总任务预算90分钟，Linux仍30分钟（以当前 `validate.yml` 为准）。0.2.2第四轮Windows构建、前端143项与服务端八项目5,370通过/0失败/590条件跳过，但因整任务30分钟预算在保存结果后进入发布步骤时被取消，发布冒烟未完成；这不是完整CI通过。该轮Persistence测试耗时5分10秒，慢于上一轮2分33秒，需给完整串行验证和发布留出预算。单项测试仍保留3分钟无进展挂起检测，不过滤失败或跳过发布步骤。合并必须等新提交的两个平台包含发布冒烟在内的任务全部成功。

工作流只读权限为默认值，只有最后发布 job 获得 contents:write。未配置额外 PAT、PVF 下载凭证或生产部署密钥。

## Linux 首次接入的传输兼容性

2026-10-07 收尾回归还定位到独立的监听启动竞态：TouchSocket 4.3.1 的 `StartAsync` 先调用 `AddListen`，随后设置内部 Running 状态；`OnAccepted` 在状态尚未 Running 时退出并且不重新挂起 Accept。启动时立即探测端口可能使整个监听停止续接。第三轮 CI 已证明 Host 启动不到一秒，超时发生在首帧回复，增加冷启动等待不能修复该问题。隔离 WSL/.NET 10.0.12 两核复现原流程20次，5次无连接回调；依据实际4.3.1程序集检查，服务端适配改为先无监听启动、再通过公开 `AddListen` 绑定原始选项。配置在成功或失败后恢复，失败时撤销已绑定监听，支持停止后重新启动。

回归同时检查绑定时状态、重复启动、两种监听配置、失败清理和重试；Networking90项通过，隔离Linux原探测/断开/重新接入流程连续40次均收到正确首帧。这里是服务端兼容性验证，不是客户端协议或生产Linux部署验收；临时诊断日志没有进入生产代码。

两核 Linux 复现了 TouchSocket 4.3.1 快速重连时的接收器复用竞态：旧 socket 的异步接收尚未结束，TcpCore 已回池，下一条连接复用其 SocketAsyncEventArgs 后立即断开。服务端现在在 OnTcpClosed 中等待 transport 接收/发送任务完成，然后才执行连接观察者清理和框架回池；不在该回调中等待 session 自身关闭，避免自等待。登录拒绝/重试集成测试加入连续 10 轮快速重连。

这次适配不改变 Game/Channel 帧格式和业务应答。启动冒烟之外仍需实际客户端验收 Linux 部署；本次未迁移正在运行的 Windows 服务。

Windows checkout 通过进程级 Git 配置启用 `core.longpaths`，以保留层级较深的已封存验收记录。该设置只作用于 checkout，不过滤检出内容或测试。
