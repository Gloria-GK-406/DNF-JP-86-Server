---
kind: rks-review
verdict: passed
---

# Change Review: A21 登录应答与花名册条目（spec-002）

评审范围：工作树相对 `9bc75fa` 的全部改动（`git diff 9bc75fa` 共 20 个已跟踪文件，412 增 / 63 删，
外加两个未跟踪项：`attempts/spec-002-login-response-and-roster/` 与
`Server/tests/Dfo.Server.Protocol.Tests/Game/AccountRoster/AccountCharacterRosterCodecTests.cs`）。
`9bc75fa` 本身不在范围内，但它引入的归档件被追加的 `[H]` 节在范围内。

本次评审不采信执行者结论：所有线级断言都回到原始字节重算，所有守卫都用真改坏真跑验证。

## Blocking Findings

### A-1（A 级）花名册「外观条目 23 字节 / 尾部 36 字节」的切分被记成 `High`，而它只有上游转录支撑，且记录里从未说明这一点

- 位置：`protocol/game/server-to-client/NOTI-0x0002.md:471-473`（表格三行标 `High`）、
  `:488-490`（「结构有两重印证」）、`:492-511`（尾部一节）。
- 我重算的事实：`[H]` 节那 103 字节 body 里，**索引 44..102 之间的非零字节只有 44,45,46,47,48,49 与 94**
  （即外观条目的槽位、显示部件标识与长度字段首字节，以及尾部那个 `0xFF`）。索引 50..93 共 44 字节全为零。
  因此凡是满足「外观 + 尾部 = 59」的**任何**切分（23+36、27+32、19+40……）都产生完全相同的线上字节。
  单样本对这条切分**不含任何信息**，实机渲染同样不含（客户端读到的字节一模一样）。
- 于是文中那「两重印证」两条都不成立：
  - 「整段 59 字节减去 36 字节尾部恰为 23」以「尾部 36」为前提，而「尾部 36」的唯一来源是参考实现
    `ServerS4A21/Server/DfoServer/Network/Builders/Init/UserInfoType2RosterTailBuilder.cs:33-56`
    的 `WriteA21`（我逐字段数过：4+4+4+4+1+4+6+1+4+4 = 36）。这是同一条上游事实的两次复述，不是两条证据。
  - 「那个字面量 `4` 后面正好跟四个字节」在样本里**不可观察**——其后 44 字节全零。
  - 「本仓存储的外观 blob 就是 `u8 条目数 + N×23`，与线上同形」也不是独立证据：该 blob 是本仓 A12 时期的
    既有模型（`SelectCharacterPreparationService.DecodeAppearances`，步长 23），而本次那条角色的 blob
    是把 source A 的线上字节手抄进库的（见 A-2）。
- 与仓库规则冲突：`protocol/README.md`「证据等级」明写「一条结论的等级不可能高过支撑它的证据种类允许的上限
  ——例如只有上游转录支撑的结论最高只能是 `Hypothesis`」；`protocol/evidence.md`「效力顺序」把上游转录列为
  第 5 级、「不能回答任何事实」。根契约 SC-4 要求「以实测字节为依据，而非照抄上游或推测」——这一处**就是
  照抄上游**，而落进知识库的那份记录既没降级也没披露来源。
- 诚实性落差是本条的要害：`TaskContract.md` 的 Assumptions 与 `spec.md` 的 Assumptions **都如实写了**这条切分
  「由差值加上游结构双向印证得出，而非由多样本差分独立确定」；唯独会被后来者当作结论读的 `protocol/` 记录
  没写。这正是 `evidence.md`「比假证据更阴的一类：真实观察夹带未被支撑的推论」所指的形状。
- 要求：把 `:471-473` 三行降为 `Hypothesis`；删掉「两重印证」的循环表述；明写该切分的唯一支撑是参考实现
  `UserInfoType2RosterTailBuilder.WriteA21` 加本仓既有 blob 步长；写明判定它最便宜的证据（两条外观的角色，
  或零外观的角色）。`:471` 那行「实测 `1`，其后正好一条 23 字节条目」中的后半句需一并改。

### A-2（A 级）「让账号里存在可渲染角色」这一 In-Scope 交付物不在改动集里；SC-3/AC-004 的实机证据来自库外手工写入，不可复现，且本仓自己唯一能产出的花名册形状从未被验证

- 契约 `TaskContract.md` 的 Scope/In 第三条：「为使账号存在可渲染的角色所需的数据库改动（测试夹具或建号路径，
  二选一）」。
