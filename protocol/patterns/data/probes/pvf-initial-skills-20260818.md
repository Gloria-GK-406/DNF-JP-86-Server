# 探针产物：各职业的初始技能

归档一次性探查的结果，使引用它的主张可以解析。见 [evidence.md](../../../evidence.md#一次性探查--直接写进文档)。

- 归档时间：2026-08-18
- 归档源：`DNFClient-debug/Script.pvf`
- 读法：`character/character.lst`（`PvfListFile` 语义，id + 相对路径）→ `character/<相对路径>`
  的 `.chr` 文件 → `[initial value]` 段内的 `[skill]` … `[/skill]`

## `character.lst`：13 个职业

```text
 0 -> character/Swordman/Swordman.chr        7 -> character/Fighter/ATFighter.chr
 1 -> character/Fighter/Fighter.chr          8 -> character/Mage/ATMage.chr
 2 -> character/Gunner/Gunner.chr            9 -> character/Swordman/DSSwordman.chr
 3 -> character/Mage/Mage.chr               10 -> character/Mage/CreatorMage.chr
 4 -> character/Priest/Priest.chr           11 -> character/Swordman/ATSwordman.chr
 5 -> character/Gunner/ATGunner.chr         12 -> character/Knight/Knight.chr
 6 -> character/Thief/Thief.chr
```

## `[skill]` 是成对的 `(技能id, 等级)`

0 号职业（鬼剑士）：

```text
[skill] 181 2 182 2 184 1 179 7 5 1 46 1 169 1 174 1
```

即 8 个授予。经 `skill/swordmanskill.lst`（id → `.skl` 相对路径）解析出的名称与类别：

| id | 授予等级 | 类别 | 名称 | 文件 |
|---:|---:|---|---|---|
| 5 | 1 | `[active]` | **鬼斩** | `Swordman/HardAttack.skl` |
| 46 | 1 | `[active]` | **上挑** | `Swordman/UpperSlash.skl` |
| 169 | 1 | `[active]` | 后跳 | `Swordman/BackStep.skl` |
| 174 | 1 | `[passive]` | 基础精通 | `Swordman/BasicAttackUp.skl` |
| 179 | 7 | `[passive]` | 物品分解 | `Swordman/DisjointItem.skl` |
| 181 | 2 | `[passive]` | 精工 | `Swordman/CompoundCraft.skl` |
| 182 | 2 | `[passive]` | 纺织 | `Swordman/CompoundWeaving.skl` |
| 184 | 1 | `[passive]` | 炼金 | `Swordman/CompoundChemistry.skl` |

**八条都属于 1 级的底子**（项目所有者 2026-08-18 确认：被动精通与后跳本来就有），
所以本段不需要再按类别或等级筛选——它就是建角时该授予的整套。

`.skl` 的字段：`[name]`（中文）、`[type]`（`"[active]"` / `"[passive]"`）、
`[required level]`（这八条全是 1）、`[maximum level]`、`[command]`（后跳是 `↓+C`）。
`[required level]` 与 `[type]` 都**不能**用来区分"该不该给"——八条 req 全为 1，而主动技有三个。

上游按同样的成对语义读（`Game/Skills/CharacterSkillProfile.cs:215`
`ParseSkillPairsInSection(text, ivStart, "[skill]", "[/skill]")`），
并在同一文件里另有 `[growtype N]` 与觉醒段的后续授予，本页不涉及。

## 与实机的关系

2026-08-18：我们建出来的角色**一个技能都没有**，教程副本里那个提示使用技能的房间
因此高亮着空的技能栏。8 个授予里哪两个会落在 A / S 快捷键位上，**尚未查证**——
快捷栏绑定是另一份数据，不在本段内。

## 持有 ≠ 可见：副职业技能不展示

精工 / 纺织 / 炼金 / 物品分解是副职业技能，游戏里要到 15 级做任务才解锁，
按常识不该在 1 级出现。但 2026-08-18 实机确认：**角色持有它们，客户端的技能列表里看不到**
（项目所有者建了守护者查看技能窗口，四条均未出现；快捷栏上也没有）。

所以"1 级持有全部 8 条"与"玩家只看到基础技能"并不矛盾，展示与解锁的门禁在客户端一侧，
不由服务端是否授予决定。**授予时不做过滤**。

曾考虑用内容字段把副职业切出来，查证后放弃——没有可靠判据：

| id | 名称 | `[skill class]` | `[required level range]` |
|---:|---|---:|---:|
| 5 | 鬼斩 | 3 | 2 |
| 46 | 上挑 | 1 | 3 |
| 169 | 后跳 | 4 | — |
| 174 | 基础精通 | 4 | 1 |
| 179 | 物品分解 | 4 | — |
| 181 | 精工 | 4 | 10 |
| 182 | 纺织 | 4 | 10 |

`[skill class]` 分不开：后跳与基础精通同为 4 而它们该有。`[required level range]` 也分不开：
物品分解没有该字段却同属副职业。按 id 列举则等于把业务知识钉死在代码里，13 个职业各钉一遍，
归档更新时无人知晓。

引用它的主张：初始技能来自归档的职业定义，不是服务端自定义；服务端授予全部声明项，不做过滤。
