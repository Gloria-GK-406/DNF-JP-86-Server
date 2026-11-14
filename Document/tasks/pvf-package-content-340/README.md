# 礼包 PVF 内容修订（#340）

- 基线：`main` `edc741684`（0.3.2，含 #346 的礼包开启修复）；分支 `task/pvf-package-content-340`。
- 输入 PVF：`PvfBuilds/revised-20261113/Script.pvf`，SHA-256 `E1F3A25C93502708D499283CF7A6B8840EDB39C354CEB09CCB9E1133F3C123F2`（线上 0.3.2）。
- 输出 PVF：`PvfBuilds/revised-20261114/Script.pvf`，SHA-256 **`A97132DDA0970B3E0CE7C3DE8052ACF0BE2664F52E26C819B1D8CB6D427CFCD2`**，74,414,936 字节。
- 范围：#328 普查的第 9–12 类（零散格式、缺失物品、缺宠物种类、过期奖励），#328 修完解析后二次核对的 6 个礼包（见“#328 二次核对”），以及按你的裁定删掉删完后没有奖励的礼包（见“删掉没有奖励的礼包”）。第 1–8 类是服务端缺陷，归 #328；第 13 类归 #341。
- 新 PVF **尚未部署**，也没有替换任何客户端目录里的 PVF。服务端和客户端必须换同一份（理由见“缺失物品”一节）。

## 工具

`Tool/PvfPackageContentRepair`，可重复执行：

```bash
dotnet run -c Release --project Tool/PvfPackageContentRepair -- audit  <Script.pvf>
dotnet run -c Release --project Tool/PvfPackageContentRepair -- repair <source.pvf> <new.pvf>
```

- `audit`：只读，统计会改什么。修好的 PVF 各项都是 0。
- `repair`：写出 `<new.pvf>`、`<new.pvf>.json`（前后两次审计、每个被替换文件的 token 数）和 `<new.pvf>.changes.tsv`（逐条改动）。拒绝原地改写和覆盖已有文件。
- 写法沿用 0.3.2 修订版的追加块方式（`Engine.cs` 取自 `PvfBuilds/revised-20261113/tool`）：每个被改文件追加一个新 chunk，其余 721,185 个条目、原有 body 字节、字符串池偏移都不动，写完逐条回读校验。
- 写完后用新读取器重新审计输出文件，任一类不为 0 就报错退出；再全归档复扫被删礼包的 ID，除“删掉没有奖励的礼包”一节列明不改的文件外还有引用也报错退出。
- 扫描范围与 #328 普查相同：`stackable.lst` 中 `[stackable type]` 为 `[booster]`、`[cera package]`、`[cera booster]`、`[booster random]`、`[booster selection]`、`[usable cera package]` 的 13,269 个礼包。`[stackable type]` 重复时取最后一个，与客户端一致（见第 9 类）。
- 奖励容器按 A21 客户端 `ImportStackableScript`（`02999F40`）的读法解析，依据工作区 `Document/礼包开启全量修复-20261114/refs/a21-client.md` §1–§4：
  - `[package data]`、`[package data selection]`、`[package data include usable period]`：平铺 (id, 数量[, 期限])。
  - `[booster info]`：类别 `[x] 抽取数 (id 权重 数量 [扩展…])… [/x]`，`[charactor]` 多一个 `` `职业` A B `` 头。
  - `[booster select category] c1 c2`：修饰标签加类别条目 (id 数量 [扩展…])，不要求闭合标签。
  - 读不通的容器原样保留并记为异常（`ParseAnomalies`）。修前 2 处，修后剩 1 处：10003593 的 `[charactor]` 权重写成浮点数（客户端截断成整数读，见“#328 二次核对”），整数序列在此中断，工具把整个 `[booster info]` 原样保留。手工核对该礼包 142 个奖励 ID，全部在列表中。

数据文件（本目录 `data/`）：

| 文件 | 内容 |
|---|---|
| `changes.tsv` | 全部 2,896 条改动：保留下来的礼包里删掉的每个奖励（礼包、容器、物品 ID、数量、原因）、因此移除的空容器、补登记的种类、去掉的到期时间、删掉的残留选择头、删除的礼包（`delete`）、礼包以外的引用（`reference`） |
| `deleted-packages.tsv` | 删除的 103 个礼包：类型、名称、原有奖励数、第几轮删空（见“删掉没有奖励的礼包”）、文件路径 |
| `repair-report.json` | 工具输出：修前、修后两次审计结果，279 个被替换文件的 token 数，以及复扫时按规则不改的引用 |

