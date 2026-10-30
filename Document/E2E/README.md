# E2E 场景收集与本地 CI

关联 [#212](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/212)。本目录维护持续更新的 [场景台账](场景台账.md)，场景定义仍引用 [2026-10-27 验收目录](../E2E-验收全测试集-20261027.md) 和 [协议覆盖对照](../E2E-协议覆盖对照-20261027.md)。台账采用 Markdown，便于 agent 修改、Git 审阅及 issue 直接链接。

## 范围和证据

首批 6 个顺序 case：AUTH-001、AUTH-002、ROLE-001、ROLE-004、QUEST-001、RECOVER-004。正常路径使用一个账号与角色连续运行；建角和选角只覆盖明确列出的子集，不代表职业矩阵、完整入场字节或所有恢复域已覆盖。

当前自动断言为 **CurrentImplementation**：HTTP 状态、频道脚本/路由、必要 Game 回包、角色/初始资产/任务数据库事实及重启后的网络可用性。`clientVerified=false`、`goldenBaseline=false` 固定写入结果；尚无本轮真实 A21 人工签收及精确报文基线。采集日志是模拟客户端与当前服务端的测试证据，不能冒充真实 A21 抓包。

主要依据：`Tool/WireSmoke/` 的 A21 帧与登录流程，`Server/tests/Dfo.Server.Host.Tests/EndToEndHostHarness.cs` 的真实 HTTP/Channel/Game 连接，`protocol/patterns/flow-login.md`、`protocol/patterns/world-entry.md`，以及各协议当前 codec。70 服务端和 ServerS4A21 不参与本框架的进程/CI 基础设施设计，也不作为通过判据。

## 执行与隔离

实现入口为 `Tool/E2E/run.py`，仅使用 Python 标准库与系统 OpenSSL；Host 是独立的正式 .NET 进程，不构造测试 DI，不替换生产规则、仓储、时钟、随机或地图。

1. 校验本地完整 PVF 的固定 SHA-256；缺失或不匹配直接失败。
2. 正式 Host 在新的运行目录创建当前结构数据库；准备阶段通过真实 HTTP 注册一个专用空账号。
3. 正常停止 Host、检查数据库完整性及账号/角色前置，再复制 `baseline.db` 为 `run.db`。基线密码只留在本轮内存，不写入 manifest。
4. 用同一 `run.db` 启动正式 Host，顺序执行所有 case；运行期只有正常网络请求修改业务数据，SQL 连接使用只读模式。
5. 某项失败则后续依赖项全部标 `blocked`，保存首个异常、报文及状态；不重试变绿、不自动刷新预期。
6. 正常停止自己启动的进程并复核 baseline 哈希。超时被迫结束进程也计失败。

频道保留 `UseScriptCatalog=true` 和随 Host 构建复制的完整生产脚本；不把脚本解析关掉来换取随机端口。脚本固定的 Game 端口由本套件独占，Channel 与 HTTP 选择空闲端口。端口冲突失败，不停止其他进程。当前入口限定 Linux，在常开专用 runner 上串行执行，避免干扰 Windows 玩家服务。

首批固定素材配方 `starter-v1`：一个正式注册的账号、初始无角色；ROLE-001 自己测试创建职业 0 的 `E2EProbe`，随后接任务 1016。所有前置通过生产接口准备；不复制玩家库、不在步骤间补写数据库。账号密码和运行时票据每轮重新签发，不能照抄录制值。后续功能需要的道具、额度和互斥角色应扩展准备配方并增加 fixture 版本。

## 本地运行

```bash
export PATH="$HOME/dnf-e2e/dotnet:$PATH"
export PVF_ARCHIVE_PATH="$HOME/dnf-e2e/assets/Script.pvf"
export PVF_SHA256="$(cut -d ' ' -f1 "$HOME/dnf-e2e/assets/Script.pvf.sha256")"
dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj -c Release
python3 -m unittest discover -s Tool/E2E -p 'test_*.py' -v
python3 Tool/E2E/run.py
```

`--out` 可指定新的结果目录，已有目录不会被覆盖。缺素材、环境失败或没有 case 时退出码非零，并生成失败报告。报告保护单测仅验证报告契约，不算作 E2E 业务 case。

## GitHub Actions 与 runner

[Server E2E (local PVF)](../../.github/workflows/e2e.yml) 独立于云端 Unit CI 和 Release。目标标签为 `self-hosted, Linux, X64, dnf-e2e`，只接受手动触发、main 和受控 `codex/e2e/**` 分支推送；路径筛选限制为服务端、测试工具和该 workflow。不接受 fork PR 或 `pull_request_target`。仓库及受控分支的写入权限即有权执行本地 runner 的代码。

`codex/e2e/**` 用于首轮接线及后续 E2E 基础设施分支自测；main 推送用于持续回归。全局 concurrency 禁止同时占用生产脚本端口，30 分钟任务预算，失败不继续执行业务步骤。

已配置主机：Ubuntu 24.04 x64，LAN `192.168.8.210`，Tailscale 主机 `lan-ubuntu-210`；runner 名称 `dnf-e2e-ubuntu-210`。路径和资产由部署拥有，repo 内不保存账号密码或注册令牌：

| 内容 | 路径 |
|---|---|
| runner | `~/dnf-e2e/runner` |
| 用户级 .NET SDK | `~/dnf-e2e/dotnet` |
| 固定 PVF 与哈希声明 | `~/dnf-e2e/assets/Script.pvf`、`Script.pvf.sha256` |
| CI 工作目录 | `~/dnf-e2e/runner/_work` |

PVF 固定快照为 73,085,159 字节，SHA-256 `01f1be685d46647961df65207a36304777db20ef5a3d03c68efaa92f8b0c5189`。来源为本地 `DNFClient-A21-protocol/Script.pvf` 的 2026-10-30 实际快照，与旧目录的候选快照不同；两者不能混用。更换素材时先复制、验哈希并验证配方，再更新私有 `.sha256` 文件；结果始终记录实际哈希。

runner 通过官方 `config.sh` 注册、`svc.sh install` / `svc.sh start` 安装 systemd 服务，非 root 运行且开机启动。检查：

```bash
cd ~/dnf-e2e/runner
sudo ./svc.sh status
```

重新部署按 [GitHub 添加自托管 runner](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners) 获取当前安装包和临时注册令牌，校验安装包后注册同一仓库并加 `dnf-e2e` 标签。现有 runner 在线时不要重复注册；无需把本机 GitHub PAT 复制到 Linux。

## 结果与维护

每轮 `artifacts/e2e/<runId>/` 保存 `results.json`、JUnit XML、Markdown 汇总、模拟客户端帧记录、Host 日志、逐 case 前后只读状态及 baseline/run 数据库。manifest 包含代码提交、SDK、Host DLL/频道脚本/PVF/baseline 哈希、fixture 配方、时间与部署端口。

CI 将脱敏后的报文、业务状态、日志和报告作为 artifact 保留 14 天，Summary 展示每项结果；PVF、SQLite、测试密码、HTTP 登录正文和 Game 登录票据不上传。数据库在 runner 工作区保留到下一次 checkout 清理；需要长期留存失败库时在清理前复制到私有证据目录。它不是正式基线。

台账只维护场景设计和各阶段就绪状态；每轮结果保存在独立 run，不覆盖前一轮失败。新增场景时先检查现有 caseId，复用场景并补参数/子集；新功能确无归属时新增唯一 ID 并关联 issue/PR。一个“自动通过”不得同时升级人工、抓包和正式基线状态。
