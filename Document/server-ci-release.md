# 服务端 CI 与版本发布

## 约定与触发

适用仓库：DNF-JP-86-Server。客户端补丁和启动器由各自仓库管理。

- 开发使用功能分支，经 PR 合并到 `main`，不直接向主干推送开发提交。
- PR 面向 main 时运行 CI；合并产生 main push 后再次运行 CI。
- Windows x64 / Linux x64 分别还原、Release 构建、运行测试、生成候选包并启动验证；任一失败均标红。CI 不创建 GitHub Release。
- `vMAJOR.MINOR.PATCH` 或 `vMAJOR.MINOR.PATCH-rc.1` tag 推送后触发 Release。
- Release 必须先通过 tag 校验、两个平台的测试、自包含发布与启动冒烟测试，再创建 GitHub Release。预发行 tag 自动标记为 prerelease。
- tag 必须指向 main 历史上的提交。校验使用完整 Git 历史和 `merge-base --is-ancestor`，不要求恰好等于最新 main，避免并行合并造成误拒。
- 当前私人仓库没有强制分支/tag 保护，开发和 tag 管理依赖约定；workflow 校验无法阻止有人改写 workflow 或直接推主干。

## 发布操作

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

附件为 Windows `.zip`、Linux `.tar.gz`，各有 `.sha256` 校验文件。压缩包带单一顶层目录，包含 .NET 10 自包含运行时、服务端程序、默认配置、频道资源、经验配置、本文档和 `release.json`（版本、提交、SDK、目标平台）。无需另装 .NET；Linux 仍需要系统的 glibc、ICU、OpenSSL 等原生运行库。本流程的 Linux 实测基线为 Ubuntu 24.04 x64，Windows 为 Windows Server 2022 x64，不声明 Alpine/musl 或旧发行版兼容。

运行入口：Windows `Dfo.Server.Host.exe`；Linux `./Dfo.Server.Host`。Linux tar 包保留可执行权限。

Host 内组合 HttpApi 与 GameServer，仍为一份发布包和一个进程；组件边界、启动顺序和 HTTP 单开行为见 [host-component-composition.md](host-component-composition.md)。原 LauncherAuth 配置键与 HTTP 路径保持兼容。

默认 `Networking:Enabled` 与 `LauncherAuth:Enabled` 均为 false。实际部署需配置地址、端口、数据库与 PVF，再开启所需服务。数据资源不随包发布：

- 使用合法持有的 `Script.pvf`，通过环境变量 `PVF_ARCHIVE_PATH` 指定，或放入 `Data/Pvf/Script.pvf`。
- 默认数据库为程序目录下 `Data/dfo-server.db`。可以用环境变量 `Database__Path` 指定外部数据位置。
- 环境变量配置键使用双下划线，例如 `Networking__Enabled=true`。
- 升级时保留现有数据库、PVF 和部署配置；先备份数据库。本工作流只产出附件，不自动部署，不复制开发机数据库，不修改正在运行的服务。

启动冒烟测试只确认发布后的本机可执行文件能在独立临时目录启动、加载依赖和默认资源；网络关闭，不是游戏实机验收。

## 测试范围与维护

云端不分发 PVF，已有内容依赖测试及新增 PvfFact/PvfTheory 会明确显示跳过。普通业务、协议编解码、持久化、网络和 Host 测试继续执行。TRX 和 Actions Summary 都保留通过、失败、跳过数量，不能把绿灯解读为全部内容或客户端已验证。

本机完整内容验证：设置 `PVF_ARCHIVE_PATH` 后运行 `dotnet test Server/86JP.Server.slnx -c Release`。不得用 continue-on-error 或宽泛过滤来掩盖失败。

CI 使用 .NET 10 最新稳定维护版（setup-dotnet 10.0.x，符合 global.json），实际 SDK 写入发布包清单。Actions 依赖固定提交 SHA；更新 SDK/Action 时仍应经过 PR 和两个平台验证。

工作流只读权限为默认值，只有最后发布 job 获得 contents:write。未配置额外 PAT、PVF 下载凭证或生产部署密钥。

## Linux 首次接入的传输兼容性

两核 Linux 复现了 TouchSocket 4.3.1 快速重连时的接收器复用竞态：旧 socket 的异步接收尚未结束，TcpCore 已回池，下一条连接复用其 SocketAsyncEventArgs 后立即断开。服务端现在在 OnTcpClosed 中等待 transport 接收/发送任务完成，然后才执行连接观察者清理和框架回池；不在该回调中等待 session 自身关闭，避免自等待。登录拒绝/重试集成测试加入连续 10 轮快速重连。

这次适配不改变 Game/Channel 帧格式和业务应答。启动冒烟之外仍需实际客户端验收 Linux 部署；本次未迁移正在运行的 Windows 服务。