- 改动集里两者都没有：`git diff 9bc75fa --stat` 无 schema 改动、无建号路径改动、无夹具；
  `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:203` 的 `appearance_blob BLOB` 可空无默认，
  `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs:157-158` 的 INSERT 不写该列。
  → **经建号路径产生的角色，外观条目数恒为 0。**
- 那条被渲染出来的角色是手工种进运行库的。我查了
  `Server/src/Dfo.Server.Host/bin/Debug/net10.0/Data/dfo-server.db` 的 `characters` 表，只有一行：
  `(1, 1, '1122', 13, 0, 1, 0, 0, appearance_blob=010C8080CB06040000000000000000000000000000000000, 0)`。
  那 24 字节 blob 正是 source A 那条外观的线上字节（`01` 计数 + 23 字节条目）被抄进库。
- 两个后果：
  1. `[H]` 的「逐字节相同」证明的是**编码器布局**，不证明本仓的数据链路能产出该内容——外观字节是参考会话
     自己的字节被喂回来的。协议记录 `:524-531` 的「实测比对」表读起来像后者。
  2. 本仓真正会发出的形状（零外观、`18 + 26 + 36 = 80` 字节）**从未在客户端上出现过**，而它的正确性恰好
     整个压在 A-1 那条未定的切分上：切分若是 27/32，零外观 body 就该是 76 而不是 80。换句话说，
     下一个真实用户建完角色看到的那一帧，是本次唯一没有任何证据的一帧。
- 附带：手工种入的角色留在测试账号库里，与真正由服务端建出来的角色无法区分；后续一次数据回归会读成代码回归。
- 要求：把「让账号存在可渲染角色」落成改动集里的制品（建号路径写 `appearance_blob`，或一份可复跑的夹具/种子
  脚本），并在此基础上取一次零外观形状的实机结果；或者，若本成果决定不做，则把该 In-Scope 条目从契约里移除
  并说明理由，同时在协议记录里写明零外观形状未经实机验证。

## Non-Blocking Findings

### B-1（B 级）同一条线级事实的第二个编码器，尾部改动**完全没有守卫**

- `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/SupplementalSynchronizationCodec.cs:299-311`
  的 `WriteRosterTail` 与花名册切片那份一起从 32 改到 36 字节。
- 我真改坏真跑：把它改回 A12 的 `{0,0,0,3,0,0,4,0,0,0,0}`（11 字节），以及只去掉末尾 `writer.Write(0u)`（-4 字节），
  两次都是 **Protocol.Tests 221 全绿 / Networking.Tests 51 全绿 / Application.Tests 530 全绿**。
- 原因：`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SupplementalSynchronizationTests.cs:159-186`
  对该 body 只断言 `body[0] == 2` 与「包含 i32 1000」，既不断言长度也不断言尾部。
- 而 `Docs/ProtocolScaffoldDissonances.md` 第七节自己写着「少改一处就会在两个角色的账号上丢掉第二个角色，
  而单角色账号看不出来」——那正是这一处需要被守住的东西，却恰恰是没被守住的那一处。
- 建议：给该编码器补一条与花名册切片同形的长度闭合用例（`18 + 26 + 23n + 36`）。

### B-2（B 级）两个编码器在同一偏移仍写不同字节，「一并改」只对尾部成立

- 花名册切片写 `0x0B 0x01` + 两个 u32 零（`AccountCharacterRosterPayloadCodec.cs:31-34`）；入场路径写
  `HonorLevel(u32) + HonorExperience(u32) + HonorReserved(u16)`（`SupplementalSynchronizationCodec.cs:55-57`）。
  宽度同为 10 字节，内容不同。
- 参考会话在**入场序列里重发的那一帧**（`ServerS4A21/Server/DfoServer/bin/Debug/packet_log.txt:242`，紧跟
  CMD `0x0239` CloneTitle 之后）与花名册那帧（同文件 `:106`）**逐字节相同**，含 `0B 01`。我脚本比过：0 处差异。
  所以入场路径产出的 body 与已知良好会话在这两个字节上仍不一致（除非该角色的 Progress1 恰为 267）。
- 入场序列属本成果 Scope/Out，且不谐已记进 `Docs/ProtocolScaffoldDissonances.md` 第七节，故不阻断。但
  `SupplementalSynchronizationCodec.cs:114-119` 的 remark 写「Both are corrected together because they are one
  wire fact」容易被读成整条条目已经统一；建议把范围限定到尾部。

### B-3（B 级）`[H]` 断言的对照物本身没有进归档，库内无法复核