## 第 9 类：零散格式（10 个礼包）——不改 PVF，回报 #328

逐个看了原文，并对照客户端解析器。10 个都是**客户端能正常读、只有服务端读不了**，按要求不改 PVF。

| 礼包 | 原文 | 客户端读法（依据） | 服务端现状 |
|---|---|---|---|
| 7835 赛丽亚的惊喜礼物 | `[booster info]` 最后一个 `[etc] 1 [/etc]`，抽 1 个、没有条目 | 读完抽取数后读到非整数，核对是闭合标签就收下这个空类别（`029A28AD..029A2A52`，Confirmed） | `Unsupported reward pool [etc]` |
| 10089779、10089780 | `[equipment] 1 [/equipment]`，同上 | 同上 | `Unsupported reward pool [equipment]` |
| 10006039、10089175 | 类别 (7,2) 写成 `[equipment] 100100480 1 [equipment]`，闭合标签漏了斜杠 | 类别条目读到非整数就结束，不查闭合标签；第二个 `[equipment]` 读不到条目，无害（a21-client.md §4.1，Confirmed） | `Invalid fixed reward [equipment]`（第二个 `[equipment]` 后没有数字） |
| 10089179 | (7,2)、(7,3) 两处同样漏斜杠 | 同上 | 同上 |
| 10148495、10148496 | 类别 (0,6)、(1,6) 是 `[equipment] [/equipment]` 空类别，对应类别名“无” | 键存在、条目为空；选中时 OK 按钮不可用（§3.3，Confirmed） | `Invalid fixed reward [equipment]`（空数字列表） |
| 2660474 | `[stackable type] [cera package] 0` 写了两次，值相同 | `[stackable type]` 处理器直接 `mov [edi+0x390], 类型码`，后者覆盖前者（`029A0C4B..029A0DF1`，Confirmed） | `Duplicate stackable type` |
| 10099183 | 整段物品头写了两遍：先 `[cera booster]`，后 `[booster]`，后面跟 `[booster info]` | 同上，最终是 `[booster]`(0x10)，走 2 字节 0x00A0 开启 | `Duplicate stackable type` |

给 #328 的结论：服务端应当
1. 接受空的 `[booster info]` 类别（不参与抽取）和空的自选类别（不可选）；
2. 自选类别遇到非整数就结束当前类别，不要求闭合标签；
3. `[stackable type]` 重复时取最后一个。

## 第 10 类：缺失物品（287 个礼包）

规则：奖励 ID 在 `equipment.lst`、`stackable.lst` 里都没有，就从礼包里删掉这一条。

- 源 PVF 里 3,056 个缺失 ID 在 287 个礼包中出现 4,193 次。其中删空的礼包整包删除（见下节），不再逐条改写；保留下来的礼包共删 2,247 条：自选块 1,616、`[package data]` 587、`[booster info]` 44。`[package data selection]` 和自选块里的 `[default select]`、`[recommend]` 都没有命中缺失 ID。
- 和普查口径（278 个礼包、2,866 个 ID）的差别：普查的 278 个全部包含在内。多出的 9 个是自选礼包，奖励写在 `[stackable]` 类别里（10000597、10000612、10000649、10012346、10012424、10012685、10014622、10014764、490001973）；服务端现在读不了这个类别（#328 第 7 类），普查到不了奖励检查，客户端则把它当正常类别读（a21-client.md §4.1）。ID 数多出来也是同一原因。
- 容器只在**被本次删空时**移除，原本就空的保持原样（例如上面第 9 类那些空类别）：
  - `[package data]` 删空就去掉标签和闭合标签；
  - `[booster info]` 的类别删空就整个类别去掉（24 处），整个 `[booster info]` 删空就去掉。抽取权重由客户端读入时累加，删条目后其余条目按比例重新分配；
  - 自选块里的类别删空就去掉类别标签（275 处），但 **`[booster select category] c1 c2 … [/booster select category]` 本身一律保留**。
  - 保留下来的礼包里没有整段 `[package data]` 或整个 `[booster info]` 被删空的情况；这两种只出现在整包删除的礼包里。
