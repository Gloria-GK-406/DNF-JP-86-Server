# 70 服务端原生深渊静态参考

日期：2026-10-07。基线：`81f4373f`。本报告是 **70Reference**；未运行服务、PVF、数据库或客户端，未抓包。Confirmed 表示所列 ELF 指令直接支持；High 表示符号、字段与调用链一致但仍含解释；Unknown 表示本次未闭合。这里的数值、协议和行为均不能直接升级为 A21 / WireCaptured / ClientVerified。

## 目标身份与复现

|目标|长度|SHA-256|
|---|---:|---|
|`E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r`|36,360,954|`CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`|
|`E:/Project/DNF/Research/1995chen-dnf/build/dnf_data/home/template/init/df_game_r`|36,360,954|`DD0AE672B1DCE3FF9C6C403BEAEC71B3EF666B14DEB567D4CAB0DA99D8711F9E`|

二者为 ELF32、little endian、ET_EXEC、EM_386、SYSV，entry `0x0807E910`。本次默认分析前者；后者仅用于机器码对照。`.text` 地址 `0x0807E910`、长度 10,907,404；存在 `.symtab` 与部分 DWARF。两个整文件 hash 不同，不能称全文件等价。已选深渊、掉落、地图、累积、清场、稀有度等函数在同地址具有相同完整函数机器码，逐项见 [70-template-comparison.txt](evidence/70-template-comparison.txt)。

Windows Python 3.11 + pyelftools + capstone 5.0.7 只读解析。WSL 查询遇到 `E_ACCESSDENIED` 后使用此替代。复现脚本 [70-inspect.py](evidence/70-inspect.py) 支持 `identity`、`symbols`、`asm`、`calls`、`compare`、`strings`、`xrefs`、`data`。例如在本工作树执行：

```powershell
python Document/abyss-research-20261007/evidence/70-inspect.py asm 0x08535726 0x085334A4 0x080CBABE
python Document/abyss-research-20261007/evidence/70-inspect.py compare 'Hell|hell|SelectDungeon|MakeDrop|ItemGen|LuckPoint|ClearDungeon'
```

`compare` 比较双方 `.text` 在相同地址的完整符号长度机器码，非仅比较函数名。`xrefs` 是字节操作数候选定位，需回看指令，不能单独作为真实引用证明。身份原文见 [70-identity.txt](evidence/70-identity.txt)。

## 入口、随机资格与主动选择

Confirmed 调用主链：`CParty::dungeon_start 0x085A0954` → `CheckHaveHellPartyPassItem 0x085A7B8A`（call `0x085A0CC3`）→ `CBattle_Field::check_random_appear_hell_dungeon 0x0830A862`（call `0x085A0D3F`）→ `SelectDungeon 0x082FF486`（call `0x085A0DAE`）→ `UseSealDoorItems 0x085A8038`（call `0x085A146F`）。证据：[70-callers.asm.txt](evidence/70-callers.asm.txt)、[70-entry.asm.txt](evidence/70-entry.asm.txt)。

`check_random_appear_hell_dungeon` 使用 Party `+0x1868` 的 world-map 指针，全局开关 `0x093701EC`，以及 world-map `+4` 的 `IsInHellDungeon` 标志。遍历 0..3 有效队员，以角色 word `+0x27` 求最高/最低等级：最高等级减 dungeon.standardLevel ≤5，并且单人或队内等级差 ≤8，才置输出资格布尔值。多人等级差 >8 清 world-map 指针；dungeonMode==1 亦清指针并清资格。另有 Party `+0x1864/+0x1865` 与 dungeonType==1 的组合门，字段名称未完整闭合，保留偏移 Unknown。

资格通过不等于出现。`SelectDungeon` 的随机分支还要求资格=true **且人数 !=1**。该参考的随机深渊最终至少两人；不能因为资格函数接受单人便推断单人会出现。

`SelectDungeon`：type==1 为主动分支，在 `0x082FF525` 调 `sub_hellparty_value`，选深渊难度并置 Battle `+0x1B8=true`。非主动分支先调用 `CheckHellPartyCondition 0x082FF988`，通过后 `0x082FF609` 调同一值消耗函数并置深渊标志。最终若 Dungeon `+0x89D` 为 false，又在 `0x082FF6CA` 清 `+0x1B8`；该 dungeon 字段的脚本身份本次 Unknown。

