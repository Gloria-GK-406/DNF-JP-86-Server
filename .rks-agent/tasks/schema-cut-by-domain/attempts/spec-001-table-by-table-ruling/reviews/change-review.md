# Change Review — spec-001 全表归属裁定与目标形状

被评审的完整结果：`Docs/Refactor/TableOwnershipRuling.md`
验收依据：本 attempt 的 `TaskContract.md`（revision 1）与 `spec.md`（RQ-001..RQ-011 / AC-001..AC-011 / EV-001..EV-004），以及根 `TaskContract.md` 的合并判据与红线。

评审在独立的新上下文中进行，每一轮的评审者只拿到交付物、验收边界与代码，不带执行者的推理过程。

## 第一轮

两个并行的独立上下文，各带一个镜头，互不重叠。

| 镜头 | 判定 |
|---|---|
| 事实准确性——逐条到代码里核实记录声称的每一个事实 | `changes_required` |
| 覆盖完整性与推理质量——覆盖是否闭合、判据是否被倒推、是否越红线或漏答规格 | `changes_required` |

两轮都确认了同一件事：**清点这一层是闭合的**。53 张表逐条裁定无重复、与库形状声明的表集合完全相等；附录 224 行无重复源列、无一源两落点；31 张不变表 196 列逐张与声明相符；196 + 224 = 420。被具名的失效（静默丢列）在列级上确实被堵住了。问题全部出在判据与事实两层。

### 事实镜头的发现

| 严重度 | 断言 | 实际 |
|---|---|---|
| blocking | M-2 称 `character_avatar_detail.item_uid` 是 `items` 已有的代理键 | 它是**头像 UID**，值取自物品自己 `item_core` 偏移 5..8 的 `Core.Value`；`character_new_items.item_uid` 是 AUTOINCREMENT 行号，向领域层暴露为 `StorageEntryId`。两者连宽度都不同（`int` 对 `long`）。照原裁定实施会连错列 |
| major | 「只有 13 张表有生产写入路径」 | 14 张。漏 `dungeon_persistent_effect_outbox`（两条 UPDATE），而文档自己的逐表裁定第 53 行标 yes，内部矛盾 |
| major | 「一次选人载入读 33 张的其中 33 张」 | 33 是 `CharacterEntryAssembly` 一家的数；整个选人流程读满 53 张（51 张无条件，2 张视频道 PvP 策略） |
| major | 「金币上限今天由物品侧读取」 | 两侧都读：`CharacterEntryAssembly` 喂给金币上限规则服务，`InventoryEntryAssembly` 另读一次 |
| major | `character_new_items` 的插入路径「两条」 | 三条，第三条在 `SqliteItemStore.FillAsync`。等式在三处全部成立，结论正确但证据枚举不全 |
| major | M-4 只点出 PvP 表多一条 UNIQUE | 多四项：五列 `NOT NULL`、`CHECK (page_index BETWEEN 0 AND 1)`、级联外键、UNIQUE |
| major | 两处称某对表「在 task contract 里被列为允许的合并候选」 | 出自 `routing-decision.md`，不是契约 |
| minor | `characters` 的写入清单 | 少一条位置更新（`SqliteCharacterStore.FillPositionAsync`） |
| minor | 若干可复核位置 | `item_core` 偏移应指编解码器而非某条 insert；「三张必需的一对一行由建角色写入」应指 `CharacterRosterService`；`character_init_flags` 的插入写两列；`SqliteCharacterGoldLimitTable` 的类声明行 |

同时核实为真：53/420 的计数、逐表 Key 与 Card. 全部与声明一致、Writes 列 53 行与代码一致、六张至多一行表全部坐实、两张租借表共用读取器且领域层跨表查重、两张 legacy 表仅被空判且守卫行为一字不差、`character_pvp_skill_state` 的两列时间戳零出现、附件三列出处到达领域层、subtype-1 插入恰好 21 列、账本算术全对。

### 覆盖与推理镜头的发现

| 严重度 | 问题 |
|---|---|
| blocking | 写入者普查在文档内部就对不上（13 对 14），而「第三条判据大多无法观测」这一整个框架架在该普查上 |
| blocking | M-6 的五处 1:1 折入一条判据说明都没有，替代品是一条未经授权的形状规则「至多一行的表是列组不是表」，RQ-002 未满足 |
| blocking | `character_pvp_skill_state` 与 `account_settings` 结构同型：后者被第三条判据否决，前者被合并且第三条判据根本没被讨论——最清楚的一处倒推 |
| blocking | M-4 用「等 `mode` 列存在之后」论证合并前提（循环），且把第三条判据标为「成立」而论证反证了它；同一条「触发不同」的理由在别处两次被用来否决合并 |
| blocking | 与根契约的冲突至少三处未登记：收集箱（SC-1 / Scope）、职业专属技能页（SC-5）、`character_expert_job` 保留（SC-4）；已登记的账号侧分歧只指名 SC-6 而未指出同样违反 Scope/In |
| blocking | 红线只审了一张表。`character_item_values.list_kind`（`'cooltime'`/`'effect'` 使 `value` 含义不同）才是最像通用键值行的一张，被漏在目标形状里；`items.owner_scope` 是本裁定新造的形状，最该被辩护却没有 |
| major | `character_condition` 是「subtype-0 减去能命名的三样，剩下的都归它」——85% 可由协议分段解释，与根 SC-3 冲突 |
| major | `character_stats` 中七列语义未定，被给了结论而非按规格 Constraints 悬置 |
| major | stay-apart 表的 Fails 列多处指错判据；两行标了不属于三条判据的「content」 |
| major | M-5 违反文档自定的「推断不得单独支撑合并」 |
| major | 推断被单向约束：只禁止推断支撑合并，未禁止推断否决合并，于是 SC-5 指定要合并的一处被纯推断的写入者理由否决 |
| major | 「按件的到期」在目标形状里有三处并存，而 `items` / `character_rentals` 这对最该被问的相邻概念全文未被拿出来对判据 |

## 整改

全部 blocking 与 major 均已处理，交付物重写。要点：

