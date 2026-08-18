# 客户端内嵌脚本运行时：Squirrel 已移除，Lua 半铺未通

Act.86 客户端里同时留着两套脚本系统的痕迹：一套 Squirrel（`.nut`），一套 Lua。
本文记录二者各自处在什么状态、Lua 环境在系统里怎么组织、以及做客户端扩展时
哪些是可以直接站上去的地基。

数据模型侧（`.act` / `.obj` / `.atk` / `.apd` 能表达什么、扩展面在哪）见
[BehaviorDataModel.md](BehaviorDataModel.md)。

## 取证基线

| 对象 | 标识 |
|---|---|
| 归档 | `DNFClient-play/Script.pvf`，58,781,889 B，2015-09-24，588,559 文件 / 4,558 chunk |
| 可执行文件 | `DNF.exe`，25,228,288 B，2024-12-01，ImageBase `0x00400000`，SizeOfImage 107,556,864 |
| 内存转储 | `DNFClient-debug/dumps/dnf-dump-20260817-100117-955-7824/DNF-memory-rva.bin`，整镜像，holes=0 |

归档读法：[`Tool/PvfProbe`](../../Tool/PvfProbe/)，`PVF_ARCHIVE_PATH` 指向 `Script.pvf`。
本文两类事实各由一条命令复现：

```text
PvfProbe files [substring]   全部 588,559 条文件表记录：dataType、size、路径
PvfProbe pool  [substring]   全部内部字符串：UTF-8 790,868 条 + UTF-16 205,019 条
```

**两者之差是本文若干结论的直接来源**：字符串池不随文件被剔除而收缩，所以
「池里有、文件表里没有」的路径说明该文件是**打包时被抽走的**，而非从未存在。
最短的例子——

```text
PvfProbe files .nut     0 of 588559 file(s)
PvfProbe pool  .nut     37 + 0 entries shown
```

`DNFClient-play` / `-debug` / `-ClientHackLab-20260812` 三处的 `Script.pvf` 是同一文件的硬链接。

### 转储的局限（读下文前必须知道）

`DNF.exe` 是加壳的：15 个 section，`.X?9` / `.,_W` / `.lA0` 这类垃圾名，除末三个外
raw size 全为 0。**磁盘上的静态字符串搜索完全无效**——连 `Script.pvf` 都搜不到。

上表那份转储是 `analysis_only` / `pre_feature_plugins` 阶段抓的，只解出很少明文：
≥8 字符的可打印串 10,666 处、RTTI 类型描述符 373 个。**游戏自有的字符串字面量基本不是明文**
（`.pvf`、`ImagePacks`、`sprite`、`stackable` 全部 0 命中）。

但存在一条关键的不对称，本文的判断全建立在它上面：

> **静态链接进来的第三方库，其字面量原样保留。**
> zlib（`invalid distance code`、`inflate 1.2`）、CryptoPP（14 处）、CRT
> （`StringNarrow: wcstombs() call failed`、`vtable constructor failed: %s`）、FMOD、
> 以及 Lua 的全部 rodata —— 都是明文。

字符串混淆只处理自家代码。所以对第三方库而言，「搜不到」等价于「没链进来」。

## 一、Squirrel：VM 不在了，注册表还在

### 1.1 注册表：`etc/nutappendagedefines.etc`

7,720 字节的 token 脚本，是 `.nut` 附加效果（appendage）系统的派发表：

```text
[nutAppendageInfo]
[path]  Appendage/Character/ap_PowerOfDarkness.nut
[key]   onStart
[name]  onStart_appendage_PowerOfDarkness
[/nutAppendageInfo]
```

- **190 条 `[nutAppendageInfo]` 映射**，189 个不同的 `[name]`
- **37 个不同的 `.nut` 路径**：`Appendage/Character/*`、`Character/ATMage/*`、
  `Character/CreatorMage/*`，覆盖 ATMage、Avenger、CreatorMage、PowerOfDarkness、
  scythe_mastery 这批 2014–2015 期职业
