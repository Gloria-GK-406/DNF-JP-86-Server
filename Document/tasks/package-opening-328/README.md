# 礼包开启全量修复（#328）

- 分支：`task/package-opening-328`
- 主干基线：`ef20398a3`（0.3.2）
- PVF：线上 0.3.2 修订版 `PvfBuilds/revised-20261113/Script.pvf`（SHA-256 `e1f3a25c…`）

## 范围与裁定

#328 只修服务端代码缺陷，也就是普查中的第 1–8 类，原始分类见 [survey-20261114.md](survey-20261114.md)。其余各类另开 issue：
- 第 9–12 类改 PVF：零散格式、缺失物品、缺宠物种类、过期奖励，见 #340；
- 第 13 类：其他开箱物品，见 #341。

用户裁定（2026-11-14）：
1. 逐暗者、阐释者、闪烁之光三个武器装扮礼盒，开出的时装不可交易，与礼盒说明一致。
2. 已经在玩家背包里的 `[cera package]` 不处理，让玩家自己删除。
3. `[charactor]` 职业名后的两个数，做到时再实机核对含义；在此之前只记录，不执行。

## 证据

| 资料 | 内容 |
|---|---|
| [refs/a21-client.md](refs/a21-client.md) | A21 客户端解析与自选窗口（ClientStatic） |
| [refs/a21-duplicate-fields.md](refs/a21-duplicate-fields.md) | 五个单值字段重复时后者覆盖（ClientStatic） |
| [refs/70-server.md](refs/70-server.md) | 70 df_game_r 的解析与发放（70Reference） |
| [refs/csharp-reference.md](refs/csharp-reference.md) | 上游与第三方 C# 参照 |

## 实现

| 类 | 改动 | 依据 |
|---|---|---|
| 1 宠物消耗品误判 | 只有装备类型 `[creature]` 才是宠物；stackable 的 `[creature]`、`[feed]` 归宠物消耗品，按放置规则进消耗品区 | 70 `IsCreatureItem`，两套 C# 一致 |
| 2 重复字段 | `PvfRepeatedFields`：`[attach type]`、`[stack limit]`、`[usable period]`、`[equipment type]`、`[stackable type]` 在顶层重复时取最后一次；其他字段照旧拒绝重复。`PvfDocument.TryRead` 和各自读顶层的读取器共用这一处 | ClientStatic Confirmed |
| 3 时装扩展字段 | 第一个数 → `AvatarTypeRow`，取该时装 `[avatar type select]` 那一行的期限（0 为永久），越界时用时装自己的期限；第二个数 → 时装属性。删除 10007316 的写死特判，三个武器礼盒改由 `UntradeableAvatarBoxes` 加交易锁（裁定 1） | 70 `getAvatarPeriod`；A21 窗口把第一个数当行下标 |
| 4 服务类奖励 | `IServiceItemRewardRuleService`，商城礼包展开与开礼包共用：有背包使用入口的扩容券（金库、物品栏）当物品发，其余拒绝。补银质、黄金金库升级工具的容量（40、72） | 70 `IsSpecialItem` 表，PVF 说明 |
| 5 职业池 | `[charactor] \`job\` A B N (id w cnt)*`；只为开启者的职业抽，没有对应职业的奖励时拒绝且不消费；A、B 只记录（裁定 3）；空池跳过 | ClientStatic 解析，两套 C# |
| 6 类别自选 | 0x00A0 的 context1/2 是类别坐标；按坐标找类别；N>0 时选 N 个，N=0 时整类发放；空类别合法 | ClientStatic Confirmed，70 `_processBoosterSelect` |
| 7 自选块修饰 | `[stackable]`、`[cera]` 等都是类别；默认勾选、推荐只是界面标记；强化等级作用于其后的装备行，255 时按 `[random probability]` 抽取。增幅、品级、强制时装属性尚未实现，继续拒绝 | ClientStatic；第三方 T 的强化实现 |
| 8 商城礼包选择组 | 见下节 | 70 发放链 |

### 第 8 类：商城礼包

发放入口调查的结论：本仓没有一个现成的点能同时完成"展开"并覆盖全部入口。本分支按下面三条处理：

1. **开礼包时展开**：`IPackageOpeningRuleService.ExpandAsync`，按 70 `_processItem → ProcessSpecialItem` 的做法。
   - 普通、自选、装扮三种开启都在抽取之后调用它。
   - 抽到的奖励如果是 `[cera package]` 或 `[cera booster]`，换成它自己的内容，可以递归，最多 8 层。
     - `[cera package]` 发固定部分；
     - `[cera booster]` 按 `[booster info]` 抽取；
     - 期限取外层和自身的较小值；
     - `[package data selection]` 选择组没有人选，按 70 不发。
2. **背包里不开有选择组的商城礼包**（裁定 2）：`PurchaseSelectionGroups` 大于 0 的定义，从背包开启时拒绝且不消费。选择组只在商城购买时由客户端选择，这条已有实现。
3. **配置入口不收这两类**：`PermanentOrdinaryItemRules.Allows` 排除 `ExpandsWhenGranted` 的物品，GM 官方邮件、玩家自助后台发信、创角初始物品、每日补给因此都不能再配置它们。这几个入口不做展开，原因是：
   - 邮件单封最多 10 个附件；
   - 预览和回执显示的是礼包本身；
   - 每日补给按物品 ID 计数。