- **M-2 改正。** 连接列写明为 `item_core[5..9]` 的头像 UID，并明确禁止按 `items.item_uid` 连接。账本中 `character_avatar_detail.item_uid` 由「落点」改为「退役」（其值已在物品自己的 core 里），退役列数 42 → 43，落点 182 → 181。同时补记：退役 `owner_id` / `character_id` / `item_id` 会删掉载入时三处一致性校验，论证是合并使不一致「无处可存」而非「不再检查」，并要求实施在合并后的形状上证明该论断、保留 30 字节宝珠孔校验。
- **普查改正。** 14 张有写入、39 张只读；写入者表补 `dungeon_persistent_effect_outbox` 与 `characters` 的位置更新。
- **立处置原则并逐处给判据。** 新增「一对一表」一节：其列若属于已有归宿的概念则迁入、表消失；若本身是无处可归的概念则保留——形状本身不构成解散理由。M-7 用一张表给出五处折入各自的三条判据逐条判断。据此 `character_expert_job` 与 `account_settings` 保留（同一条原则，两侧一致），并新建 `character_settings` 作为角色侧对应物。
- **消除双标。** `character_pvp_skill_state` 改为折入新的 `character_pvp`（它是 PvP 事实，PvP 这个概念有归宿），其第三条判据被明确论证；与 `account_settings` 的差别写成两处可对照的段落。
- **M-4 重写。** 第三条判据被重新表述为「关于同一行里的列」：PvE 与 PvP 行集不相交，任何用例都不会在它正在改的行里遇到另一方的列；与 `account_settings`（两者会共用一行）形成明确对照。PvP 表多出的四项保证全部列出并要求逐一存续。
- **登记全部契约冲突。** 新增一节列出 C-1..C-5：账号设置（SC-6 与 Scope/In）、`character_expert_job` 保留（SC-4）、职业技能页（SC-5，因合并需要一个重新解释载荷列的判别列而被红线排除）、收集箱（SC-1，因其载荷是模板号而非实例）、三处到期（Scope/In）。
- **红线逐表审。** 新增一节逐张检查目标形状中所有带候选判别列的表。`character_item_values` 判为**违反**并给出理由；`items.owner_scope` 给出辩护（它只决定 `owner_id` 指向哪张表，不改变其余列的含义）；`character_dimensions.dim_key` 判为未决。`character_item_values` 的修复登记为 D-11，理由是它落在本裁定所服务的四块工作之外，属排期修复而非容忍的例外。
- **`character_condition` 收窄到 43 列。** 显示开关四列迁往 `character_settings`，`is_event_character` / `is_hardcore_mode` 迁往 `characters`（创建期属性，与已在那里的 `grow_type` 同类），PvP 标记迁往 `character_pvp`；并正面回应「残渣」这一反对：所有可命名的组都已被取走，剩下的逐列回答同一个问题，两组不回答该问题的列（装备生物六列、语义未定列）被登记而非隐藏。
- **推断规则改为双向。** 键判据必须观测；推断既不能单独支撑合并，也不能单独否决合并——否决须依托观测到的判据、红线，或明确记为「因证据不足而拒绝」。据此重写 M-5（键观测 + 概念具名 + 写明何者可推翻它）、邮件附件（键判据因「必须观测」而失败，另有观测到的四列出处）、职业技能页（依托红线）、两处「证据不足」（S-8、S-9，登记为 D-9）。
- **Fails 列逐行改正**，使论证与所指判据一致；「content」不再作为判据出现。新增 S-14（`items` / `character_rentals`）并说明三处到期是三件不同的事。
- **语义未定列改为「暂定归宿」**并集中登记于 D-4，明确其归宿是暂定而非裁定。
- **事实与出处逐条订正**：33 与 53、金币上限两处读点（次序结论因此更强而非更弱）、三条插入路径、四项 PvP 约束、`routing-decision.md` 的出处、编解码器位置、`CharacterRosterService`、`character_init_flags` 插入两列。D-6 由「有条件」改为已证；`character_avatar_detail.item_id` 的等式已由写侧与读侧断言共同证明。

### 整改后的机械自校验

| 检查 | 结果 |
|---|---|
| 逐表裁定行数 / 与库形状声明的表集合 | 53 行，编号连续，无重复，集合完全相等 |
| 附录行数 / 重复源列 / 一源两落点 | 224 行，零重复，零双落点 |
| 附录 22 张源表 vs 其声明 | 逐列相等，无漏无多 |
| 不变表 ∩ 重分配表 | 不相交；31 + 22 = 53 |
| 不变表各自的列数 | 逐张与声明相符，合计 196；196 + 224 = 420 |
| 退役 / 落点 | 43 / 181 |
| 目标形状各表标题声明的列数 vs 附录去重落点 | 差恰为文档声明的 4 个无源新增列（`character_pvp.character_id`、`character_pvp.skills_initialized`、`character_settings.character_id`、`character_skills.mode`），标题合计 157 = 153 + 4 |

## 第二轮

一个新的独立上下文，逐条确认第一轮 19 项发现的整改状态，并对整改新引入的内容做新的检查，同时独立复数一遍全部数目。

判定：`changes_required`。第一轮 19 项中 17 项确认修好，2 项部分修好并引入新问题；另有 1 条 blocking、3 条 major、4 条 minor 的新发现。独立复数的全部数目与整改后的自校验一致，唯二不符的两处即下表前两行。

