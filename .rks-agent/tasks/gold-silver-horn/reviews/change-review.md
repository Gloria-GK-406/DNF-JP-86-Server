---
kind: rks-review
verdict: approved
---

# 新·金角银角完整结果独立审查

## 绑定与范围

- 审查目标：`task/gold-silver-horn-20261019@d5b04b34d3f6eab488594425e442eeab2b6175bb`
- Spec baseline：`ccc0e845e3006d653567bf01dd86b31c4d266e34`
- 提交链：planning `9a98db9b`、implementation `b6b40427`、文档修复 `d5b04b34`
- 治理来源：`TaskContract.md` revision 1、`spec.md`、`routing-decision.md`，以及已批准且绑定 revision 1 的独立 TaskContract review
- 审查类型：完整代码变更与交付文档混合审查；采用 `reviewing-work` 的 Code 与 Deliverable 判据
- 独立性：审查者未参与本任务生产、意图对齐或契约审查，也未修改被审代码、现服或玩家数据

本结论批准上述精确 HEAD 作为部署候选，不替代部署后 verification，也不把尚未发生的现服切换或用户实机验收记为完成。

## Verdict

`approved`。在本审查范围内未发现需要阻止部署候选进入现服切换与独立验证阶段的代码或交付文档缺陷。

## 决定性证据

### 1. 活动、准入、地图与既有玩法隔离

- 活动 2332 以独立 managed event 注册；033F 继续使用既有 category 0 编码并投影 content 4123，未把金银角附着到活动 69/75。GM disable 同时影响入口投影和服务端准入。
- `GoldSilverHornDomainService` 在进入共用准入事务前拒绝非 4123、非普通模式、练习、循环意图、多人和已有运行；`GoldSilverHornRuleService` 对真实 PVF 的 50 级、单人、难度 2、免疲劳、免放弃惩罚、经验倍率 0、无门票声明失败关闭。
- 难度并非只靠文档约定：共用 `DungeonDifficultyRuleService` 会按 4123 的唯一 `SupportedDifficulties=[2]` 拒绝其他难度。
- 真实 PVF 回归实际构建四房的全部六张 MAP，覆盖银角、金角、双 Boss 三候选及其怪物声明；藏宝地、南部溪谷、牛头乐园等关联准入和地图回归包含在 109 条相关用例中。

### 2. 角色每日额度、事务和日切

- 新 family 使用独立的 `gold-silver-horn.4123.entries:<period>` key，复用 `character_daily_counters`，无需 schema 迁移；读取、CAS 写入、持久重载和角色隔离均走既有 quota 权威边界。
- 准入预检读取当前周期计数，事务消费发布 `GoldSilverHornEntryRecorded`，建局返回失败或抛错时要求当前事务回滚；成功建局才随业务事务提交。放弃路径没有退款动作。
- 真实 PVF 回归覆盖三次成功后第四次拒绝、零疲劳进入、低等级/多人/模式/难度/已有运行拒绝、事务未提交、工厂建图异常、持久重载和 06:00 业务日切，未发现拒绝或失败扣次。
- 选角、提交后的 `UsageQuotaChanged` 与在线日切均从持久权威快照重读；投影前后复核精确 session identity，日切发送失败不会标记已交付，下一 tick 可重试。

### 3. 042B 与协议证据边界

- 客户端 loader 与 handler 的静态取证闭合了正文为两个 little-endian `u16`；当前 PVF 只配置 silver 槽 4123/上限 3，因此实现保持 offset 0 为 0，只把已用次数写到 offset 2。
- 编码向量 `0/1/3 -> 00000000/00000100/00000300` 有独立测试；协议目录和 catalog 同步说明这是 `ClientStatic`/`CurrentImplementation`，未冒充 `WireCaptured` 或 `ClientVerified`。
- 选择角色、提交后变化和日切是当前服务发送策略；交付文档把原生日服发送顺序及客户端 UI 表现保留为实机验收项。

### 4. 奖励购买、开罐与共用解析修复

- PVF、客户端请求布局和既有 A21 商店实机记录共同支持：顶层 `[need material]` 是 NPC 商店购买成本；真正的抽奖开启材料字段是 `[lottery use need item]`，传说罐的 `[lottery use cost]` 才是 700 万开启金币费。
- 生产变更只停止将顶层 `[need material]` 投影成 package/lottery 开启材料，保留 NPC 商店购买扣料、`[sealing removal item]` 钥匙/封印材料、`[lottery use need item]` 失败关闭和金币原子扣除。
- 五件真实商品均通过“购买后碎片归零再开启”的命令级测试；两个传说罐另覆盖金币不足保持罐子/金币，以及奖励写入故障回滚罐子与 1500 万金币，成功时只扣 700 万和一个源物品。
- 109 条关联 Application 回归覆盖 Package、Lottery、NPC 商店、quota 与既有活动；105 条关联 Protocol 回归通过，没有观察到共用解析修复导致的旧玩法回归。

### 5. 构建、全量回归与发布候选数据保护

- 失败基线 6/6 失败，实施后同组 6/6 通过；金银角专项完整集 15/15、关联 Application 109/109、关联 Protocol 105/105 均通过。
- 无 PVF 的八项目完整回归原始 TRX 合计 7553：6671 passed、882 skipped、0 failed；跳过项按无 PVF 条件存在，不被写成真实 PVF 覆盖。解决方案构建记录为 0 warning、0 error。
- 发布候选 manifest 绑定精确提交 `d5b04b34...`，版本 `0.2.7+d5b04b34...`，记录 12 个自有程序集 SHA-256、配置 hash、PVF hash及旧运行身份。
- 只读数据库副本演练 `integrity=ok`、foreign key violations 为空；108 张既有表的行数/内容散列一致，`changedExistingTables=[]`、`addedTables={}`。这支撑可部署性和数据保护，不代表现服已切换。

## 交付文档审查

`Document/GoldSilverHorn-20261019.md` 给出了入口、准入、次数、地图、疲劳/经验/退出、兑换与开启规则，并提供覆盖资格、三次额度、重登、三 Boss、奖励、退出、GM 开关和在线日切的可执行实机清单。研究文档记录了 PVF 路径、客户端 RVA、字段来源、复现方式和证据等级；实现说明与当前代码一致，没有把自动测试提升为玩家验收。

## 尚未完成但不阻塞本 gate 的边界

- 现服仍运行旧版本；RQ-007/AC-007 的实际替换、监听/HTTP 健康、运行程序集身份、数据库运行后检查和回退输入确认必须由部署后的独立 verification 新鲜核验。
- 4123 的真实帧顺序、入口 UI 次数、Boss/AI、掉落体验、传说罐客户端交互和重登表现仍待用户实机验收；在此之前不得标为 `ClientVerified`，不得自动关闭 #155。
- 自动化验证证明当前服务端实现与真实 PVF 内容的行为边界，不证明原生日服的开放时刻、精确掉率或原生推送时机。
