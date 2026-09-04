# TASK-01 — 人群是一个值，且只有一个地方把它变成连接

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 人群类型（封闭层级，以角色表达）与它的解析器（人群 → 收件人）。第一版只放有真实调用方的形态 `TheseCharacters`；`OneCharacter` 不单独立类型，它是前者的退化情形。
- Referenced spec requirements: RQ-003, RQ-004
- Referenced plan sections: T-001, BD-001, BD-002, BD-010
- Owned boundary: `Server/src/Dfo.Server.Abstractions/Notifications/`（人群类型）、`Server/src/Dfo.Server.Application/Notifications/`（解析器）、`Server/tests/` 新增判据
- Permitted files or interfaces: 上述新建路径；`Server/src/Dfo.Server.Application/ApplicationModule.cs` 的接线一处
- Dependencies: 无
- Required dependency outputs: 无
- Acceptance conditions:
  - 人群是封闭层级，非法形态不可表达（例如「既是一组角色又带一个频道」这种组合构造不出来）
  - 人群以角色表达；类型上不出现任何连接标识
  - 解析器是把角色变成连接的唯一新增位置；角色当下没有连接的，不出现在收件人里
  - 全量判据通过
- Task-specific authorization: 不建 Git 检查点、不提交。接受提交由工作区控制器在完整评审与新鲜验证之后做一次。
- Required methods: test-driven-development
- Required checks: CK-001, CK-002, CK-003, CK-004
- Required evidence: 人群类型的成员枚举（附命令与原始输出）；非法组合的构造尝试及编译器拒绝方式；无连接角色被排除的判据；全量判据计数

## Execution Result — Round 1

### 交付

- 新增 `Server/src/Dfo.Server.Abstractions/Notifications/INotificationServices.cs`：
  `NotificationAudience`（抽象 record，私有无参构造，嵌套 `TheseCharacters(IReadOnlyList<CharacterId>)`）、
  `NotificationRecipient(CharacterId, NetworkConnectionId)`、`INotificationAudienceResolver`
- 新增 `Server/src/Dfo.Server.Application/Notifications/NotificationAudienceResolver.cs`（internal sealed，
  注入 `ISelectedSessionDomainService`，唯一成员 `Resolve`）
- 新增 `Server/src/Dfo.Server.Application/Notifications/NotificationDispatchingExtensions.cs`
  （`AddNotificationDispatching()`，本轮只注册解析器）
- `ApplicationModule.cs`：加一行 `services.AddNotificationDispatching();` 与对应 using
- 新增 `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationAudienceTests.cs`（6 条）

### 一处边界在实现中被证伪，据此改了设计与说法

初版注释写的是「私有构造函数封闭了层级，外部无法声明新形态」。用编译探针验证时**第一个探针通过、第二个
探针没通过**：

```
探针 1  internal sealed record EveryoneOnThisChannel(int ListenerPort) : NotificationAudience;
        → error CS7036: 未提供与"NotificationAudience.NotificationAudience(NotificationAudience)"
          的所需参数"original"对应的参数
探针 2  internal sealed record EveryoneByCopy(NotificationAudience Source, int ListenerPort)
            : NotificationAudience(Source);
        → 已成功生成。0 个警告
```

record 会生成 protected 复制构造函数，外部只要手上有一个 audience 就能借它派生进来。尝试把复制构造函数
私有化：

```
error CS8878: 复制构造函数"NotificationAudience.NotificationAudience(NotificationAudience)"
必须是公共的或受保护的，因为该记录未密封。
```

C# 不允许。所以 **record 做的封闭层级不可能完全封闭**，这不是这里的取舍，是语言的规定。据此：

1. 注释改为如实写出两条限制（复制构造函数 CS8878；switch 对类层级永不穷尽），并说明两者都落到解析器
   最后一个分支
2. 判据从「外部无法派生」改成实际为真的两条：唯一能凭空造出 audience 的构造函数是私有的；另一个构造函数
   是语言强制的复制构造函数
3. 新增一条判据用探针 2 那条路造出一个未教过的形态，证明解析器**指名它**而不是解析成空

**Spec 的 Non-Goals 里那句「将来加一种时编译器会强制所有解析处理它」比代码强**，需要在 Spec 上追加更正。

### 检查

