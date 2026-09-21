# 增值订阅服务：服务端与 PVF 前置调查

日期：2026-09-21。关联 #110 / #111，父任务 #45，M06。

本报告只包含服务端代码与本地 PVF 证据。未修改生产代码、数据库或运行中的服务端，未执行客户端操作。客户端调查由用户交给另一个 agent；本报告完成后等待该报告，暂不关闭 #110、不批量创建具体订阅实施单。

## 1. 基线与证据边界

- 服务端基线：main `abf2be1fae563fe230772712b0303504417caeb9`。
- PVF：`E:/Project/DNF/DNFClient-A21-debug/Script.pvf`。
- SHA-256：`19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A`。
- 使用仓库既有 `Tool/PvfProbe/bin/Release/net10.0/PvfProbe.dll` 的 `dump`、`files`、`lst` 只读提取，未改探针或脚本。
- 下文“代码确认”仅证明当前服务器行为；“PVF 确认”仅证明此档案中的声明、商品关系和说明文本。均不能代替客户端消费或实机效果。
- premiumlist 的部分旧名称显示为编码错乱，不据此命名；常见契约名称通过对应商品脚本及商城中文名交叉确认。

## 2. 订阅候选与商品关系

`etc/premiumlist_new.etc` 有 23 个 `[type]` 段、76 个显式 `[item]` 子项；辅助 `premium-item-map.json` 保存项与时长关系。旧类型中包含网吧、历史奖励等内容，不能将所有 type 自动纳入本轮玩家订阅。

| 候选服务 | premium type | PVF 对照证据 | 当前服务端状态 |
|---|---:|---|---|
| 霸王之契约 | 22 | item 30：`contract_monarch1.stk`；`[over equipinfo]`；商城对应名称 | 礼包可以发放期限；完整装备越级资格未在本轮确认 |
| 达人之契约 | 27 | item 43：`contract_expert3.stk`；`[over skill] 5` | 有选角时的过期技能修正；不能等同完整学习/在线到期闭环 |
| 成长之契约 | 84 | item 2660703；`[bonus exp] 20`、`[quest item drop rate] 20`、`[independent drop rate]` | 经验有通用配置入口；未见按这些 PVF 字段完整应用 |
| 晶之契约（晶体契约） | 97 | item 10000391 与商城晶之契约名称 | 有晶体类型/档位字段及选角投影；未在检索范围找到完整效果写入链 |
| 黑钻 | 未确认 | 当前商城黑钻入口已有“服务暂未开放”的处理；未由本轮 PVF 提取确认对应订阅类型 | 不应先套用一个任意 premium type；等客户端确认资格来源 |

类型 29、33、80、88、92、100 等同样有声明；其中文业务归类、A21 入口和是否属于本轮范围须继续核对，不强行套入上述四种契约。

### 魔王契约：按单项购买的直接 PVF 证据

`etc/cerashop.etc [selectable character premium]` 为九列式数据（九列结构也与当前商城 reader 一致）。存在一个 product=-1、item=2681927、槽=-1 的“魔王之契约”汇总行，以及以下八类商品，各自有 7 和 30 的时长选项：

| 选择槽 | 服务名称 | 商品 item ID | 7 天 product ID | 30 天 product ID |
|---:|---|---:|---:|---:|
| 0 | 黄金卡牌免费翻 | 2681934 | 100542 | 100550 |
| 1 | 双倍袖珍罐服务 | 2681931 | 100539 | 100547 |
| 2 | 任务助手 | 2681932 | 100540 | 100548 |
| 3 | 7 种 Buff 效果 | 2681933 | 100541 | 100549 |
| 4 | HP/MP 自动恢复 | 2681929 | 100537 | 100545 |
| 5 | 免费解除虚弱状态 | 2681930 | 100538 | 100546 |
| 6 | 自动修理 | 2682205 | 100817 | 100818 |
| 7 | 高效开罐 | 2682206 | 100882 | 100883 |

商品 `contract_devil7.stk` 文案明确让购买者选择期限与特殊服务项，并称适用于同账号大区所有角色；`chn_60days_pack.stk` 说明八项服务 60 天，`[package data]` 列出上述八个 item。商城另有 `[charac premium package]` 组合入口。

**结论：PVF 支持单项订购与多项组合交付，是按单项订阅建模的强证据。** 但“是否任一项有效就显示魔王契约图标”“八个槽如何映射真实期限载体”“是否不存在额外总资格记录”仍须客户端确认，不能仅凭商品表下最终结论。