- 为什么保留自选块：`c1 c2` 是客户端下拉框的坐标键（a21-client.md §3.2）。保留后类别坐标、`[booster category num]`、`[booster category name]` 都不变，删空的块在客户端显示为空列表、OK 按钮不可用，和 PVF 里原有的“无”类别一样。保留下来的礼包里共 248 个块被删空，分布在 15 个自选礼包里；没有出现“删了一部分、剩下的不够 `[booster selection num]`”的块。
- 虽然坐标不变，每个类别里的格子数和内容变了。0x00A0 只带物品 ID，服务端按 (c1,c2) 找条目（#328 第 6 类）。服务端和客户端用的 PVF 不一致时，客户端能选的物品服务端可能不认，所以必须同时换。

## 删掉没有奖励的礼包（103 个）——按你的裁定

裁定：删完后没有奖励的礼包，从 PVF 删除。清单在 `data/deleted-packages.tsv`。

- **第 1 轮 95 个**：第 10、11 类删空的礼包。自选 50、普通 `[booster]` 18、装扮礼包 24、`[cera booster]` 3（50 + 18 + 24 + 3 = 95）。
- **级联 8 个**：这些礼包的奖励全是第 1 轮删掉的礼包，删后也没有奖励，按同一规则删除。工具重复判定直到不再有礼包变空：

  | 礼包 | 类型 | 原奖励 | 轮次 |
  |---|---|---|---|
  | 10000116 | `[cera booster]` | 10000117、10000119–10000121 | 2 |
  | 10000357、10012681 | `[booster selection]` | 9 个块，每块是 10000328–10000347 中的两个 | 2 |
  | 10000614 | `[booster selection]` | 6 个块，每块是 10000606–10000611 中的一个 | 2 |
  | 10000950 | `[cera package]` | 10000891 | 2 |
  | 10014763 | `[booster]` | 10014764 | 2 |
  | 10012682 | `[cera booster]` | 10012681 | 3 |
  | 10000537 7期稀有装扮套装礼盒 | `[cera booster]` | 10000357 | 3 |

删除做法：

- 从 `stackable/stackable.lst` 删掉这 103 行。客户端和服务端都按列表找物品，列表里没有就不存在。
- `.stk` 文件本身留在归档里，成为未登记文件。归档里原本就有这类文件（例如 10000422、10001349、10012215、10012231）。删除文件条目要重写文件表和哈希表，超出已验证的追加块写法，所以不删。
- 其他礼包里引用被删礼包的条目，按第 10 类的规则删除，共 207 条（`[package data]` 190、`[booster info]` 17），原因写 `package … deleted`。
- 礼包以外的引用：全归档扫描脚本里的整数 token 和 UTF-16 文本里的数字。改了 4 个文件、8 处：

  | 文件 | 删掉的内容 |
  |---|---|
  | `etc/iteminfo.dat` | 10000597、10000649、10001567、10014763、10014764 的物品记录（每条 6 个 token：ID、2、等级、名称、说明、图标） |
  | `event/levelupreward.evt` | `[creator mage]` 2 级奖励 `2 10000155 1` 这一行 |
  | `event/eventreward/reward_alljob.evt` | 60 级邮寄奖励 `60 10014844 1` |
  | `event/seriaroomdecoanimation.evt` | `[reward item] 9 10000074 1`，删后 `[reward item]` 是空块 |

- 扫到但不改的引用（写在 `repair-report.json` 的 `RemainingIgnoredReferences`）：
  - 文件名带 `(r)`、`(f)` 前缀的：`etc/(f)iteminfo.dat`、`etc/(r)iteminfo.dat`、`etc/(f)cerashop.etc`、`etc/(r)cerashop.etc`、`etc/(r)cerashop_first.etc`、`etc/itemdictionary/(r)stackabledictionary.etc` 等。它们是其他地区版本的遗留：名称是韩文，`(r)cerashop.etc` 旁边有服务端实际读取的 `etc/cerashop.etc`（后者不含被删礼包）。本仓服务端只读无前缀的文件，客户端是否读取这些文件未核对。
  - 4 处 `.xui`：`ui/event/kingofpick/`、`ui/event/chn_event_2015/luckypickevent/` 的活动面板用 `ItemIndex` 显示 10014989、10014991 的图标。服务端没有实现这个活动，改界面文件会动布局，不改。
  - 8556、8557 在技能、被动对象、任务列表、图像索引、`baseitemdictionarytable.etc` 里出现，是其他编号空间的数，不是物品引用。
