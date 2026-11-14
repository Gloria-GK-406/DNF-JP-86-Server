# A21 客户端：礼包类 PVF 标签的解析与开启 UI（只读静态取证）

日期：2026-11-14　　性质：ClientStatic（只读反汇编，未启动游戏、未实机验证）

## 0. 证据底座与约定

| 项 | 值 |
|---|---|
| 主镜像 | `E:\Project\DNF\DNFClient-A21-debug\dumps\dnf-dump-20260922-194214-883-23144\DNF-memory-rva.bin`，SHA-256 `AAAE24216E0E260AFA528445775E767D104F658FD68FDAD21EC7BB601B9C8A3D` |
| 复核镜像 | `E:\Project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin`，SHA-256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`；下文引用的 5 段代码（`02999F40..029AA200`、`01519E70..01523300`、`01A77400..01A78620`、`0294F060..0294F700`、`02FE2590..02FE2675`）与主镜像逐字节相同 |
| 客户端 exe | 按工作区 `AGENTS.md`，各客户端 `DNF.exe` SHA-256 `1F8244D3…B00DD`（本次未重算） |
| 地址 | **本文一律写 VA**（ImageBase `0x00400000`），RVA = VA − `0x400000`。协议页 `0x00A0.md` 的 “RVA `0111B50A`” 即本文 VA `0151B50A` |
| 工具 | Python + capstone 5.0.7；字面量解密沿用 `DNF-JP-86-ClientHack/ClientHack/tools/literal_cipher.py` 的算法。脚本与反汇编中间件在 `C:\Users\Gloria\AppData\Local\Temp\claude\3576ed8239f617e2\7af18ebb-4b79-43c5-9f4b-4bb7b6b68904\scratchpad\clientre\`（`dre.py`、`dumpr.py`、`imp_stackable.asm`、`bsw.asm`、`tag_handlers.txt` 等） |

置信度：**Confirmed** = 直接读到的指令；**High** = 指令 + 标签名/调用关系的强推断；**Hypothesis** = 只有间接证据。

### 0.1 标签字符串怎么找到的

- 客户端字面量是加密的（ClientHack `Docs/a21-client-cartography.md`）。现成的 `literals-a21.csv` 里**只有闭合标签**（如 `[/booster info]` @`03638680`），开标签缺失——该 CSV 对这个大函数是不完整的。
- 做法：从 `[/booster info]` 的唯一交叉引用 `029A27A6` 回到所在函数 **`02999F40`**（`ImportStackableScript`，源文件 `DNFShared\GameScript\RDARScriptItem.cpp`，由 `02758AA5` 调用），整函数线性反汇编，对每个 `push imm32` 试解密，得到全部开标签。
- 该函数用一个“字符串 switch”宏：首次运行把 `(标签, __LINE__)` 注册进静态 map（主表 `04E1C0FC`，stackable 类型表 `04E1C0C4`），之后查 map 得到行号再二分/跳表分派。**规律：某标签的处理器 = 紧挨在 `push <该标签>` 之前那条 `jne` 的目标。** 已用 `0294F060` 的跳表逐项核对此规律成立（Confirmed）。完整 238+62 条映射见 scratchpad `tag_handlers.txt`。
- 读数原语：`028A9A70` 读下一个 token 字符串；`028AB5D0(&ok,1)` 试读整数（不是整数则 ok=0、token 留给下一次读）；`028AB5A0(1)` 读整数；`028A9310(&v,1)` 严格读整数；`028AC0F0` 读反引号字符串；`00423EE0` 宽串相等。

### 0.2 stackable 类型码（`[stackable type]` → 脚本对象 `+0x390`）

处理器 `029A0C4B..029A0DE7`（Confirmed）：`[upgradable legacy]`=0x0D、`[cera package]`=0x0E、`[cera booster]`=0x0F、`[booster]`=0x10、`[booster random]`=0x11、`[unlimited waste]`=0x12、`[booster selection]`=0x27、`[usable cera package]`=0x2B。

### 0.3 礼包相关字段在脚本对象里的位置（全部 Confirmed，见各节处理器）

| 偏移 | 内容 |
|---|---|
| `+0x4D4` | `[package data]` / `[package data include usable period]` 的条目 vector（元素 12 字节：id, count, period） |
| `+0x4E8` | `[package data selection]` 组 vector（元素 20 字节 = 一个内层 vector<12 字节条目>） |
| `+0x4FC` u8 | `[skin avatar add socket]` |
| `+0x500` | `[booster info]` 类别 vector（元素 0x28 字节） |
| `+0x514` | `[booster select category]` 的 `std::map<(u8,u8), vector<条目0x44>>` |
| `+0x53C` i32[11] | `[booster info]` 按类别累计的抽取数（tooltip 用） |
| `+0x568` u8 | `[hide booster info]` |
| `+0x8A0/+0x8A4` | `[booster category num]` 的 a、b |
| `+0x8A8` | `[booster selection num]` |
| `+0x8AC` | `[booster category name]` 字符串 vector |
| `+0x8C0` | `[booster category autoget from job]`：vector<vector<jobId>> |
| `+0x8D4` | `[booster category autoget from item]`：vector<vector<itemId>> |
| `+0x8E8` u8 | 顶层 `[equipment upgrade]` |
| `+0x1E0` u8 | `[default select auto job and growType]`（值==1） |

条目结构（`[booster info]` 与 `[booster select category]` 共用，大小 0x44，构造 `02945EB0`）：`+0 itemId`、`+4 累计权重`（仅 booster info）、`+8 u16 count`、`+0xC i32 broadcast 扩展`、`+0x10 u8 upgrade`、`+0x11 u8 separate`、`+0x12 u8 grade`、`+0x13 bool 默认选中`、`+0x14 bool 推荐`、`+0x18 vector<(a,b)> random probability`、`+0x2C vector<(a,b)> force insert avatar option`、`+0x40 u16 装扮扩展1`、`+0x42 u8 装扮扩展2`。

类别名 → 类别号（`0294F060` 开标签、`0294F680` 闭标签，Confirmed）：`[avatar]`0、`[special avatar]`1、`[cera]`2、`[creature]`3、`[equipment]`4、`[stackable]`5、`[etc]`6、`[special creature]`7、`[emblem]`8、`[broadcast]`9、`[charactor]`10、其他 11。

---

## 1. `[package data selection]`（及 `[package data]`）

### 解析（Confirmed）

| 标签 | 处理器 | 读法 |
|---|---|---|
| `[package data]` | `029A2532` | **先清空** `+0x4D4`，再循环读 `(id, count)` 直到下一个非整数 token |
| `[package data include usable period]` | `029A26AF` | **先清空** `+0x4D4`，循环读 `(id, count, period)` |
| `[package data selection]` | `029A25FB` | 每出现一次，循环读 `(id, count)` 成一个局部 vector，整体 push 进 `+0x4E8` 作为**一组**（`029FA5B0`）。不清空——可出现多次，形成多组。无闭合标签 |

注意 `[package data]` 与 `[package data include usable period]` 共用 `+0x4D4` 且都会清空，后出现者覆盖前者（Confirmed）。

### 客户端如何使用

- **Tooltip**（`024392F0` 的尾段 `02439554..024397A6`，Confirmed）：遍历 `+0x4E8` 的每一组，**只渲染元素数 ≥2 的组**，逐个元素用字符串 `0x3306`（非末项）/`0x3307`（末项）格式化“物品名 + 数量”。即把每组显示成“A×n / B×m / …”的一行——“多选一”的展示形态（High；字符串表内容未取）。
- **EVENT_ALL_USER_GIFT（CMD `0x01A7`）流程**（`01A14640`，vtable `032F909C`，同模块有 `CNRDPackageItemSelectionList` / `CNRDPackageItemSelectionItem` 类名字面量 @`01A13520`/`01A13640`）：把礼包拆成步骤——`[package data]` 里本身是 `[cera package]`(0x0E) 的条目 → 步骤(1, itemId)；**每个 `[package data selection]` 组 → 步骤(2, 组序号)**；最后步骤(3, 礼包 id)（Confirmed）。说明在该流程里每组要让玩家选一个（High）。
- **普通开启**：`[booster]`/`[booster random]`（0x10/0x11）开启发的是 **2 字节 `0x00A0`（只有 i16 槽位）**（见 §7），请求里没有任何 selection 信息（Confirmed）。
- **查不出**：`[cera package]`(0x0E)/`[cera booster]`(0x0F) 的“使用”分派没有定位到。物品使用分派器 `01A78470` 的谓词只覆盖 0x10/0x11/0x27/0x0D/0x1C/0x1D/0x13/0x14；按 `[script+0x390]==0x0E/0x0F` 的直接比较只在 `01A145FB/01A146F0`（上面的全员礼物）与 `0246D039/0246D3EC`（物品格绘制）出现。卡在：这两类物品对象的类型字段经客户端的“对齐混淆访问器”（`027BC0F0`… 五路跳转）读取，常量比较不在固定栈槽，模式扫描没扫到；需要从物品类 vtable 入手或实机断点。因此 **“普通开启 cera package 时客户端是否弹出每组多选一的 UI、选择结果用哪个 CMD 发出”目前没有答案**。

---

## 2. `[booster info]` 与其中的 `[charactor]`

### 结构（处理器 `029A2780`，Confirmed）

```
[booster info]
  <类别开标签> [charactor 专属头] pickCount
      itemId weight count [类别扩展]
      ...
  <类别闭标签>
  ...