`CheckHellPartyCondition` 同时要求：

```text
truncTowardZero(Party.float[+0xB0C]) >= get_rand_int(100)
truncTowardZero(Party.float[+0xB0C]) >= hellpartyScript.int[+0x90] // guide line value
```

第一比较在 `0x082FF9C0..0x082FF9CE`，第二在 `0x082FF9FC..0x082FFA09`。这里使用的是 `get_rand_int 0x086B1B87`，不是掉落 MT wrapper；其备用生成与边界不能直接解释为严格均匀百分率。因此本报告不提供一个未经证明的固定百分比概率。

`Dispatcher_HellPartyStart::dispatch_sig 0x0821D9A6` 检查角色 state==5；`HellPartyStart::read 0x08233E8E`、`process 0x08233E98` 均直接返回 0。不能凭名称把它认作已闭合的深渊柱交互触发协议。

## 蓄积值、消耗与参数映射

Confirmed：Party 浮点 `+0xB0C` 为此链使用的值 V。`ClearDungeon 0x085A9330`：人数为 1 时将 BattleData total 写为 script `+8 [base value]`；多人且 Party `+0xCEE/+0xCEF/+0xCDC` 的相关门均未置位时，在 `0x085AA2E6` 调 `cal_hellparty_value(dungeonDifficulty, levelDifferenceBucket, memberCount-1)`。这些排除标志具体产品语义 Unknown。

`get_rate_level_difference 0x085B59CA` 取 dungeon.standardLevel 与 Party 最大等级之差的绝对值：≤10→桶1，11..19→桶2，≥20→桶3；dungeon 为 null→0。

`cal_hellparty_value 0x085B58AC`，设 i=副本难度，j=等级差桶，k=人数-1：

```text
B = Party.int[+0xD18]
L = Party.float[+0xD24 + 4*j]
D = Party.float[+0xD34 + 4*i]
P = Party.float[+0xD48 + 4*k]
A = Party.float[+0xD58] if Dungeon.byte[+0x89C] else 0
Vnew = V + B * L * (1 + D + A + P)
if truncTowardZero(Vnew) > Party.int[+0xD0C]: Vnew = float(cap)
```

直接运算原文 `0x085B594C..0x085B5967`；上限比较 `0x085B5970..0x085B59AE`。入口只检查 j!=0、k>=0、i<=4，本次没有看到完整上下界防护。注意上限是先截断再比较；不能改写为数学上完全等价的 `min(Vnew,cap)`。`+0x89C` 对应古代加成的解释为 High；本次未核验其全部写入来源。

`importHellPartyScript 0x08909857` → `reset_hell_party_value 0x0830A57C` 映射如下。Battle 嵌入 Party `+0xB24`，因此两套偏移能互相核对。rate 从 script 读出的 float 在 reset 时除以 **10000**（常量 `0x08C23D74`）。

|PVF标签|script偏移|Party偏移|解释|
|---|---:|---:|---|
|`[max base value]`|0x00|0xD0C|cap int|
|`[min base value]`|0x04|0xD10|min int|
|`[base value]`|0x08|另写 BattleData total|单人分支初值相关；不等于已证明全部 V 初始化|
|`[base rising value]`|0x0C|0xD18|B int|
|`[sub base rates]`|0x10|0xD1C|乘法保留比例|
|`[pass sub base rates]`|0x14|0xD20|读入存在；本次未闭合应用点|
|`[level difference rates]`|0x18..0x24|0xD24..0xD30|j=1..3 使用后三项|
|`[difficulty rates]`|0x28..0x38|0xD34..0xD44|五档 D|
|`[party rates]`|0x3C..0x48|0xD48..0xD54|四档 P，含首项默认读取|
|`[ancient dungeon add rate]`|0x4C|0xD58|A|
|`[guide line value]`|0x90|条件函数直接读 script|资格后的最低值门|

`sub_hellparty_value 0x085B690A` 并不是减法：`tmp=V*Party.float[0xD1C]`；若 `trunc(tmp)<min` 则 `tmp=float(min)`；最后 `V=tmp` 并通知。入口主动/随机两分支均调用它。函数名不能代替指令公式。

