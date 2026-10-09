# 地下城怪物声明解析重构

任务契约、规格、计划、调查与验证见 [任务目录](tasks/monster-declaration-parser-20261009/TaskContract.md)。

```text
PVF typed tokens
→ PvfMapActorDeclarationParser：按section和游标解析变长语法
→ DungeonMapActorDeclarations：统一声明＋附属/条件内容
→ PvfDungeonRoomActorSource：缓存、补APC静态等级、适配既有无条件actor接口
→ DungeonRoomRuleService：等级与模板身份
→ Cell：死亡/奖励/清房账本
→ START_MAP：只编码已确定的来源与索引
```

新声明保存坐标、等级模式、放置方式、rank、dummy、NPC绑定、精英特征、阵营/原team值、随机/固定掉落参数、来源父子序号以及条件参数。不会先拼str再正则猜边界；普通精英缺count的合法末记录仍可解析。

追击歼灭战22036保留五条MAP索引，其中61150为真实Boss，离屏dummy64639不领取普通清房、经验或奖励资格。根特防御21314提供敌对Boss APC10409与三十对象子模板，模板使用原父/子编号，不能进入普通实体编号计数。已确认场景波次不会随Boss死亡被普通杂兵清除逻辑抹掉。

证据边界：实际PVF 10,293个map.lst引用均可结构解析；70/upstream仅为参考。条件脚本、blood/ultimate等专属机制保存声明而不默认生成，非已证实OBJ控制器不猜语义。用户需实机验证追击Boss入房和根特防御波次。

聚焦回归521项通过，无失败或跳过；独立审查approved。已部署版本 `0.2.3+monster-declarations.20261009.f2fa6e3e`，PID 32956，六端口与健康/启动接口已验证。数据库沿用并备份，旧运行目录保留。完整测试、部署与实机边界见 [完成证据](tasks/monster-declaration-parser-20261009/verification/completion-evidence.md)。
