# 86JP

DNF 86JP 版本的服务端重实现及协议知识库。客户端补丁与登录器已拆为独立私有仓库。

**要动代码，先读 [`AGENTS.md`](AGENTS.md)**——那是本仓的架构图与硬性约定。本页只说明**东西都在哪**。

## 目录

| 目录 | 是什么 |
| --- | --- |
| [`Server/`](Server/) | 服务端。`86JP.Server.slnx`，.NET 10，源码在 `src/`、判据在 `tests/` |
| [`protocol/`](protocol/README.md) | **协议资料库**。客户端协议的结论**及其证据**，四层组织，规矩见它自己的 README |
| [`Docs/`](Docs/) | 服务端设计文档、领域裁定及协议参考记录。见下面「Docs 里有四类东西」 |
| [`Tool/`](Tool/) | 独立工具：`PvfProbe`（探档案）、`WireSmoke`（打线）、`CrapMeter`（量变更风险，见 [`Document/CrapMeasurement.md`](Document/CrapMeasurement.md)） |

## Server

项目引用形成多条边界，`Application` 不引用 `Infrastructure`、`Persistence` 或 `Networking`。
下图箭头表示“引用”，省略各项目对 `Data` / `Abstractions` 的共同引用：

```text
Application → Abstractions → Data
Infrastructure → Dfo.Pvf
Protocol → Application, Networking
HttpApi → Application
GmWeb → HttpApi, Application
GameServer → Protocol, Networking, Infrastructure
Host → GameServer, HttpApi, GmWeb, Application, Infrastructure, Persistence
```

| 项目 | 装什么 |
| --- | --- |
| `Dfo.Server.Data` | 纯数据形状，无行为 |
| `Dfo.Server.Abstractions` | 跨项目契约。**接口后缀就是它的类别**，对照表在 `AGENTS.md` |
| `Dfo.Server.Application` | 领域服务、领域 store、规则服务、命令处理器 |
| `Dfo.Server.Infrastructure` | 读归档（PVF）的内容源 |
| `Dfo.Server.Persistence` | SQLite 落库 |
| `Dfo.Server.Networking` | Socket 与 TouchSocket 边界 |
| `Dfo.Server.Protocol` | 按 `<Endpoint>/<Protocol>/` 切片的协议实现 |
| `Dfo.Server.HttpApi` | 启动器注册、认证与 HTTP 边界 |
| `Dfo.Server.GmWeb` | GM 管理 HTTP 功能 |
| `Dfo.Server.GameServer` | Game 服务组合与启动编排 |
| `Dfo.Server.Host` | Generic Host 组合根 |
| `Dfo.Server.Utilities` | 空项目占位 |
| `Dfo.Pvf` | 档案读取库 |

`Server/tests` 按生产项目一一对应，另有 `Integration/`。

### 构建与判据

```bash
dotnet build Server/86JP.Server.slnx
PVF_ARCHIVE_PATH=<Script.pvf 的路径> dotnet test Server/86JP.Server.slnx --nologo
```

**那两条 `A_missing_archive_*` 跳过是「档案确实加载了」的标志**，不是缺陷：它们各有一句
`Skip.If(ContentArchive.Available, …)`，只在**够不到档案**时才真跑。一跑下来它们没被跳过，
说明这次根本没读到档案，内容侧的判据全都没验到。

`TreatWarningsAsErrors` 是开的，CS9113（未读的主构造器参数）是错误：**加了接口却没接线，编译不过。**

## Docs 里有四类东西

`Docs/` 是历史堆积形成的，四类混在一个平面上。读之前先认清是哪一类：

1. **本仓的设计与裁定** —— `ItemWorldModel.md`、`Refactor/TableOwnershipRuling.md`、
   `CharacterDomainCut.md` 等。**都带日期与基准提交**，读的时候先看那一行。
2. **客户端逆向** —— 已迁往独立ClientHack仓库，服务端通过跨仓库链接引用证据。
3. **参照服务端 `ServerS4A21` 的资料** —— 文件开头有醒目的免责块。
   **它们描述的不是本仓**，当业务语义的参照读，**不要当本仓事实去取证**。
4. **协议失谐记录** —— `ProtocolScaffoldDissonances.md`、`InventoryCoordinateDissonances.md`：
   只记不修，等实机样本。

**事实该写在哪、什么值得写、过期的怎么办**，一处写清，见
[`AGENTS.md` 的「记什么，不记什么」](AGENTS.md#记什么不记什么)。

## 独立客户端项目

- [补丁与调试研究](https://github.com/Gloria-GK-406/DNF-JP-86-ClientHack)
- [Windows登录器](https://github.com/Gloria-GK-406/DNF-JP-86-Launcher)

三个仓库本地同级放在E:/Project/DNF。服务端构建不引用两个客户端仓库。拆分记录见Document/repository-split-20260926.md。