| 严重度 | 问题 | 依据 |
|---|---|---|
| blocking | M-1 把称号册的 `category` 直接当作 `list_type` 带过去。`category` 被校验为 0..4，而 `list_type` 存的是容器类型（`Main=0`、`Avatar=1`、`PersonalCargo=2`、`Equipment=3`、`Pet=7`）。同一角色的「主背包 0 号槽」与「称号册 category 0 的 0 号槽」会得到同一条键，直接撞 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`；红线一节据此写的「`list_type` 对三者含义相同」也因此为假 | `InventoryContainerType` 里称号册另有专属编号 `TitleBookGeneral=19`..`TitleBookEvent=23`；文档对其余需要变换之处都写了变换，唯独此处没写 |
| major | 红线一节称「其余 30 张无候选判别列」，目标形状是 42 张、已列 7 张，应为 35；且本裁定**新造**的判别列 `character_skills.mode` 未被审——目标形状自己写着 `CHECK (page_index BETWEEN 0 AND 1)` 只对 PvP 行成立 | 独立清点 |
| major | 附录把 `character_pvp_skill_state.character_id` 送去 `character_condition.character_id`，而逐表裁定第 16 行写的是「→ `character_pvp`」，两处矛盾；`character_pvp.character_id` 的「无源」注解也与其自身说法相抵。（无源列失去去向：无——224 列仍逐列唯一） | 附录与逐表裁定对读 |
| major | `character_settings` 的成员是凭列名读出来的，代码里没有支撑，且存在反向信号：四个显示开关在生产代码里只是不透明透传，与 `emotion_index`/`action_byte` 并排上线；而代码**自己命名**的设置概念 `CharacterSettingsEntryState` = 快捷键 + 选项块 + `user_state_bits`，四个显示开关一个都不在其中。`is_event_character`/`is_hardcore_mode` 迁往 `characters` 同样只是读名字——subtype-0 唯一的插入只写 `character_id`，没有任何语句写过这两列 | `CharacterModels.cs` 的 `CharacterSettingsEntryState`；`CharacterEntryStateServices.cs` 的构造处 |
| minor | D-2 与 M-7 仍写「六列装备生物」，整改把 `pet_display_flag` 移走后实为五列，目标形状小节自己写的是 five | 三处对读 |
| minor | 「七张至多一行的表：角色侧六张、账号侧一张」——账号侧还有 `account_cargo_state`（`account_id PRIMARY KEY`，逐表裁定第 5 行 Card. 也标 1），应为八张 | 声明与逐表裁定 |
| minor | D-7 只提到 30 字节宝珠孔校验须存活，同处还有两个颜色的 0..65535 范围校验 | `InventoryEntryAssembly` 的时装详情校验 |
| minor | M-6 称 `CharacterRosterService` 把 `characters`/subtype-0/subtype-1「三张必需行一起写」，被引注释的三行其实是 subtype-0/subtype-1/`character_init_flags`，`characters` 在同一方法更早处插入 | `CharacterRosterService` |

第二轮同时确认：清点层面除上述两处自相矛盾外严丝合缝——53 行裁定、224 行附录、无重复源列、22 张源表列集与声明完全相等、43 退役 / 181 落点、31 张不变表 196 列、标题合计 157。

## 第二轮的整改

- **称号册的键。** M-1 写明 `category` 不是容器类型，必须变换：`list_type = 19 + category`（`TitleBookGeneral`..`TitleBookEvent`），并说明不变换会与主背包行撞键。目标形状的 `list_type` 一行与红线对 `items` 的判词同步改写，后者改为「一旦称号册的 `category` 变换成容器类型则含义相同——不变换则不然，这正是 M-1 要求变换的原因」。
- **红线覆盖。** 「其余 30 张」改为「42 张中的其余 35 张」；新增 `character_skills.mode` 一行，判为「Clear，但只是勉强，而且是本裁定自己造的」——五个非键列在两种 mode 下含义相同，差别是一条约束而非一层含义，并写明它处在红线的近侧边缘。
- **PvP 标记的键。** 附录改为 `character_pvp_skill_state.character_id → character_pvp.character_id`；目标形状里 `character_pvp.character_id` 由「无源新增」改为有源，无源新增列因此由 4 条减为 3 条，账本改为 181 落点合并为 154 个目标列 + 3 条无源。
- **`character_settings` 依代码重建。** 成员改为代码自己命名的那三样中的一对一半：`character_option_blob` 与 `user_state_bits`；`character_hotkey_slots` 作为同概念的一对多半保留为独立表，与 `character_expert_job`/其配方同型，并新增 S-14 记录该对。四个显示开关退回 `character_condition`，`is_event_character`/`is_hardcore_mode` 退回 `character_condition`。两处读名字得出的判断登记为 D-12，明确「若被证实则移动」，由角色侧实施承接。
- **列数随之改正**：`character_settings` 6 → 3，`characters` 30 → 28，`character_condition` 43 → 48，`character_pvp` 仍 8（7 有源 + 1 无源）。
- **四条 minor 全部改正**：装备生物五列、八张至多一行表（账号侧两张）、D-7 补两个颜色范围校验、M-6 的三张必需行出处。
- **Fails 列改名为「Refused on」**，并在表前写明拒绝可依托的三种依据（观测到的判据、红线、明确记录的证据不足），使 S-4 与 S-8/S-9 的记法不再看起来像判据错标。
- **D-4 补上承接者**（角色侧实施在语义确定前承接这些暂定归宿），回应 RQ-011。

### 整改后的机械自校验

| 检查 | 结果 |
|---|---|
| 逐表裁定行数 / 与库形状声明的表集合 | 53 行，编号连续，无重复，集合完全相等 |
| 附录行数 / 重复源列 / 一源两落点 | 224 行，零重复，零双落点 |
| 附录 22 张源表 vs 其声明 | 逐列相等，无漏无多 |
| 不变表 ∩ 重分配表 | 不相交；31 + 22 = 53 |
| 不变表各自的列数 | 逐张与声明相符，合计 196；196 + 224 = 420 |
| 退役 / 落点 | 43 / 181 |
| 目标形状各表标题声明的列数 vs 附录去重落点 | 差恰为 3 个无源新增列（`character_pvp.skills_initialized`、`character_settings.character_id`、`character_skills.mode`），标题合计 157 = 154 + 3 |

## 第三轮

一个新的独立上下文，确认第二轮八项整改并复查是否引入新问题，同时第三次独立复数全部数目。

判定：`changes_required`。八项中五项确认修好，一项算术仍错，一项未修，一项修好但证据链有未登记的反向信号；另有 4 条 major、3 条 minor 的新发现。**核心发现是：第二轮的整改在事实层面对，但没有传播干净——数字、清单、分组在三处同时失真。**

| 严重度 | 问题 |
|---|---|
| major | 红线一节的收尾是一句可证伪的全称断言（「其余若干张没有候选判别列」），而至少三张目标表带候选判别列未审：`character_creatures.mode_flag`、`mailbox_attachments.item_type`/`item_kind`、`character_daily_schedule_states.mode_or_state`。同时「其余 35 张」按已列 8 行算应为 34——第二轮加了第 8 行却没改数 |
| major | `character_condition` 的「残渣」辩护段没有随 43→48 更新：仍写 43 列（与目标形状标题矛盾）；「创建期属性已迁往 `characters`」这一句在退回之后成了空话；退回来的四个显示开关加上 `emotion_index`/`action_byte` 构成**第三组**不回答该概念之问的列，而辩护只登记了两组 |
| major | 子契约 Constraints 与 SC-6 的措辞是关于**目标形状**的，而目标形状按本裁定自己的判词含一张 Violates 的表（`character_item_values`）。这是第六处对契约字面的偏离，而 C 表自称「五处全部收录」 |
| major | 两轮整改各自引入了一批承重事实，可复核位置表始终没动。至少七类新事实无可复核位置 |
| major | `character_settings` 的唯一有源判据 `user_state_bits` 实有三处读者，其中一处进公共态、一处直接上线——它同时满足用来接纳它的测试与用来否决显示开关的反向信号，这一点未被记录 |
| minor | 「六列装备生物」在两处仍未改为五列 |
| minor | C-5 引 S-14，第二轮插入新条目后应为 S-15 |
| minor | D-6 只证了写侧；该列今天仍被 `SELECT` 与 `WHERE` 读，且有一条索引指名它 |
| minor | Deferred 表条目乱序 |
| minor | M-6 同段前句仍说「三张必需行由建角色一起写」，与紧接着的更正互相打架 |
| minor | M-2 正文仍只提 30 字节宝珠孔校验，未同步两个颜色范围 |

同时确认：`19 + category` 的映射**经独立证实正确**——仓库内 `Docs/GM工具_新背包表结构与ItemCore语义.md` 有一张 `category → list_type` 映射表，0→19 general、1→20 specific、2→21 pvp、3→22 despair、4→23 event，与该式完全一致。清点层面第三次独立复核仍严丝合缝：53 行裁定、224 行附录、43 退役 / 181 落点、196 + 224 = 420、157 = 154 + 3、14 张写入者 / 8 个文件。

## 第三轮的整改

- **红线扫完。** 新增三行：`character_creatures.mode_flag` 判 Clear（它决定两列是否上线，不决定含义，与 `items` 的稀疏列同理）；`character_daily_schedule_states.mode_or_state` 判 Unresolved 并登记 D-13；`mailbox_attachments.item_type`/`item_kind` 判 Unresolved 并指回 D-1（它无写入者，该问题只能在邮件功能建成时回答）。收尾改为陈述方法而非全称断言：「目标形状中的每一张都被看过；带候选的十一张已列出，其余 31 张按其声明与读取者没有」。
- **`character_condition` 的辩护段重写。** 列数改为 48；「凡能**凭证据**命名的组都已迁走」并逐项列明迁往何处，删去已成空话的那一句；把不回答该概念之问的列由两组改为三组，第三组（四个显示开关 + `emotion_index` + `action_byte`）明写为「服务端不解释、直接上线，因此本裁定说不出它们属于发生过的事还是玩家选的」，并写明早先那次按列名的迁移已被撤回。四十八列里有六列是残渣，明说而非用一个要拉伸的概念名盖住。
- **补 C-6。** 目标形状保留一张本裁定自判违反红线的表，登记为第六处偏离，并说明为何是登记而不是靠重读句子化解；「五处」全部改为「六处」。
- **可复核位置补十行**：`category` 0..4 的校验处、称号册容器类型枚举与那张 GM 映射表、称号册 `item_core` 的 82 字节校验、`CharacterSettingsEntryState` 的定义与构造处、`user_state_bits` 的另两处读者、四个显示开关的透传落点、subtype-0 唯一插入只写 `character_id`、时装详情两个颜色范围校验、`character_creatures.mode_flag` 的上线条件、被退役列仍被读且有索引指名。
- **`user_state_bits` 的反向信号入册。** M-6 明写它是这张表唯一非凭名字入选的列，而同一证据不是单向的；登记为 D-14，并写明若它被判为公共态，本表只剩选项块一列、应重新考虑。
- **五条 minor 全改**：两处「六列」改五列、C-5 改引 S-15、D-6 补读侧与索引、Deferred 重排为 D-1..D-14、M-6 删去自相矛盾的前句、M-2 补两个颜色范围。
- **RQ-003 的口径差异明写**：十五条拒绝中十二条指名三条判据之一，其余三条分别依托红线与证据不足，并说明这比「挑一条最近的判据来背锅」更值。

### 整改后的机械自校验

| 检查 | 结果 |
|---|---|
| 逐表裁定 / 附录 / 源表列集 | 53 行；224 行，零重复、零双落点；22 张源表逐列相等 |
| 不变表 ∩ 重分配表；并集 | 不相交；31 + 22 = 53；196 + 224 = 420 |
| 退役 / 落点 | 43 / 181 |
| 目标形状标题 vs 附录去重落点 | 差恰为 3 条无源新增列，标题合计 157 = 154 + 3 |
| 红线表行数 vs 收尾所称 | 11 行；42 − 11 = 31，与文字一致 |
| 交叉引用 | 全部 M / S / C / D 编号均可解析，无悬空引用 |
| Deferred 编号 | D-1..D-14 顺序 |

## 第四轮

一个新的独立上下文，确认第三轮十一项整改是否传播干净，并对「同一事实在两处以上出现」做系统对读。

判定：`changes_required`。十一项中八项 fixed、三项 partially fixed；另有 2 条 major、3 条 minor。**数字层这一次是干净的**——评审对读了 11 张目标表的列数（标题 / 正文 / 附录）、十五类计数、四类清单、69 条交叉引用，第三轮的「同一改动只改一处」在数字上不再复现。剩下的不同步全部落在叙述与归属层。

| 严重度 | 问题 |
|---|---|
| major | 红线普查仍未扫完。收尾断言「其余 31 张按其声明与读取者没有」被三张证伪：`character_achievement_complete.achievement_id`（对 `p1..p4`，与已判 Unresolved 的 `character_dimensions` 完全同型）、`character_buy_restrict_items.entry_id`/`flag`（`sentinel_or_value` 自陈其名，领域层直接叫它 `Unknown730Entries`）、`account_settings.hotkey_key_type`（可能决定 `hotkey_slots` BLOB 怎么读）。另有三张较弱同型者（`account_cargo_state.selection_key`、`mailbox_messages.mail_type`、`character_item_locks.state`）虽可判 Clear，也应留一行 |
| major | `character_condition` 辩护段的让步规模少说了三分之二：三组已改为 5 + 5 + 6 = 16 列，收尾却仍写「Six of forty-eight」；同段把六列整体标为 D-12，而 D-12 只登记了四个显示开关与两个创建期属性，`emotion_index`/`action_byte` 全文无任何悬置登记 |
| minor | 两处可复核位置指向不存在的路径：`Game/SelectCharacter/StateSynchronizationPayloadCodec.Entry.cs` 实为 `Game/StateSynchronization/...`。所述事实在正确文件里都成立，是纯字符串错误 |
| minor | D-4 标题写「Eight columns」而其列举覆盖 12 列；`trailing_byte` 与 `black_count` 在目标形状所在行没有 D-4 标记，与同表 `champion_break_*` 那行不对称 |
| minor | M-6 的 `character_stats` 与 `character_pvp` 概念句读起来像完整枚举而实际不是；`pvp_stat_a` 全文既未解释也未登记 |

同时确认：补入的十行可复核位置**事实全部为真**（含那张 `category → list_type` 的 GM 映射表逐行与 `19 + category` 一致）；C-6 与「六处」全文无漏改；「六列装备生物」四处均为五；D-1..D-14 顺序；交叉引用零悬空。

## 第四轮的整改

- **红线扫完并复核。** 新增六行：`character_achievement_complete.achievement_id` 与 `character_buy_restrict_items.entry_id`/`flag` 判 Unresolved 并登记 D-15；`account_settings.hotkey_key_type` 判 Unresolved 并指回 D-3；`account_cargo_state.selection_key` 判 Clear 并给出结构性理由（每账号至多一行，无从跨行选择含义）；`mailbox_messages.mail_type` 与 `character_item_locks.state` 判 Clear。红线表由 11 行增至 17 行，收尾改为「其余 25 张」，17 + 25 = 42 自洽。
- **残渣计数与归属改正。** 「Six of forty-eight」改为「Sixteen of forty-eight — five, five and six」。D-12 扩写为覆盖全部六列不透明透传列（四个显示开关 + `emotion_index` + `action_byte`），并保留两处按名读法的登记；目标形状里 `emotion_index`/`action_byte` 单独成行并标 D-12。
- **D-4 改正为十三列**：七列进 `character_stats`、五列进 `character_condition`、一列（`pvp_stat_a`）进 `character_pvp`；目标形状中 `black_count`/`trailing_byte` 与 `pvp_stat_a` 各自标上 D-4。
- **M-6 两句概念句补全**，明确它们不是完整枚举，并把 `pvp_stat_a` 写进 `character_pvp` 的概念句。
- **两处失效路径改正**为 `Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs`。
- **Deferred 重排为 D-1..D-15。**

### 整改后的机械自校验

这一轮把反复出现的失效（同一数字改一处漏一处）做成了机械检查，共 15 项，全部通过：

| 检查 | 结果 |
|---|---|
| 附录无重复源列；22 张源表逐列与声明相等 | ok |
| 不变表列数与声明相符；不变 ∩ 重分配为空且并集 = 53；196 + 224 = 420 | ok |
| 目标形状标题合计 vs 附录去重落点 + 声明的无源新增列 | ok（157 = 154 + 3） |
| 账本段落的落点 / 退役 / 合并后列数三值 vs 附录 | ok（181 / 43 / 154） |
| `character_condition` 辩护段所述列数 vs 其标题 | ok（48） |
| 红线表行数 vs 收尾所称，且两者之和 = 目标表数 | ok（17 + 25 = 42） |
| 契约偏离处数：正文两处 vs C 表行数 | ok（六 / 6 行） |
| 全部 M/S/C/D 交叉引用可解析 | ok |
| Deferred 编号连续且有序 | ok（D-1..D-15） |
| 装备生物列数在全文只有一种说法 | ok（five） |
| 写入者普查两数之和 = 53 | ok（14 + 39） |
| 文档引用的每一条仓库路径确实存在 | ok |

## 第五轮

一个新的独立上下文。前四轮查的都是「对不对、全不全」，本轮换了镜头：**把这份记录交给四个互不相识的实施者，他们照它做会做出一致的东西吗？**

判定：`changes_required`。数字层这一轮**完全干净**（评审独立复数十七项全部自洽，77 处交叉引用零悬空，22 条完整路径全部存在）。问题全部落在判断层与完整性层，且这个新镜头挖出了前四轮都没看见的三件事。

| 严重度 | 问题 |
|---|---|
| major | **`character_item_values` 在记录里有两种互斥读法。** 三处说它「落在本裁定所服务的四块之外」，而 D-11 的承接人栏写的是「物品侧实施」——四块之一；同时目标形状把它列在不变表里、账本把它 5 列算作原样保留。物品侧实施者若照 D-11 拆表，42/196/420 三个数当场作废；若不拆，C-6 就永远立着。两个合理的人，两个不同的库 |
| major | **11 张改动表里只有 3 张给了键与约束，其余 8 张只给了列与来源**，全文也没有「未写明者沿用源表声明」这类约定。可空性在合并处真的会分岔（`pc_room_state` 可空并入一批 `NOT NULL DEFAULT 0`；`expert_job_type` 是 `NOT NULL DEFAULT 0` 并入一张全可空的表）。**这个缺口已经吞掉一件真东西**：`character_pvp_skill_state` 声明的 `FOREIGN KEY … ON DELETE CASCADE` 在折入 `character_pvp` 后一个字都没提，而同一次改动里 `character_pvp_skills` 的同一条外键被列为「四项保证之一，丢掉任何一条都是静默丢失」 |
| major | **存在第二处与金币上限同型的跨块耦合，记录未涉及。** `PvpSkillProfileService.LoadOrInitializeAsync` 是一个方法，同时读写 `character_pvp_skill_state`（→ 角色本体块）与 `character_pvp_skills`（→ 技能块），类注释自己写着「读它、写技能、写标记是一个序列，而序列是一个决定」。记录却说「技能与账号侧两不相干，可放任意位置」 |
| major | 红线普查**第四次**在同一处被证伪：`character_daily_challenge_entries.track_like_id`、`character_condition.champion_break_mode`、`character_expert_job.expert_job_type` 三张带候选未列，而收尾仍是全称断言 |
| major | C-1..C-6 六处契约偏离**全部没有承接人**——Deferred 表每行都有 Resolved-by 栏，C 表一行都没有。按根契约字面，SC-4、SC-5、SC-6 三条会判不合格，而记录没说谁有权重述、也没说实施要不要等 |
| minor | 五条：目标形状里几张表的 `character_id` 只列一个来源而 `items.item_uid` 列全；M-1 只写入向变换未写读回的逆变换；「The account side merges nothing」与逐表裁定第 6、7 行冲突；**账号块在本裁定下实际已无任何目标形状改动**，记录未点明；「Rows 8-11 and 16」对第 16 行已不成立 |

## 第五轮的整改

- **`character_item_values` 直接裁掉两读：拆表。** 新增一节「Where one table becomes two」（X-1），说明三条判据管的是「两张是不是一个概念」，这里是反方向：一张装了两个。`list_kind` 取 `'cooltime'`/`'effect'` 使 `value` 一为剩余冷却、一为效果量值，是本库最清楚的一处红线违反；拆成 `character_item_cooldowns` 与 `character_item_effects`，`list_kind` 退役——一行在哪张表里就说明了它原来说的事。它本就在物品块的地界内（同一块要重写锁、租借与收集箱），所以在此裁定而非推迟。C-6 随之消失，D-11 随之删除，其后各条重排为 D-11..D-14。
- **补「Keys and constraints」两节。** 一节立三条规则：列的类型/可空/默认沿用源列，多源取最严且分歧须由实施记录抉择（并点名已知的两处分歧）；源表的每一条约束都在其列的落点上存续；每一条索引在其源列的落点上重建，并点名 `idx_character_new_items_character_space` 要改建在 `owner_id` 上。另一节逐张给出 13 张改动表的键与必须携带的约束——**`character_pvp` 的级联外键就此写死**，`character_skills` 的四项保证给出可执行写法（`CHECK (mode <> 'pvp' OR page_index BETWEEN 0 AND 1)`），并注明 `NOT NULL` 与级联现在也约束 PvE 行、属收紧且无既有数据可与之矛盾。
- **补第二条次序结论。** 新增一节说明 `PvpSkillProfileService` 同时属两块，用与金币上限完全相同的论证得出同样的答案：**角色本体先，技能跟上**。并把总结改为：角色本体先行，物品侧与技能各自跟随、彼此之间任意次序，账号侧没有自己的工作。
- **红线普查改成结构上不可能再漏：逐张列出目标形状全部 43 张表，一表一行**，无候选者也写明「无」。43 行中 21 张无候选、14 张判 Clear、8 张判 Unresolved（各自登记 D-1/D-3/D-4/D-10/D-12/D-14），**没有一张判违反**。收尾不再是全称断言。
- **C 表补上承接归属**：明写重述与否由根控制器裁决，且**四块实施不应在某条被抵触的成功条件被重述之前就据其开工**。
- **账号块为空这件事写进摘要**：账号侧五张表全部不变，两张 cargo 表归物品块，根契约设想的第四块工作实际没有内容——这是控制器分解实施前需要知道的事。
- **五条 minor 全改**，含 M-1 补写读回的逆变换 `category = list_type - 19`。

### 整改后的机械自校验

16 项，全部通过：

| 检查 | 结果 |
|---|---|
| 逐表裁定 53 行，与库形状表集合完全相等 | ok |
| 附录 229 行，零重复源列；23 张源表逐列与声明相等 | ok |
| 不变表 30 张、191 列，与重分配表不相交，并集 53；191 + 229 = 420 | ok |
| 退役 / 落点 | 44 / 185 |
| 目标形状标题合计 165 = 162 有源 + 3 无源，逐表相符 | ok |
| 账本段落三值 vs 附录 | ok |
| `character_condition` 辩护段所述列数 vs 其标题 | ok（48） |
| 红线表一表一行，行数 = 目标表数；Clear/Unresolved 计数与正文相符 | ok（43 行；14 / 8） |
| 契约偏离处数：正文两处 vs C 表行数 | ok（五 / 5 行） |
| 全部 M/S/C/D/X 交叉引用可解析 | ok |
| Deferred 编号连续有序 | ok（D-1..D-14） |
| 装备生物列数全文一种说法；写入者两数之和 = 53 | ok |
| 文档引用的每一条仓库路径确实存在 | ok |

## 第六轮

一个新的独立上下文，核实第五轮六项整改，并延续「实施者能否各自写出建表语句而不回来问」的镜头。

判定：`changes_required`。六项中五项确认修好、一项部分修好。**数字层、清点层、交叉引用层完全干净**（评审从零重数十七项全对，七个受拆表影响的数字全部传播干净，旧数字全文无残留）。但第五轮新加的「键与约束」两节，在它最该管住的那张表上留了两个缺口——而这两节正是为了关掉这一类而写的。

| 严重度 | 问题 |
|---|---|
| blocking | `character_avatar_detail.jewel_socket` 声明为 `BLOB NOT NULL CHECK(length=30)` **且无默认值**。按新立的规则「列沿用源列的类型/可空/默认」，它在 `items` 上仍是 `NOT NULL` ——**任何非时装物品都插不进去**。而 M-2 与红线一节都写着这六个 `avatar_*` 是「absent-or-present」。同一份文档给出两个互斥答案，其中一个建出的表不可用。另五个 `avatar_*` 是 `NOT NULL DEFAULT 0`，照规则会变成恒为 0，也不是「absent」 |
| blocking | `character_avatar_detail` 的 `PRIMARY KEY (item_uid)` 无承接、无登记。该列退役后，「每个头像 UID 至多一行明细」这条保证在 `items` 上无人承担——而 M-2 恰恰要求按一个不再有唯一性约束的值连接。文档自己的标准是「丢掉一条约束就是静默丢失一条今天有的保证」，且对同类损失（legacy 守卫）是专门开一节登记的 |
| major | `character_skills` 的 `NOT NULL` 清单两处不一致（键表点四列、目标形状说「五个非 BLOB 列」、源表实为五列含 `character_id`、加 `mode` 后目标表非 BLOB 列是六列）。SQLite 里复合主键**不隐含 NOT NULL**，漏掉 `character_id NOT NULL` 是真松了一格；`mode` 自身的类型/可空/默认全文未定 |
| major | 三个无源新增列（`character_skills.mode`、`character_pvp.skills_initialized`、`character_settings.character_id`）没有类型/可空/默认——规则一节只覆盖「有来源的列」 |
| major | 索引规则是全称句，但六条声明里有两条落在退役表上（该句对它们为假），另两条会重建成两条完全相同的索引 |
| minor | 「item side collapses to five tables」拆表后应为六；「The four blocks of implementation」与文档自己两处「第四块没有内容」不一致；`character_daily_challenges` 与 `character_invisible_flags` 两张改动表不属于任何一块且不在根契约 Scope/In 内，未登记；`character_condition`/`character_stats` 的 `character_id` 仍各只列一个来源 |

同时确认为真：拆表决定本身有观测证据（领域层把同一批行按 `list_kind` 拆成 `Cooldowns`/`Effects` 两个具名列表）；两读已彻底消除；D-11..D-14 重排后引用全部指对；`character_pvp` 的级联外键与源声明逐字一致；`character_skills` 四项保证一一对应且写法可执行；第二条次序结论的事实为真；红线 43 行集合与目标形状完全相等，**不存在第 44 张**；账号块为空成立。评审另逐张比对了 23 张源表的全部约束，除上述两条外**其余全部追到归宿**。

## 第六轮的整改

- **补「稀疏压过继承」这条规则。** 一列若只有新表的一部分行能有它，就变为可空，无论源表怎么说；其上的 `CHECK` 改写为「不存在时也成立」。并点名理由：六个 `avatar_*` 在源表上是 `NOT NULL`，是因为那张表的每一行都是时装；原样带过会让 `items` 变成一张普通物品插不进的表，而 `avatar_jewel_socket` 连默认值都没有。**不存在必须真的是不存在。**
- **`items` 的约束写全**：`UNIQUE`、`owner_scope` 的取值 `CHECK`、`item_core` 的 82 字节 `CHECK`、时装列放宽为可空、宝珠孔的 `CHECK` 改写为 `avatar_jewel_socket IS NULL OR length(...) = 30`、`deposited_by_character_id` 可空。
- **头像主键的损失单独成节登记**，与 legacy 守卫那节同格式：说明丢的是什么、为何不补（补它要把 M-2 退役的那一列放回来）、损失的边界（服务端本身不会造出两件物品共用一个头像 UID 的情形），以及想要拿回保证的实施者该怎么做——加回该列与一条部分唯一索引，不必重开本裁定。
- **`character_skills` 的 `NOT NULL` 清单统一**为六列（`character_pvp_skills` 的五个非 BLOB 列加 `mode`），并写明 `character_id` 必须点名，因为 SQLite 的复合主键不隐含 `NOT NULL`。
- **新增「三个无源列的声明」一节**：`mode TEXT NOT NULL CHECK (mode IN ('pve','pvp'))`、`skills_initialized INTEGER NOT NULL DEFAULT 0`、`character_settings.character_id INTEGER PRIMARY KEY`，各附理由。
- **索引改为逐条给结局**，六条各一句：一条原地存续、一条改建在 `owner_id` 上、两条合成一条（且已是新主键的前缀，可直接不建）、两条随退役表一同消失。
- **新增「Beyond the contract's scope」一节**：`character_daily_challenges` 的合并与 `character_invisible_flags` 的改名不在根契约 Scope/In 内——不是抵触（那是 C-1..C-5），是没被点名。按根契约的 Requires Confirmation 登记，并写明若被驳回则该表原样不动、其余不受影响。同时把这两张指派给角色本体块。
- **三处计数与措辞改正**：物品侧六张、「三块不是四块」、两张表的 `character_id` 补列全部来源。

### 整改后的机械自校验

16 项全部通过，与第五轮同表，另加：目标形状标题 165 = 162 有源 + 3 无源；红线 43 行一表一行；契约偏离 5 处；D-1..D-14；全部引用路径存在。

## 第七轮

一个新的独立上下文，集中火力查第六轮新加的五节——前六轮有一个稳定规律：每一轮新加的内容，在它第一次被评审时都被发现不完整。

判定：`changes_required`。数字层第三次完全干净。问题**全部落在第六轮新加的内容上**，规律第七次成立。其中一条是我在派发时预判的那处，实际比预判更硬。

| 严重度 | 问题 |
|---|---|
| blocking | 稀疏规则声称「`items` 是本裁定中唯一一处」为假。`character_gold_limits.gold_carry_limit`/`.auction_gold_limit` 是 `INTEGER NOT NULL` **且无默认值**，与 `avatar_jewel_socket` 完全同型；该表今天零写入者零行，而它并入的 `character_stats` 由建角色时的 subtype-1 插入建行、只写 21 列不含这两列——**照继承规则建出的表，建角色即插入失败** |
| blocking | 文档从头到尾没说**哪些目标表需要每角色一行、由谁建这一行**。三处后果各异：`character_pvp` 的键源是稀疏表而其余六列每角色必有，行集自相矛盾；`character_settings.user_state_bits` 源声明是 `NOT NULL DEFAULT 3`（不是 0）且值直接上线，若按「无行→0」读回则**下发字节由 3 变 0，直接违反 SC-8**；`character_expert_job` 两列由「每角色必有」变成「取决于有没有行」。另有 `character_dimension_flags.flag1..4` 可空无默认，而其接口契约注释写着「无行时四个都是 0」，继承后建行会是 NULL，读侧 `GetInt32` 会炸 |
| major | 稀疏规则只改写了可空性与 CHECK，没说丢掉源列的 `DEFAULT`。五个 `avatar_*` 是 `NOT NULL DEFAULT 0`，两条规则并读的结果是「可空但默认 0」——文档自己写的「Absent must mean absent」当场落空 |
| major | `items` 漏第五条约束：`account_cargo_new_items` 的 `UNIQUE(account_id, slot_index)` 严格强于合并后的 UNIQUE（后者允许同账号同槽位不同 list_type 两行） |
| major | 头像主键损失一节的承重论证站不住。「头像 UID 来自物品自己的 core」为真但推不出「不会重复」——`item_core` 偏移 5..8 是通用 `Value` 字段，可叠加物品在此存**数量**（`StackableItemCore.Create`），全库无头像 UID 分配器、无唯一性校验 |
| major | `items` 一节没提两条读写路径直接依赖的事实：头像列的存在判别是 `item_core[0] == 8`；账号仓库里的头像**不带明细**（而角色侧有 kind 无明细即抛） |
| minor | `character_skills` 的 `NOT NULL` 清单在目标形状节仍写「五个非 BLOB 列」，与键表明列的六列冲突——第六轮声称已统一的正是这一处 |

第七轮同时确认：六条索引结局无遗漏（`IndexDeclarations` 确实只有六条）；「Beyond the contract's scope」两条判断成立（根契约 Scope/In 确实未点名这两张表）；`character_crystal_contract` 的两个默认值与读侧替代值逐值相等，是同族里唯一不需要动的一处。

## 第七轮的整改

- **稀疏一节改写为两条规则**：一条「稀疏压过继承」，明写放宽可空性的同时**丢弃源列的 DEFAULT**（并点名五个 `avatar_*`）；另一条「窄表的 `NOT NULL` 不能带进宽表」，逐一列出两处会建出不可插入的表的情形（宝珠孔、两个金币上限），以及一处同型但无危害的（次元四个标记，改为 `NOT NULL DEFAULT 0` 使「无行」与「有行」说同一件事），并说明 `character_crystal_contract` 为何什么都不用改。
- **新增「哪些目标表每角色一行、谁建这一行」一节**，逐张给出六张表的今天与之后，并单列两个易错的承重默认值：`user_state_bits` 必须默认 **3**（它的值直接上线，改成 0 就动了下发字节），`crystal_cube_grade` 必须默认 **255**。
- **`character_pvp` 的行集裁定写死**：`character_id` 改为无源新增列，理由是被折的 `character_pvp_skill_state` 只对做过 PvP 页播种的角色有行，而本表每角色有行；该表三列整体退役，其含义由 `skills_initialized` 承接。退役 44→45、落点 185→184、无源列 3→4，标题合计仍 165。
- **`items` 补两节**：一节写它携带但写不成 `CHECK` 的两条规则（存在判别 `item_core[0] == 8`；账号仓库行的头像列必须全空，而角色侧有 kind 无明细即抛，该断言须在合并形状上存续）；一节写它从仓库那条 UNIQUE 上损失的强度，说明今天仓库只有一种 list_type 故暂时无害，并给出恢复办法（一条 `WHERE owner_scope = 'account'` 的部分唯一索引），登记为 D-16。
- **头像主键改为悬置**：删去站不住的论证，代之以可核实的表述（偏移 5..8 是通用字段、可叠加物品在此存数量、全库无分配器与唯一性校验），因此该损失**不能被论证为无害**，登记为 D-15 交物品侧裁定。
- **`character_skills` 的清单在最后一处也改为六列**。

### 整改后的机械自校验

16 项全部通过。目标形状标题 165 = 161 有源 + 4 无源；退役 45 / 落点 184；红线 43 行；契约偏离 5 处；Deferred D-1..D-16；全部引用路径存在。

## 第八轮

一个新的独立上下文，集中查第七轮新加/改写的六处，并按要求把发现分成**阻塞实施（A）**与**不阻塞（B）**两级。

判定：`changes_required`，**A 级仅一条**。数字层第四次完全干净。规律第八次成立：第七轮新加的六处里五处经得起复核，唯独稀疏那一节的穷尽性再次落空。

**A-1（阻塞）**：`character_expert_job` 自有的四列 `giveup_count` / `disjoint_machine_grade` / `disjoint_machine_endurance` / `enchanter_endurance` 声明为可空无默认，与本裁定点名的次元四标记**完全同型**——今天该表可以没有行、读侧补四个 0，而本裁定把它改成每角色一行、建角色时写。照继承规则建表，建角色插入只写 `character_id`，四列即为 NULL，而读侧用 `GetInt32` 取，NULL 会抛：**一个缺行今天能活下来的载入，在有行之后反而失败**。评审同时指出它被漏掉的原因：「多源取最严」那条规则举的两个例子都不是多源列，冲突发生在目标表里相邻列之间而非同一列的多个源之间，于是这一处被错归进了「记录抉择即可」那一类。

**B 级六条**：承重默认值只点了两个，实为五个（漏 `skill_tree_index` 的 `-1`——它经映射上线，-1 → `0xFF`、0 → `0x00`，写错即改下发字节；漏 `return_user_flag` 的 1、`pc_room_id` 的 65537），且「每角色一行」表里 `characters` 一行写「unchanged」而它恰恰新增了这五个中唯一会改下发字节的那一个；`character_pvp_skill_state` 的行集裁定有三处未传播（逐表裁定第 16 行、M-7 键格、M-7 正文，仍说「两列退役」或「它的键成为 `character_pvp` 的键」）；第七轮新加的四条承重事实无可复核位置；`items` 的「写不成 CHECK 的规则」漏第三条（kind==8 时 `item_core[5..9] > 0`，正是 M-2 连接键的前提）；「多源取最严」规则与其两个例子不匹配；合并后角色主物品读取必须**排除** list_type 19..23，否则称号册行会撞进只接受 0/1/2/3/7 的容器类型解析而抛。

第八轮同时确认：六张每角色一行的表已穷尽（目标 43 张里再无第七张）；`character_pvp` 行集裁定在账本一侧全对；`items` 两条规则的表述与代码一致且 D-16 的判断成立（仓库今天只有一种 list_type，且该表无任何生产写入）；D-15 的新表述逐条为真（偏移 5..8 是通用 `Value` 字段、可叠加物品在此存数量、全库无分配器无唯一性校验）；`character_skills` 的 `NOT NULL` 清单全文只剩一种说法。13 张改动表里 12 张已可直接写出建表语句，差的正是 `character_expert_job`。

## 第八轮的整改

- **A-1 修掉**：稀疏一节由三种情形扩为四种，把 `character_expert_job` 四列与次元四标记并列，并写明它比后者更危险的地方（缺行今天能活、有行反而抛）；八列统一取 `INTEGER NOT NULL DEFAULT 0`；键表里 `character_expert_job` 一行也点明这一条。
- **「多源取最严」改写**：明说本裁定没有任何一列的多个源互相冲突，真正发生的是「一列落在与自己声明不同的邻居旁边」，而邻居的声明不构成改动该列的理由——把两个例子从「分歧」重新归类为「邻居」，并指向下面那条真正管事的规则。
- **承重默认值由两个扩为五个**，做成一张表，逐列写明默认值与「为什么写错会出事」，并把三个上线的标出来；「每角色一行」表里 `characters` 一行改写为与 `character_stats` 同格。
- **`character_pvp_skill_state` 的行集裁定传播到全部三处**：逐表裁定改「三列全部退役」；M-7 键格改为「同键成立，但它不成为 `character_pvp` 的键」；M-7 正文改为「一个布尔值落在一张总是存在的行上，三列全退」。
- **补第三条 `items` 携带的规则**（kind==8 时 UID 必须为正）。
- **补角色主物品读取须排除称号册五种 list_type**，写在 M-1 的变换那一段里。
- **补四条可复核位置**：`StackableItemCore.Create`、四张可选一对一表各自的读侧替代值、副职业四列用 `GetInt32` 读、`skill_tree_index` 的上线映射与「建角色写不到它」。

### 整改后的机械自校验

16 项全部通过。目标形状标题 165 = 161 有源 + 4 无源；退役 45 / 落点 184；红线 43 行；契约偏离 5 处；Deferred D-1..D-16；全部引用路径存在。

## 第九轮

一个新的独立上下文，按收尾判据（阻塞级清零）做确认。

判定：**`passed`。A 级为零。**

第八轮七处整改中五处完全经得起复核（稀疏第四种情形、行集裁定的五处一致、`items` 两条新规则、四条可复核位置、`character_expert_job` 现已可写出建表语句）。两处的**穷尽性断言为假**，但两处的操作性规则都还在且给出正确答案，实施者照做不会建错表、不会改下发字节、不会得到两种互不相容的结果——因此判 B：

| # | B 级发现 |
|---|---|
| B-1 | 「本裁定没有任何一列的多个源互相冲突」为假。实有九列：`character_skills` 的五列（`character_pvp_skills` 侧 `NOT NULL`、`character_skills` 侧裸 `INTEGER`）与 `items` 的四列（两张 new_items 侧 `NOT NULL` 且带 82 字节 `CHECK`，称号册侧两者皆无）。取最严的规则仍在，且九列的最终声明都已在键表里写死 |
| B-2 | 承重非零默认值列了五个，实为八个——漏 `characters` 自己的 `direction`(5)、`area_state`(3)、`level`(1)，其中前两个建角色时不写、默认值即实得值，与 `skill_tree_index` 同型 |
| B-3 | 称号册 `item_core` 的「82 字节校验」说强了：`EntryBlob.Bounded` 只在超过上限时抛，是**至多** 82 而非**恰好** 82；并入 `items` 的 `CHECK(length = 82)` 是一次收紧 |
| B-4 | 「Two rules `items` carries」标题下已有三条 |
| B-5 | `character_condition` 的建角色写入仍须显式给 `pc_room_state = 0`（该列合并后仍可空无默认，读侧 `IsDBNull` 即抛），而「每角色一行」表那一行没像 `characters`/`character_stats` 那样写明 |
| B-6 | 第八轮自身新增的五类承重事实无可复核位置（规律第九次成立） |
| B-7 | 「the eight columns the rule above gives a default to」实为十列 |

第九轮同时第五次从零复算机械层，全部与文档相符；并确认 **13 张改动表全部可直接写出建表语句**。RQ-010 由不满足升为满足（行集裁定五处一致，机械复核无一表两归属、无一列两落点）。

## 第九轮的整改

七条 B 级全部改掉——它们都是「某句话说得比事实强」，改起来短，且这份记录要做三块实施的设计权威，不该留已知的假陈述：

- **B-1**：改为「九列的多个源互相冲突，且九列的最终声明都已逐条写死而非交给规则」，并把两个原本被误当作「分歧」的例子重新归类为「邻居」——邻居的声明不构成改动该列的理由。
- **B-2**：承重默认值表由五行扩为八行，补 `characters` 的三列，并把「其余默认为零」改为准确表述。
- **B-3**：M-1 与可复核位置两处都改为「以 82 为上限而非恰好 82」，并写明并入 `items` 是一次收紧、而该表零写入者故无行可违反。
- **B-4**：标题与引言改为三条。
- **B-5**：「每角色一行」表里 `character_condition` 一行补写「仍须显式给 `pc_room_state`」。
- **B-6**：补六条可复核位置（`return_user_flag` 上线与 `pc_room_id` 的范围校验、角色主读取无 list_type 过滤与容器类型解析只收五种、非正头像 UID 的拒绝、仓库头像不带明细而角色侧有 kind 无明细即抛、`pc_room_state` 为 NULL 即抛、次元四标记的「无行读四个 0」及其正确文件）。
- **B-7**：八列改十列并逐组点名。

### 整改后的机械自校验

16 项全部通过（与第八轮同表）。文档 1392 行。

## 第十轮

一个新的独立上下文，范围收窄到只看第九轮那七条 B 级整改改对了没有、有没有改坏。

判定：**`passed`。A 级为零。**

七条中六条完全经得起复核（B-2 的三个默认值与「建角色不写 `direction`/`area_state`」逐条属实且八个已穷尽；B-3 两处都改了且新表述属实；B-4 标题引言正文一致；B-5 该列确为可空无默认、读侧 NULL 即抛；B-6 六条位置逐条到代码核过全部指对；B-7 十列算术对，且与稀疏一节的「八列」不矛盾——后者只指那两组 4+4，金币上限在自己那一条里已单独给过）。B-1 的「九列」经逐列比对**属实且穷尽**。

三条新引入的 B 级（都是归纳句说得比事实强，操作性条目本身正确）：

| # | 问题 |
|---|---|
| N-1 | 「九列都已写死」对 `items` 的 `owner_id`/`list_type`/`slot_index` 不成立——这三列的最终可空性全文未写出，只能靠「取最严」推出（推出的结果正确）。另「the title book is neither」对 `item_core` 为假：称号册的该列是 `BLOB NOT NULL`，只是没有 82 字节 `CHECK` |
| N-2 | 「三个上线」少算——`characters.level` 与 `.direction` 也上线，实为五个；且「三个在 `characters` 上、全靠建角色不写它们才留住」与表里 `level` 那一行（「Named by the creation write」）自相矛盾 |
| N-3 | 次元四标记那条可复核位置把接口契约算在读取者所在文件名下，该接口实际在 `Abstractions/Persistence/CharacterEntryTables.cs` |

## 第十轮的整改

三条各是一句话，全部改掉：

- **N-1**：改为「五列是 `character_skills` 的、四列是 `items` 的」，逐组说清分歧在哪；把 `items` 三列的最终声明**写进键表**（`owner_id`、`list_type`、`slot_index` 取最严为 `NOT NULL`），使「已写死」由自述变成事实；并订正称号册 `item_core` 是 `NOT NULL`、缺的只是 CHECK。
- **N-2**：「三个上线」改为「五个」（已核实：`level` 在 `StateSynchronizationPayloadCodec.Entry.cs` 两处上线，`direction` 经 spawn 进城镇投影与副本选择三处上线）；引言改为「八个里有两个是靠建角色不写才成为实得值」，并把表里三行各自的实际情形写清——`direction` 上线且不被写、`level` 上线且被写、`area_state` 今天全库无读者而每次位置更新都被写成 3，列它只为防止声明被悄悄改掉。
- **N-3**：位置改为指向接口所在文件，并说明同一承诺写在两处。

三条改动的每一条事实我都另行到代码核实过（`characters` 三列的默认值、建角色插入的列清单、`level` 与 `direction` 的上线点、称号册 `item_core` 的 `NOT NULL`、接口文件位置）。16 项机械自校验仍全部通过。

**说明**：这三处是在第二次 `passed` 之后做的整改，未再单起一轮评审。理由是它们全部是句子层的事实订正、每一条都已独立到代码核实、且不触及任何裁定或归属；再起一轮的边际价值不足以抵它的成本。这一判断记在此处以备复核。

## 评审结论

`passed`，无未解决的阻塞性发现。十轮评审共发现并修正：两条会连错列或撞键的硬伤、两条会建出不可用表的声明缺口、一条会改变下发字节的默认值、一处跨块耦合遗漏、一张自相矛盾的表处置，以及数十处事实与传播错误。交付物可进入新鲜验证。