| ID | 命令 / 方式 | 结果 |
|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` | 已成功生成，0 个警告，0 个错误 |
| CK-002 | 上面两个编译探针 + `The_only_way_to_make_an_audience_from_nothing_is_private_to_the_hierarchy` | 见上，探针原始输出已录 |
| CK-003 | `A_character_nobody_is_playing_is_not_a_recipient`、`An_audience_whose_characters_are_all_away_resolves_to_nobody` | 通过 |
| CK-004 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 60+52+510+256+6+223+968 = **2075 通过 / 2 跳过 / 0 失败**（基线 2069/2/0，净 +6，全部为本轮新增） |

### 迁移前的连接解析位置枚举（T-001 步骤 3）

命令 `grep -rn "FindByCharacter" Server/src --include=*.cs`，原始输出 8 行：

```
Abstractions/Sessions/ISessionPresence.cs:85          接口声明
Application/Notifications/NotificationAudienceResolver.cs:40   本轮新增（解析器）
Application/Sessions/SelectCharacterLifecycleServices.cs:48     清理通告：旁人 → 连接
Application/Sessions/SelectCharacterLifecycleServices.cs:98      存活判断（判 null，不取连接）
Application/Sessions/SelectCharacterLifecycleServices.cs:141     PlanPvp：取会话去查运行态，不为投递
Application/Sessions/SelectCharacterLifecycleServices.cs:171     PlanPvpPublication ← T-005 要摘掉的那一处
Application/Sessions/SelectedSessionDomainService.cs:48          实现本身
World/ExperienceBonusDomainService.cs:31                          待 T-005 复核用途
```

迁移之前 `:171` 仍在，属预期。

### Executor Self-check — Round 1

- 拥有边界内：只动了 Notifications 两个新目录、ApplicationModule 两行、一个新判据文件。未改任何既有断言。
- 未做的：`OneCharacter` 未单独立类型（计划明写它是 `TheseCharacters` 的退化情形）；`InArea` 形态未加
  （等 TASK-06 裁定）。
- **未验证到的**：解析器目前只有判据在用，生产调用方要到 TASK-05 才出现。`ExperienceBonusDomainService:31`
  的用途本轮没看，留给 TASK-05 的枚举。
- 重复角色：`TheseCharacters` 按给定顺序原样保留、不去重，已写进注释。今天唯一的将来调用方
  （`pvp.ReadyOn`）按角色为键，天然无重复。

## Independent Quick Review — Round 1

- Verdict: **changes_required**
- Reviewed contract revision: 1（无修正案）
- Owned diff identity: 基线 `b05d47e4`，未提交。`M Server/src/Dfo.Server.Application/ApplicationModule.cs`（+2 行）；新增
  `Server/src/Dfo.Server.Abstractions/Notifications/INotificationServices.cs`、
  `Server/src/Dfo.Server.Application/Notifications/{NotificationAudienceResolver.cs,NotificationDispatchingExtensions.cs}`、
  `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationAudienceTests.cs`。
  `.rks-agent/` 下的流程产物未计入 owned diff，只作输入读。
- Reviewer: fresh context，未参与执行

### 复跑并逐项核对一致的部分

| 执行方的说法 | 评审方复跑 | 结论 |
|---|---|---|
| `grep -rn "FindByCharacter" Server/src --include=*.cs` 8 行 | 复跑，8 行，**文件名与行号逐条一致**（`:85 :40 :48 :98 :141 :171 :48 :31`） | 一致 |
| `:98` 判 null 不取连接 / `:141` 取会话查运行态不为投递 / `:171` 取 `ConnectionId` 投递 | 读源码复核：`:98` `is null`；`:141` `runtimes.GetRequired(owner)`；`:171` `new PvpParticipantPublicationTarget(value!.ConnectionId, …)` | 一致，`:171` 迁移前仍在属预期 |
| CK-001 零警告零错误 | `dotnet build Server/86JP.Server.slnx --nologo` → 已成功生成，0 个警告，0 个错误 | 一致 |
| CK-004 分项 60+52+510+256+6+223+968 = 2075/2/0 | 全量复跑：Pvf 60、Networking 52、Protocol 510、Persistence 256、Host 6、Infrastructure 223（跳过 2）、Application 968 | **七个分项逐个一致**，合计 2075 通过 / 2 跳过 / 0 失败，较基线 2069 净 +6；无既知偶发失败出现 |
| 新增 6 条判据 | `--filter FullyQualifiedName~NotificationAudienceTests` → 6 条全部通过，方法名与 CK-002/CK-003 引用的名字对得上 | 一致 |
| 探针 1 报 CS7036 | 独立 scratch 工程（只引用 `Dfo.Server.Abstractions`，不改仓库文件）复现，错误号与消息文本一致 | 一致 |
| 边界：只动两个新目录 + `ApplicationModule.cs` 两行 + 一个新判据文件 | `git status --short` / `git diff` / 目录清点：新目录各自只有声明的文件，未改任何既有判据 | 一致，未越界 |
| CS9113 风险 | `NotificationAudienceResolver` 的主构造器参数 `sessions` 在 `Reachable` 中被读；record 位置参数不受 CS9113 约束；全量构建 0 警告 | 无风险 |

### F-1（阻断，纯文字）关于「封闭层级」的说法仍有一处比代码强，且被执行方自己的探针 2 证伪

我没有复用执行方的探针，而是另建一个只引用 `Dfo.Server.Abstractions` 的 scratch 工程（仓库文件未动）编译：

```csharp
public sealed record CharactersAndAChannel(NotificationAudience Source, int ListenerPort)
    : NotificationAudience(Source);