### 效果次数和触发条件不能忽略

`etc/chn_characpremium.etc` 有不同效果块。各块首值均出现 364，本轮未确认其语义，不把它当作服务编号或到期时间。

- `[free_stamina] 364 10`；商品说明：免费找索西雅解除虚弱，每天限用 10 次。
- `[auto_repair] 364 6`；商品说明：快捷栏或穿戴装备耐久归零时自动免费修理，每天限用 6 次，适用地下城/决斗场/街头争霸。**不能直接等价成所有 NPC 修理免费。**
- `[recovery_hp_mp] 364 1`；商品说明：地下城 HP 低于 50% 时回满 HP/MP，虚弱时无效，每次地下城一次；“次数不限”不能忽略紧随其后的每局限制。
- 其余块包括 plus_goldcard、quest_helper、buff_service、lottery_onemore、lottery_fastbreak、limited_quest。它们是效果配置，不能因存在一个块就自动建一个独立订阅 issue。

这些次数是否账号共享、何时重置、如何和客户端消费一致，均未闭合。

## 3. 当前实现路径

| 职责 | 当前位置 | 行为与缺口 |
|---|---|---|
| 持久化 | `Persistence/Accounts/AccountTables.cs`；`account_premiums(account_id,premium_type,end_time)` | 每账号每 type 一条绝对到期时间；UpsertExpiry 直接覆盖，没有领域资格语义 |
| 礼包识别 | `Infrastructure/Content/PvfPackageOpeningDefinitionSource.cs:Premiums` | 从 premiumlist_new 扫 `[item]` 后紧邻 `[term]`，结果存 Type/Days |
| 发放与续期 | `Application/Inventory/OpeningInventoryDomainService.cs` | 直接读表，`max(now,oldExpiry)+days*count*86400`，与礼包事务共同写入；领域服务缺位 |
| 发放通知 | `Protocol/Game/GameInstanceDelivery.cs` | `PremiumGrantedMessage` 编码为 NOTI 0042：u16=2、u8 type、i64 剩余秒；这是服务端编码，等待 A21 侧核实 |
| 登录权益 | `Application/Accounts/AccountEntryAssembly.cs`、`AccountEntryStateServices.cs` | 直接加载整张账号权益列表 |
| 登录编码 | `StateSynchronizationPayloadCodec.Inventory.cs` | 数量 u8、类型取低字节、剩余秒 clamp 到 0；未统一有效状态过滤 |
| 魔王服务投影 | `StateSynchronizationPayloadCodec.Entry.cs:EncodePremiumService` | 使用 type-580 映射八槽；不是已确认的 A21 订阅编号。选角工厂明确不发送独立 PREMIUM_SERVICE |
| 服务使用次数 | `CharacterEntryAssembly.cs:LoadProgressionAsync` | 只用 LotteryDoubleCounter 填入 slot=1，并非八种服务的完整次数管理 |
| 达人过期修正 | `SelectCharacterPersistenceServices.cs`、`CharacterEntryRuleServices.cs` | 根据 PVF over-skill 类型在选角阶段判断过期、修正超限技能；使用 UtcNow，未走统一权益服务 |
| 经验权益 | `World/ExperienceBonusDomainService.cs`、`ExperienceBonusRules.cs` | 直接查表，依据配置 PremiumRates 应用加成；不等于自动解析成长契约全部效果 |
| 商城订阅购买 | `CeraShopRuleService.cs` | 当前拒绝 IsService 商品；定义 reader 虽已识别商城服务类别，购买链尚未交付这些服务 |
| 免费恢复 | `Characters/WeaknessDomainService.cs`；`RecoverStaminaSlice.cs` | 只有普通金币恢复；免费资格接口位置已预留，非普通 mode 拒绝，未消耗免费次数 |

以上相对路径均基于 `Server/src/Dfo.Server.*` 对应项目；协议文件路径均在 Dfo.Server.Protocol 内。

## 4. 已发现的具体问题

