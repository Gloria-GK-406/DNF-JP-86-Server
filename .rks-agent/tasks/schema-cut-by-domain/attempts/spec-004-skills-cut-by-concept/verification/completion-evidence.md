---
kind: rks-verification
decision: pass
---

# Verification — spec-004 技能按概念重切

## 被评价的主张

「同一个技能概念不因它服务 PVE 还是 PVP 而重复承载；职业专属技能页保持分开，且非本职业持有时仍被拒绝而非忽略。对外协议行为一字不变。」

## 逐条条件与证据

| 条件 | 证据 | 结果 |
|---|---|---|
| SC-1 技能由一处承载，`character_pvp_skills` 不存在，表数 44 → 43 | `SkillCarrierShapeTests`：七列与裁定的目标列表相等；`character_pvp_skills` 不在库里；库形状声明清点 **43 张表**，即裁定给出的目标形状 | pass |
| SC-2 `mode` 不重新解释任何一列 | `EachReadAnswersWithItsOwnModeAndNoneOfTheOther`：一个角色同时持有两组页，两条读取各自只答自己那组；`TheTwoModesDoNotSeeEachOther` 在库层说同一件事。每一列在两个 mode 下走同一条读写路径 | pass |
| SC-3 四条保证逐条存续 | `TheSkillTableCarriesWhatOnlyOneOfItsSourcesDeclared`（五条 Theory：新键、`UNIQUE`、页范围检查、mode 取值检查、级联外键）与 `EverySkillColumnButTheBlobIsRequired`（六列）；行为层另有六条 `NOT NULL` 拒绝、页范围两向、`UNIQUE` 两向、同槽拒绝、级联清除。评审逐条对照改动前的声明确认限定方式正确——页范围对 pvp 不松、对 pve 不紧；`UNIQUE` 与主键把 mode 并入键列，按 mode 投影后与原表等价 | pass |
| SC-4 收紧不改变任何现行行为 | 三处收紧逐一证明：`NOT NULL` 与级联外键安全（建角色提供全部值，角色行本就存在）；`UNIQUE` 依赖「没有职业声明重复的主动技能」，由新增的 `PvfInitialSkillDefinitionSourceTests.No_job_declares_the_same_active_skill_twice` 对真实档案逐职业断言。评审核实该循环覆盖的 0..12 正好等于 `MaxJobId`，且全仓写 PvE 技能行的路径只有建角色一条 | pass |
| SC-5 职业专属页仍分两张表，非本职业持有仍被拒绝 | 两张表仍在库中（形状守卫断言）；`AJobSpecificPageHeldByAnotherJobIsRefused` 两例——此前全仓无人守，`inapplicable` 这个词在测试里一次都没出现过 | pass |
| SC-6 播种序列行为不变 | `SeedingTheDuelPagesWritesThemOnceOnTheDuelSide`：非空 profile 播种后两行落在决斗侧、普通侧为空、`character_pvp.skills_initialized` 置为 1；第二次用另一个 profile 播种什么都不变。删掉 `MarkSkillsInitializedAsync` 的调用会让它变红 | pass |
| SC-7 对外协议行为一字不变 | 全量 **1132 通过 / 2 跳过 / 0 失败**。整记录表征用例通过，**期望输出一字未改**——其 diff 只有一行播种语句加上 `'pve'` | pass |
| SC-8 表征守住的列仍被守住，且守卫可证伪 | 表征的改动只落在播种语句上。可证伪性演示两次：去掉 `ReadEntryAsync` 的 mode 条件 → 红；去掉页范围检查 → 形状与行为守卫各红一条。整改后另做三次：普通插入写错 mode、决斗插入写错 mode、标记不写，三次各红一条。第二轮评审独立重做七处，**七处全红且每次只红被点名的那一条** | pass |
| SC-9 上一轮确立的形状未回退 | 清点 68 个类，**零个指名超过一张表**；领域记录 `CharacterModels.cs` 与 `ICharacterEntryStateServices.cs` 的 `git diff` 为空 | pass |

## 约束

| 约束 | 证据 | 结果 |
|---|---|---|
| 裁定是设计权威 | 实施未偏离；整改时按评审的 B 级建议补写了裁定的一段——四条保证里有三条现在也约束普通行，且 `UNIQUE` 依赖的档案事实钉在哪条断言上。评审核实新文字与代码逐句一致 | pass |
| 红线：`mode` 不重新解释任何一列 | 两个 mode 下每一列含义相同、读写路径相同；`mode` 选的是一组行而不是邻列的读法。对比 S-4 拒绝的那处：那里需要一个决定载荷列含义的判别列 | pass |
| 四条保证按 `mode` 限定后逐条存续 | 见 SC-3 | pass |
| 非本职业持有仍被拒绝 | 见 SC-5 | pass |
| 不为既有数据编写回填、修复或迁移路径 | 无任何回填或迁移语句。`character_skills` 整张表退出可补列清单，理由写进库形状声明的类注释：`mode` 是 `NOT NULL` 无默认值，而这一列的全部意义正是「一行的 mode 无法从这行推出来」；旧形状的库被替换而不是被补 | pass |
| 覆盖不可净减少 | 用例数 1102 → 1134，Δ+32，**无删除、无取代**：+1 档案重复守卫、+13 形状守卫、+18 行为守卫 | pass |
| 构建零警告 | `dotnet build Server/86JP.Server.slnx` 零错误零警告 | pass |
| 全程不产生任务级提交 | `git rev-parse HEAD` 仍为 `1a77ef8a1de23ebb0d6ba283f44ca178b2496953` | pass |

## 一处记录而未改的观察

**级联外键在运行时不生效。** 全仓没有任何地方执行 `PRAGMA foreign_keys=ON`，SQLite 默认关闭外键，所以 `character_skills` 与 `character_pvp` 的 `ON DELETE CASCADE` 今天都只是声明。本成果照裁定把声明搬了过来——搬运不能丢东西——并让对应用例自己打开那个 pragma，因为那是分辨「声明还在」与「声明没了」的唯一办法。评审确认这是既有状况而非本次引入，且今天没有硬删角色的路径。不属本契约范围。

## 评审经过

两轮，均在独立的新上下文中进行。

- **第一轮判 `changes_required`**，一条 A 级：合并把「哪一组页」从表名降级成语句里的字符串字面量，读的那一半有守卫，**写的那两条一个都没有**——评审真改真跑证明把两处字面量各改成对方、以及删掉标记写入，三次全量都全绿；而我为写入路径新写的那条用例的回读查询没带 `mode`，对写进哪一组页毫无判断力。已修三处并各自验过可证伪。九条 B 级中接受了两条（裁定补写、另外三条语句补守卫）。
- **第二轮判 `passed`**：七处 mode 字面量与那次标记写入现在各有一条只有它能打红的用例，七次真改真跑七次全红且每次只红一条；整改只加测试与文档、未动产品代码（评审用时间戳与 diff 算术两条独立证据核实）；裁定新增段落与代码及其指名断言逐句一致；九条 B 级无一升级。

## 结论

`pass`。九条成功条件各有当前证据，八条约束均成立。库形状到达裁定给出的目标：**43 张表**。

这一块的教训与前两块同源而更尖锐：**合并把一个由表名承担的区分交给了一个字面量，而字面量不被任何东西检查。** 前两块丢的是约束，这一块丢的是「区分本身」的守卫——读的那一半我想到了，写的那一半没有，而写错的后果（新建角色技能栏空掉、决斗页混进普通页）只在实机显形。第一轮评审用一次真改真跑把它逼了出来。