public static NotificationAudience Make() =>
    new CharactersAndAChannel(
        new NotificationAudience.TheseCharacters([new CharacterId(1), new CharacterId(2)]), 10011);

public static (IReadOnlyList<CharacterId> Who, int Where) ReadBothHalves()   // 两半都读得回来
{
    var value = (CharactersAndAChannel)Make();
    return (((NotificationAudience.TheseCharacters)value.Source).CharacterIds, value.ListenerPort);
}
```

→ `已成功生成。0 个警告，0 个错误`。

即：**「一组角色 + 一个频道」这个组合值在层级外部造得出来，并且它是一个 `NotificationAudience`。**
这正是契约受理条件第 1 条与 Spec 更正段共同点名的那个反例。据此，下面四处仍然过强：

1. `Server/src/Dfo.Server.Abstractions/Notifications/INotificationServices.cs:16`
   —— "The shapes are the nested records below and no others"。字面为假。同一 `<remarks>` 两段之后的
   "Two limits" 收回的是「外部可以派生」这个**机制**，没有把结论带回这句**断言**。
2. 同文件 `:19-20` —— "That last part is what this buys over an enum beside an identifier, where
   \"everyone, and also these three characters\" is a value the type system is happy with."
   比较项不成立：在本层级下，"these three characters, and also a channel" 同样是类型系统乐于接受的值
   （即上面那段代码）。真正买到的不是「造不出来」，而是「造出来的会撞到解析器最后一个分支、被指名」。
3. `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationAudienceTests.cs`
   中 `The_only_way_to_make_an_audience_from_nothing_is_private_to_the_hierarchy` 的 `<summary>`：
   "The shapes an audience can take are the ones written in the hierarchy, and no others." 同第 1 条；
   而且该用例实际断言的是**公开嵌套类型集合**，不是「全部派生类型」，`<summary>` 比它自己的断言还宽。
4. `.rks-agent/tasks/notifications-know-their-audience/spec.md` 更正段：
   「**仍然成立的是 RQ-003 本身**：……「既是一组角色又带一个频道」这样的非法组合造不出来」
   —— 这正是探针 2 造出来的东西。执行方发现了机制（CS8878 / 复制构造函数）却把它保留的这个具体例子
   原样留下了，这一句现在是本轮唯一一处**没被自己的证据支持**的论断。

需要说清楚两件事，免得整改改过头：

- **"there is no way to build a value that is two shapes at once" 这句本身是真的**——嵌套形态都是 `sealed`
  的兄弟，一个值不可能同时是其中两个。过强的是把它等同于「组合值造不出来」。
- **这不是设计缺陷，实现不用改**。真正的保护是解析器最后一个分支，已经有判据钉着
  （`A_shape_the_resolver_was_not_taught_is_named_rather_than_resolving_to_nobody`）；我用上面那个值走的
  也是同一条路。所以 F-1 只需改措辞。

### 判据可证伪性抽查（不实跑变异）

- 去掉 `Reachable` 里的 `is { } session` 卫兵改用 `session!.ConnectionId`
  → `A_character_nobody_is_playing_is_not_a_recipient` 抛 NRE，变红。
- 最后一个分支从 `throw` 改成 `return []`
  → `A_shape_the_resolver_was_not_taught_is_named_rather_than_resolving_to_nobody` 的 `Assert.Throws` 变红。
- 私有无参构造函数改成 `protected`
  → `The_only_way_to_make_an_audience_from_nothing_is_private_to_the_hierarchy` 的 `Assert.All` 变红
  （`IsPrivate` 为假，且空参数列表不匹配复制构造函数模式）。
- 解析器排序或去重
  → `Recipients_come_back_in_the_order_the_audience_named_them` 变红。

四条都真会红，判据不是摆设。

### 非阻断观察（不要求本轮处理）

- N-1 `typeof(NotificationAudience).GetNestedTypes()` 用默认绑定标志，只返回 **public** 嵌套类型。将来加一个
  `internal`/`private` 嵌套形态不会触发那条枚举断言。今天只有一个公开形态，影响为零。
- N-2 `ApplicationModule.cs` 那行 `AddNotificationDispatching()` 本轮没有判据覆盖（注册后无人取用）。
  本轮无生产消费方，留到 TASK-05 合理。
- N-3 `INotificationAudienceResolver` 的 `*Resolver` 后缀不在 `AGENTS.md`「接口后缀标明类别」表里，
  且 `Abstractions` 下无先例。**不作为违规**：该表在实际代码里本就不穷尽（现有 `*Accessor`、`*Adjuster`、
  `*Container`、`*Participant`、`*Book`、`*Effect` 等同样未列）。

### 需要的整改

只改文字，不动实现、不动判据断言：

1. `INotificationServices.cs:16` 与 `:19-20`：把「形态就这些、没别的」「组合值造不出来」改成实际为真的说法——
   *层级内*造不出来；层级外借语言强制的复制构造函数派生出来的形态，会在解析器最后一个分支被指名。
2. `NotificationAudienceTests.cs` 上述 `<summary>`：收到与该用例实际断言等宽（公开嵌套形态的枚举）。
3. `spec.md` 更正段：把「「既是一组角色又带一个频道」这样的非法组合造不出来」改为与探针 2 一致的说法。
   RQ-003 相对「枚举 + 一个 id」的收益改述为：**没有任何一个被解析的形态能同时是两个**，而组合值即使从外部
   造出来也到不了投递、会被指名。

契约受理条件第 1 条括号里的例子与 F-1 直接相关：它是否需要一条正式修正案，由 `executing-tasks`
的控制方裁定，评审方不代改契约、不改实现文件、不动任务运行态。

### 本轮实际执行的命令

```
git -C <worktree> status --short ; git -C <worktree> diff ; git -C <worktree> log --oneline -1
grep -rn "FindByCharacter" Server/src --include=*.cs
dotnet build Server/86JP.Server.slnx --nologo
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
PVF_ARCHIVE_PATH=… dotnet test Server/tests/Dfo.Server.Application.Tests/... --filter FullyQualifiedName~NotificationAudienceTests -v n
dotnet build <scratchpad>/probe/probe.csproj --nologo          # 探针 A（CS7036）与探针 B（成功生成）
```

## Controller Disposition — after Quick Review Round 1

`task-rework`。F-1 是文字过强，不改批准的边界也不改中心做法，本地可改。

快审另外提的一个问题——「契约受理条件第 1 条括号里的例子是否需要正式修正案」——**需要**，见下。
那个括号里写的正是被证伪的反例，留着它会让后来的读者以为这一条已经满足。

## Task Unit Contract Amendment — Revision 2

- Contract revision: 2
- 改动范围：仅 Acceptance conditions 第 1 条。其余条款不变。

原文：

> - 人群是封闭层级，非法形态不可表达（例如「既是一组角色又带一个频道」这种组合构造不出来）

改为：

> - 人群的各个形态是彼此 sealed 的兄弟，一个 audience 不会同时是其中两个；唯一能凭空造出 audience
>   的构造函数是私有的，所以照常规写法新增形态编译不过。**「非法形态不可表达」做不到**——record 未
>   密封时 C# 强制复制构造函数至少 protected（CS8878），外部记录可借它派生并携带任意字段，包括
>   「一组角色 + 一个频道」。因此本条的受理点是：越过上述两条造出来的形态，必须走到解析器最后一个
>   分支被**指名**，而不是解析成空收件人。

理由：修正案只把受理点挪到实际可达成、且已有判据钉住的那一条。Spec 的 RQ-003 相应更正记在
`spec.md` 的 Corrections 段 C-2。

## Execution Result — Round 2

只改措辞与一条判据，**未动任何实现**。

### 四处过强表述的处置

| 位置 | 处置 |
|---|---|
| `INotificationServices.cs` 的 `<remarks>` | 重写。把快审给的那段反例代码**原样写进注释**，明说 "That compiles, and both halves read back"，并把收益降级为「非法组合不再是顺手就能写出来的那种值」 |
| 同处拿 enum 作比较的那句 | 一并重写，不再声称对方能表达而这边不能 |
| `The_only_way_to_make_an_audience_from_nothing_is_private_to_the_hierarchy` 的 `<summary>`/`<remarks>` | 改成与断言等宽：只说「凭空造不出来」+「形态是那几个嵌套兄弟」，并明写「Deliberately not the stronger claim」 |
| `spec.md` Corrections | 追加 C-2，把 C-1 里那句一并收回；删掉 C-1 末尾引用旧用例名的段落（该段已被 C-2 取代） |

### 判据的改动

- `A_shape_the_resolver_was_not_taught_is_named_rather_than_resolving_to_nobody`
  → 更名为 `The_shape_that_should_not_exist_is_named_rather_than_resolving_to_nobody`，
  替身从随便一个 `UnknownShape` 换成**快审给的那个具体反例** `CharactersAndAChannel(Source, ListenerPort)`。
  这样判据本身就是证据：那个组合造得出来，而且被指名。
- 采纳快审的非阻断观察：`GetNestedTypes()` 加 `BindingFlags.Public | BindingFlags.NonPublic`，
  否则它只看 public 嵌套类型，断言比它自称的弱。

### 检查（Round 2）

| ID | 命令 | 结果 |
|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` | 已成功生成，0 个警告，0 个错误 |
| CK-002 | `NotificationAudienceTests` 全部 6 条 | 通过 |
| CK-003 | 同上 | 通过 |
| CK-004 | 全量判据 | 见下方 Round 2 计数 |

