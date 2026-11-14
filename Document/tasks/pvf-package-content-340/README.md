# 礼包 PVF 内容修订（#340）

- 基线：`main` `ef20398a3`（0.3.2）；分支 `task/pvf-package-content-340`。
- 输入 PVF：`PvfBuilds/revised-20261113/Script.pvf`，SHA-256 `E1F3A25C93502708D499283CF7A6B8840EDB39C354CEB09CCB9E1133F3C123F2`（线上 0.3.2）。
- 输出 PVF：`PvfBuilds/revised-20261114/Script.pvf`，SHA-256 **`EBF8DDD3927478D1192C7729B0E5483D79525E8607C2B589B9C2FDB3F3B17ABB`**，74,090,993 字节。
- 范围：#328 普查的第 9–12 类（零散格式、缺失物品、缺宠物种类、过期奖励）。第 1–8 类是服务端缺陷，归 #328；第 13 类归 #341。
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
- 写完后用新读取器重新审计输出文件，任一类不为 0 就报错退出。
- 扫描范围与 #328 普查相同：`stackable.lst` 中 `[stackable type]` 为 `[booster]`、`[cera package]`、`[cera booster]`、`[booster random]`、`[booster selection]`、`[usable cera package]` 的 13,269 个礼包。`[stackable type]` 重复时取最后一个，与客户端一致（见第 9 类）。
- 奖励容器按 A21 客户端 `ImportStackableScript`（`02999F40`）的读法解析，依据工作区 `Document/礼包开启全量修复-20261114/refs/a21-client.md` §1–§4：
  - `[package data]`、`[package data selection]`、`[package data include usable period]`：平铺 (id, 数量[, 期限])。
  - `[booster info]`：类别 `[x] 抽取数 (id 权重 数量 [扩展…])… [/x]`，`[charactor]` 多一个 `` `职业` A B `` 头。
  - `[booster select category] c1 c2`：修饰标签加类别条目 (id 数量 [扩展…])，不要求闭合标签。
  - 读不通的容器原样保留并记为异常（`ParseAnomalies`）。全部 13,269 个礼包里有 2 处，都不藏缺失物品：
    - 10003593（`chn_box_ontime_20140510.stk`）：`[gunner]`、`[at gunner]` 两段 `[charactor]` 的权重写成浮点数 `252.5`、`4.5`，整数序列在此中断。手工核对该礼包 142 个奖励 ID，全部在列表中。客户端对浮点权重的读法没有查。
    - 490001919：第一个 `[booster select category] 0 0` 没有闭合，后面跟着 `[action usable place]`、`[booster category num]` 和真正的各个块。工具只跳过这个头，后面的块照常检查；该礼包没有缺失物品。这是 #328 第 7 类说的“顶层标签混进选择段”的例子。

数据文件（本目录 `data/`）：

| 文件 | 内容 |
|---|---|
| `changes.tsv` | 全部 5,076 条改动：删掉的每个奖励（礼包、容器、物品 ID、数量、原因）、因此移除的空容器、补登记的种类、去掉的到期时间 |
| `empty-packages.tsv` | 删完后整个礼包没有奖励的 95 个礼包，**待你裁定** |
| `repair-report.json` | 工具输出：修前、修后两次审计结果，以及 294 个被替换文件的 token 数 |

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

