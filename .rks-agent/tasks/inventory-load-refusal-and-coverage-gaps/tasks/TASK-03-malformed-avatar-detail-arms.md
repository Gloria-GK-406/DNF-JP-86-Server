# TASK-03 — 钉住畸形时装细节拒绝的三个分支

## Task Unit Contract

- Revision: 1
- Outcome and references: 覆盖 RQ-007 / AC-009/AC-010 / plan T-003、DP-003。**不改生产代码**。
- Owned boundary:
  - 判据：`Server/tests/Dfo.Server.Persistence.Tests/Inventory/AvatarCarrierRulesTests.cs`（两条同方法的兄弟拒绝已在此）。
  - 假 `ICharacterItemTable` 放该用例文件内的私有嵌套类；只实现 `ReadByCharacterAsync`，其余成员抛 `NotSupportedException`。
  - **生产代码一字不动**（变异核实期间除外，须还原并核散列）。
- Required checks:
  1. 三条新判据（珠宝槽长度≠30、`Color1 = 65536`、`Color2 = 65536`）直接绿，各自断言 `InvalidDataException` **且**消息含 `is malformed or has the wrong owner`。断言异常类型是必需的：守卫若被删，颜色分支会在随后的 `checked((ushort)…)` 上抛 `OverflowException`。
  2. 全量判据退出码 0，跳过数仍为 2。
  3. CK-003（＝AC-010）：分三次，各删该 `if` 条件的一个子句（长度 / `Color1` / `Color2`），确认**对应的那一条**变红而另两条仍绿；每次还原并 `sha256sum -c` 通过。
- Dependencies: 无

## Result (round 1)

- **Changed:** `Server/tests/Dfo.Server.Persistence.Tests/Inventory/AvatarCarrierRulesTests.cs` 新增 4 条判据（珠宝槽长度、`Color1` 越界、`Color2` 越界，＋1 条全部在范围内的正例）与三个私有嵌套类（`Handing` 假 `ICharacterItemTable`、`EveryTemplateAnAvatar`、`NoWornSlots`）。**生产代码零改动。**
- **Why:**
  - 用假 `ICharacterItemTable` 而非真库播种（DP-003）：`avatar_jewel_socket` 上有 `CHECK (… length(…) = 30)`，长度那一支**在真库上不可达**。三个分支统一走假件，钉住的才是 Application 层这条守卫本身，而不是 schema。假件只答 `ReadByCharacterAsync`，其余 6 个成员抛 `NotSupportedException`——装载若走得比这更远会当场说出来，而不是静默通过。
  - 断言 `InvalidDataException` **且**消息含 `is malformed or has the wrong owner`：删掉颜色那一支后，越界值会落到紧随其后的 `checked((ushort)detail.Color1)` 上抛 `OverflowException`（`InventoryEntryAssembly.cs:406-407`）。只断言「抛了东西」的判据在那时仍会绿。
  - 正例 `AnAvatarWhoseFactsAreAllInRangeIsCarried` 的 `Color1` 取在 `ushort.MaxValue` **上**而不是下方：把范围写成错误比较符（如 `>=`）的实现会被它挡住。
- **Checks:**
  - 4 条新判据直接绿（`AvatarCarrierRulesTests` 共 9 条全绿）。
  - 全量判据：退出码 **0**，**2251 总 / 2249 通过 / 2 跳过 / 0 失败**（2237 ＋ 4 ＋ 4 ＋ 4）。落盘：`scratchpad/task03-full.txt`。
  - CK-003 三臂变异核实，散列 `f12af46b698d88fd73d904dfbe880634e56228b38997c579f679e99ed5e40f10`。逐次从守卫条件中去掉一个子句：
    - 去 socket 子句 → 只有 `AnAvatarWhoseJewelSocketIsNotThirtyBytesIsRefused` 红（失败 1 / 通过 8）
    - 去 Color1 子句 → 只有 `AnAvatarWhoseFirstColourIsPastTheCeilingIsRefused` 红（失败 1 / 通过 8）
    - 去 Color2 子句 → 只有 `AnAvatarWhoseSecondColourIsPastTheCeilingIsRefused` 红（失败 1 / 通过 8）
    - 三次还原后 `sha256sum -c` 均为 `OK`。**每一支各自可辨，互不代偿。**

## Independent Quick Review — IQR-03-R1

- Reviewed execution round: Result (round 1)
- Contract revision: 1（无修正案）
- Owned diff identity: `git diff -- Server/tests/Dfo.Server.Persistence.Tests/Inventory/AvatarCarrierRulesTests.cs`，diff 摘要 `5c491537ea372afe9336cde58c9931efbe45dba429c39d2ef554b49011e6ab3c`；文件 `966de0f89f0c2dbfd2388726beb62887d77a565b59e04add98c892b2541a37fd`
- Verdict: **passed**

### 既有判据完整性（本轮首要核对项）

用例文件是被整体重写交付的，故逐字核对既有区域：**owned diff 的删除行数为 0**，全部 180 行均为 `+`。因此工作树文件＝HEAD 文件按行插入所得，HEAD 上四条既有判据（`AvatarCarrierRulesTests.cs:32/54/73/92`，其中 `ACharactersAvatarWithoutAUsableUidIsRefused` 为 2 组 `InlineData`）的名字、断言、播种值与文档注释**逐字未动**。「既有判据的期望值与断言不得为迁就实现而改写」未被触犯。

### 生产代码未动