`send_hellParty_value 0x085B697C` 发送 `put_header(0,0xCF)`，body 为 i32 `trunc(V*100/cap)`，发给队伍。常量100为 `0x08CBBBCC`。无抓包，不能称此消息即 A21 对应布局。

`CParty::init 0x0859A974` 的 `0x0859ABC2` 调 reset 参数，`0x0859AC07` 清 world-map 指针。`game_start 0x0859D718` 读 V 并生成通知，单人在 `0x0859DD89` 写 BattleData total 为 script+8；这不证明所有 V 初值与持久化。额外核验 raw byte xrefs：`clear_reward` 的 `0x085B0B76` 是读取 V 构包；`send_loadingcheck_msg` 的 raw 命中 `0x085B2047` 实际位于立即数/位移拼接中，并非 V 字段引用（详见 [70-value-reset.asm.txt](evidence/70-value-reset.asm.txt)）。**V 是否跨队伍、切图、重登录持久化，Unknown**。

`[random intensity]` 与 `[get hellparty item id]` 在两个 ELF 的 `.rodata` 字面扫描均未命中。这里只能证明该扫描没有标签字面；不能据此称 A21 标签与本报告 V 同义，也不能称任何运行资源不支持它。两项跨版本映射 **Unknown**。

## 深渊难度、地图组与怪物

`importHellPartyScript` 的 `[difficulty]` 先读 count≤6，每条名称后读五个整数：第1存 byte `script+0x6C+d`，第2存 u16 `+0x72+2d`，第3存 byte `+0x7E+d`，第4存 byte `+0x84+d`，第5存 byte `+0x8A+d`。名称到中文“普通/困难/非常困难”等的映射需实际脚本文本，本次 Unknown。

第1整数通过 `add_hell_party_monster` 写 monster byte `+0x11`，再由掉落 ref `+0x3D` 进入尝试次数，Confirmed。第3/4分别供随机/主动难度选择，第5供事件类型。第2整数应用身份本次 Unknown。

`GetHellPartyDifficulty 0x082FFA2E` 主动读取 `+0x84` 的六字节，随机读取 `+0x7E`，调用 `get_rand_int(100)`，累计 signed byte 槽1..5，返回第一累计≥抽值的槽；未命中返回0，SelectDungeon 对0报错。随机还可能通过 `increase_hell_party_ratio 0x0859BBA8` 调整前两项：Party平均等级>59 后由 QuickParty 的 basic reward(type0xC)调整；QuickParty全算法本次 Unknown。`GetHellPartyEventType 0x082FFB06` 读 signed byte `+0x8A+d`。

`procSpecialPassiveObject 0x089E7958` 处理 `.rodata 0x08E1C0F4 [hellparty]`，生成按 mapId 保存的 groupOrder map。行包含 groupId、weight、difficulty byte；按 difficulty 求 u16 权重总计。组内容也扩展成 passive object 的 action 数据（action 5）。这证明地图/被动对象中的深渊组桥梁，不足以给出完整柱交互协议或客户端视觉动作。

`add_monster_hell_party 0x08301B8C` 在 Battle `+0x1B8` 为真时，按 MapInfo `+4` 的地图ID查 groupOrder，然后调用 `add_hell_party_monster 0x083071BA`。后者：

1. 清 MapInfo `+0x8C` 的组计数 map。
2. 每个 order 行按当前深渊难度取 u16 总权重，以 `get_rand_int(total)` 抽值，遍历同难度记录累计 weight，选第一累计≥抽值的 groupId。
3. 查该 groupId 的怪物ID序列，到 ID≤0 终止，Add_Mob 到当前图。
4. 写 monster `+0x334=u16 orderKey`、`+0x336=u16 groupId`、`+0xC=i32 monsterId`、`+0x10=byte dungeon.standardLevel`、`+0x11=byte script[0x6C+d]`、`+0x338=byte d`。组脚本对应 flag 非零时 monster `+8=5`，否则0（普通怪/APC角色解释 High）。
5. MapInfo 计数 map 插入 `(groupId, monsterCount-1)`（`0x0830751E`）。

组抽取使用 `get_rand_int`；不可套用掉落 MT 的精确均匀分母。`[hellparty monster group]` 有 A..E 内容结构，但与 A21 同名组、固定尝试数的对应关系未证明。

## 原生奖励结算触发点

