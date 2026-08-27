---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: A21 进入游戏世界前的协议对齐

## Context

### Source Request

> 那就处理掉登录进游戏世界前的全部协议问题。你可以清理掉S4A21的服务进程。然后按你所需的来修改数据库结构等。不过不能修改服务架构。对了，最好用sdd来渐进重构。并且协议文档要同步更新，结束任务时候新的协议文档应该已经落地。

前置对话确定的事实：上游 `ServerS4A21` 仅作线索源不作规范；`DNFClient-A21-protocol`
是本任务要服务的客户端；`protocol/` 知识库与服务端实现当前对的是 A12。

### Current Situation

主干 `0d37e9a` 已切到 A21 线（`S4A12` 分支保留 A12 书签），但 `Server/` 下的协议实现
与 `protocol/` 知识库仍是 A12 结论。A21 客户端目前连本服务端会崩在
`NOTIFUNC_CHANNELINFO`。

可用证据与工具：

- `ServerS4A21/Server/DfoServer/bin/Debug/packet_log.txt`：一局跑通副本留下的
  **458 帧已知良好 A21 会话**，覆盖频道握手、登录、花名册、建号、选人与入场全程。
- `DNFClient-A21-protocol`：`0100_NetworkHook`（双向抓帧 + 四个读原语 + `ISocket::Write`）
  与 `0110_ServerRedirect` 均已移植到 A21；`[FrameCapture]` 已开，`[ReadTrace]`
  可按需开启以取得客户端**按调用 RVA 切分字段**的记录。
- 频道载荷的 AES-128-ECB + zlib 密钥由日期派生，本仓已有同一实现，可离线解密。

已在本任务开始前离线定死、待实现的线级结论（均有实测字节）：Game 帧 C→S 头
13→14 字节；频道列表 0x12 记录布局与本仓一致但首个 u16 应为 1；CMD LOGIN 响应
偏移 4 缺一字节且版本字节 18→20；NOTI 0x0001 CHANNELINFO 相差 8 字节（缺
extraAddressCount、两个标记字节应为 `'0'`、表大小应为 1271/1218、末尾缺 i32 0）。

`AGENTS.md` 与 `Docs/NetworkingFrameProtocol.md` 均把 Game 入站头记作 13 字节。

## Goal

A21 客户端能从启动一路走到进入游戏世界（角色站进城镇），沿途每一个协议的服务端
产出与该客户端实际接受的字节一致；`protocol/` 知识库、帧基线文档与 `AGENTS.md`
的相关描述同步反映 A21 结论，任务结束时新协议文档已经落地。

## Scope

### In

- Game 帧层入站头长度与字段（13→14）。
- Channel 端点四个切片（Connect、Info、Script、ScriptVersion）的 A21 线级布局。
- Game 初始连接通知 NOTI `00/0001` CHANNELINFO。
- Game CMD `01/0001` LOGIN 的入站解包与出站响应。
- 花名册链路：CMD `01/0008` GET_USERINFO 与 NOTI `00/0002` USERINFO（含 subtype 与
  逐角色条目布局）。
- 选人接受与入场状态同步序列：CMD `01/0004`、沿途必发的 NOTI 集合与其包体，
  含物品条目布局与入场状态各 subtype。
- 进城前沿途其余 C→S 报文的解包容错（例如变长的 `01/008F`）。
- 为满足上述结论所需的数据库结构调整。
- `protocol/` 记录与 `protocol/catalog.md` 状态、`Docs/NetworkingFrameProtocol.md`、
  `AGENTS.md` 中的帧与协议描述的同步更新。

### Out

- 进入游戏世界之后的玩法协议（副本、战斗、交易、公会、PVP、邮件、商城等）。
- 服务架构变更（见 Constraints）。
- `ClientHack/` 插件的代码改动；本任务只按需调整其 ini 配置。
- `ServerS4A21`、`S4A21ClientPatch`、`.worktrees/legacy-reference` 的任何修改。
- 客户端可执行文件与 DLL 的修改。

## Constraints

- **不得修改服务架构**：`AGENTS.md` 记载的分层与项目引用图、协议切片边界规则、
  handler 业务语义规则、命令总线与管线次序、领域事务与载体协议、注册表接线方式
  均保持不变。改动限于线级布局、payload codec、消息 record 字段与其取值来源。
- 开发阶段擦库不迁移：不得为既有数据编写回填、修复或迁移路径。
- 协议结论必须以实测字节为依据；推翻既有结论时保留“为什么被推翻”，遵循
  `protocol/README.md` 的证据等级与安全边界。
- `protocol/` 不得含凭据、令牌、聊天隐私或个人可识别信息；C→S `0x0001` LOGIN
  样本不得入库。
- 不得改动 `Tool/`。
- 测试须 `dotnet test Server/86JP.Server.slnx -m:1`；`PVF_ARCHIVE_PATH` 与
  `Pvf__ArchivePath` 指向 `D:/project/DNF/DNFClient-debug/Script.pvf`。
- C# 与文档文件 CRLF、无 BOM UTF-8；net10.0；`TreatWarningsAsErrors` 保持开启。
- 合并、推送、PR、分支或工作树清理需单独授权。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | A21 客户端连接本服务端后完成频道握手并列出频道 | 客户端到达频道/登录界面的实机记录，或本服务端产出与已知良好会话对应帧逐字节一致的比对 |
| SC-2 | 登录成功并进入选人界面，花名册按账号实际角色渲染 | 实机走到选人界面；花名册包体与实测字节的逐字段比对 |
| SC-3 | 选中角色后进入游戏世界且不崩溃 | 实机进城；`clienthack-faults.log` 无导致退出的记录 |
| SC-4 | 每一处改动的线级布局都以实测字节为依据，而非照抄上游或推测 | 每条结论指向抓包帧、`[ReadTrace]` 记录或客户端反汇编中的读序之一 |
| SC-5 | `protocol/` 记录与 catalog 状态、帧基线文档、`AGENTS.md` 的帧与协议描述已反映 A21 | 相应文件的最终内容；被推翻的旧结论保留了原因 |
| SC-6 | 解决方案构建零错误零警告，全量测试通过 | 最终基线上的构建与 `-m:1` 全量测试输出 |
| SC-7 | 服务架构未被改动 | 项目引用图、切片目录结构、`ProtocolModule` 接线方式、handler 四类允许行为与命令注册的执行方式/访问方式未变的比对 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 「那就处理掉登录进游戏世界前的全部协议问题。你可以清理掉S4A21的服务进程。然后按你所需的来修改数据库结构等。不过不能修改服务架构。对了，最好用sdd来渐进重构。并且协议文档要同步更新，结束任务时候新的协议文档应该已经落地。」 | 在本仓实现进入游戏世界前的 A21 协议对齐并同步协议文档；可终止 `ServerS4A21` 的服务进程；可按需修改数据库结构；可启动本服务端与 A21 客户端以取证；含 SDD 工作流制品、任务工作树与已接受结果的本地提交 |

## Requires Confirmation

- 合并到主干、推送、开 PR、清理分支或工作树。
- 任何越出「进入游戏世界前」范围的协议改动。

## Assumptions

- `packet_log.txt` 记录的那一局是 A21 客户端接受的行为基线；当它与本仓既有结论
  冲突时，以抓包字节为准。
- A21 客户端的 B1 exe 在当前 boot 内有效；跨重启需要先跑 `_rebuild/rebuild_a21_b1.ps1`
  才能启动。
- 两个服务端都监听 7001 与 10011，同一时刻只能起一个。