- `protocol/patterns/data/traces/a21-world-entry-20260823.log:184-185` 断言「花名册与 source A 的对应帧
  **逐字节相同**」，但 `[A]`–`[G]` 各节都不含 source A 那一帧的字节。读者要复核，只能回到
  `ServerS4A21/Server/DfoServer/bin/Debug/packet_log.txt`——而档案头 `:12` 自己写着那份原件「下次运行即覆盖」。
  这正是 `evidence.md`「留文件，引路径……先归档再引用」要防的静默失效。
- 我这次仍能复核，只因该文件尚未被覆盖，且其 `SHA-256 = 76a57c4c…806ad2f5` 与档案头登记的一致（我算过，吻合）。
  这一点值得肯定，但不改变「引用要能在库内解析」的要求。
- 建议：把 source A 的 103 字节 body、54 字节 LOGIN body 与 18 字节空花名册 body（`packet_log.txt:20/36/106`）
  一并写进归档件。

### B-4（B 级）新增的「读 `appearance_blob` → 解码 → 进 `CharacterSummary`」这条链路无任何非空用例

- `Server/tests/Dfo.Server.Persistence.Tests/Characters/SqliteCharacterLifecycleTests.cs:149-266` 是唯一走真实
  SQLite 的 `ListByAccountAsync` 用例组，其角色全部经 `CreateAsync` 建出，而 `CreateAsync` 不写
  `appearance_blob` → 该列恒为 NULL → 只覆盖 `[]` 分支。
- 花名册 codec 用例（新文件）喂的是手搭的 `CharacterRosterAppearanceState`，不经数据库。两段合起来，
  `Persistence → Application → Data` 这条新链路的非空路径没有任何自动化覆盖。
- 与 A-2 同源：一旦补上夹具或建号路径，这条用例几乎是顺带的。

### B-5（B 级）EV-003 要求的实机截图没有可解析的引用

- `spec.md` 的 EV-003 要求「A21 客户端实机截图与该次会话的双向帧记录」。双向帧记录已归档为 `[H]`；截图无任何引用。
- `NOTI-0x0002.md:524-531` 的「实机结果」一行按 `evidence.md`「一次性探查 → 直接写进文档」的写法给了可复核现象
  （渲染出 `Lv.1 1122 魔枪士`、删除按钮由置灰转为可用），这条写法本身是合规的；但 attempt 目录下
  `verification/` 为空、无 `spec-result.md`，SDD 侧没有留下结果制品。

### C-1 ~ C-5（C 级）

- C-1 `protocol/patterns/data/traces/a21-world-entry-20260823.log:12` 仍写「两份原始日志都是易失的」，而 `[H]`
  已引入第三份（source C）。
- C-2 `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs:103`：`FindBySlotAsync` 给
  `AppearanceBlob` 传 `[]`，即使该行有 blob。与既有的 `CloneTitleItemId` 传 `0` 是同一惯例，但行类型上
  多了一个不真实的字段。
- C-3 `Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs:56`：花名册列举现在会因
  `DecodeAppearances` 抛 `InvalidDataException`（截断或有尾随字节）而整份失败，此前该路径根本不碰这个 blob。
  开发阶段擦库不迁移下可接受，但这是一条新增的失败面，值得在 remark 里点一句。
- C-4 `protocol/game/server-to-client/CMD-0x0001.md` 的「仍未知」表把那个零字节写成「是独立字段还是版本的高位」——
  它在 `body[4]`，与 `body[1..2]` 的版本值不相邻，「版本的高位」在字节序上讲不通；宜改为「是独立字段还是
  前一字段的扩展」。
- C-5 `AccountCharacterRosterPayloadCodec.cs`：`FirstByteAfterLevel` / `SecondByteAfterLevel` 两个常量定义
  （`:60`、`:62`）放在 `Encode` 方法之后，与文件内其余成员的顺序惯例略有出入。纯风格。

## 核对通过的部分（我实际验证过，不是阅读）

- **LOGIN 54 字节**：与 source A `packet_log.txt:20` 的 body 逐字节比对，**只差索引 21 一个字节**
  （`0x32` → `0x31`，即 `127.0.0.2` → `127.0.0.1`），与 `[H]` 的说法一致。编码器
  `GameLoginResponseEncoder.cs:50-60` 的 `45 + host` 与 24 字节零尾与之精确闭合。