- 客户端影响未实机核对：`seriaroomdecoanimation.evt` 的空 `[reward item]` 块客户端怎么读、`levelupreward.evt` 删行后客户端的显示，都没有看过。这两个活动本仓服务端都不读。

## 第 11 类：缺宠物种类（25 只）——补回 `creature.lst`

礼包奖励里有 25 只宠物装备的 `[creature species]` 在 `creature.lst` 里没有，涉及 15 个种类（普查只统计到其中 16 只，因为其余的在解析失败的礼包里）。

**选择补种类，不删宠物。理由：这些种类的定义本来就在 PVF 里，只是 `creature.lst` 少了这一行；补回一行不是新造内容，删宠物则会无谓地扔掉 25 只能用的宠物。**

证据：

| 种类 | 补回的行 | 依据 | 用到它的宠物 |
|---|---|---|---|
| 70 | `Bakal/bakal.cre` | 台服 70 | 63120 |
| 85 | `GirlsDay/girlsDay.cre` | 台服 70 | 63126 |
| 143 | `Princess_Petit/princess_petit_period.cre` | 台服 70 | 63180 |
| 144 | `Petit_Bebe/petit_bebe_period.cre` | 台服 70 | 63182 |
| 158–160、163–168 | `SDCharacterKR/<职业>/SDCharacter_<职业>.cre`（9 行） | 台服 70；`.cre` 自身 `[name]` 为 `name_0<种类>` | 63195–63197、63200–63208、63211–63216 |
| 172 | `brown/brown.cre` | 自带编号 | 63217、63218（brownie） |
| 477 | `petit_skeleton_gold/petit_skeleton_gold_7.cre` | 自带编号 | 100990022 |

- **台服 70**：`Research/tw70-pvf-archive/Script.20130704.pvf` 的 `creature/creature.lst` 有完全相同的编号和路径（Confirmed，用 `read_legacy_pvf.py` 读出）。另外，这 13 行的 lst 格式路径（如 `SDCharacterKR/SwordMan/SDCharacter_SwordMan.cre`、`Bakal/bakal.cre`）作为无人引用的字符串仍留在现行 PVF 的字符串池里（例如 `SDCharacterKR/SwordMan/SDCharacter_SwordMan.cre` 紧挨现存的 `2015SDAvatar/SwordMan/SDCharacter_SwordMan.cre`）——说明 86 的 `creature.lst` 原本就有这些行，后来行被删了，字符串没删（High）。所以这 13 行写回时没有新增字符串。
- **自带编号**（172、477，台服 70 时还没有这两个种类）：PVF 里没登记的 `.cre` 中，只有 `brown/brown.cre` 的 `[name]` 是 `name_0172`，它的动作文件叫 `brownie_*.ani`，对应 brownie 宠物；`name_0477` 有 `petit_skeleton_gold.cre` 和 `petit_skeleton_gold_7.cre` 两份，逐 token 相同，取与 7 天宠物 100990022 对应的 `_7`。`name_0NNN` 与编号的对应只是旁证：已登记、且 `[name]` 为 `name_0NNN` 的 120 行里，79 行编号吻合，其余是复制的 `.cre` 沿用了原来的名字（High）。这两行新增了 62 字节字符串。
- 15 个 `.cre` 引用的全部 `.ani` 都在 PVF 里，`.ani` 引用的全部 `.img` 都能在 `DNFClient-A21-debug/ImagePacks2`（与 0.3.2 Pure 包一致）的 NPK 索引里找到。所以客户端能显示这些宠物（静态核对，未实机召唤）。
- 新行插在第一个更大编号之前，`creature.lst` 由 972 个 token 变为 1,002 个。
- 只补礼包宠物用到的种类。`creature.lst` 还缺 75–78、146、478 这几个编号（其中 75–78、146 台服 70 有记录），对应的宠物不在任何礼包里，不属于本任务，没有改。

## 第 12 类：过期奖励（6 个物品）——去掉到期时间