### Executor Self-check — Round 2

- 改动仅限：`INotificationServices.cs` 的 `<remarks>`、`NotificationAudienceTests.cs` 两处注释 +
  一条判据的名字与替身 + 一个 BindingFlags、`spec.md` 的 Corrections 段、以及本 Task 文件。
  **生产逻辑一行未改**（`NotificationAudienceResolver.cs`、`NotificationDispatchingExtensions.cs`、
  `ApplicationModule.cs` 本轮零改动）。
- 快审说「实现不用改」，我确认并照做了，没有借整改顺手改实现。
- 仍未验证的：`AddNotificationDispatching()` 那行本轮仍无判据覆盖（无生产消费方），留 TASK-05。

**Round 2 全量判据**：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
→ 510 + 52 + 60 + 256 + 6 + 223（跳过 2）+ 968 = **2075 通过 / 2 跳过 / 0 失败**，与 Round 1 同值
（本轮未增删判据条数，只改了其中一条的名字与替身）。未遇既知偶发失败。

## Independent Quick Review — Round 2

- Verdict: **passed**
- Reviewed contract revision: 2（Contract revision 1 + `Task Unit Contract Amendment — Revision 2`，仅受理条件第 1 条被改）
- Owned diff identity: 基线 `b05d47e4`，未提交。`git status --short` 与 Round 1 逐字相同：
  `M Server/src/Dfo.Server.Application/ApplicationModule.cs`，加三个未跟踪新目录
  （`Abstractions/Notifications/`、`Application/Notifications/`、`Application.Tests/Notifications/`）。
  `.rks-agent/` 下的流程产物只作输入读，不计入 owned diff。