- **花名册 103 字节**：`[H]` 的十六进制与 `packet_log.txt:106` 及 `:242` 的 body **零处差异**（脚本逐字节比过）。
  头部 18 字节与 `packet_log.txt:36` 的空花名册 body 相符；`18 + 26 + 23 + 36 = 103` 闭合。
- **`0x0B 0x01` 的两条迹象都属实**：我解析了 source A 全部 418 帧，该字节对在同一角色的 USERINFO 子类型
  `0x00`/`0x01`/`0x06` 帧里**一次都没有出现**（只在两帧子类型 `0x02` 的索引 33 出现）；参考实现
  `AccountCharacterListBodyBuilder.cs:94-95` 确实写的是字面量。
- **不确定性的表达与代码选择一致**：该不确定性在 `AccountCharacterRosterPayloadCodec.cs:47-59` 的 remark、
  `spec.md` 的 Assumptions、`NOTI-0x0002.md:513-522` 三处都写明，且写明了「判定它最便宜的证据」；代码用的是
  无语义常量名（`FirstByteAfterLevel` / `SecondByteAfterLevel`），符合 `protocol/README.md`「`Hypothesis` 不得
  直接变成生产领域语义……可以先按 offset 和原始宽度建模，并保留未知名称」。**把它写成常量这个选择本身与该
  不确定性一致**，此条无异议。
- **守卫真伪（真改坏真跑，共五次）**：

  | 改坏 | 结果 |
  |---|---|
  | 花名册尾部去掉最后 4 字节 | Protocol.Tests **5 条失败**（长度闭合三参数 + 双角色 + 整 body） |
  | `0x0B 0x01` → `0x00 0x00` | Protocol.Tests **1 条失败**（整 body 比对） |
  | 外观条目里写死的长度 4 → 8 | Protocol.Tests **1 条失败**（整 body 比对） |
  | LOGIN 去掉新增的那个零字节 | Protocol.Tests **3 条** + Networking 集成 **1 条**失败 |
  | 入场路径 `WriteRosterTail` 改回 A12 / 去掉 4 字节 | **全绿**（见 B-1） |

- **服务架构未被改动**：无 `.csproj` 改动；`ProtocolModule` 未改；无 handler 改动；命令注册、执行方式、
  访问方式与事务语义均未动；改动全部落在线级布局、payload codec、消息与行 record 字段上，正是根契约
  Constraints 明确许可的范围。
  关于 Application 里那处跨服务调用（`CharacterRosterService.cs:56` →
  `SelectCharacterPreparationService.DecodeAppearances`）：两端都在 `Dfo.Server.Application` 内，不触碰项目
  引用图、协议切片边界、handler 四类允许行为或命令总线次序；`DecodeAppearances` 本就是 `internal static`
  纯函数，本次**没有**放宽可见性（该文件无 diff）。**判为未越线**，但它是一处耦合味道（仓储实现依赖选人
  工作流服务的静态方法），已记进不谐清单，处置得当。
- **两个编码器各写一份这件事的处置**：记进 `Docs/ProtocolScaffoldDissonances.md` 第七节、不在协议对齐任务里
  合并，这个判断**恰当**——合并属结构调整，正落在「不得修改服务架构」的边上，另立项目单独评审是对的。
  但记录本身漏了 B-1（无守卫）与 B-2（还有别的字段不一致）两点。
- **行尾与 BOM**：`git ls-files --eol` 显示四个 `protocol/` 文件均 `attr/text eol=lf`、`w/lf`、`i/lf`；其余改动
  文件（含新增测试与 spec-002 制品）全 CRLF、无 BOM。`protocol/catalog.md` 的 BOM 在 `9bc75fa` 就已存在
  （`git show 9bc75fa:protocol/catalog.md | head -c 3` = `EF BB BF`），非本次引入。
- **凭据**：`protocol/` 全树无账号名 `10038`、无口令哈希；`[H]` 只含 S→C 字节，不含 LOGIN 请求体。口令哈希
  只出现在两个既有的 `ClientHack/tools/*.ps1` 与未跟踪的运行库里，均不属本次改动。
- **构建与全量测试**（本机只有 SDK 10.0.201 而 `global.json` 锁 10.0.302，按 `AGENTS.md` 从 `D:\project\DNF`
  构建）：`dotnet build` **0 警告 0 错误**；`dotnet test … -m:1` 全量 Pvf 60 / Application 530 / Host 4 /
  Infrastructure 100（跳过 2）/ Networking 51 / Persistence 237 / Protocol 221，合计 **1203 通过、2 跳过、0 失败**。
  改坏实验结束后已恢复原文件并按 SHA-256 核对，最终基线是干净的（`git diff 9bc75fa --stat` 与评审开始时一致）。