- **19 种 `[key]` 回调**：`onStart` 36、`onEnd` 36、`proc` 35、`prepareDraw` 27、
  `isEnd` 27、`drawAppend` 7、`onSetHp` 4、`isDrawAppend` 3、`onDestroyObject` 2、
  `onDamageParent` 2、`onApplyHpDamage` 2、`getImmuneTypeDamageRate` 2、
  `procStatus` / `onVaildTimeEnd` / `onSourceKeyFrameFlag` / `onChangeState` /
  `isInAuraRange` / `enableAuraEffect` / `disableAuraEffect` 各 1

同文件还有 `[nutAppendageEffect]` 块，把 `.nut` 绑到具体 `.ani`。

回调名的形状（`onStart` / `proc` / `isEnd` / `drawAppend`）是 Squirrel class 的方法集。

### 1.2 脚本文件：一个都不在

全量枚举 588,559 个文件条目：**扩展名 `.nut` 的文件数为 0**，`appendage/character/`
目录下文件数为 0。37 个路径全部是**只存在于字符串池的悬空引用**。

对照之下，**它们的美术资源一个不少**：`atmage` 相关 637 个文件、
`character/priest/animation/avengerawakening/powerofdarkness*.ani.als` 等 113 个动画条目都在。

> **资源留下了，脚本被抽走了。**

### 1.3 二进制侧：零痕迹

`SQUIRREL_VERSION`、`Squirrel 2/3`、`sqstdlib`、`SQVM`、`sq_compile`、`sqstd_`、
`the index`、`wrong type of parameter`、`unreachable code`、`callable expected`、`.nut`
—— 转储中命中数**全部为 0**。

结合上文的不对称（zlib / CryptoPP / CRT / FMOD / Lua 的字面量都是明文），
**Squirrel 没有被链接进这份客户端**。

（排除误报：池里 80 处 `squirrel` 全是盗贼技能 `FlyingSquirrel`，与语言无关。）

### 1.4 推论与不确定性

`[name]` 那个扁平化 C 标识符（`procStatus_antifreezing`）是关键。若 VM 还在，
`[path]` + `[key]` 已经足够定位一个 Squirrel 方法，第三个字段纯属多余；
它的形状恰是**原生函数注册表的查找键**。

最可能的历史：把 `.nut` 逐个改写成 native 实现，保留 `.etc` 作为派发表，
VM 和脚本源一起删除。

**未验证**：2024 版客户端是否仍在读这份 `.etc`。这需要运行期取证。

## 二、Lua：VM 在位，标准库全开

### 2.1 存在性

转储 RVA `0x029BA3C0` 处是原版 banner：

```text
$Lua: Lua 5.1.5 Copyright (C) 1994-2012 Lua.org, PUC-Rio $
```

RVA `0x029B9C00`–`0x029BC130` 是一整块 Lua rodata，含 VM opcode 名表
（`MOVE` `LOADK` `GETGLOBAL` … `CLOSURE` `VARARG`）与完整的编译器报错串
（`chunk has too many syntax levels`、`function or expression too complex`）——
只有真正编进 lvm/lparser/ldebug 才会有，不是残留的字符串块。

该地址落在主镜像 `.code`（RVA `0x023D3000` + `0x7D8000`，壳在运行时解开的段）内，
**属于主程序自身，不是旁挂 DLL**。

### 2.2 `luaL_openlibs` 注册表 —— 八个库全开

`linit.c` 的 `lualibs[]` 位于 **VA `0x02DBA5E0`**：

| 库 | `luaopen_*` |
|---|---|
| base（空名） | `0x004105D0` |
| package | `0x00414E30` |
| table | `0x00416A70` |
| io | `0x004123B0` |
| os | `0x00413330` |
| string | `0x0040DC90` |
| math | `0x0040AA50` |
| debug | `0x004091B0` |

终止项齐全，顺序与 Lua 5.1 原版 `linit.c` 完全一致。

该表只被 `luaL_openlibs` 使用——**表在，等于全库注册**，包括 `io`、`os`、`debug`、`package`。

### 2.3 `base_funcs[]`

位于 **VA `0x02DBA148`**，24 条：