- Reviewer: fresh context，未参与执行，也未参与 Round 1 快审
- 审的范围：只审 Round 2 的整改。Round 1 已核对一致的部分（`FindByCharacter` 枚举、`:171` 迁移前仍在、
  边界、CS9113）不重开。

### 1. F-1 是否真的解决：逐句读完四处，**没有一句再比代码强**

| 位置 | 改后的说法 | 核验 |
|---|---|---|
| `INotificationServices.cs:16-19` | "shapes below are siblings, each sealed, so no audience is two of them at once"；"the only constructor that makes one out of nothing is private"；"writing a new shape the ordinary way does not compile"；**"it is less than closed -- so here is exactly where it stops"** | 三条分述都真：`TheseCharacters` 是 sealed；凭空构造函数私有（有判据钉住）；常规写法撞 CS7036。并且**主动声明自己弱于「封闭」**。Round 1 点名的 "The shapes are the nested records below and no others" 已删除 |
| `INotificationServices.cs:21-35` | 把快审的反例代码原样写进注释，"That compiles, and both halves read back"；"this hierarchy does **not** make an illegal combination unrepresentable the way a real union would" | 与证据完全一致，是**对强断言的否定**而非弱化版的强断言 |
| 原 `:19-20` 拿 enum 作比较的那句 | **整句删除**，未用更弱的版本替换 | Round 1 指出比较项不成立，删掉是对的；改后不再有任何「对方能表达而这边不能」的对比 |
| `NotificationAudienceTests.cs:90-103`（`The_only_way_to_make_...` 的 `<summary>`/`<remarks>`） | `<summary>`：「凭空造不出来」+「the shapes **offered** are the ones written in it」；`<remarks>` 明写 **"Deliberately not the stronger claim that no other shape can exist"** | 与断言等宽：断言查的正是构造函数集合 + `NotificationAudience` 的嵌套子类型集合。"offered" 一词把范围限在本类型提供的形态上，不再覆盖「全部派生类型」 |
| `NotificationAudienceTests.cs:123-136`（新用例的注释） | "So these three characters, and also a channel is a value that can be made, and **no wording upstairs should say otherwise**" | 与代码一致，且反向约束了上游措辞 |
| `spec.md` C-1 | 只剩 Non-Goals 那句的两条否证（复制构造函数 CS8878、switch 永不穷尽）。Round 1 点名的末尾段「仍然成立的是 RQ-003 本身……非法组合造不出来」**已删除** | 无残句 |
| `spec.md` C-2 | 以「C-1 最初写的是「……」」的形式**引用**那句已删段落，随即判定「**这句也是假的**」，附反例代码，再给出实际为真的三条 | **C-1 与 C-2 不矛盾**：被推翻的句子是作为「引文 + 判假」出现的，不是残留论断。这是 append-only 更正日志的正确写法（该段开头已声明「原文保留在上，更正写在这里」） |

