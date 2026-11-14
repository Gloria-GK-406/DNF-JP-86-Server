# 70 版 df_game_r 礼包解析与开启参照

- 目标文件：`E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r`（Linux x86 ELF，带符号）
- 工具：Swift 6.1.2 工具链的 `llvm-objdump` / `llvm-nm`，加上一个把 rodata 字符串标到反汇编里的小脚本
- 对照 PVF：`E:\Project\DNF\Research\tw70-pvf-archive\Script.20130704.pvf`（Garena TW 2013-07-04），用同目录的 `read_legacy_pvf.py` 只读解出
- 中间产物：`C:\Users\Gloria\AppData\Local\Temp\claude\3576ed8239f617e2\7af18ebb-4b79-43c5-9f4b-4bb7b6b68904\scratchpad\70re\`（`import_stackable.asm`、`usebooster.asm`、`handler.asm`、`cerashop.asm`、`pkg.asm`、`addl.asm`、`pvf\` 等）
- 适用范围：70 和 A21 不是同一个版本，本文只作参照，不能直接当 A21 的事实用。
- 标注约定：**【反汇编确认】** 表示控制流或数据流在反汇编里逐条读过；**【PVF 统计】** 表示从 70 PVF 数出来的；**【推测】** 表示由命名或上下文推断，没有逐条核对。

## 0. 总览：70 里礼包的三条加载和开启路径

### 0.1 加载（PVF → 内存）【反汇编确认】

- `ImportStackableScript(STStackableScript*, char const*, bool, int)` @ `0x089aeec8`（约 52 KB）负责解析所有 stackable 标签。
- `[stackable type]` 的映射表在 0x089b0bc9 起的 if-链里，结果写入 `script+0x1e0`，标签后面跟的那个整数写入 `script+0xa4`（sub type）：
  `[cera package]=14(0xe)`、`[cera booster]=15(0xf)`、`[booster]=16(0x10)`、`[booster random]=17(0x11)`、`[booster selection]=34(0x22)`、`[usable cera package]=38(0x26)`；
  其余：`[waste]=0 [material]=1 [material expert job]=2 [recipe]=3 [throw]=4 [set]=5 [legacy]=6 [stackable legacy]=7 [quest]=8 [creature]=9 [feed]=10 [etc]=11 [only effect]=12 [upgradable legacy]=13 [unlimited waste]=18 [quest receive]=19 [avatar emblem]=20 [unlimited etc]=21 [disguise]=22 [global effect]=23 [expert town potion]=24 [contract]=25 [multi upgradable legacy]=26 [random upgradable legacy]=26 [multi upgradable legacy bonus cera]=27 [enchant waste]=28 [upgrade limit cube]=29 [tarot card lottery]=30 [tarot card]=31 [town and dungeon]=32 [unlimited town and dungeon]=33 [creature expitem]=35 [dye]=36 [teleport potion]=37`。
- `CStackableItem::set_item(STStackableScript&)` @ `0x0850d81e` 把脚本字段拷到 CItem 上：
  - type 14 或 38 时：`script+0x268`（`[package data]`）→ `item+0x21c`；`script+0x274`（`[package data selection]`）→ `item+0x234`；`[secret add item]` 非空时 → `item+0x228`。
  - type 15 或 16 时：`script+0x280`（`stStackableBooster_t`：booster 元素 vector + 自选类别 map）→ `item+0x240`。
  - **type 17（`[booster random]`）：拷 booster 数据之后，把类型强制改成 16**（`0x0850dcf8: mov [eax+0x2b0], 0x10`）。所以在 70 里 `[booster random]` 就是 `[booster]`。
  - type 34 时：拷 booster 数据，并把 `BoosterSelectInfo`（`script+0x41c`）拷到 `item+0x264`。自选数量 `[booster selection num]` 最终落在 `item+0x26c`。
- `CStackableItem::GetItemType()`（vtable+0xc）返回 `item+0x2b0`，也就是上面的 stackable type。

### 0.2 开启路径【反汇编确认】

| PVF 类型 | 70 的开启入口 | 处理函数 |
|---|---|---|
| `[booster]`(16)、`[booster random]`(17→16) | 背包使用，C→S **0xA3 (163)** `Dispatcher_UseBoosterItem::dispatch_sig` @ `0x08208d40` | `SpecialItemHandler::respond_special_item_booster` @ `0x0827ca4a` |
| `[booster selection]`(34) | 同上，0xA3 带类别和物品列表 | `respond_special_item_booster_selection` @ `0x0827ccfc` → `CCeraShop::_processBoosterSelect` @ `0x08327894` |
| `[usable cera package]`(38) | 背包使用，C→S **0x207 (519)** `Dispatcher_CeraPackageOpen` @ `0x081e76fa`/`0x081e788a` | `respond_special_item_package` @ `0x0827c204`（handler 5） |
| `[cera package]`(14)、`[cera booster]`(15) | **不能从背包开**：商城购买结果（`CCeraShop::ProcessIPGUserData`）、邮件包裹（`CMailBoxHelper::_DoGetPackageItem`）里经 `ProcessSpecialItem` 即时展开；作为别的礼包的奖励出现时也会被递归展开 | package / booster handler |

- `Dispatcher_UseBoosterItem` 只接受 GetItemType 为 0x10 或 0x22 的物品（`0x08208e77`/`0x08208e8e`），所以 `[cera booster]`(15) 用 0xA3 开会报错。
- `CCeraShop::IsSpecialItem` @ `0x08324a1e` 的默认分支（`0x0832576b` 起）：type 0x26 → 返回 false（不是特殊物品）；0xe → special 5（package）；0xf/0x10 → 6（booster）；0x22 → 12（booster selection）。
- `SpecialItemHandler` 构造函数 @ `0x0827bcec` 建的 handler 表：1 cargo、2 premium、5 package、6 booster、7 cargo_abs、8 角色栏扩展、9 skill confirm、10 skill tree style、11 背包扩展、12 booster selection、13 cerapoint giftcard。

### 0.3 所有奖励最终走同一个发放函数【反汇编确认】

`WongWork::CCeraShop::_processAdditionalItem(CUser*, ulong id, ushort count, uchar a, uchar b, vector<Inven_Item>& out, stCeraShopItemParam_t&, uchar upgrade)` @ `0x083267a2`：

1. `id == 1`（70 PVF 里 `stackable/coin.stk`）→ `_processCoin(count)`：`CInventory::SetPayCoin(GetPayCoin()+count)`、`SendCoin`、记 `PayCoinAdd` 日志，不发物品。
2. `find_item(id)` 为空 → 静默跳过。
3. 物品本身是 stackable 且类型为 16 或 34（嵌套的 booster / 自选礼包）→ `make_item` 后直接 `insertItemIntoInventory`（reason 0xf），背包满就用 `ReqDBSendNewMailCashShop` 发邮件。**不会自动展开。**
4. 其余物品：stackable 按 1 次插入（count 当堆叠数）；非 stackable（装备、时装）**循环 count 次，一件一件发**。`upgrade≠0` 时 `SetUpgrade(upgrade)`，另有 5 个硬编码 ID（0x71db/0x72d0/0x742d/0x7556/0x7661）强制 +12；之后调用 `autoUnsealItem`。
   - `IsAvatarItem()`（vtable+0x10）为真 → 时装分支，见第 6 节。
   - `IsCreatureItem()`（vtable+0x14）为真 → `_processCreature`，见第 8 节。
   - 其他 → `_processItem` @ `0x08325b22`：**先递归调用 `ProcessSpecialItem`**。如果它是特殊物品（硬编码的服务道具、premium 表里的物品、`[cera package]`/`[cera booster]`），就直接生效或展开，不进背包；否则 `insertItemIntoInventory`（reason 0xf），背包满就发邮件。
5. 被处理物品是 stackable 且 `_isPackageItem` 为真时，不写进回包列表（`0x083273f5`）。

**这条发放链上没有任何地方读取 `[attach type]`，也没有设置交易限制**（例外只有 stackable 的 `make_item` 自带的 trade limit，见第 9 节）。

---

## 1. Package 类里的 `[package data selection]`

**70 支持，但只在商城购买路径生效。【反汇编确认 + PVF 统计】**

- 解析（`0x089b3ef8` 起）：每遇到一个 `[package data selection]` 标签，就把其中连续的 `(itemId, count)` 整数对读成一个 vector，push 到 `vector<vector<STStackablePackageData>>`（`script+0x274`）。**一个标签就是一个"选择组"**，多个标签就是多个组。`[package data]` 的格式相同（`(id,count)` 对），存到 `script+0x268`。
- 发放（`respond_special_item_package` @ `0x0827c204`）：
  - 先复制 `item+0x21c`（`[package data]`）作为基础列表。
  - 读 `param+0xc` 处的 `vector<tagPackageItemSelection>`，每个元素是 `{int packageItemId; u16 groupIdx; u16 choiceIdx}`（8 字节）。
    - 元素多于 1 个时，只逐条打日志 `kdd372418Log::Package Selection item error`，**不拒绝**（`0x0827c3e2`）。
    - 对 `packageItemId == 本包 ID` 的每个选择：如果 `groupIdx >= 组数` 或 `choiceIdx >= 该组大小`，打日志 `Package Selection error` 并**停止处理后续选择，但基础物品照发**（`0x0827c54c`–`0x0827c650`）；合法时把 `group[groupIdx][choiceIdx]` 追加到发放列表。
  - 没有提供选择时，选择组被静默忽略，只发基础物品。
- 选择从哪里来：`tagPackageItemSelection` 只在 `CCeraShop::ProcessIPGUserData`（`0x08322108`，商城购买的 IPG 结果）和 `ItemVendingMachine::ProcessIPG_ResultOutput` 里被 push。**背包开包的 0x207 请求没有这个字段**（见下），所以从背包打开 `[usable cera package]` 时无法选。
- 【PVF 统计】70 PVF 里 381 个 `[package data selection]` **全部**出现在 `[cera package]`（商城包，购买时展开），`[usable cera package]` 中是 0 个。A21 把它用到了可使用礼包上（1140 个），这是 70 没有的组合，A21 的客户端应当有带选择字段的新请求或新字段。
- `[secret add item]`（32 个，只出现在 `[cera package]`）：解析成 `(id,count)` 对，存到 `item+0x228`；本次没追到使用点。

## 2. `[booster info]` 里的 `[charactor]` 段

**70 不存在。【反汇编确认】**

- `.rodata` 里没有 `charactor` 这个字符串（`[character]` 等其他拼写有，但都不在 stackable 解析里）。
- `[booster info]` 的子标签只能经 `stStackableBooster_t::tag2Type` @ `0x089c2832` 识别：`[avatar]=0 [special avatar]=1 [cera]=2 [creature]=3 [equipment]=4 [stackable]=5 [etc]=6 [special creature]=7 [emblem]=8`，其他一律返回 9。
- 在 `[booster info]` 解析里，tag2Type 返回 9 会走失败出口（`0x089b40ea` → `0x089b434a` → `0x089b439a` → `0x089ba7f3`，返回值 0），**整个 stackable 导入失败**。所以 70 的 PVF 不可能有 `[charactor]`，这是 A21 新增的写法。70 的 `getBoosterItem` 也没有任何按职业筛选的逻辑（见第 10 节）。

## 3. `[booster selection num]` = 0 且有多个奖励

**70 支持，语义是"所选类别里的物品整类全给"。【反汇编确认 + PVF 统计】**

- 解析（`0x089b4b06`）：一个整数，存到 `script+0x424`，最终落在 `item+0x26c`。【PVF 统计】65 个自选礼包中：0 有 17 个、1 有 45 个、2 有 2 个、3 有 1 个。
- 开启请求（`Dispatcher_UseBoosterItem`，物品类型 34）：
  1. 读 `short slot`、`byte cat1`、`byte cat2`（`0x08208fbb`/`0x08208fff`）。
  2. `n = item+0x26c`。**n 为 0 时**，在类别 map `item+0x24c` 里查 `(cat1,cat2)`：找到但 vector 为空 → 报错 `selectionBooster empty`；找到 → `n = 该类别 vector 的大小`；没找到 → n 仍是 0（`0x08209044`–`0x08209195`）。
  3. 循环读 `int itemId`，读 `min(n, 20)` 个（`0x0820921c`：`i < n && i <= 19`），放进 `vector<Inven_Item>`。
- 发放（`_processBoosterSelect` @ `0x08327894`）：
  - 用 `(cat1,cat2)` 查 map，找不到就返回失败。
  - 对客户端给的每个 ID，**遍历类别里的所有条目，ID 相同就发**（`0x08327976`–`0x08327a02`）。
  - 结论：n=0 时客户端要把整个类别的 ID 全部回传，服务端逐个匹配后全发。
- 副作用（70 的行为，仅供参考）：`Dispatcher_UseBoosterItem` 在 `0x08209484` 先删掉礼包，**之后**才调用 `ProcessSpecialItem`（`0x08209595`）。类别不存在等失败时，礼包已经被扣掉了。

## 4. `[booster selection]` 选择段里的各类标签

`[booster select category]` 的解析在 `0x089b43c7`–`0x089b4990`，存成 `map<pair<char,char>, vector<stStackableBoosterItemInfo_t>>`（`script+0x28c` → `item+0x24c`）。【反汇编确认】

- 段头两个整数 `(x, y)` 是 map 键 `pair<char,char>`，与客户端请求的 `(cat1, cat2)` 对应。【PVF】常见 `0 0`、`1 0`…（一维）或 `0 0`、`0 1`…（二维）。
- 段内子标签逐个处理：

| 标签 | 70 是否存在 | 解析和语义 |
|---|---|---|
| `[booster equipment upgrade]` | 有 | 一个整数存到局部变量，作用于**本段之后出现的 `[equipment]` 条目**，写入条目的 upgrade 字节（+0xa）；发放时作为 `_processAdditionalItem` 的 `upgrade` 参数，执行 `SetUpgrade(n)`（`0x08327a4d`、`0x08326d88`）。只有 tag2Type==4（equipment）的条目会带上，其他类型为 0。【确认】 |
| `[default select]` | 有 | `count` 后跟 `count` 个 ID；条目 ID 在列表里时置 `isDefault=1`（条目 +0xb）。**服务端发放不读这个字段**，推测只给客户端 UI 用。【解析确认；"仅 UI"为推测】 |
| `[recommend]` | 有 | 格式同上，置 `isRecommend`（+0xc），服务端不读。 |
| `[stackable]`、`[cera]`、`[avatar]`、`[special avatar]`、`[equipment]`、`[etc]`、`[creature]`、`[special creature]`、`[emblem]` | 有 | 都经 tag2Type 识别。条目格式：一般是 `(id, count)`；`[avatar]`/`[special avatar]` 是 `(id, count, ext1, ext2)`（**没有权重**）。发放统一走 `_processAdditionalItem`，ID=1 时发 PayCoin。【确认】PVF 里有 `[stackable] 2660452 8` 这样的实例。 |
| `[action usable place]` | 有，但**不属于选择段** | 这是顶层 stackable 标签（`0x089b2615`），列出 `[village]`/`[seria room]` 等，存到 `script+0x1f8`。【PVF】82 个文件里它都在顶层。如果本仓把它当成选择段里的内容，那是范围判断错了。另外 `Dispatcher_UseBoosterItem` 不调用 `verifyUsablePlace`，只要求 `get_state()==3`（物品 2675649 例外）。 |
| `[booster category num]` | 有，但**不属于选择段** | 顶层标签（`0x089b49b7`）：`N` 后跟 N 个整数，N 取 0、1、2，分别存到 `+0x41c`/`+0x420`；N>2 时整行静默忽略。【PVF】`1 6`、`2 7 4` 这类。服务端发放不读这两个值，只按 map 键查找；推测是客户端 UI 的行列数。 |
| `[booster category name]` | 有 | 顶层标签，字符串列表，存到 `+0x428`，客户端 UI 用（推测）。 |
| `[booster equipment grade]`、`[booster equipment separate]`、`[random probability]` | **不存在** | `.rodata` 里没有这些字符串，是 A21 新增的。如果按 70 的代码去解析，选择段里的未知标签 tag2Type 返回 9 但**不会报错**（选择段解析没有检查 9），后面的整数会被当作 `(id,count)` 条目吃掉，只有客户端请求到那个 ID 才会被发出。【推测：由控制流推出，没有实测】 |

## 5. 自选礼包不同类别间重复物品 ID

**70 不检查，也不需要检查。【反汇编确认】**

- `_processBoosterSelect` 只查客户端指定的那个 `(cat1,cat2)` 类别，别的类别里有没有同一个 ID 无关紧要。
- 同一类别内：客户端给的每个 ID 会匹配该类别里**所有**相同 ID 的条目，都发出去。客户端重复发送同一个 ID 时也会重复发放（没有去重，也没有校验与 selection num 一致）。服务端唯一的约束是最多读 20 个 ID。
- 【PVF 统计】70 的多类别礼包里，不同类别共用同一个 ID 很常见（例如 8387 每个类别都有 `[stackable] 2660452 8`，10000220 每个类别都有 `[etc] 10000219 1`）。

## 6. `[avatar]`/`[special avatar]` 5 元组的扩展字段

**格式：`(物品, 权重, 数量, ext1, ext2)`，两个扩展字段都存成 u8。【反汇编确认】**

- 解析（`0x089b4264`–`0x089b42dd`）：只有 tag2Type 为 0 或 1 时才多读两个整数，分别存到 `stStackableBoosterItemInfo_t` 的 +0xd（ext1）和 +0xe（ext2）。条目结构（16 字节）：`{int id; int cumWeight; u16 count; u8 upgrade(+0xa); u8 isDefault(+0xb); u8 isRecommend(+0xc); u8 ext1(+0xd); u8 ext2(+0xe)}`。-1 会截断成 255。
- 发放（booster handler `0x0827cb9a`–`0x0827cc07`）：调用 `_processAdditionalItem(id, count, a=ext2, b=ext1, …)`。在时装分支（`0x08326e4a`–`0x08326fbc`）里：
  - **ext1 是 `[avatar type select]` 的行下标，不是天数。**
    - `CEquipItem::getAvatarPeriod(uchar idx)` @ `0x08151120`：`idx < typeSelect.size()` 时取 `typeSelect[idx].period`，否则取时装自己的 `[usable period]`（没有这个字段就是 0，即永久）。
    - 同一个 ext1 还会以有符号形式传给 `CEquipItem::getAvatarSocket(int idx, emblem&)` @ `0x08150f36`，决定徽章孔布局；`-1` 表示取第一个孔数大于 0 的行。
    - 期限交给 `CAvatarItemMgr::RegistNotAssignedAvatarItem(id, period, …)`。
    - 背包满改发邮件时，`expire = now + period*86400`，**period 为 0 或 365 时视为永久**（`0x08326f04`/`0x08326f0a`）。
  - **ext2 是时装属性下标**：作为 `CInventory::AddAvatarItem` 的第 4 个参数（char），写入 `Inven_Item +0xb`（word，符号扩展，`0x08509d00`）。推测对应时装的 `[avatar select ability]` 第几条（按名字和数据推断）。
  - **两个字段都与交易限制无关**，70 的发放链上没有任何交易锁。
- 【PVF 统计 + 关联】70 PVF 的 booster info 时装条目里，ext 组合与 A21 观察到的一致：
  - `(-1,0)` 130 次：时装带 typeSelect 表，255 越界，取 `[usable period]`。
  - `(0, 1..38)` 约 720 次：幸运袋里带 `[avatar select ability]` 的时装，ext1=0 取第 0 行（7 天），ext2 选属性。
  - `(7/8/10/15/18/30/45, 0)`：这些时装大多**没有 typeSelect 表**，下标越界后回落到 `[usable period]`，而它们的 `[usable period]` 往往就等于这个数（例如 55489 的 usable period=15、ext1=15；39603 的 usable period=30、ext1=30）。也就是说，策划写的是天数，70 服务端当下标用，两者碰巧一致。
  - `(3,6)`、`(3,50)`：带 typeSelect 表（3 行：7 天 / 30 天 / 永久），3 越界，回落到 `[usable period]`（缺省时为永久）。
- typeSelect 表的行格式【PVF】是 5 个整数一行，第 1 个是期限天数（例如 `7 0 0 700 0 | 30 0 0 1400 0 | 0 0 0 …`）。从结构体偏移 +0x10 推测第 5 个是孔数。
- 对本仓的影响：把 10007316 的 `(3,0)` 解释成"不可交易"，**在 70 里没有依据**。在 70 中它的含义是"typeSelect 第 3 行（不存在就用 usable period），属性下标 0"。A21 是否改了语义需要另外核实。

## 7. 奖励是服务类道具

**70 不拒绝，而是"即时生效或原样入包"。【反汇编确认；ID 含义为推测】**

- 非 booster 类奖励都走 `_processItem` → `ProcessSpecialItem(id)` @ `0x0832587e`：
  1. 先查 premium 表 `map<ulong, STPremiumItemData>`（全局 `0x948b198`），命中按 special 2（premium）处理。
  2. 否则调用 `IsSpecialItem`，其中有一张**硬编码物品 ID 表**（已提取到中间产物 `isspecial_table.txt`），例如 5/6/68/110/2670009 → cargo(1)；50 → cargo_abs(7)；2660239 → 角色栏扩展(8)；821 → skill tree(10)；690000221…225 → cerapoint 礼券(13)；其余大部分 → premium(2, 下标)。还有按类型判断的 `[cera package]`、`[cera booster]`、`[booster]`、`[booster selection]`。
  3. 命中就调用对应 handler 直接生效（扩背包、扩仓库、开 premium、加点数），**不放进背包**；没命中就作为普通物品入包或发邮件。
- 嵌套情况：`[cera package]`/`[cera booster]` 作为奖励时会被递归展开；`[booster]`/`[booster selection]` 在 `_processAdditionalItem` 第 3 步就被截住，作为物品入包；`[usable cera package]` 不算特殊物品，作为物品入包。
- 硬编码 ID 是 70 的物品编号，不能直接搬到 A21。可以借鉴的是机制：服务类奖励即时生效，否则作为普通物品给出。

## 8. 奖励是 `[stackable type] [creature]` 的宠物消耗品

**70 支持，走宠物专用插入路径。【反汇编确认】**

- `CStackableItem::IsCreatureItem()` @ `0x08514a9c` 在 type 为 9（`[creature]`）或 10（`[feed]`）时返回真，所以 `_processAdditionalItem` 会进入 `_processCreature` @ `0x08325d66`：
  1. `user_creature::CCreatureMgr::CheckItem(user, item, bool&)`。
  2. 如果 `Inven_Item` 类型字节为 5（推测是宠物蛋），先 `CCreatureItemConverter::SetEggItem`。
  3. `insertItemIntoInventory`（reason 0x19）。失败时：类型 5 发 `ReqDBSendNewCreatureMail`，其他发 `ReqDBSendNewMailCashShop`。
  4. 成功后 `CCreatureMgr::InsertCreatureItem(...)`，然后 `SendUpdateItemList(…, 7, slot)`（第 7 个物品空间，推测是宠物栏）。
- 70 不拒绝宠物换名卡这类 `[creature]` 消耗品，它们会进宠物相关的物品空间。

## 9. 奖励物品 `[attach type]` 异常

**70 的发放链不检查 attach type。【反汇编确认】**

- stackable 的 attach type 映射（`ImportStackableScript` `0x089b03c4` 起）：`[free]=0 [trade]=1 [trade delete]=2 [sealing]=3 [account]=5 [trade limit]=7`。装备另有 `[sealing trade]`（字符串在 `0x08e16219`）。不在表里的写法不赋值，保留默认。
- `_processAdditionalItem`、`_processItem`、`respond_special_item_*` 里**都没有调用 `CItem::GetAttachType`**（交叉引用见上方函数列表；调用方都是交易、仓库、个人商店、分解等）。
- 唯一与 attach type 相关的发放行为在 `CStackableItem::make_item` @ `0x0850f8b8`：`GetAttachType()==7`（trade limit）时设置 `Inven_Item::SetTradeLimitCount(getTradeLimitCount())`。同一个函数还按 `[usable period]` / `[expiration date]` 设置到期时间（+0xb）。
- 结论：物品绑定属性由物品自身定义决定，礼包只负责发放，**不会因为 attach type 拒绝开启**。

## 10. `[booster info]` 的 `[equipment]`/`[etc]` 池格式

**格式与抽取规则都已确认。【反汇编确认 + PVF 统计】**

- 解析（`0x089b401a`–`0x089b4398`）：
  - 子标签（tag2Type 0–8）后紧跟 **1 个整数 N**，存到元素 +4，表示"本元素抽 N 次"。
  - 之后是条目列表，直到对应的结束标签（`tag2EndType` @ `0x089c295a`，开始和结束标签必须匹配，否则整个导入失败）：
    - `[avatar]`/`[special avatar]`：`(id, weight, count, ext1, ext2)`
    - 其余（`[equipment]`/`[etc]`/`[stackable]`/`[cera]`/`[creature]`/`[special creature]`/`[emblem]`）：`(id, weight, count)`
  - weight 在解析时就累加成前缀和存进条目（+4）；元素 +8 存总权重。
  - 元素结构：`{type@0, N@4, totalWeight@8, vector<item>@0xc}`。
- 【PVF 统计】N 的取值：etc 2990×1、14×2、1×3；avatar 341×1、18×2、8×3；stackable 310×1、5×2。count 字段 etc 常见 1/10/5/3/2…。
- 抽取（`CStackableItem::getBoosterItem` @ `0x0850f75e`）：对 `item+0x240` 的**每个元素**各自独立：重复 N 次，`r = get_rand_int(totalWeight)`，取第一个满足 `r < cumWeight` 的条目（放回抽样）。
  - **所有元素都会出货**，元素之间不互斥。
  - **没有按职业、等级、类型筛选**。元素类型只决定条目格式，发放方式只看物品本身（时装、宠物、特殊物品、普通物品）。
- 发放（`respond_special_item_booster`）：对每个抽中条目，先 `set_parent_booster_item_id(礼包ID)`（vtable+0x60），再 `_processAdditionalItem(id, count, ext2, ext1, …, upgrade=0)`，最后 `SaveInventory`。
- `[cera]` 不是"点券专用池"：条目的 ID 可以是任意物品，只有 ID=1（coin）会转成 PayCoin。【PVF】`[cera]` 里最常见的 ID 是 1、8、1085、50 等。

## 11. 70 支持的其他开箱类 stackable 类型

| 类型 | 70 入口 | 说明 |
|---|---|---|
| `[booster]` 16 / `[booster random]` 17 | 0xA3 `Dispatcher_UseBoosterItem` | 17 在 set_item 里被改写成 16，完全等价。【确认】 |
| `[booster selection]` 34 | 0xA3 | 见第 3–5 节。 |
| `[usable cera package]` 38 | 0x207 `Dispatcher_CeraPackageOpen` | 只发 `[package data]`，不支持选择组。【确认】 |
| `[cera package]` 14 / `[cera booster]` 15 | 商城购买、IPG、邮件包裹时即时展开；作为奖励时递归展开 | 背包里不能开。【确认】 |
| `[legacy]` 6 | `DisPatcher_Lottery` → `CUser::lottery_item` @ `0x086733a2` | set_item 中 `createLotteryInfo`。【入口确认；内容未展开】 |
| `[stackable legacy]` 7、`[upgradable legacy]` 13 | 同上 | `createStackableLotteryInfo`。【同上】 |
| `[multi upgradable legacy]` 26、`[multi upgradable legacy bonus cera]` 27 | `DisPatcher_MultiBoxLottery` @ `0x0820b802` → `CUser::multibox_lottery_item` @ `0x08674968` | `createStackableMultiBoxLotteryInfo`；`multibox_lottery_item` 里也会调用 `ProcessSpecialItem`。【入口确认】 |
| `[upgrade limit cube]` 29 | `stLimitCubeResult` 表（set_item `0x0850dd8f`） | 未展开。 |
| `[tarot card lottery]` 30 | 有类型；入口未追 | 【推测】 |
| 其他会复用 `getBoosterItem` 的场景 | `Dispatcher_DisJointAvatar::_getResultItems`、`Dispatcher_CompoundEmblem`、`Dispatcher_Compound_Equipment_Upgrade_Card`、`EventClassify::CEventActionMng::getResultItemFromLottery`、GM 指令 `CBoosterTest` | 说明 booster info 这套权重池格式在 70 里是通用的随机池格式。 |

---

## 附：0xA3 / 0x207 协议形状（70）【反汇编确认】

- **C→S 0xA3**（`Dispatcher_UseBoosterItem`）：`short slot`；物品类型为 34 时再跟 `byte cat1, byte cat2`，然后 `min(n,20)` 个 `int itemId`（n 的取法见第 3 节）。
- 服务端校验顺序：
  1. 不在交易中、不在开个人商店
  2. 有当前角色
  3. 物品类型为 16 或 34
  4. state==3（物品 2675649 例外）
  5. `check_change_job_type`：`[usable job]` 与转职要求
  6. `Arad_GiftItem_Set::level_check`
  7. `CSecu_ProtectionField::CheckItem`
  8. `[minimum level]` ≤ 角色等级
  9. `check_empty_count(ITEM_TYPE 5, 1)`（类型 5 的含义未核）
  10. 冷却
  11. 扣 1 个（`IsDeletableItem` 时）
  12. 调用 `ProcessSpecialItem`
- **S→C 0xA3**：`byte 1, short slot, int 该格剩余数量, short n, n×(int id, int count)`。回包列表里没有被展开的包、PayCoin 显示为 id=1。
- **C→S 0x207**（`Dispatcher_CeraPackageOpen::read` @ `0x081e76fa`）：`short slot, byte n(≤35), n×(int itemId, byte ability)`。这 n 对构成 `vector<pair<int,int>>`，package handler 发放时按 itemId 匹配，把 ability 当作 ext2（时装属性）；ext1 固定传 0xFF，也就是使用时装默认期限（`0x0827c7af`–`0x0827c8b9`）。
- **S→C 0x207**：`byte 1, short slot`。只接受类型 38；成功后才删除礼包（与 0xA3 先删后发不同）。