Confirmed：`kill_monster 0x0830BC78` 对 monster `+0x334 !=0` 在 `0x0830C131` 调 `KillHellPartyGroupMonsterCnt 0x0830D704`。该函数：若 Battle `+0x1CA` 已真则提前返回；按 monster `+0x336` 找组计数，清 monster `+8`；计数为0时输出 `groupClear=true` 并 erase 组；map为空才置 Battle `+0x1CA=true`，否则计数减1并输出false。

`kill_monster` 把 groupClear 送给 `_MakeDropItems 0x0830ADF6`（call `0x0830CD58`），后者写 ref byte `+0x40`。**普通装备结算门是每组最后一怪，不是必然全深渊最后一怪**。全组表空的 `+0x1CA` 是另一个状态。

`CMonsterDrop::generateSpecificItem 0x08537070` 在 ref `+0x41` 为真时直接转 `Hell::hellPartySpecificItem 0x08535B08`；其中先查 APC/monster 的独立掉落、装扮、捕捉等来源，随后仅在 ref `+0x40=true` 时循环普通装备生成。循环次数通常为 signed ref `+0x3D`；但当前末怪 content.IsHellMonster()==true 时 local 次数置0。**具体哪类末怪的 IsHellMonster 标记、怪物顺序，需实际脚本/内容身份，Unknown**。独立来源不是仅 groupClear 时执行。

没有观察到原生硬编码 A8次/B6次循环。次数来自 `[difficulty]` 第一整数上述链。当前参照代码中的固定次数或 `BaseDropRate=100` 不能用来倒推此 ELF。

`_MakeDropItems` 的 ref 字段：

|偏移|来源/用途|
|---:|---|
|0x00 i32|参数 `[ebp+0x24]` baseDropRate；上游完整计算本次未闭合，不能声明固定100|
|0x35 byte|dungeon.standardLevel，部分特殊副本会有例外|
|0x36 byte|普通副本难度，索引掉落概率五档|
|0x38 i32|monster ID|
|0x3C byte|monster角色类别|
|0x3D byte|monster+0x11，普通装备尝试次数|
|0x3F byte|Party最高等级|
|0x40 byte|groupClear|
|0x41 byte|monster+0x334 !=0|
|0x42 byte|monster+0x338，深渊难度，索引稀有度六阈值表|

证据：[70-kill-init.asm.txt](evidence/70-kill-init.asm.txt)、[70-drop-ref.asm.txt](evidence/70-drop-ref.asm.txt)、[70-drop-generation.asm.txt](evidence/70-drop-generation.asm.txt)。

## 一次普通装备尝试：抽样顺序与分母

`Hell::generateSpecificItem 0x08535726` 一次最多追加一个 Inven_Item，顺序如下：

1. `_getGenItemProbIdx 0x08535A88` 按 ref+0x35 level，选择第一条 `lower≤level≤upper` 的概率行，无行即结束。
2. 取行 `+8+4*普通副本难度` 的整型 probability。阈值 `T=truncTowardZero(probability*baseDropRate/100)`；乘/有符号除100指令在 `0x0853577C` 附近（常数 `0x51EB851F`）。
3. `0x085357C6` 调 `generateNumber(0,1000)`；r>T 则失败，因此成功为 r≤T。
4. 过门后 `0x085357F2` 调 `generateNumber(1,1000000)`，以深渊难度 ref+0x42 选 `Hell.this+0x30+24*d` 六阈值表。
5. `0x08535835` 调 `CLuckPoint::GetItemRarity(table,r,2)` 取得稀有度。
6. `0x08535866` 调 `chooseItem(type=2,rarity,level)` 抽候选ID；成功造物品后调用 UseLuckPoint，最后 append一个物品。空池/造物失败会使这次没有装备。

**`generateNumber(1,1000000)` 的第一个1是 RNG序号，不是下界。** `CGenerateRandomNumber::generateNumber 0x085334A4`：`0x085334D0 imul index,0x9C8`，`0x085334E2 call CMTRand::randInt(&N)`，无 min/max 差、无结果+1。序号越界才 fallback 到 get_rand_int。

