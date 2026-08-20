# 客户端行为的数据模型与扩展面

回答一个问题：**给引擎喂数据能做到什么程度，从哪一步开始必须写原生代码。**

结论先行——界线不在「技能 vs 非技能」，而在**角色自身的动作状态机**上：

- 角色技能的动作、连段、取消、位移 —— **原生硬编码，数据侧零入口**
- 除此之外的几乎一切（实体、投射物、召唤、伤害判定、buff、特效、怪物行为）
  —— **完全数据驱动，已有 5 万+ 文件在跑**

脚本运行时侧（Lua VM 现状、地址表、`[ai pattern]` 分派）见
[ScriptRuntimes.md](ScriptRuntimes.md)；把这套模型当成扩展手段来用能走多远，
见 [ExtensionSurface.md](ExtensionSurface.md)。

## 取证基线

归档 `DNFClient-play/Script.pvf`（2015-09-24，588,559 文件），
读法见 [ScriptRuntimes.md](ScriptRuntimes.md#取证基线)。

本文的统计全部由 [`Tool/PvfProbe`](../../Tool/PvfProbe/) 复现，两条命令：

```text
PvfProbe files [substring]   文件表；按扩展名切分即得下面的规模表
PvfProbe tags  <ext>         该文件类型的标签词汇表，例如 tags act / tags obj / tags apd
```

`tags` 的扩展名形式扫的是**全库该类型的每一个文件**，不经过 `.lst`——
问的是「这种文件能表达什么」，而不是「这批文件用了什么」。这正是判断
一个功能能否用数据搭出来所需要的那个问题。

按扩展名的规模（前若干）：

```text
ani 261,135   equ 126,971   act  52,066   stk 32,943   ai  15,892   obj 15,591
atk  13,928   til  12,255   key  11,757   ptl  9,139   map  8,180   qst  6,986
als   6,270   mob   4,634   skl   2,740   dgn  1,233   aic  1,052   etc  1,021
xui     867   apd     644   lua      34
```

数据类型只有两种：token 脚本 587,658 个（`DataType == 1`）、UTF-16 文本 901 个
（`DataType == 3`，即 `.lua` 与 `.xui`）。

## 一、技能定义里没有行为

`skill/swordman/tripleslash.skl`，4,875 字节，全文只有这些东西：

```text
[type] [active]        [skill class] 1        [maximum level] 60
[command] (RIGHT) & (SKILL)                   [consume MP] 12 120
[cool time] 6000 6000                         [static data] 600 -1000
[level info]  6 158 198 198 47 138 ...        <- 60 级 x 6 列纯数值
[dungeon] / [pvp] / [death tower] / [warroom] <- 四套环境各一份数值
```

扫完全部 2,740 个 `.skl`，标签词汇表里**没有任何一个字段指向动作、动画或攻击信息**。
出现频次前列的全是 `[level info]` 3,782、`[static data]` 3,221、
`[skill fitness growtype]` 2,560、`[icon]`、`[maximum level]`、`[required level]`
这类元数据与数值。

再看动作文件的归属：**52,066 个 `.act` 里，`monster/` 33,014、`passiveobject/` 18,290，
`character/` 下只有 1 个，还是 UI 用的**（`ui/selectcharacter/action/alpha.act`）。

> **判定：角色技能的行为是 C++ 写死的**，按技能在 `skill/<class>skill.lst` 里的
> index 索引；`.skl` 只负责喂数值。

## 二、数据驱动的那一半

### 2.1 `.act` 是一门行为脚本语言

不是配置文件。52,066 个文件在跑它，标签词汇表（按出现次数）：

| 类别 | 标签 |
|---|---|
| 控制流 | `[TRIGGER]` 102,527、`[BEHAVIOR]` 83,630、`[DO BEHAVIOR]` 92,728 |
| 条件 | `[CHECKUP]` 22,154、`[CHECKUP OBJECT]` 12,339、`[<=]` `[=]` `[>=]`、`[IS INDEX]`、`[RANDOM]` 9,923、`[GET TIME]` 5,527、`[DISTANCE]` 6,824 |
| 生成 / 销毁 | `[CREATE PASSIVEOBJECT]` 28,636、`[DESTROY]` 8,090 |
| 状态迁移 | `[SET ACTION]` 15,669、`[SET SPEED]` 9,686、`[MOTION]` 51,200 |
| 表现 | `[BASE ANI]` 51,192、`[SUB ANI]` 23,658、`[PARTICLE FILENAME]` 28,631、`[SOUND]` 13,398、`[SPEECH]` 4,718 |
| 战斗 | `[ATTACK]` 7,533 |
| 作用对象 | `[ME]` 85,662、`[MONSTER]` 5,132、`[CHARACTER]` 4,852、`[PASSIVE]` 7,474 |
| 定位 | `[POS]` 32,823、`[X AXIS]` 8,079、`[USE MY DIRECTION]` 9,190、`[USE MAP POS]` 5,381 |

条件、随机、计时、生成实体、切动作、打伤害、播动画/粒子/音效 —— 一个完整的行为 VM，
已经在引擎里跑了十几年。

### 2.2 `.obj`：passiveobject，数据驱动的实体

15,591 个。主要字段：

```text
[basic action] -> .act        [attack info] -> .atk       [etc attack info]
[basic motion] -> .ani        [etc action] / [etc motion]
[homing] / [homing use] / [homing follow] / [homing velocity] / [homing check gap]
[hp max] / [hp destroy] / [object destroy condition] / [vanish on move collision]
[piercing power] / [team] / [layer] / [pass type] / [floating height] / [width]
[int data] / [string data]    <- 参数
[destroy particle] / [sound category] / [last action]
```

投射物、召唤物、地面效果、陷阱都是这个。**它自带 hp、阵营、追踪、穿透、销毁条件**，
生成之后按自己的 `.act` 独立演完整套。

### 2.3 `.atk`：声明式的命中与伤害

13,928 个：

```text
[damage] / [damage bonus] / [weapon damage apply] / [attack type] / [elemental property]
[hit info] / [damage reaction] / [attack enemy] / [attack direction]
[lift up] 浮空 / [push aside] 击退 / [blow] / [down] 倒地 / [knuck back] / [hit down]
[physic] / [no element] / [blood] / [no blood] / [hit wav]
```

伤害数值、属性、命中反应（浮空/击退/倒地）全是数据。

### 2.4 `.apd`：挂在角色身上的 appendage

644 个，是 buff / debuff / 光环 / 持续特效的载体：

```text
[type]              <- 选择原生 appendage 类别
[duration] / [max overlap] / [create limit count]
[int data] / [float data] / [string data]   <- 该类别的参数
[buff] / [effect animation] / [icon image]
[target] / [destroy when attack count] / [destroy when hit count]
```

`[type]` 选原生类、其余是参数——这正解释了 Squirrel `.nut` 系统存在的原因：
当某个 appendage 的逻辑超出现有原生类别的表达力时，就给它挂一段脚本
（见 [ScriptRuntimes.md 第一节](ScriptRuntimes.md#一squirrelvm-不在了注册表还在)）。

## 三、一个完整效果 = 100 字节

`passiveobject/character/thief/flyingsquirrelfireexplosion.obj` 全文：

```text
[floating height] 1
[layer] [normal]
[pass type] [pass all]
[piercing power] 1000
[basic motion]  ../../Common/Animation/FireExplosion.ani
[attack info]   AttackInfo/FlyingSquirrelFireExplosion.atk
[string data]   ../../Common/Particle/FireExplosionParticle1.ptl (x4)
[name]          flying squirrel fire explosion
```

一个会播动画、放四层粒子、按 `.atk` 结算伤害、穿透力 1000 的实体，100 字节数据，零代码。

而且 **163 个 `.skl` 带 `[usable passiveobject]`** ——
「技能 = 生成一个 passiveobject，剩下的它自己演」本来就是官方的常规做法。

## 四、扩展面判断

### 4.1 小代价可达

- **新的技能表现与判定**：写一套新的 `.obj` + `.act` + `.atk`，复用现成 `.ani`
  （261,135 个动画）。生成、飞行、追踪、穿透、多段、连锁生成子对象、
  浮空/击退/倒地反应 —— 全在 `.act` 的词汇表里，无需原生代码。
- **角色身上的效果 / 光环 / buff**：`.apd`，`[type]` + 参数 + `[effect animation]`。
- **怪物侧的任意行为**：`.act` + `[ai pattern]` 本就全数据驱动。

触发这些，乐观情况下只需绑 2–3 个原生函数：

```text
world.spawnPassiveObject(objIndex, x, y, z, team)   <- 最关键的一个
actor.addAppendage(target, apdIndex, duration)
actor.playAnimation(target, aniPath)                <- 最脆的一个，见 4.2
```

**第一个函数一绑，绝大部分「游戏外技能」就通了。**

### 4.2 必须写原生代码

- 角色自身的**新动作状态**：新连段、新取消规则、新位移 / 无敌帧。数据侧零入口（见第一节）。
- 让角色播任意动画（上面第三个绑定）介于两者之间：能强行调，但会与原生状态机冲突，
  且服务端不认。**不要把设计押在它上面。**

### 4.3 最划算的折中

**角色只播一个现成的通用施法 / 挥击动作，真正的效果全部交给 passiveobject。**
完全绕开角色状态机，表现力几乎不打折——这也是官方自己的做法。

## 五、双端约束

**伤害与状态归服务端。** 客户端生成的 passiveobject 打出的伤害，服务端不认。

正确形态是：**新增的 `.obj` / `.act` / `.atk` 数据只写一份，客户端负责表现，
服务端负责判定** —— 双端读同一份 PVF，比两边各写一套逻辑省力。

但服务端目前还没走到这一步。[`Dfo.Server.Infrastructure/Content/`](../../Server/src/Dfo.Server.Infrastructure/Content/)
下的 PVF content source 全是定义类的（creature、dungeon、quest、town、
initial skill、skill point、item placement、collection box 等），
**没有一个消费 `.obj` / `.act` / `.atk`** —— 战斗判定那条线是空白。

## 六、未验证事项与下一步

1. **`[CREATE PASSIVEOBJECT]` 的原生入口地址** —— 整条路的唯一硬前置，
   需要一份进游戏之后的内存转储（现有两份都是早期阶段，只解出 373 个 RTTI）。
2. **服务端战斗判定面** —— `.atk` 的消费需从零建。
3. **PVF 写入** —— 新增文件要重打包 `Script.pvf`，而 [`Dfo.Pvf`](../../Server/src/Dfo.Pvf/) 目前只读。
   起步阶段可绕开：让客户端插件从散文件读新数据，验证通了再回头做打包。
4. **`[CHARACTER]` 在 `.act` 中的可用动词范围** —— 4,852 处出现，
   但哪些动词对角色实际生效尚未逐条确认；这直接决定 4.2 那条界线能不能往回推一点。