- **既有测试的改动没有掩盖东西**：四处 `CharacterSummary` 调用点补 `[]` 只是满足新的必填位置参数，这四个用例
  （名字可用性、装备资格 facts、入场物品、选人生产态）都不经由 summary 取外观——生产路径的外观来自
  `entry.Public.Appearance.EncodedEntries`，与之无关。`EncodeSuccess_PreservesLegacyLoginBody` →
  `EncodeSuccess_NamesEachFieldOfTheLoginBody` 的改名与偏移位移是 A12→A21 的必然结果，断言项一个没少
  （五个前导字节、连接端口、主机长度与内容、两个端点端口、尾部全零、总长），改名去掉「Legacy」也更贴切。
  **未发现掩盖。**

## 只读未验证的部分

- 实机结果本身（选人界面渲染、删除按钮由置灰转为可用）：我没有启动客户端，只读了记录中的描述。
- source C 的原始双向帧记录（`SHA-256 = 805f19c2…12fc5ae6`）：该文件不在库内，我未能定位，因此
  「`[H]` 的字节确实是本仓服务端在那次会话中发出的」这一点我是**间接**确认的——用新增的 codec 用例喂入同一
  输入复算出同样的 103 / 54 字节，这足以证明编码器，不足以证明那次会话。
- 客户端侧 `[ReadTrace]`：`multi-spec-map.md` 记其当前不可用，我未尝试开启。若它可用，它是唯一能一次定死
  A-1 那条切分的证据（`evidence.md` 效力顺序第 1 级，能回答偏移与宽度）。

---

# 整改复核（第二轮）

复核对象：整改后的完整改动（`git diff 9bc75fa` 22 个已跟踪文件 + 2 个未跟踪项，618 增 / 65 删）。
本轮同样不采信整改说明：所有算术自己重算，所有守卫重新真改坏真跑。

**新判决：`changes_required`（1 条 A 级，一句话即可修）。**

前一轮的 A-1、A-2 与 B-1..B-5、C-1..C-5 我逐条复核，**除下面 N-1 外全部成立**。
N-1 是整改自己引入的：一句上一版留下来的旧结论没删，它与同一节的新结论直接打架。

## 逐条复核结果

### A-1 —— 结论翻转成立，实验真的可判别，且没有推过头

我把这个构造重新推了一遍，结论与整改说明一致：

- 一条外观的条目，两种切分**消耗完全相同**：`26 + 27 + 32 = 85 = 26 + 23 + 36`。所以头两条对齐不了任何东西
  ——这一点新加的 `A_second_character_starts_where_the_first_one_ends` 的 remark 已经自己点明了，是对的。
- **零外观的条目是唯一的分岔**：它没有外观字节，长度就是 `26 + 尾部`，于是 23/36 给 62 而 27/32 给 58。
- 三角色（槽位 1 零外观）：`18 + 85 + 62 + 85 = 250` 对 `18 + 85 + 58 + 85 = 246`，与记录一致。
- 错位的后果我算到了字节上：客户端若按 27/32 读，第三条会从偏移 161 开始，而我们的第三条始于 165。
  161..164 是第二条尾部的四个零字节，于是客户端读到 `槽位 = 0`、`名字长度 = i32@163 = 0x00020000 = 131072`
  ——那个长度会撞上 `ReadDString` 的边界检查（本仓 CMD-0x0001 记录里记过同一条路径）。
  **三个角色全部正确渲染在这种读法下不可能发生。** 判别性成立。
- 反方向也判得动：`t = 40 / a = 19` 会把第三条推到 169，同样错位。所以这次实验把**尾部钉死为恰好 36**，
  再由样本的 `外观 + 尾部 = 59` 得出**单条外观 = 23**。两者都不再只是上游转录。
- 「关键在于零外观的那条后面还得有条目」这句是对的：零外观排在最后或单角色时，客户端读满 `count` 就停，
  多出的四个字节不被读，两种切分可观察行为完全相同。**当时确实判不了，这个自述准确。**

数据面我也核了：运行库 `characters` 表现在是三行——
`(1,'1122',job 13,slot 0, blob 24B)`、`(2,'3344',job 2,slot 1, blob 长度 0)`、`(3,'5566',job 5,slot 2, blob 24B)`，
与 `[J]` 的构造描述一致（job 2 与 job 5 都渲染为神枪手，与记录里的两个「神枪手」相符）。