[/booster info]
```

- 类别号用 `0294F060` 映射；类别 11（未知名）→ 解析失败分支 `029A9967`。
- `pickCount` 用严格读整数（`028A9310`），失败 → 失败分支。
- 条目：`itemId`（读到非整数时检查是否闭标签）、`weight`（**存为累计和** → 条目 `+4`；类别总权重 → 类别 `+8`）、`count`（u16，`+8`）。
- 类别扩展：
  - avatar(0)/special avatar(1)：再读 2 个数 → `+0x40`（**u16**）、`+0x42`（**u8**）。即 5 元组 `itemId weight count ext1 ext2`。
  - broadcast(9)：再读 1 个数 → `+0xC`（i32）。4 元组。
  - 其他类别：3 元组。
- 闭标签必须与开标签同类别（`029A2A2C` 用 `0294F680` 比对），不符 → 失败分支 `029A9962`。
- 类别 0x28 字节：`+0 类别号`、`+4 pickCount`、`+8 总权重`、`+0xC jobId`、`+0x10 u16`、`+0x12 u16`、`+0x14 条目 vector`。
- `[/booster info]` 后（`029A2A9D`）：对每个类别 `script+0x53C[类别号] += pickCount`。

### `[charactor]` 段（Confirmed 解析，语义部分 Hypothesis）

只有类别 10 多一个头（`029A27EA..029A2883`）：

```
[charactor] `[swordman]` A B pickCount
    itemId weight count      ← 3 元组
    ...