`InventoryEntryAssembly.cs` 当前散列 `f12af46b698d88fd73d904dfbe880634e56228b38997c579f679e99ed5e40f10`，与 Result 中记录的变异前散列**逐字符相同**——三轮 CK-003 变异后确已完整还原。该文件工作树内相对 HEAD 的差异只有 `RequireOneNameEach`（`InventoryEntryAssembly.cs:311/339/736`），属 TASK-01 所有，不在本单元 owned boundary 内。

### 假件是否真到达 `WithAvatarDetail`

沿 `LoadCharacterAsync` 逐关核对，`Handing` 交出的行无一处被提前短路：

| 关卡 | 位置 | 该行取值 | 结果 |
|---|---|---|---|
| `RequireOwnershipAsync` | `:288` | 真库已播 `accounts`/`characters` | 通过 |
| `RequireOneNameEach`（TASK-01 新增） | `:311` | 单行 `ItemUid = 9` | 通过 |
| `ParseOwnerType("character")` | `:671` | → `Character` | 通过 |
| `ParseContainerType(1, allowAccountCargo:false)` | `:682` | → `Avatar` | 通过 |
| 归属比对 | `:316` | `OwnerId == Character` | 通过 |
| `RequireSlot(4, 9)` | `:716` | `4 >= 0` | 通过 |
| `IdentityOfAsync` → `ClassifyAsync(2500)` | `:278` | `EveryTemplateAnAvatar` 答 `WorldClass = Avatar`（非 `Interchangeable`） | 通过 |
| `WithAvatarDetail` kind 闸 | `:378` | `StackableItemCore.Create(8,…)` → `ItemKind = 8` | 进入 |
| UID 闸 | `:383` | `Create(…, 4242)` → `Core.Value = 4242 > 0` | 进入 |
| `detail is null` 闸 | `:391` | 假件恒给 `AvatarDetailRow` | 进入 |
| 目标守卫 | `:395-400` | | **命中** |

`ICharacterItemTable` 7 个成员中，`InventoryEntryAssembly` 对 `characterItems` 的其余 5 处调用（`:146`/`:187`/`:195`/`:234`/`:239`）全在保存路径上，`ReadMainAsync` 在该类中**零调用**——静态即可断定 6 个 `Unreached()` 成员在装载路径上不可达。正例 `AnAvatarWhoseFactsAreAllInRangeIsCarried` 走完整条 `LoadCharacterAsync`（含 `:65-73` 五个后续读）并通过，是这条推断的运行时佐证。

### 三条断言的载荷

1. **DP-003「长度支在真库上不可达」属实**：`CharacterDatabaseSchema.cs:199` 上 `avatar_jewel_socket BLOB CHECK (avatar_jewel_socket IS NULL OR length(avatar_jewel_socket) = 30)`；同行 `avatar_color1 INTEGER`、`avatar_color2 INTEGER` **确无 CHECK**。用假件统一三支的理由成立。
2. **异常类型断言确是载荷**：删去任一颜色子句后，`65536L` 会落到 `InventoryEntryAssembly.cs:406-407` 的 `checked((ushort)…)` 上抛 `OverflowException`；`Assert.ThrowsAsync<InvalidDataException>` 会因类型不符而红。若只断言「抛了东西」则不会。消息片段 `is malformed or has the wrong owner` 与 `:399` 字面一致。
3. **正例的挡位有效**：`UsableFacts()` 取 `JewelSocket = new byte[30]`、`Color1 = ushort.MaxValue`、`Color2 = 0`，三个值全部**坐在边界上**——把 `> ushort.MaxValue` 误写成 `>=`、把 `< 0` 误写成 `<= 0`、把 `!= 30` 误写成 `< 30` 以外的形状，都会让它变红。断言逐值核对（`JewelSocket.Length`、两个颜色的实际取值），不是「没抛就算过」。

### 检查复核

- `--filter FullyQualifiedName~AvatarCarrierRulesTests`：**通过 9 / 失败 0 / 跳过 0**，构建零警告（无 CS9113——`Handing` 的主构造参数 `avatar` 在 `ReadByCharacterAsync` 内确被使用；另两个嵌套类无主构造参数）。
- 未按要求重跑全量；Result 中 2249/2/0 与 AC-016 的 `2237 + 4 + 4 + 4` 自洽（本单元 4 条＝3 拒绝＋1 正例）。
- 工作树内**无** `.bak`/`.orig`/`.sha`/`.rej` 残留。
- 分层：新代码只触及 `Dfo.Server.Abstractions`／`Application`／`Persistence`／`Data`，均为 Persistence.Tests 既有依赖，构建自证。

### 注释体量

owned diff 净增 **34 注释行 / 125 代码行 ＝ 0.27**，低于 AGENTS.md 给的 `Server/src` 基线 0.46。三段 remarks 各承担一件「代码说不出的事」——为何不用真库播种（schema CHECK 的出处）、为何断言异常类型（`checked` 的存在）、正例为何取在天花板上——无重复记账，未出现「本轮」措辞，未指向 `.rks-agent/`。

### 观察（非缺陷）

契约 Required checks 第 1 条写「三条新判据」，实交 4 条（多一条正例）。这是 plan 风险 R-2「判据写成空判据」的直接缓解，落在同一 owned boundary 内，且已计入 AC-016 的加法，不作为缺陷。AGENTS.md:589 仍写着这条规则「在 `Server/tests` 全域零命中」——该记载由 T-005／RQ-010 负责更新，不属本单元。

未发现须本地修正的缺陷。