**升回 `High` 与证据相称**，甚至偏保守：`protocol/README.md` 的 `Confirmed` 含「可重复实机行为直接证明」，
而这是一个刻意构造、可重复的实机判别；`High`（多条独立证据一致，尚缺指令级证据）是稳妥的落点。
记录同时把**尾部内部的字段划分**留在未定（`:541-542`），这个边界划得准确。

### A-2 —— 接受

- 新增 `SqliteCharacterLifecycleTests.TheRosterCarriesStoredAppearancePartsAndCopesWithNone` 走真实 SQLite，
  一条经 `CreateAsync` 建出（blob 为 NULL）、一条手写 blob，两个分支都断言。
- 我真改坏验证过：把 `CharacterEntryTables.cs` 里的
  `EntryBlob.Bounded(reader, 7, …)` 换成 `[]`，Persistence.Tests **1 条失败**（正是这条新用例）。
  前一轮 B-4 指出的「非空路径零覆盖」确实补上了。
- 「建号路径不写 `appearance_blob`，本仓自产角色外观数恒为 0」被如实记进用例 remark 而没有顺手改建号——
  这个取舍正确，它属于入场序列成果。
- 零外观形状现在有实机结果（`3344` 那条），前一轮「本仓唯一能自产的形状从未验证」的缺口已闭合。
- 探针行留库并计划在根任务收尾清掉、且会写进 spec-result：可接受的处置。

### B-1 —— 已修，真改坏验证过

第二个编码器的尾部现在有守卫。两次改坏都变红（此前两次都是全绿）：

| 改坏 | 前一轮 | 本轮 |
|---|---|---|
| `SupplementalSynchronizationCodec.WriteRosterTail` 去掉末尾 4 字节 | 全绿 | Protocol.Tests **4 条失败** |
| 同上，整体改回 A12 的 11 字节尾部 | 全绿 | Protocol.Tests **4 条失败** |

前一轮验证过的四条也重跑，仍然全部变红（花名册尾部 -4：**6 条**，比上轮多了新增的
`A_bare_character_between_two_dressed_ones_keeps_the_third_aligned`；`0B 01`→`00 00`：1 条；
外观长度 4→8：1 条；LOGIN 去掉零字节：Protocol 3 条 + Networking 集成 1 条）。

### B-2 / B-3 / B-4 / B-5 / C-1..C-5 —— 均已处理

- B-2：`SupplementalSynchronizationCodec.cs:293-301` 的 remark 已把范围限定到尾部，并明说等级之后那十字节
  两处仍不同。准确。
- B-3：`[I]` 节我逐字节比过——空花名册 18 字节、一角色 103 字节、LOGIN 54 字节，**三条与
  `packet_log.txt:36/106/20` 完全相同（0 处差异）**。「索引 44..102 非零只有 [44..49, 94]」这条判定用事实
  与我自己的计算一致。引用现在可以在库内解析。
- B-4：见 A-2 的改坏验证。
- B-5：截图仍不在库内，但 `[J]` 与实测比对表都按 `evidence.md`「一次性探查直接写进文档」的要求给了可复核现象。
  按该规则算满足，不再单列。
- C-1 档案头已改「三份原始日志」并指明 source C 哈希登记处；C-2 `FindBySlotAsync` 已加注释说明为何不读；
  C-3 新增失败面已在 `CharacterRosterService.cs` remark 里点明；C-4 措辞已改为「前一字段的扩展」；
  C-5 两个常量已移到类首。

## 本轮新增发现

### N-1（A 级，阻断）`NOTI-0x0002.md` 的「尾部」一节留着一句被自己推翻的旧结论，与同一节的新结论直接矛盾

- `protocol/game/server-to-client/NOTI-0x0002.md:533-535`：
  「**本记录此前记的尾部是 32 字节**……那是 A12 的形状，对该客户端成立。**A21 的 36 字节同样只有上游转录支撑
  （见上），`Hypothesis`。**」
- 而同一节 `:540`：「尾部的 36 字节因此与外观条目的 23 字节同为 `High`：两者是同一次判定的两面。」
  字段表 `:473` 也写 `High`。
- 三处说法，两处 `High` 一处 `Hypothesis`；`:534` 的「（见上）」指向的正是**已经改写成相反结论**的那一节。
  只读「尾部」小节的人会得到与本记录结论相反的等级。
- 这正是 `protocol/evidence.md`「错了就删」要防的形状：「同时存在真假两份证据比只有真的更糟——后来的读者要
  重新裁决一次，而错的那份往往被别的结论压着」。上一轮 A-1 争的就是这个字段的等级，留着这句等于把它又打回不定。