[/charactor]
```

- 反引号字符串经 `028A6640` 查表 `04DB322C`（表由 `02FE2590` 初始化，Confirmed）：`[swordman]`0、`[fighter]`1、`[gunner]`2、`[mage]`3、`[priest]`4、`[at gunner]`5、`[thief]`6、`[at fighter]`7、`[at mage]`8、`[demonic swordman]`9、`[creator mage]`10、`[at swordman]`11、`[knight]`12、`[demonic lancer]`13；查不到返回 14（表第 15 项恰是 `[all]`）。注意格式是**带方括号**的职业名。结果存类别 `+0xC`。
- A、B 两个整数按 u16 存到类别 `+0x10/+0x12`；任一缺失则整个 `[booster info]` 静默结束（`029A2A87`）。A/B 的含义没有找到消费方（Hypothesis：转职/成长类型相关）。

### 客户端是否本地按职业筛选

- **开启请求不带任何职业信息**：0x10/0x11 开启只发 2 字节 `0x00A0`（§7，Confirmed）。所以抽取与按职业筛选只能在服务端做。
- 客户端对 `[booster info]` 的已知消费：
  - 开启前预检 `01A78160`：遍历所有类别所有条目，只检查 itemId==0x335 的特殊条件，不看职业（Confirmed）。
  - Tooltip `024392F0`：除非 `[hide booster info]`，按 `+0x53C[类别]` 输出每类“N 个”（字符串 `0x116FF+类别号`，含 charactor=10）；若物品类型为 0x11（`[booster random]`）则只输出总数（字符串 `0x330A`）（Confirmed）。不看职业。
- 没找到读取类别 `+0xC/+0x10/+0x12` 的代码，所以**无证据表明客户端会按 `[charactor]` 做本地筛选**（Hypothesis：客户端只做展示计数）。

---

## 3. `[booster selection num]` 为 0、`[booster select category]` 与 `[booster category num]` 的数字含义

### 3.1 解析（Confirmed）

| 标签 | 处理器 | 读法与存放 |
|---|---|---|
| `[booster category num]` | `029A2EDD` | 读 n；n>2 忽略；n≥1 读 a；n==2 读 b。存 `+0x8A0=a`、`+0x8A4=b`（n==1 时 b=0，n==0 时都 0） |
| `[booster selection num]` | `029A36E9` | 一个整数 → `+0x8A8` |
| `[booster category name]` | `029A3757` | 循环读反引号字符串 → `+0x8AC` |
| `[booster category name type]` | `029A3806` | 读 n 再读 n 个字符串，**丢弃**（客户端不用） |
| `[booster category autoget from job]` | `029A387F` | 重复 `[category] `job` `job`…`（`02949D90`，**不带方括号**的职业名表，例：`swordman`），每个 `[category]` 成一个 jobId 列表 → `+0x8C0`，直到闭标签 |
| `[booster category autoget from item]` | `029A39A7` | 重复 `[category] id id …` → `+0x8D4`，直到闭标签 |
| `[booster select category]` | `029A2FA0` | 见 §4；头部两个整数 **c1 c2** |

### 3.2 两个数的含义（Confirmed）

BoosterSelectWindow（`UI/BoosterSelect/BoosterSelectWindow.xui`，打开函数 `01521620`）把 `[esi+0x1F0]` 指向 `script+0x8A0`，有两个下拉控件：控件 1 `[+0x1B4]`、控件 2 `[+0x1BC]`。

- `[booster category num] n a b`：n = 分类层数（0/1/2），a = 第一层选项数，b = 每个第一层选项下的第二层选项数。
- `[booster category name]` 的下标布局（`01521740..015217E9`、`01521347..015213E4`）：`name[0]`=控件 1 标题；`name[1..a]`=第一层选项；`name[a+1]`=控件 2 标题；`name[a+2 + c1*b + j]`（j=0..b−1）= 第一层第 c1 项下的第二层选项。
- 当前选择（`01520C30`）：`[+0x1E8] = 控件 1 选中下标`（控件不显示且非自动选择时为 0），`[+0x1EC] = 控件 2 选中下标`（不显示为 0）。随后以键 `(u8 [+0x1E8], u8 [+0x1EC])` 在 `script+0x514` 中查，命中则把该类别的条目 vector 复制到窗口 `[+0x1D4]` 作为当前可选列表。
- 因此 **`[booster select category] c1 c2` 的两个数 = 第一层下标、第二层下标（0 起），是 map 键**；n=0 时只有键 (0,0) 可用。

### 3.3 `[booster selection num]` 的含义（Confirmed）

窗口逻辑（选择点击 `0151BC52..0151BD25`，OK 按钮启用 `01521D1B..01521D6F`，发包 `0151C6C0`，校验 `0151C440`）：

| selection num | UI 行为 | 发出的 selectedIds |
|---|---|---|
| N>0 | 点物品往 `[+0x2E8]` 列表里加 id，已满 N 个不再加；N==1 为单选替换；OK 按钮**仅当已选个数 == N 时可用**；计数文案 `0x893E` “%d/%d” | 列表 `[+0x2EC..0x2F0]` 中的 id，按点选顺序 |
| 0 | 不需要点选，文案 `0x893D`；只要当前类别条目非空 OK 就可用 | **当前类别（c1,c2）下的全部条目 id，按 PVF 声明顺序**（`0151C7D4..0151C863` 遍历 `[+0x1D4]` 步长 0x44 写 `+0` 的 itemId） |

所以 **selection num=0 且多个奖励 = “选一个类别，类别内全部发放”**（不是“全部礼包内容都给”，而是 c1/c2 指定的那一个类别的全部条目）。

### 3.4 自动选择类别（autoget，Confirmed）

打开时若 `[+0x8C0]` 或 `[+0x8D4]` 非空，且 **b==0 且 selection num==0**（`01521906..01521912`）：

- 按职业（`0151AEC0`）：取当前角色 jobId，找第一个包含它的 `[category]` 序号；
- 按物品（`0151AFE0`）：找第一个“玩家持有其中任一物品”的 `[category]` 序号（`05229BE0`，参数 0x2A）；
- 由 `0151B760` 在候选中定出 c1，锁定控件 1、置 `[+0x2FC]=1`；找不到 → 提示框（按物品时列出所需物品名，消息 `0x81BA`/`0x209`）并失败。
- 置位后窗口 process（`01521D71..01521DB9`）**不等玩家操作直接调用 `0151C6C0` 发包**：context1 = 自动得到的 c1，context2 = 0，selectedIds = 该类别全部条目。

`[default select auto job and growType]`（→`+0x1E0`）与窗口标志 `[+0x2FD]/[+0x2FE]` 相关：控件 2 会按角色职业名/成长类型名（`01861380`、`016490A0`）自动选中同名选项（`01521DBE..01521ED8`，High）。

---

## 4. `[booster select category]` 块内各标签

### 4.1 块语法（处理器 `029A2FA0`，Confirmed）

```
[booster select category] c1 c2
    （以下任意顺序、可多次；遇到 [/booster select category] 结束）
    [booster equipment upgrade] u
    [booster equipment separate] s
    [booster equipment grade] g
    [default select] N id1 … idN
    [recommend] N id1 … idN
    [random probability] a b a b …        ← 要求此前 [booster equipment upgrade] == 255
    [force insert avatar option] a b a b …
    <类别开标签>  条目…                     ← 0294F060 的 12 种名字之一
