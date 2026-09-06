# 86JP

DNF 86JP 版本的服务端重实现，外加为它服务的客户端补丁与逆向资料。

**要动代码，先读 [`AGENTS.md`](AGENTS.md)**——那是本仓的架构图与硬性约定。本页只说明**东西都在哪**。

## 目录

| 目录 | 是什么 |
| --- | --- |
| [`Server/`](Server/) | 服务端。`86JP.Server.slnx`，.NET 10，源码在 `src/`、判据在 `tests/` |
| [`protocol/`](protocol/README.md) | **协议资料库**。客户端协议的结论**及其证据**，四层组织，规矩见它自己的 README |
| [`Docs/`](Docs/) | 服务端设计文档、领域裁定、客户端逆向记录。见下面「Docs 里有四类东西」 |
| [`ClientHack/`](ClientHack/README.md) | 客户端补丁，独立的 Windows x86 纯 C 工程，与服务端解耦 |
| [`Tool/`](Tool/) | 独立工具：`PvfProbe`（探档案）、`PvfProxy`、`WireSmoke`（打线） |

## Server

分层由项目引用强制，不是靠自觉：

```text
Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host
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
2. **客户端逆向** —— `a21-units/`（41 份）、`a21-protocol/`、`a21-vmp1/`，加 `client-*`、
   `themida-*`、`clean-exe-*` 等。**这是对一份固定二进制的测量，不会过期**，但它是
   `ClientHack` 那条线的产物，不描述服务端。
3. **参照服务端 `ServerS4A21` 的资料** —— 文件开头有醒目的免责块。
   **它们描述的不是本仓**，当业务语义的参照读，**不要当本仓事实去取证**。
4. **协议失谐记录** —— `ProtocolScaffoldDissonances.md`、`InventoryCoordinateDissonances.md`：
   只记不修，等实机样本。

**事实该写在哪、什么值得写、过期的怎么办**，一处写清，见
[`AGENTS.md` 的「记什么，不记什么」](AGENTS.md#记什么不记什么)。