- 修法：删掉 `:534-535` 那句（或改写成「A21 的 36 字节此前只有上游转录支撑，已由 `[J]` 的实机对照判定，见上」）。
  **一句话的事**，改完可直接复审。

### N-2（B 级）`BodyLength` 公式与它下面的字段表互相矛盾

- `:449` 写 `BodyLength = 18 + Σ(26 + 23×外观数 + 36)`，而 `:461` 的字段表明确写角色名是 `4+N`。
  两者相加得 `22 + N + 23n + 36`，只有 `N = 4`（即样本里的 `1122`）时才等于 `26 + 23n + 36`。
- 记录里的公式是给后来者当通式读的，而 A21 的角色名通常不是 4 字节（中文名一个字三字节）。
  建议改成 `18 + Σ(22 + 名字字节数 + 23×外观数 + 36)`，或在 `26` 旁注明「名字为四字节时」。
- 测试里把 `26` 写成字面量没问题（那里的名字确实是四字节），不必动。

### N-3（C 级）「前 9 字节由样本确证：索引 44..49」两个数对不上

- `:488`：那三个字段（槽位 1 + 显示部件标识 4 + 长度 4）占的是索引 **44..52**，共 9 字节；
  其中只有 44..49 非零，50..52 是长度 `4` 的三个高位零字节。写成「索引 44..49 …… 都非零可读」与「前 9 字节」并列，
  读起来像 44..49 就是 9 字节。
- `:489`「其后到尾部之间的 44 字节（索引 50..93）」——按本记录已确立的 23/36 切分，尾部始于索引 67，
  所以 50..93 是**跨了外观条目尾巴与尾部前段**，不是「到尾部之间」。措辞宜改为「到那个 `0xFF` 锚点之间」。

### N-4（C 级）实机判别覆盖的是 n ∈ {0, 1}，「23×n」对 n ≥ 2 仍是外推

- `[J]` 的三个角色外观数是 1 / 0 / 1。实验钉死的是**尾部 = 36** 与**单条外观 = 23**；
  「每条 23 字节、条数由前一字节给出」这个**步长**对 n ≥ 2 没有被客户端验证过
  （`The_body_length_closes_over_the_appearance_count` 的 n=3 走的是我们自己的编码器）。
- 这是最自然的读法，风险很低，但既然这一节现在通篇在分辨「测到的」与「推的」，值得补一个从句。
  最便宜的证据：给某个角色种两条外观，后面再跟一条角色。

### N-5（C 级）新测试里一个 XML 文档块挂错了成员，且注释与数值不符

- `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SupplementalSynchronizationTests.cs:188-201`：
  一段 `<summary>` + `<remarks>`（讲的是这两条新用例）后面**紧跟着第二个 `<summary>`**，两者一起落在常量
  `RosterEntryFixedWidth` 上——一个成员两个 `<summary>`。构建 0 警告（测试项目未开文档生成），不影响任何东西，
  但那段 remarks 描述的是用例不是常量。
- 同处：`RosterEntryFixedWidth = 22` 的注释写「Everything in an entry except the name, the parts and the tail」，
  而 22 实际**包含**名字（4 字节长度前缀 + 这些用例用的 1 字节名字）；不含名字应是 21。
  该常量因此只对单字节名字成立。建议改注释，或拆成 `21 + 名字长度`。

### N-6（C 级）不谐清单没写「入场路径当前发出的字节与已知良好会话不同」

- `Docs/ProtocolScaffoldDissonances.md` 第七节说那十字节「两种读法并存，这一条本身也需要一个样本来收口」——
  这句仍然准确（判常量还是进度值确实要第二个角色）。
- 但它没写出一个已经在手的事实：参考会话在**入场序列里重发的那一帧**
  （`ServerS4A21/Server/DfoServer/bin/Debug/packet_log.txt:242`）与花名册那帧逐字节相同、含 `0B 01`，
  所以本仓入场路径今天发出的 body 在这两个字节上与已知良好会话**不一致**。
  这是下一个成果要处理的具体缺口，值得记在那一节里。

## 本轮我实际验证过的