补充核验：C-2 结尾把相对「枚举 + 一个 id」的收益改述为「不是『非法组合不可表达』，而是『非法组合不再是顺手
就能写出来的那种值』」，与 `INotificationServices.cs:32-33` 的 "make anything else a deliberate act" 同调，
两处不打架。

我另外把两个源文件里所有绝对语气的词（`cannot / no other / impossible / unrepresentable / never /
closed / exhaustive / only / no way`）逐个 grep 出来读了一遍，共 15 处，**没有一处超出代码**：
`never exhaustive`（真）、`is less than "closed"`（自认弱）、`does not make ... unrepresentable`（否定式）、
`no other shape can exist` 出现在 "Deliberately **not** the stronger claim that ..." 里面（被否定）。

**F-1 已解决。**

### 2. 「未动实现」属实

- `git diff Server/src/Dfo.Server.Application/ApplicationModule.cs` 仍是声明的 +2 行
  （`using Dfo.Server.Application.Notifications;` 与 `services.AddNotificationDispatching();`），与 Round 1 相同。
- 三个新文件未跟踪、无法用 git 比对轮次，改用文件 mtime 分簇（`ls --time-style=full-iso`）：

```
12:03:52  NotificationAudienceResolver.cs        <- Round 1 时间簇，本轮未写
12:03:57  NotificationDispatchingExtensions.cs   <- Round 1 时间簇，本轮未写
12:04:16  ApplicationModule.cs                   <- Round 1 时间簇，本轮未写
--------  （Round 1 快审在此之间发生）
12:17:04  INotificationServices.cs               <- Round 2 改动
12:17:32  NotificationAudienceTests.cs           <- Round 2 改动
12:17:59  spec.md                                <- Round 2 改动
```

三个实现文件的时间戳整整早 13 分钟，落在 Round 1 交付簇里，**本轮零改动**，与自检一致。
另读了 `NotificationAudienceResolver.cs` 全文复核：`Resolve` 仍是两臂 switch，最后一臂仍抛
`InvalidOperationException` 并带 `audience.GetType().Name`；`Reachable` 的 `is { } session` 卫兵仍在。

### 3. 新判据真的会红——本轮**实跑了变异**，而不是只做推理