1. **时长单位丢失，代码确认 + PVF 确认。** 五个 item（10096109/10/11/12/13，对应 type 22/27/80/84/97）声明 term=60 且 `[is term unit minute] 1`。当前 parser 只取 term 为 PremiumDays，发放乘 86400。若由现有礼包路径交付，会把 60 分钟按 60 天计算。尚未执行实机发放。
2. **部分显式映射遗漏。** type 33 的 item 198、2660290、2660358、917，在 item 与 term 中间插入 `[restrict]`。当前紧邻判断无法识别为契约映射；后续可能拒绝或走其他分类，不断言必然交付错误。
3. **同一零值三种解释。** 经验规则将 end_time<=0 视为不声明期限；达人规则将 <=now 判为过期；登录投影将剩余时间写为 0。必须在迁移前明确零值/永久/缺失语义，不自动批量改历史记录。
4. **编号体系没有统一映射。** 礼包限定 type<=127，登录低字节截断，而独立服务编码按580..587映射。这是不同路径的现有假设，不能据此建立直接强转的领域枚举。
5. **到期与次数未形成共享闭环。** 未在本轮搜索到统一到期通知/订阅状态服务；选角修正与经验现算各自判断。魔王八项服务的每日/每局次数也未统一。
6. **日期来源分散。** 发放使用业务时点，经验通过 TimeProvider，达人过期直接 UtcNow；后续应统一注入时钟以验证到期边界。

## 5. 对 #111 的实现约束建议

- 保持用户已确认的 IPremiumServiceDomainService → For(accountId) → IAccountPremiumServices 边界；具体作用范围等证据确认，当前持久化账号级不能证明次数也账号共享。
- 以独立订阅单元记录期限，商品/礼包转换为一组订阅变更。组合礼包不必额外生成一个总订阅。
- Extend 接收明确单位的 Duration/TimeSpan；不要让所有商品的 term 默认为天。
- HasBenefit 只负责有效订阅映射。对于每日或每局限额，资格存在和“本次还能否使用”需要区分；执行时原子校验/消费额度，具体副作用仍由虚弱、修理等领域执行。
- 尤其不能只凭 HasBenefit(FreeWeaknessRecovery) 就永远返回零价而不处理每日十次；报价、免费额度消费与实际恢复需要一致的业务流程。
- 统一状态及剩余时间查询；到期时间持久化。在线调度用于撤销/更新投影，业务查询不能在过期后等 tick 才失效。
- 原有礼包写入、经验读取、达人修正、登录投影均是迁移清单，不引入第二套长期并存的权威期限。

## 6. 等待客户端报告裁决

1. 魔王总入口的显示条件、剩余时间汇总算法；八槽真正的服务编号与期限字段。
2. 580..587 是否旧服编号，当前 u8/i64 剩余秒及独立 PREMIUM_SERVICE 消息是否符合 A21。
3. 免费恢复请求 mode=0/1 与资格/次数的关系；每日十次由谁显示和判定，次数作用域及重置点。
4. 自动修理、HP/MP 自动恢复的触发者和上行/下行流程，次数及每局状态的角色/账号边界。
5. 黑钻资格来源与期限载体；是否属于同一订阅体系及疲劳之外可先交付的权益。
6. 永久/零值及在线到期的客户端消费；常见契约编号、显示和真实效果的对应。

## 7. 后续拆分建议（未执行）

合并客户端报告后，为确认独立到期的各服务逐项建单；按 PVF 目前证据，魔王八个选择项可作为候选八项，四种常见契约作为候选四项，黑钻暂保留未定映射。开罐、任务助手等存在上游玩法依赖，不能用订阅图标替代完整验收。

推荐先以免费解除虚弱验证基础服务，但必须把每天十次的额度与恢复请求纳入该服务 issue。当前不实施，不关闭调查，不调整用户游戏状态，等待另一侧报告。

## 辅助文件与复核

- `premiumlist_new.txt` / `premiumlist.txt`：类型定义原始探针输出。
- `premium-item-map.json`：按 type 与 item 块提取的结构关系，只作辅助，原始 dump 为复核入口。
- `cerashop.txt` / `cerashop-premium-sections.txt`：商城原始输出和增值服务相关片段。
- `chn_characpremium.txt` / `premiumserviceeffect.txt`：效果参数与图标配置，不能据文件名推断业务身份。
- `item-*.txt` / `*.stk.txt`：常见契约及魔王商品脚本。
- `premium-files.txt` / `devil-files.txt`：档案路径索引。

提取方式：设置 PVF_ARCHIVE_PATH 后执行 `dotnet Tool/PvfProbe/bin/Release/net10.0/PvfProbe.dll dump <档案路径> 20000`；商品映射使用 `lst stackable/stackable.lst <itemId> 250`。未进行生产测试，因为没有生产改动；检查包含提取成功、类型/条目计数及关键商品双向对照。