- 共删 4,193 条，涉及 3,056 个不同 ID、287 个礼包。分容器：自选块 3,210、`[package data]` 785、`[booster info]` 198（4,193 = 3,210 + 785 + 198）。`[package data selection]` 和自选块里的 `[default select]`、`[recommend]` 都没有命中缺失 ID。
- 和普查口径（278 个礼包、2,866 个 ID）的差别：普查的 278 个全部包含在内。多出的 9 个是自选礼包，奖励写在 `[stackable]` 类别里（10000597、10000612、10000649、10012346、10012424、10012685、10014622、10014764、490001973）；服务端现在读不了这个类别（#328 第 7 类），普查到不了奖励检查，客户端则把它当正常类别读（a21-client.md §4.1）。ID 数多出来也是同一原因。
- 容器只在**被本次删空时**移除，原本就空的保持原样（例如上面第 9 类那些空类别）：
  - `[package data]` 删空就去掉标签和闭合标签（24 处）；
  - `[booster info]` 的类别删空就整个类别去掉（38 处），整个 `[booster info]` 删空就去掉（21 处）。抽取权重由客户端读入时累加，删条目后其余条目按比例重新分配；
  - 自选块里的类别删空就去掉类别标签（779 处），但 **`[booster select category] c1 c2 … [/booster select category]` 本身一律保留**。
- 为什么保留自选块：`c1 c2` 是客户端下拉框的坐标键（a21-client.md §3.2）。保留后类别坐标、`[booster category num]`、`[booster category name]` 都不变，删空的类别在客户端显示为空列表、OK 按钮不可用，和 PVF 里原有的“无”类别一样。共 752 个类别被删空，分布在 65 个自选礼包里；没有出现“删了一部分、剩下的不够 `[booster selection num]`”的类别。
- 虽然坐标不变，每个类别里的格子数和内容变了。0x00A0 只带物品 ID，服务端按 (c1,c2) 找条目（#328 第 6 类）。服务端和客户端用的 PVF 不一致时，客户端能选的物品服务端可能不认，所以必须同时换。

### 删完整个礼包变空（95 个）——待你裁定

清单在 `data/empty-packages.tsv`：自选 50、普通 `[booster]` 18、装扮礼包 24、`[cera booster]` 3（50 + 18 + 24 + 3 = 95）。这些礼包本身没删，只是里面没有奖励了。客户端里能看到、能使用，但开不出东西。可选做法：删掉礼包物品（同时要处理商城、掉落、任务等引用），或另配内容。

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

## 核对

- 新 PVF 回读：721,185 个条目，294 个被替换（`creature.lst` 1 + 礼包 287 + 过期物品 6），其余条目和原有字符串偏移不变（工具内建校验）。
- `audit` 新 PVF：缺失奖励 0、缺种类宠物 0、过期奖励 0、待补种类 0；解析异常仍是上面那 2 处（PVF 未改）。第 9 类 PVF 不改，靠 #328 服务端处理。
- 可重复：用最终版工具对同一输入再跑一次，输出 SHA-256 与 `changes.tsv` 逐字节相同。
- 抽查 `PvfProbe dump` 新旧对比：10014623（`[booster random]`）、10090038（自选，类别删空保留坐标）、10000328（装扮礼包删空）、721（`[package data]` 删 63069）、`creature/creature.lst`、`stackable/chn_490003449.stk`。
- 服务端带真实 PVF 的测试，新旧 PVF 各跑一遍对比，结果见下节。

## 服务端测试

`dotnet test`（Release），`PVF_ARCHIVE_PATH` 分别指向旧、新 PVF，同一份构建：

| 项目 | 旧 PVF | 新 PVF |
|---|---|---|
| Dfo.Pvf.Tests | 82 通过 | 82 通过 |
| Dfo.Server.Application.Tests | 6,830 通过、26 失败、8 跳过 | 相同 |
| Dfo.Server.Infrastructure.Tests | 1,034 通过、17 失败、2 跳过 | 相同 |
| Dfo.Server.Host.Tests | 322 通过、1 失败、1 跳过 | 相同 |

失败的测试名单新旧完全一致，是这些测试和 0.3.2 修订版 PVF 之间已有的不匹配（例如掉落池、黑暗精灵遗迹、日常补充），与本次改动无关，没有处理。

## 未覆盖

- 没有实机验证：没开过修订后的礼包，没召唤过补回种类的宠物。
- 第 9 类的空类别、漏斜杠、重复类型要等 #328 的服务端解析改完才能打开。
- 95 个空礼包等裁定。