为了不碰被审工作区（三个新文件未跟踪，改坏了 git 救不回来），我把源码树（`Server` 去掉 bin/obj，6.6M）
复制到 scratchpad 沙箱里做变异，被审工作区全程只读。沙箱基线先复跑绿：6/6 通过。

**变异 A —— 解析器最后一臂 `throw` 改成 `return []`**（`_ => [],`）：

```
失败 ...NotificationAudienceTests.The_shape_that_should_not_exist_is_named_rather_than_resolving_to_nobody [1 ms]
   Assert.Throws() Failure: No exception was thrown
失败!  - 失败: 1，通过: 5，已跳过: 0，总计: 6
```

**只有这一条红，其余 5 条仍绿**——新判据既真的钉住了那一臂，也没有过度耦合。
同时这也实证了那个组合值确实**走到**最后一臂（否则改这一臂不会影响它）。

### 4. `GetNestedTypes(Public | NonPublic)` 确实变强了，且仍通过

沙箱里往 `NotificationAudience` 内加一个非公开形态
`private sealed record Hidden(int Port) : NotificationAudience;`（能编译，嵌套类型可用外层私有构造函数），
然后对照两组绑定标志跑 `The_only_way_to_make_an_audience_from_nothing_is_private_to_the_hierarchy`：

| 绑定标志 | 结果 |
|---|---|
| `GetNestedTypes(BindingFlags.Public \| BindingFlags.NonPublic)`（改后） | **失败** `Assert.Equal() Failure: Collections differ` —— 抓到了 |
| `GetNestedTypes()`（改前默认，只看 public） | **通过** —— 对隐藏形态视而不见 |

即 Round 1 的 N-1 被真实关闭：这处不是措辞调整，是断言变强。在被审工作区上该用例**仍然通过**
（下方全量与定向复跑均绿）。`.Where(typeof(NotificationAudience).IsAssignableFrom)` 这层过滤还顺带挡住了
将来可能出现的编译器生成嵌套类型，不会把断言变脆。

### 5. 全量判据计数：与执行方声称的**逐个分项一致**

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

```
Networking     52 通过 / 0 跳过        Protocol      510 通过 / 0 跳过
Pvf            60 通过 / 0 跳过        Persistence   256 通过 / 0 跳过
Host            6 通过 / 0 跳过        Infrastructure 223 通过 / 2 跳过
Application   968 通过 / 0 跳过
```

52+510+60+256+6+223+968 = **2075 通过 / 2 跳过 / 0 失败**，与 Round 2 声称的
510+52+60+256+6+223（跳过 2）+968 一致，也与 Round 1 同值（本轮未增删判据条数）。
**未出现既知偶发失败**（无 `ObjectDisposedException: SQLitePCL.sqlite3`，无
`CharacterDatabaseSchema.KeepWritersAndReadersApart` 相关栈）。

`dotnet build Server/86JP.Server.slnx --nologo` -> 已成功生成，**0 个警告，0 个错误**
（TreatWarningsAsErrors 开启，CS9113 未触发）。

定向复跑 6 条，全绿，新名生效：

```
已通过 The_only_way_to_make_an_audience_from_nothing_is_private_to_the_hierarchy
已通过 A_character_nobody_is_playing_is_not_a_recipient
已通过 The_shape_that_should_not_exist_is_named_rather_than_resolving_to_nobody
已通过 An_audience_of_characters_resolves_to_the_connections_they_are_on
已通过 An_audience_whose_characters_are_all_away_resolves_to_nobody
已通过 Recipients_come_back_in_the_order_the_audience_named_them
```

### 6. 修正案 Revision 2 的措辞：**没有又造一句比代码强的话**

逐句核：

1. 「各个形态是彼此 sealed 的兄弟，一个 audience 不会同时是其中两个」——真（`TheseCharacters` sealed，
   不能作基类，外部形态只能与它并列而非兼任）。
2. 「唯一能凭空造出 audience 的构造函数是私有的」——真，且被判据钉住。
3. 「照常规写法新增形态编译不过」——真（CS7036）；「照常规写法」这个限定词恰好是必需的，没有省掉。
4. 「**「非法形态不可表达」做不到**」——主动认负。
5. 「record 未密封时 C# 强制复制构造函数至少 protected（CS8878）」——与语言规则一致。
6. 「本条的受理点是：越过上述两条造出来的形态，**必须**走到解析器最后一个分支被指名」——这个「必须」是
   本修正案里最强的词，但它对当前解析器**普遍为真**：`TheseCharacters` 是 sealed 无法被继承，所以任何外部
   派生形态都只能落到 `_` 臂。已由变异 A 实证。