```text
assert         0x0040F510    next           0x0040ECD0    setmetatable   0x0040E370
collectgarbage 0x0040EB20    pcall          0x0040F860    tonumber       0x0040DF70
dofile         0x0040F490    print          0x0040DD40    tostring       0x0040FA80
error          0x0040E190    rawequal       0x0040E800    type           0x0040EC10
gcinfo         0x0040EAE0    rawget         0x0040E8F0    unpack         0x0040F5C0
getfenv        0x0040E5D0    rawset         0x0040E9E0    xpcall         0x0040F940
getmetatable   0x0040E280    select         0x0040F7C0
loadfile       0x0040F170    setfenv        0x0040E670
load           0x0040F340
loadstring     0x0040F050
```

Lua 代码整块占 VA `0x004091B0`–`0x00416A70`，约 90 KB 连续，排在 `.text` 最前面
（链接顺序第一）。

### 2.4 复现方法

两张表都是 `luaL_Reg` 数组（`{const char* name; lua_CFunction func;}`）。取法：
在转储里定位名字串（如 `package` 于 RVA `0x029B9CE0`），把 `ImageBase + RVA`
打包成小端 4 字节，在数据区搜该指针，命中处即数组中的某一项；按 8 字节步进
前后展开即可读出整表。

**核心 API（`lua_newstate` / `lua_pcall` / `lua_gettop` / `lua_pushcclosure` 等）
不在任何表里**，同法取不到——这是下文不建议复用游戏 VM 的主要原因。

## 三、Lua 在数据侧怎么接

### 3.1 入口：`[ai pattern]` 按扩展名分派

全库 587,658 个 token 脚本扫描，**只有 3 个文件引用 `.lua`**：

| `.mob` | tag | 槽位 |
|---|---|---|
| `clientonly/lua/boss_spiderprince/spider_prince.mob` | `[ai pattern lua]` | Event / DestinationSelect / Action / MoveMethod |
| `clientonly/lua/metalgear/metalgear_(70034).mob` | `[ai pattern]` | Event / DestinationSelect / Action_easy / MoveMethod |
| `clientonly/lua/powerstation/rockslotor/rockslotor.mob` | `[ai pattern]` | Autonomic / Eventually |

上线版本的同一只怪：

```text
monster/newmonsters/spider/slotorblind/slotorblind.mob
[ai pattern] [normal] ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai [/normal]
```

**同一个 `[ai pattern]` 槽位，靠扩展名分派 `.ai` / `.lua`** —— 不新增管线，
只让老槽位多认一种文件。`[ai pattern lua]` 是显式变体，仅 1 个文件在用，
应为更早的写法。

`monster.lst` 里对应的上线 id：SlotorBlind 61326、RabbitMan 61450、rockslotor 61273、
spider_prince 65017 / 65618、Metalgear 70034。**引用 `.lua` 的三份都在 `clientonly/`**，
服务端永不刷；`.lua` 文件本身则有一半已铺到上线目录（`monster/newmonsters/*/ai/*.lua`），
只差把 `[ai pattern]` 那一行的扩展名改掉。

### 3.2 沙箱与加载器都在 Lua 层

`clientonly/lua/minimumrequire.lua`（UTF-16 文本，注释是韩文按 GBK 存的）：

```lua
io = {};                              -- 直接抹掉 io，注释写"干脆让它没法用"
UtilBridge = {};                      -- 原生桥；注释：客户端最多给 4096 字节
function loadDNFScript(modulename)
    text = UtilBridge:readAllTexts(modulename);
    if text then return assert(loadstring(text)); end
    return "";
end
package.loaders = {};
table.insert(package.loaders, loadDNFScript);
```

`require` 被整个重定向到 PVF。这同时**证明 base 与 package 库确实被 open 了**
（`loadstring` / `package.loaders` / `table.insert` 都在被实际使用）。

注意沙箱只是 Lua 层的赋值覆盖，不是 VM 层裁剪——`luaL_openlibs` 依然开满（见 2.2）。

### 3.3 暴露给脚本的原生 API