正常构造链：Mgr `0x08533BE1` 以3构造 CMonsterDrop；其中 `0x0853A71F mov [esp+4],2` → `0x0853A72A` 构造内嵌 Hell；Hell `0x0853A57F` → CItemGenerator `0x08533B5F` → CGenerateRandomNumber `0x085334F9` 保存count=2并构造两个MT。因此本调用序号0、1均使用MT。证据：[70-constructor-loader.asm.txt](evidence/70-constructor-loader.asm.txt)、[70-constructor-protocol.asm.txt](evidence/70-constructor-protocol.asm.txt)、[70-rarity-pool.asm.txt](evidence/70-rarity-pool.asm.txt)。

`CMTRand::randInt(&N) 0x080CBABE` 先生成覆盖N的mask，对随机数mask后若 value>N则重抽（`0x080CBB0E` 比较/setb）；允许 value==N。故正常路径第一门样本域 **0..1000，共1001个值**；稀有度样本域 **0..1000000，共1000001个值**，不是百万个值。若理想均匀抽样、0≤T≤1000，则第一门概率 `(T+1)/1001`；T<0 时0，T≥1000时1。这里表列整数1000/百万是参数上界，不是严格概率分母。原文：[70-drop-ref.asm.txt](evidence/70-drop-ref.asm.txt)。

## 稀有度、组队修改与候选池

`Hell::loadScript 0x08535344`：`[basis of rarity dicision]` 每条六int写到 `this+0x30+24*d`；这些是直接比较的**累计阈值**，不是六个需要再次累加的独立权重。`[drop prob count]` 创建行；`[dungeon difficulty drop prob]` 各行读 lower/upper 与五概率。`[item drop ref table]` 读200条等级窗口的 `(index,belowByte,aboveByte)`。实际 loader 文件名也已闭合：Mgr initialize `0x08533E9E` 传 `.rodata 0x08C99458` 的 `Etc/ItemDropInfo_Monster_Hell.etc` 为第二文件参数，`CMonsterDrop::loadScript 0x085380FA` 在 `0x08538143` 调内嵌 Hell loader。另两文件原文字面为 `Etc/ItemDropInfo_Monseter.etc`、`Etc/ItemDropInfo_Monseter_Extra.etc`（保留原拼写）。地址/字面见 [70-data.txt](evidence/70-data.txt)、[70-constructor-loader.asm.txt](evidence/70-constructor-loader.asm.txt)。这是70加载路径证据，不能替代A21标签映射。

`CLuckPoint::GetItemRarity 0x08550BE4` 正常 type2、有用户时先取 luck 参数，按 rare区宽 `C2-C1` 作万分比调整；再取首个非null用户，若其Party非null则读取 `get_party_overlapped_drop_ratio_unique/rare`，若Party为null亦停止搜索。组队对 unique区/rare区阈值有影响，不能只用人数乘尝试次数表达。可核验公式（按指令中的阈值槽命名，不引入未证明的客户端稀有度名；int乘法按x86实际32位中间值）：

```text
delta1 = -trunc((C2-C1)*luckPercent/10000)
delta3 -= trunc((C3-C2)*partyUniqueRatio*0.01) // 若 C3>999999 则 delta3=0
delta1 -= trunc((C2-C1)*partyRareRatio*0.01)
if C1!=0: delta0 = trunc(C0*delta1/C1)
return first i in 0..5 with C[i]+delta[i] >= r
if none: return 0
```

万分比 helper 为 `GetTenThousandPercentage 0x08550FC9`；队伍 ratio 的乘数 `0x08CA1350` 实际float约0.00999999978（百分比）。Luck参数表、重叠率具体计算与玩家分配所有权未完全闭合，Unknown。成功物品会调用 `UseLuckPoint 0x08550B14`，不能据此称每次尝试都会消耗。

`CItemGenRateTable::generateTable 0x0853466C` 遍历 dropType1..4、rarity0..5、basisLevel1..200。窗口从 `-below` 开始，循环 offset<above（上界不含），并限制 basis+offset≤200；遍历该grade的内容，筛 creationRate!=0、rarity相等、`_itemTypeVerify`、排除 isSpecialMonsterDropItem；Hell使用的mode=true跳过某额外virtual predicate。每个候选累加 creationRate，存 `map<cumulativeWeight,itemId>`。