6 个奖励物品带有已经过去的 `[expiration date]`。按裁定删掉这个标签和它的值，物品变为永久：

| 物品 | 原到期时间 | 文件 |
|---|---|---|
| 10004550 +9 羊年特级装备强化券 | 2015-01-15 06:00:00 | `stackable/event/chn_event_20141230_sheep_puzzle/chn_sheep_young_70por_10004550.stk` |
| 490003449、490003451–490003454 钓鱼活动物品 | 2016-12-15 06:00:00 | `stackable/chn_490003449.stk` 等 5 个 |

- 普查写的是 490003449–490003454，其中 490003450 本身是礼包（钓鱼王的鱼饵礼盒），不是奖励，没有到期时间。
- 改的是物品自身，所以商城、掉落等其他途径得到的这些物品也一起变成永久。礼包奖励里没有到期时间还在未来的物品。

## #328 二次核对（6 个礼包）

#328 按第 9 类的结论改完解析后重新普查，又有 6 个礼包读不了。逐个对照 A21 `ImportStackableScript`：

| 礼包 | 原文 | 客户端读法（依据） | 处理 |
|---|---|---|---|
| 10007852、10008885 Platinum_Emblem_Box | token 级没有不成对的数字。异常之处：多个 `[stackable] [/stackable]` 空类别（10007852 有 15 个，10008885 有 14 个），以及坐标 (7,4) 出现两块，两块都是空类别 | 空类别同第 9 类；同坐标后写覆盖（见 490001689） | 不改 PVF，回报 #328 |
| 10003593 | `[gunner]`、`[at gunner]` 两段 `[charactor]` 的权重是浮点 token：`252.5`、`4.5` | 权重用严格整数读取 `028A9310`：kind 0（整数）直接取；kind 2（浮点）用 `cvttss2si` 向零截断（`028A94A4..028A94F8`，Confirmed）。即 252.5→252、4.5→4 | 不改 PVF，回报 #328 |
| 490001689 | 先是一套残缺的 (0,0)…(6,0)，后面是完整的 (0,0)…(9,3)；两套只有 (6,0) 不同（前空后有） | 块结束时 `map[(c1,c2)]`（`0296C3D0`，查找或插入）再做 vector 赋值（`01520AC0`，源为空时清空），**后写覆盖**，空块也覆盖（Confirmed）。结果就是后一套完整内容 | 不改 PVF，回报 #328 |
| 490001919 | `[booster category num] 0 [booster selection num] 1 [booster select category] 0 0` 未闭合，紧跟 `[action usable place]`，再后面才是 `[booster category num] 1 5` 和真正的各块 | 块内读标签的 `028A9A70` 只收 kind 3/4（标签）：遇到 `[village]`、`[seria room]` 这类反引号串就跳过，吞掉下一个标签 `[/action usable place]` 并返回失败，块 (0,0) 就此以空内容结束（`028A9B0E..028A9BA6`、`029A3645..029A365B`，Confirmed）。之后顶层的 `[booster category num] 1 5`、各块照常读入，(0,0) 被后写覆盖。结果：5 个类别都能选，但 **`[action usable place]` 根本没进顶层处理器，使用地点限制丢了** | **改 PVF**：删掉那 7 个残留 token，文件变成“村庄/赛丽亚房间使用 + 5 个类别”的本意。客户端从此也有使用地点限制 |
| 54 wildavatar_giraffe 等 4 个 `[cera booster]`/`[booster]` | 随机池时装行的第 5 个数（属性号）不在该时装声明的属性里：54 的 48734、48747（`[ability case index] 250`）各有属性号 7、9、14 三行，case 250 恰好缺这三个号；52 的 40734、40747 与 2660276 的 40909、2660277 的 40913 是属性号 0，而它们的 case 50 从 1 开始 | 随机礼包开启只发 2 字节 0x00A0，客户端不参与。70 的发放链把 ext2 直接写进物品（`refs/70-server.md` §6），没有校验 | **改 PVF**：这 10 行开出来是一件属性不存在的时装，结果明显错误，从随机池删掉，其余行按权重重新分配。删后没有类别变空 |

