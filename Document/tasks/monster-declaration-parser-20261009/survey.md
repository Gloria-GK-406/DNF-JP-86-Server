# 实际 A21 PVF 怪物声明结构全量调查

- 调查：2026-10-09；只读实际资源，没有修改生产代码、PVF、运行时。
- 资源：E:/Project/DNF/DNFClient-A21-protocol/Script.pvf。
- 方法：独立 C# 程序引用已有 Dfo.Pvf.dll，枚举 map/map.lst 全部条目，读取 token 类型并识别各声明边界。代码 Program.cs；完整计数/代表 token/异常 report.json；简表 summary.txt。
- 调查工件实际位置：[Program.cs](E:/Project/DNF/.tmp/monster-declarations-survey/Program.cs)、[完整JSON](E:/Project/DNF/.tmp/monster-declarations-survey/report.json)、[简表](E:/Project/DNF/.tmp/monster-declarations-survey/summary.txt)。仓库内`PvfMapActorDeclarationParserTests.Every_current_listed_map_has_a_complete_actor_declaration_parse`可直接复现生产解析器的全MAP校验。
- 范围：map/map.lst 10,293 个条目，10,293 个文件存在并成功打开，0 缺失。未宣称覆盖 archive 中未被 map.lst 引用的孤立 MAP，也未宣称完成所有客户端生成时机。

## 普通怪物

70,000 条，[monster] 在 9,745 个 MAP 中出现（可为空）。共同前缀 8 Int32，接 placement，再可选 dummy/NPC 绑定，再 rank，再可选 champion 特征 count。

| 模式 | 条数 | 图数 | 代表 |
|---|---:|---:|---|
| I8 [fixed] [normal] | 68,585 | 9,403 | 普通格式 |
| I8 [fixed] [boss] | 1,319 | 1,184 | 普通Boss |
| I8 [fixed] [dummy] [boss] | 47 | 47 | 21670、21898、22036 |
| I8 [fixed] [NPC] npcId [boss] | 20 | 20 | 12030、15107、15374 |
| I8 [fixed] [champion] count=0 | 24 | 21 | 625、628、27104 |
| I8 [fixed] [champion]（无count） | 2 | 2 | 27108、27124，均末条 |
| I8 [fixed] [super champion] count=0 | 1 | 1 | 25630 |
| I8 [ratio] [normal] | 2 | 1 | 416 |

代表 dummy：64639 1 0 -223 -296 0 1 1 [fixed] [dummy] [boss]。
代表 NPC：61625 1 0 842 292 0 1 1 [fixed] [NPC] 1016 [boss]。
代表 champion：5001 1 0 524 215 0 1 1 [fixed] [champion] 0。

边界必须 cursor 解析。仅去掉 modulo10 不够；rank 后 count=0 也会造成下条错位。此档案未看到 count>0、dummy+NPC 组合或其它 placement。是否支持组合、非零特征应由 70/client 证据确定，不能根据没出现就禁止。末条 champion 无count确实存在，不能强制要求尾 count。

## APC

[ai character] 818 图，共 1,248 条。均为 4Int + faction + rank + 2Int，无实际扩展尾字段：
- [character][normal] 315条/273图
- [monster][normal] 394条/209图
- [monster][boss] 538条/339图
- [neutral][normal] 1条/1图

未见 APC champion；70 所述 APC 特征尾的支持能力是参考证据，不能混写成此 A21 PVF 已观察。

## 特殊对象

[special passive object] 4,338 图出现，4,335 图非空，13,214 个对象，头5Int（code,x,y,z/flags,actionCount）。
- monster action：1,146条/372图，kind + 5Int。
- item：10,271条/3,829图；trap：2,062条/742图；quest：6条/6图。
- hellparty：73条/73图，kind + 3N Int + closing，N范围11..32。全部有闭合标记。
- 不应把对象生成模板直接等价为入房已生成的怪物；需保留对象序号/子模板序号和激活时机。

## 其它声明与伴随字段