`chooseItem 0x085349A4` 空池返回-1；W=末尾累计权重；**直接 MT.randInt(W)，0..W含两端**，对票据作 lower_bound，取其itemId（若end则退一个）。在正权重且理想均匀模型下，首候选权重概率 `(w1+1)/(W+1)`，其余 `wi/(W+1)`。候选遍历次序由内容容器决定，不能假设按ID排序。这里未证明严格全职业/职业定向池；也未证明 A21 的 `[get hellparty item id]` 实现与此同一表。

## 门票、清理与协议边界

`CheckHaveHellPartyPassItem 0x085A7B8A` 仅对 world-map存在、IsInHellDungeon==true、type==1执行门票检查；其他分支返回true。该函数把 world-map `+0x34` 的 itemId向量传给 `CheckDestroyConditionSealDoor 0x085A7D86`；检查通过置 Party `+0x1865=true`。检查遍历有效队员；先尝试免费券检查（参数delete=false），成功便跳过普通材料；否则要求 dungeon.minLevel>44，并要求向量中**每个**itemId均有足量。

实际扣除在 `UseSealDoorItems 0x085A8038`：逐有效队员先调免费券函数（delete=true），成功便跳过普通材料；否则逐普通itemId删除对应数量，`0x085A81AA` 调 `CInventory::delete_item(INVEN_TYPE=1,slot,count,reason=0x17,true)`，成功后 `0x085A8252` SendUpdateItem。普通数量不是固定表：`getHellPartyEntranceItemNeedCount 0x08891BE6` 指令计算 **`10 + truncTowardZero((40*dungeon.minLevel-1800)/100)`**，例如minLevel45→10、50→12、60→16、70→20（适用前述>44门）。原文 `0x08891BEC..0x08891C13`。

免费券候选来自 world-map `+0x28` 的 `vector<pair<itemId,count>>`，按序查找；命中后delete=false直接结束，delete=true以pair.second数量删除，成功后更新库存并结束。`CheckHellDungeonFreepassItemHaveAndDel 0x085BC86E` 末尾以查到的slot>0返回，不能直接概括为所有slot/删除失败都安全。普通删除失败分支只记录日志然后继续；本次未发现本函数内部跨队员回滚。因此**不能把该参考链描述为原子扣票事务**。具体资源ID、异常路径全部结果与slot0是否可达仍Unknown。普通/免费来源的字段已核验，实际静态资源数值未读取执行。

`reset_field 0x0830A1F6` 清 Battle `+0x1CE/+0x1CC/+0x1CA/+0x1CD` 等状态并清 Battle `+0x78` 的 MapInfo 容器。可证明这条reset清掉组怪状态，不能声称所有回城、断线、取消副本、失败路径均已经覆盖。`CParty::init` 的 world-map重置见前节。证据：[70-lifecycle.asm.txt](evidence/70-lifecycle.asm.txt)。

70协议静态：`DisPatcher_SelectDungeon::read 0x081C7E10` 顺序读short、byte、byte、byte（body五字节）；参数语义须结合对应process继续核验。已闭合 NOTI `0xCF` 的 i32 V百分比。CheckHave失败分支构包 header(1,0x10)、`u8 0,u8 0x1B,u8 n,n*u16 failedPartySlot`；列表来自检查函数在 `0x085A7FF4..0x085A8001` push当前队伍槽0..3，非角色编号。这仍只是70静态布局。柱触发/地图深渊状态包、掉落物所有权/分配、退出深渊时客户端更新、入场type在A21字节的实际含义均 **Unknown**。

## 尚需验证的边界

- A21 `[random intensity]` 与70 V蓄积模型、`[get hellparty item id]` 与70候选池是否对应：未证明。
- PVF实际数值与难度组名称、IsHellMonster标记、组顺序、门票ID数量：本次只逆向读取/字段链，未执行资源。
- `[pass sub base rates]` 的使用链、V全部初始化/持久化/队伍变化规则、取消/失败/断线全路径：未闭合。
- baseDropRate上游完整算法、QuickParty难度修改、Luck/组队重叠率具体算法：未闭合。
- 所有70协议与A21协议对应、真实客户端柱/结算表现、实帧布局：没有WireCaptured或ClientVerified证据。

证据目录中保留完整函数地址、机器码和反汇编，不以旧调查报告或当前实现替代重新核验。以上足以反驳“原生必然固定A8/B6且baseDropRate恒100”“一次全深渊结束才发全部装备”“getNumber(1,百万)必然取1..百万”的先验，但不构成 A21 产品实现规格。