- 属性号集合：时装的 `[avatar select ability]` 和 `[ability case index]` 指向的 `skill/abilitydatas.dat` `[ability case]`；条目写法是 `编号 [类型] 参数…`，编号后紧跟全大写的类型标签。
- 自选块里也有 136 行时装属性号不在声明里，**没有改**：自选开启时属性由玩家在属性窗里选、随请求发来（0x00A0 尾部），块里的值只是界面默认值。服务端应校验请求里的属性号，不该校验 PVF 默认值。
- 490001919 删头之后，工具审计的 `StrayHeaders` 为 0。

## 核对

- 新 PVF 回读：721,185 个条目，279 个被替换（`creature.lst` 1 + `stackable.lst` 1 + 礼包 266 + 过期物品 6 + 礼包以外的引用 4 + 490001919 1；490001919 只删残留头），其余条目和原有字符串偏移不变（工具内建校验）。
- `audit` 新 PVF：礼包 13,166 个（源 13,269 个减去删除的 103 个）；缺失奖励 0、缺种类宠物 0、过期奖励 0、待补种类 0、随机池未声明属性 0、残留选择头 0、待删礼包 0；解析异常剩 10003593 一处（浮点权重，PVF 未改）。第 9 类 PVF 不改，靠 #328 服务端处理。
- 可重复：用最终版工具对同一输入再跑一次，输出 SHA-256 与 `changes.tsv` 逐字节相同。
- 抽查新旧对比（`PvfProbe dump` 或逐 token 导出）：10014623（`[booster random]`）、10157346（自选，35 个块删空保留坐标）、721（`[package data]` 删 63069）、490001919（删残留头）、`creature/creature.lst`、`stackable/chn_490003449.stk`；删除后 `stackable.lst` 少 103 行（86,288 → 86,082 个 token），10000357、10000537 等被删礼包的 ID 在新 PVF 列表中查不到。
- 服务端带真实 PVF 的测试，新旧 PVF 各跑一遍对比，结果见下节。

## 服务端测试

`dotnet test`（Release），`PVF_ARCHIVE_PATH` 分别指向旧、新 PVF，同一份构建：

| 项目 | 旧 PVF | 新 PVF |
|---|---|---|
| Dfo.Pvf.Tests | 82 通过 | 82 通过 |
| Dfo.Server.Application.Tests | 6,830 通过、26 失败、8 跳过 | 相同 |
| Dfo.Server.Infrastructure.Tests | 1,034 通过、17 失败、2 跳过 | 相同 |
| Dfo.Server.Host.Tests | 322 通过、1 失败、1 跳过 | 相同 |

表中“新 PVF”一列是第一版输出（`EBF8DDD3…`）的结果。删除空礼包之后，Application 与 Infrastructure 两项用 `A97132DD…` 又跑了一遍：

- Infrastructure：1,034 通过、17 失败、2 跳过，失败名单与旧 PVF 相同。
- Application：删除礼包后，主干原有的 `PackageOpeningTests.Archive_package_referencing_missing_equipment_is_refused_without_consumption` 失败。它拿 10000606 当“引用了缺失装备的礼包”样本，而 10000606 已被删，新 PVF 里也不再有引用缺失物品的礼包，所以改写成合成内容（`Package_referencing_missing_equipment_is_refused_without_consumption`，不依赖 PVF），断言不变：报 `UnsupportedContent`（原因为该奖励不存在），礼包不消耗。改写后 `PackageOpeningTests` 在新旧 PVF 下都通过。
- 礼包开启普查（#346 的 `PackageOpeningArchiveSurveyTests`）：54、490001919 在新 PVF 下已能开，从 `KnownResiduals` 删去它们的登记后，新 PVF 下普查通过（Package 1160/8085、Selection 1/2764、AvatarPackage 0/2317 失败，全部是登记过或可接受的原因）。旧 PVF 下普查会因这两个礼包失败，这是预期的：登记删去后，普查以新 PVF 为准。

其余失败名单新旧完全一致，是这些测试和 0.3.2 修订版 PVF 之间已有的不匹配（例如掉落池、黑暗精灵遗迹、日常补充），与本次改动无关，没有处理。

## 未覆盖

- 没有实机验证：没开过修订后的礼包，没召唤过补回种类的宠物。
- 第 9 类的空类别、漏斜杠、重复类型要等 #328 的服务端解析改完才能打开。
- 删除的礼包没有查过玩家库里是否已有存货；已有的物品在新 PVF 下找不到定义。