| 字段 | 图数 | 实际结构 |
|---|---:|---|
| [monster condition] | 56 | 166条，均8Int+placement+rank（10token） |
| [conditional summon monster] | 136 | 351条，全部8Int+placement+rank+3Int（13token） |
| [blood monster] | 7 | 331条，每条10Int |
| [ultimate monster] | 1 | 20条，每条10Int，图17030 |
| [monster spawn pos] | 25 | count+count个XYZ三元组，全部计数吻合 |
| [monster team] | 306 | Int列表，无count，1..15项；值包括0/50/75/100/110/200 |
| [monster specific AI] | 9,633 | [normal]/[boss]；[zone defence]后带2Int；有1个空块 |
| [attacked monster info] | 1 | 图40138，90Int，形状为10条9Int；语义待参考/client确认 |
| [event monster position] | 9,118 | 坐标列表，代表682 288 0 597 201 0 ...；不含怪物ID |
| [hide monster] | 8 | bool quoted word，代表true |
| [monster lock] | 69 | bool quoted word，代表false |
| [draw monster count] | 354 | marker，代表空值 |

conditional代表图11057：56616 1 0 6 6 0 1 1 [fixed] [boss] 381 243 0。
blood代表图16353：61158 0 1081 221 0 3000 1000 1 1 2。
ultimate代表图17030：56017 0 440 222 0 5000 4000 1 0 1。
attacked代表图40138 map/VillageAttack/1_Underfoot_Event_Hidden.map：69102 608 253 0 0 0 0 0 0。

## 验证边界

普通/APC/SPO解析探针0异常/0未消费残余；conditional/condition/blood/ultimate 的token宽度及类型全部校验0异常；spawn pos count校验0异常。其它伴随字段是全量形状盘点，尚未视为行为实现。此调查是 PVF 内容静态证据，不是实际客户端包或生成/通关行为验证。
PVF SHA-256：03154D18DE43F5A0BCB11B2935AE8BB40BABD22495700657D682E491A24A3543。

## 参考语义与实现补充

- **70Reference**：`Research/1995chen-dnf/other/等级补丁/70/df_game_r`，SHA256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`。ImportMapScript `0x089DCF54`；普通/条件怪物支持 `[fixed]/[half]/[ratio]` 与NPC绑定；精英类别后读count个字符串。APC分支`0x089E0E06..0x089E11A2`的精英特征位于尾部两个整数之前。特殊对象proc `0x089E7958`按动作种类分流，monster动作是kind+5int；其概率参数不等于掉落数量。清房`0x0830A0E8`检查敌对100及死亡状态。
- **UpstreamReference**：`ServerS4A21/Tool/PvfLib/Models/MapFile.cs:704`按游标跳过dummy/NPC；其空白字符串拆分、未知rank默认普通和截断静默break不能照搬。`DungeonActorTemplateProjector.cs:254`将特殊对象子monster投影为模板：parentIndex/order、childIndex/packetIndex、flag0=1、flag1=parentIndex，父对象包括空对象和非monster子项，不能过滤后重编号。
- **实际资源兼容**：后续严格校验发现5张图含0空槽或-1虚拟对象。34018的`-1 0 0 0 1 [trap] 2211 1 3 4 -1`合法；25277/25278及80008存在`0 1 -1 -1 0`空槽。允许0空槽和-1虚拟非monster动作并保留顺序，负数量及未知怪物父来源仍拒绝。
- **CurrentImplementation**：dummy声明保留在MAP索引中，运行资格不按普通敌人处理；这是保守投影选择，70无此修饰，A21实机待验收。特殊对象仅新增明确MAP内联monster模板；GentDefence_Wave1..3及其子系列按已确认控制器路径参与死亡账本，其他模板默认不阻塞清房。未凭任意OBJ int data形状执行定时脚本。
- 条件、blood/ultimate、attacked和其他附属信息保留，尚未实现它们的完整触发/专属地下城规则。统一语法覆盖不等于所有这些玩法均已完成。
- **70Reference补证**：monster team在`0x089E04AC..0x089E04CF`写入STAssignMonster+0x4C（默认100），`0x0834EA4F..55`到_mapMonster+0x28，`0x08301A30..39`到map_monster+0x2C，`0x0830A143..15E`仅100阻塞清房。0/50/75/110/200战斗阵营语义未证，因此保存原Team，不把Team强转APC Faction或改变战斗敌对；清房资格独立使用原值。