```lua
require("/ClientOnly/Lua/AI/AIBaseScripts.lua");
function L.begin_pattern()
    if Patrol:isTargetAttackArea(100, 100, 15, 15) then
        if Battle:isSkillCooltime(1) then Idle:setAction(-1)
        elseif Etc:getrandomIsPercent(50) then Idle:setAction(1) end
    end
end
setDefault(L);
```

全局表 `Patrol` / `Battle` / `Idle` / `Etc` 加函数 `setDefault`，对应老 `.ai` 的
patrol / battle / idle 三个状态。`aibridge.lua` 里 `getObjectInfo`、`isSkillCooltime`
等一律 `return true` / `return 0`，是纯 Lua 桩——桥当时尚未接完。

### 3.4 服务端侧的残影

字符串池里有 `serveronly/lua/drop` 及其 `document` / `common` / `event` 三个子目录名，
以及 `worlddrop.lua`、`golddrop.lua`、`conditionaldrop.lua`、`monsterspecificdrop.lua`、
`clearrewarddrop.lua`、`dragonroaddrop.lua`、`pcroomdrop.lua`、`userderiveddrop.lua`、
`objectdrop.lua`、`monsterrandomdrop.lua`、`readytodrop.lua`、`workercheck.lua`、
`runlist.lua`、`func_list.lua`、`basicdata.lua`、`basicfunction.lua`、
`userderivedrulesetdata.lua` 等文件名。

**对应文件数为 0** —— 客户端打包时剔除了 `serveronly/`。说明 Lua 当时同时在往服务端铺，
且掉落规则曾是 Lua 化的目标之一。

### 3.5 时间线

`minimumrequire.lua` 里留着 2015.05.11 的开发计划注释，列着待办事项；
`Script.pvf` 打包日期 2015-09-24。

34 个 `.lua` 里 17 个在 `clientonly/`，还有 `_temp/`、`tester.lua`。
同期 `.ai` 15,892 个、`.aic` 1,052 个仍是绝对主力。

> **状态判定：Squirrel 已拆除、Lua 铺到一半、旧 `.ai` 仍是生产系统。**

## 四、对客户端扩展工作的结论

**不建议复用游戏那个 Lua state**，理由三条：

1. **核心 API 白拿不到**（见 2.4），定位成本高于自己编一份。
2. **拿到也受制于人**：`io` 被抹、`package.loaders` 被换，state 生命周期归游戏管。
3. **自带成本极低**：Lua 5.1.5 编进插件约 200 KB，零地址依赖；同进程两个 VM 互不干扰。

游戏内嵌那份的价值转成两条：

- **可行性背书**：这个进程已经跑了九年 Lua。Lua 5.1 无 JIT、纯解释、不写可执行内存，
  对反作弊近乎透明。
- **交叉校验基准**：自编 5.1.5 行为存疑时，2.3 那 24 个地址是现成对照组。

唯一必须用游戏 state 的场景是复活 `[ai pattern] *.lua` 原生分派，但其天花板只有怪物 AI，
与客户端扩展是两件事，建议解耦。解耦后「让 PVF 里的 `.lua` 被识别」反而更容易——
那些就是普通 Lua 源码，自家 loader 一样能读能跑。

落点应为 [`ClientHack/plugins/`](../../ClientHack/plugins/) 下的新插件（如 `0200_lua_host`），
**不是 Az.dll**：插件跨分支共享，Az.dll 不共享。

## 五、未验证事项

1. **2024 版客户端是否仍执行 `[ai pattern]` 的 `.lua` 分支** —— VM 在镜像里是确定的，
   但 AI 派发器可能已砍掉该分支。
2. **是否仍读 `etc/nutappendagedefines.etc`** —— 若读，说明 native 重写版仍按该表派发。
3. **`lua_State` 的产生位置与生命周期** —— 可从引用 `LUA_PATH` 的函数
   （`luaopen_package` 的 setpath）下断，其首参即 `lua_State*`。
4. **完整 RTTI / vtable** —— 现有两份转储都是早期阶段，只解出 373 个 RTTI，
   大半还是 `std::tr1::bind` 包装。**任何 UI 绑定工作都以「抓一份进游戏之后的转储」为硬前置。**