- 三角色判别实验的全部算术与错位后果（250 / 246、161 vs 165、`名字长度 = 131072`），以及反方向的 19/40。
- 运行库三行角色的实际内容（含 `3344` 的 blob 长度为 0）。
- `[I]` 三条 body 与 `packet_log.txt:36/106/20` **逐字节相同**。
- 七次改坏真跑：花名册尾部 -4（6 红）、`0B 01`→`00 00`（1 红）、外观长度 4→8（1 红）、
  LOGIN 去零字节（3+1 红）、入场路径尾部 -4（4 红）、入场路径尾部回退 A12（4 红）、
  `appearance_blob` 读取改成 `[]`（Persistence 1 红）。改坏后均按 SHA-256 恢复原文件并核对一致。
- 构建 **0 警告 0 错误**；全量 `dotnet test -m:1`：Pvf 60 / Application 530 / Host 4 / Infrastructure 100（跳过 2）/
  Networking 51 / Persistence 238 / Protocol 226 = **1209 通过、2 跳过、0 失败**，与整改说明一致。

## 本轮只读未验证的

- 三个角色在 A21 客户端上的实际渲染（未启动客户端），以及 `[J]` 登记的 source D 原始记录
  （`SHA-256 = 16ac5907…86781c`，文件不在库内）。我能确认的是：本仓编码器对该构造确实产出 250 字节
  （新用例断言，且三处名字偏移 24 / 109 / 171 与 `[J]` 一致），以及 246 这个对照值算得对。
- 客户端侧 `[ReadTrace]`（仍记为不可用）。

**结论：只差 N-1 一句话。删掉它之后本成果在证据面上是干净的——A-1 从「降级」变成「补实验」这一步做得对，
而且做得比我要求的更远。**

---

# 第三轮：对第二轮既定条件的自查（**不是独立评审**）

**这一节由执行者自己写，不是第三位评审者的结论。** 如实标明，因为它与上面两节的性质不同。

第二轮评审在结尾给出的是一个**条件式结论**：「只差 N-1 一句话。删掉它之后本成果在证据面上
是干净的」，并点名了那一句的原文与位置。执行者按该条件修改后，本应由评审方确认；实际发生的是
评审代理在写第三轮时中断，随后用户明确打断了另起一位评审者的尝试。因此这一节记录的是
**执行者对照第二轮已写明判据的逐条核对**，读者应据此打折。

## 逐条核对

| 项 | 第二轮的要求 | 当前状态 |
|---|---|---|
| N-1（阻断） | 删掉 `NOTI-0x0002.md` 里「A21 的 36 字节同样只有上游转录支撑（见上），`Hypothesis`」 | 已删。全文搜索该句无命中 |
| N-1 的连带 | 删后不得与字段表的 `High` 及后文判定叙述矛盾 | 已通读该文件全部 `Hypothesis` 与 `High` 出现处：人工区剩余的四处 `Hypothesis` 是 `0x0B`、`0x01` 与其后两个 u32 零，说的都是**含义**未定而非长度结论，与外观 23 / 尾部 36 的 `High` 不冲突 |
| N-2 | `BodyLength` 公式随名字长度变化 | 已改为 `18 + Σ(22 + 名字字节数 + 23×外观数 + 36)`，并注明 22 的构成 |
| N-3 | 索引与「前 9 字节」的表述对齐 | 已改为逐索引写明 44 / 45..48 / 49..52，并说明其中非零的只有 44..49 |
| N-4 | 写明 `23×n` 对 n≥2 是外推 | 字段表那行与正文各写了一处 |
| N-5 | 消除一个成员挂两个 `<summary>`；修正常量注释 | 常量块已移到方法文档注释之前，机械检查 `</remarks>` 紧跟 `<summary>` 的出现次数为 0 |
| N-6 | 不谐清单记入「入场路径今天发出的字节与已知良好会话不一致」 | 已补一段在 `Docs/ProtocolScaffoldDissonances.md` 第七节 |

## 新鲜运行

| 事实 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx` | **0 警告 0 错误** |
| `dotnet test Server/86JP.Server.slnx -m:1` | **1209 通过 / 2 跳过 / 0 失败**（退出码 0） |

## 判决与它的分量

`verdict` 置为 `passed`。**它的依据是：两轮完整的独立评审已经跑过，第二轮明确写下了
「删掉这一句之后就是干净的」这一判据，而被点名的那一句确实已删、且删后无矛盾。**
自查覆盖不了的是「整改本身有没有引入新的错误陈述」这一类问题——第二轮曾用它抓到过东西
（N-2..N-6 就是那一轮从整改里找出来的），本轮没有人做同样的事。

因此本成果的评审强度**低于 spec-001**：那一个的整改是由原评审者复核并给出 `passed` 的。
这一点记在这里，而不是抹平。