修正案把受理点挪到了「实际可达成且有判据钉住」的那一条，范围只动受理条件第 1 条，未借机放宽其余条款。

### 7. 旧用例名残留：源码与 spec **零残留**，Task 文件里的三处是正当的历史/改名记录

`grep -rn "A_shape_the_resolver_was_not_taught" .`（含 `.rks-agent`）命中 3 处，全在本 Task 文件内：

- `:174`、`:182` —— 在 `Independent Quick Review — Round 1` 段内。该段是已封存的历史轮次，技能明确禁止
  改写既有轮次，**留着是对的**。
- `:268` —— 在 `Execution Result — Round 2` 的改名记录里（「旧名 -> 更名为 新名」），引用旧名是其本意。

源码、`spec.md`、`plan.md`、`TaskContract.md` 中**无任何活引用**；`spec.md:203` 已指向新名
`NotificationAudienceTests.The_shape_that_should_not_exist_is_named_rather_than_resolving_to_nobody`。
（另有几处二进制命中，是 `bin/obj` 下的编译产物，非源文件。）

### 非阻断观察（不要求本轮处理）

- **N-1（留给最终 Spec 评审）** `spec.md:122` 的 `AC-003` 期望列仍写「形态是封闭的；非法组合无法构造」，
  这正是 C-2 判假的那句话的**验收表版本**。C-2 按 ID 点名了 RQ-003 与 Non-Goals，但没点 AC-003。
  按该文件「原文保留在上，更正写在这里」的既定约定，AC-003 作为 RQ-003 的派生行由 C-2 顺带覆盖，
  **不构成本 Task Unit 的阻断**（本 Task 的受理点已由 Amendment R2 明确改写）；但照 AC-003 字面执行
  验收的后来者会去找一个代码给不了的东西。属 Spec 级记账，宜由最终评审一并处理。
- **N-2** Round 1 的 N-2 仍在：`ApplicationModule.cs` 那行 `AddNotificationDispatching()` 本轮仍无判据覆盖
  （注册后无生产消费方）。执行方已如实记录并留给 TASK-05，本轮合理。
- **N-3** `INotificationServices.cs:16` 写 "shapes ... are siblings"（复数），当下只有一个形态。属前瞻式
  措辞，不是对保护强度的过强断言，不需要改。

### 本轮实际执行的命令

```
git -C <worktree> log --oneline -1 ; git -C <worktree> status --short
git -C <worktree> diff Server/src/Dfo.Server.Application/ApplicationModule.cs
ls -la --time-style=full-iso <四个 owned 路径> <spec.md>          # 轮次分簇证据
sha256sum <四个 owned 源文件>                                     # 复核工作区未被评审方改动
grep -rn "A_shape_the_resolver_was_not_taught" .
grep -rn "The_shape_that_should_not_exist_is_named" .
grep -niE "cannot|no other|impossible|unrepresentable|never|closed|exhaustive|only|no way" <两个 owned 文件>
dotnet build Server/86JP.Server.slnx --nologo
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
PVF_ARCHIVE_PATH=... dotnet test Server/tests/Dfo.Server.Application.Tests/... --filter FullyQualifiedName~NotificationAudienceTests -v n
# 沙箱（scratchpad 内的源码树副本，被审工作区全程只读）：
tar -cf - --exclude=bin --exclude=obj Server | (cd <sandbox> && tar -xf -)
<sandbox> 变异 A：解析器 _ 臂 throw -> []                 # 目标判据变红，其余 5 条仍绿
<sandbox> 变异 B：加 private nested Hidden 形态           # 新绑定标志变红 / 旧默认标志仍绿
rm -rf <sandbox>
```

复核结束后再次 `git status --short` + `sha256sum`：被审工作区与评审开始时逐字节相同，评审方未改动任何
实现文件、判据文件或任务运行态。

## Controller Disposition — after Quick Review Round 2

`passed`。最新的执行结果、自检、快审裁定与 owned diff 指向同一轮。登记表置 TASK-01 为 `done`，
`tasks.active` 前移至 TASK-02。

快审的非阻断项（`spec.md` 的 AC-003 Then 列仍是被 C-2 推翻的那句话的验收表述）当场处理了：
C-2 追加一段明写它同时覆盖 AC-003，并给出照哪三条去核。不留给最终评审。