[/booster select category]
```

- 块内**按出现顺序依次比较**：上述 7 个修饰标签 → `[/booster select category]` → 其余一律当类别开标签交给 `0294F060`。
- 类别条目（`029A33BA..029A361F`）：循环读 `itemId`，不是整数就结束本类别；然后读 `count`（u16）；avatar/special avatar 再读 u16 → `+0x40`、u8 → `+0x42`（**4 元组**）；broadcast 再读 i32 → `+0xC`（3 元组）；其他类别 **2 元组**。
- **这里不检查闭标签，也不拒绝类别 11**：`[/equipment]` 之类会被映射成 11、后面没整数就无害；但若块内混进一个别的带数字的标签，它的整数会被当成类别 11 的 `(id,count)` 吞进同一个键的条目里（Confirmed）。
- 各类别（avatar、equipment、stackable、cera、etc…）都合法，`[stackable]`、`[cera]` 在这里就是“类别 5/2 的物品列表”（Confirmed）。
- 同一个 `(c1,c2)` 键的所有类别条目合并成一个 vector（键→vector，`029A3691` → `0296C3D0` + `01520AC0`）。
- 块结束时以 `(u8 c1, u8 c2)` 写入 `script+0x514`（`029A365B..029A36A4`）。

### 4.2 各修饰标签（解析 Confirmed，语义见括注）

| 标签 | 存放 | 作用 |
|---|---|---|
| `[booster equipment upgrade]` | 块局部 | **只对类别 4（equipment）**的条目生效：复制进条目 `+0x10`（u8）。（强化等级，High） |
| `[booster equipment separate]` | 块局部 | 同上 → `+0x11`。（分离/增幅，High，按标签名） |
| `[booster equipment grade]` | 块局部 | 同上 → `+0x12`。（品级，High，按标签名） |
| `[default select]` | 块局部 id 列表 | 读条目时若 itemId 在列表中 → `+0x13=1`。窗口建格子时（`0151E9C9..0151EA92`）默认选中项自动加入已选列表 `[+0x2E8]` → **默认被选上**（Confirmed） |
| `[recommend]` | 块局部 id 列表 | → `+0x14=1`，格子上显示推荐标记（`0151E990`，High） |
| `[random probability]` | 块局部 pair 列表 | 若当前 `[booster equipment upgrade]` 不是 255 → **整个脚本解析失败**（`029A3207..029A3211` 跳失败清理）。pair 列表复制进每个后续条目 `+0x18`。（Hypothesis：255=随机强化，pair=(强化等级, 权重)） |
| `[force insert avatar option]` | 块局部 pair 列表 | → 条目 `+0x2C`（Hypothesis：强制附加的装扮属性） |

要点（Confirmed）：修饰值在块开始时清零，**之后一直生效到块结束**，作用于**出现在它之后**的条目；不会因换类别而重置。`[default select]`/`[recommend]` 必须写在相应物品之前才会标记上。

这些强化/分离/品级/概率值**不在请求里**：0x00A0 只带 itemId；服务端必须自己按 `(c1,c2)` 找到条目再套这些属性。

### 4.3 不在块内的同名标签

- `[action usable place]`（`029A12F1`）：顶层标签，循环读反引号字符串 → 地点 id（`02937350`）→ `+0x3B0`。是使用地点限制，与选择无关。
- `[booster category num]`：顶层标签（§3.1）。
- `[booster equipment upgrade]` 等只在 `[booster select category]` 块内被识别；顶层的 `[equipment upgrade]`（`029A372E`）是另一个字段 `+0x8E8`。

如果服务端看到这些出现在“选择段里”，多半是服务端把 `[booster select category]`…`[/booster select category]` 之外的内容也算进了选择段，或 PVF 缺闭合标签——客户端的块只认上表那 7 个修饰标签。

---

## 5. 自选礼包跨类别重复物品 ID

- 客户端只在**当前键 (c1,c2)** 的条目里校验 selectedIds：`0151C440` 先用键查 `script+0x514`，再逐个确认每个待发 id 在该键条目 vector 中出现（不在 → 消息 `0x4C40` 并取消发送）（Confirmed）。
- 0x00A0 带 context1=c1、context2=c2，所以**跨类别重复 ID 不构成歧义：服务端应以 (context1, context2) 定位类别，再在类别内按 id 找条目**（Confirmed）。
- 同一类别内若重复出现同一 id（例如数量或强化不同），请求只有 id，客户端校验“任一命中即可”，服务端无法区分——这种情形客户端协议本身表达不了（Confirmed 于协议形状）。selection num=0 时客户端会把重复 id 原样发两次。

---

## 6. `[avatar]`/`[special avatar]` 5 元组的两个扩展字段

- **宽度（Confirmed）**：`[booster info]` 中 ext1 存为 **u16**（条目 `+0x40`，所以 −1 → 0xFFFF），ext2 存为 **u8**（`+0x42`）。`[booster select category]` 中 avatar 是 4 元组 `id count ext1 ext2`，落在同样两个字段。
- **ext1 的用途（显示路径，High）**：自选窗口建格子（`0151E525..0151E549`）把 `u16 +0x40` 当作**该装扮物品 `[avatar type select]` 表的行下标**：装扮脚本 `+0xE10` 起是 32 字节行的 vector（处理器 `02994986`，tag 由 `0299295B` 的 jne 规律确认），每行含若干整数和最多 5 个经 `0295A7E0` 解析的 `[A socket]`/`[B socket]`/`[C socket]`/`[D socket]`… 名称；下标有效则把该行的插槽类型数组（行 `+0x10` 个数、`+0x14` 起 u16）设给展示物品（`0151E836..0151E937`，vtable `+0x110`），下标越界（含 0xFFFF）则走装扮自身的默认值。即 **ext1 选的是“装扮插槽（徽章孔）布局”这一行，而不是期限天数**——“扩展1=期限天数”的猜测与此不符（Hypothesis 降级为不支持）。
- **ext2 的用途（Hypothesis）**：u8 `+0x42` 通过混淆访问器写入展示物品的一个字段（`0151E858..0151E908`）后交给 vtable `+0x14C`。宽度与 0x00A0 / 0x0207 尾部的 `u8 ability` 一致，推测是装扮属性编号；未找到直接证据。
- `[booster info]`（随机礼包）这一侧没有找到读取 `+0x40/+0x42` 的客户端代码；客户端只发 2 字节请求，这两个值如何影响发放由服务端决定。

---

## 7. 开启请求：用哪个 CMD、context 与 selectedIds

| 物品类型 | 客户端路径 | 请求 |
|---|---|---|
| `[booster]` 0x10 / `[booster random]` 0x11 | 使用分派 `01A78470` → 谓词 `01A77A20`（类型==0x10/0x11，Confirmed）→ `01A782C0`：先 `01A78160` 预检，然后 `01A783EC..01A7840C` 发 **CMD `0x00A0`，body 只有 i16 槽位**（2 字节），播 `GIFT_OPEN`。物品 id `0x28EF04`/`0x28F16C` 特例先弹确认窗 0x2C0，确认后由 `01267660`（`012676A0` 构包）发同样的 2 字节 0x00A0 |
| `[booster selection]` 0x27 | 谓词 `01A77B50`（==0x27）→ `01A774A0` 打开 BoosterSelectWindow（窗口 id 0x199）→ OK → `0151C6C0`（`0151C764..0151C8B7` 构包） | **CMD `0x00A0`**：`i16 slot, u8 context1, u8 context2, i32 selectedIds[n], u8 0` |
| 自选中含需选属性的装扮 | 装扮属性窗（同模块字面量 `interface/cashshop/cs_setavataedit.img`、`etc/NewbieRecommendSelectOption.etc`，`01209FE0`）先校验（`01206730`，失败消息 `0x1A0E`），再取窗口 0x199 调 `0151B490`（`0151B4FC..0151B71A` 构包） | **CMD `0x00A0`**：同上，但尾部 `u8 m, (i32 itemId, u8 ability)[m]`，取自属性窗 `[+0x424]` 列表元素的 `+0x14`/`+0x1C` |
| `[usable cera package]` 0x2B（装扮礼包） | 同一种装扮属性窗（`[+0x420]` 物品、`[+0x424]` 列表）→ `012BBA60` | **CMD `0x0207`**：`i16 slot, u8 n, (i32 itemId, u8 ability)[n]`（与 0x00A0 尾部是同一种元素） |
| `[cera package]` 0x0E / `[cera booster]` 0x0F | **未定位**（见 §1 “查不出”） | 未知 |

0x00A0 选择形态各字段的来源（Confirmed）：

- `slot`：`016EF980(物品)` 得到的背包格号。
- `context1` = 窗口 `[+0x1E8]` = 第一层下标 c1；`context2` = `[+0x1EC]` = 第二层下标 c2；二者正是 `[booster select category] c1 c2`（§3.2）。无分类时都为 0；autoget 时 c1 为自动得到的类别、c2=0。
- `selectedIds`：selection num>0 时为玩家选中（含 `[default select]` 预选）的 id，个数恰为 selection num；=0 时为该类别全部条目 id（§3.3）。线上无独立计数字段，与协议页一致。
- 尾字节：无装扮属性窗时恒为 `u8 0`（`0151C8A9`）。
- 发送前双重校验：`0151C440`（键存在、每个 id 在该键条目内，Confirmed）+ `0151C8D0`（待发条目中若有装备表里满足某条件的物品则先弹窗 0x19A，回调 `0151CD00` 再发；条件含义 Hypothesis）。
- 装扮属性窗那一行：`0120A04B → 0151B490` 的调用与构包是 Confirmed；**从自选窗口何时转入装扮属性窗**的触发条件没有追到（Hypothesis：所选条目含可选属性的装扮时）。

---

## 8. 查不出 / 未覆盖

1. `[cera package]`/`[cera booster]` 的开启分派与是否弹出 `[package data selection]` 多选一 UI（§1）。卡在类型字段的混淆读取，静态扫描没命中；建议从 `CNRDPackageItemSelectionList`（`01A13520` 附近）的 vtable 反查创建者，或实机在 `029FA5B0` 写入的数据上下读断点。
2. `[charactor]` 头部两个 u16（A、B）的含义：未找到读取类别 `+0x10/+0x12` 的代码。
3. `[booster info]` 中装扮 ext1/ext2 在随机礼包这一侧的客户端用途：未找到；§6 的结论来自自选窗口的展示代码。
4. `[random probability]`、`[force insert avatar option]` 的 pair 语义只到“存进条目”为止；窗口里这两个 vector 被复制（`0151CB4C`、`0151CCC7`）但具体展示/用途未追。
5. 字符串表 id（`0x3306/0x3307/0x116FF+n/0x330A/0x893D/0x893E/0x4C40`）的文本未取，文中对文案的描述是按用法推断。