没有改动的入口：抽奖罐、自动售货机、制作、任务奖励和掉落。这些入口的内容表里有没有这两类物品还没普查，现状是当普通物品发。商城直接卖的 `[cera booster]`（例如 wildavatar 52–56）仍按物品进背包，与改动前一致。客户端在背包里能不能开这两类，没有证据。

### 第 7 类补充：锻造与品级

| 字段 | 写入的物品字段 | 证据与置信度 |
|---|---|---|
| `[booster equipment separate] s` | 锻造 `GenuineUpgrade`，范围 0–8 | PVF 10006048 的说明写"锻造等级为 +5"，块内 s=5；样本全部是武器。High |
| `[booster equipment grade] g` | 品级种子 `InstanceValue = EquipmentQualitySeeds.FixedFor(g)`，g 为 0..4 档，4 是最上级 | grade 与强化、锻造同向。写固定种子是 Hypothesis |
| `[force insert avatar option]` | 未实现，继续拒绝 | PVF 里没有样本 |

这些修饰会作用到块内 `[equipment]` 类别的每一行，但非装备物品不套用（70 只对装备套用）。

## 验证

- 新增测试：
  - `PvfPackageOpeningParseTests`：职业池、类别、修饰、重复类别、`[package data]` 覆盖；
  - `PackageOpeningDrawTests`：按类别查找、整类发放、职业池、随机强化、时装行期限、强化只给装备。
- 普查：`PackageOpeningArchiveSurveyTests` 遍历全部礼包。设置 `PVF_ARCHIVE_PATH` 时运行；设置 `PACKAGE_SURVEY_OUT` 时输出逐包明细。只允许以下失败原因：
  - 档案内容缺陷（#340）；
  - 带选择组的 `[cera package]`。
- 基线对照：主干 `ef20398a3` 在同一 PVF 下有 17 个基础设施测试失败（读档案的内容断言，与本分支无关）。本分支没有新增失败。

## 普查结果

| 阶段 | 普通 | 自选 | 装扮 | 合计 |
|---|---:|---:|---:|---:|
| 修复前（11-14 普查） | 1,713 | 876 | 245 | 2,834 |
| 本分支（同一 PVF） | 1,238 | 90 | 137 | 1,465 |
| 本分支 + #344 新 PVF `EBF8DDD3…` | 1,183 | 3 | 24 | 1,210 |

本分支在同一 PVF 下剩下的 1,465 个：

| 原因 | 数量 | 去向 |
|---|---:|---|
| 带选择组的 `[cera package]`，只在购买时展开，不从背包开启 | 1,140 | 按裁定处理 |
| PVF 内容缺陷：缺失物品、缺宠物种类、过期奖励 | 303 | #340 |
| PVF 内容错误，已在 #344 修正：54 的属性号、490001919 的未闭合块 | 2 | #344 |
| 服务类奖励还没有直接生效的实现 | 20 | 后续 |

换成 #344 的新 PVF 后，剩下的主要是：带选择组的商城礼包 1,140 个；删空后没有奖励的礼包 45 个，等用户裁定；服务类奖励 29 个。

另外，#340 对照客户端确认了以下读法，本分支都已照做：
- 只写抽取数、没有条目的空随机池；
- 漏了斜杠的类别闭合标签；
- 空的自选类别；
- 随机池的抽取数、权重、数量遇到小数时向零截断（`028A9310` 用 `cvttss2si`，252.5 读作 252）；
- 同一类别坐标写了两块时，后一块覆盖前一块，空块也会覆盖（`0296C3D0` + `01520AC0`）；
- 自选块里时装行的属性号只是窗口默认值，属性由玩家在窗口里选、随请求上传，所以只校验请求里的属性号。

本分支已经按这些读法实现。490001919（未闭合的块吞掉了使用地点限制）和 54 等礼包（随机池里的属性号超出该时装可选范围）的内容错误，由 #344 在 PVF 里修正。

用户测试流程见 [用户测试流程.md](用户测试流程.md)。

## 未覆盖与后续

- **服务类奖励直接生效**：都没有实现，继续拒绝。
  - 商贸许可证 16 和拍卖行优惠券 137：要扩展 premium 解析，读 `[target item]` 和块级 `[term]`。137 的时长未定（说明写 30 天，块级 term 读出来是 1）。
  - 无限疲劳 29、角色栏扩展券 2660239（并入 #327）、技能类型扩展券 821（解锁第 2 套技能页）。
  - 恶魔契约分项：时长取外层包装那一行。
  - 10012575、名称装饰卡 400330075。
- **10007846（30 天恶魔契约）**：`[booster info]/[cera]` 里写的是固定的 (id, 数量) 列表，按随机池读会读错。它本身是服务道具，目前拒绝。
- **时装属性号写在哪个字段**：本仓写在 `Attribute`（+0x0A），上游和第三方写在 `Durability`（+0x0B）。要用实机核对礼包开出的时装属性显示（参见 `0x0207.md`）。
- **`[charactor]` 的 A、B**：待实机核对（裁定 3）。
- **实机验收**：2026-11-14 用户按测试流程逐项开启，14 步全部通过，见 [用户测试流程.md](用户测试流程.md)。
