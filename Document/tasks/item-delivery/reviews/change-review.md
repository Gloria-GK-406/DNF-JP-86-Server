# 统一奖励交付变更审查

## review round1（历史结论）

```yaml
kind: rks-review
verdict: changes_required
```

目标：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/despair-tower`，HEAD `4aec17baa083271f322012043511f9f2845112db` 上的 **dirty diff / review round1**；2026-10-11 审查。独立审查上下文，无生产代码修改，无构建/测试执行，无部署。

依据：工作区 AGENTS.md、TaskContract revision 1、spec.md RQ-001..008；`reviewing-work/SKILL.md` 与 `references/code.md`。本轮专注实现代码及关联既有调用；测试适配与最终迁移矩阵仍由主执行者完成，未将本轮结论当成完整验收。

## 阻断发现

### F1 / P1：新支持的装扮、宠物邮件仍使用普通装备线形

位置：`Server/src/Dfo.Server.Persistence/Mail/SqliteMailboxRepository.cs:82-100`；关联 `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs:685-730,755-773`。

统一服务将所有非堆叠实例描述为 `MailAsset.Equipment`；仓储据此同时将 `asset_kind` 和 `item_kind` 写为 `equipment`，也没有写入装扮 `detail_json`。现有邮件编码器却仅在 `ItemKind == "creature"` 时写宠物额外标量，在 `ItemKind == "avatar"` 时写装扮外观块。因此满包装扮/宠物奖励虽然可以进数据库，打开邮件时仍生成与模板类型不一致的内容；单纯放宽 custody 的实例类别并未完成协议支持。违反 RQ-006/007、SC-3/5。

最小修复：区分邮件托管资产种类与客户端展示种类，按实例的实际 kind 写 `item_kind`，保存并投影装扮详情；保留 custody 使用的 equipment 类别及 UID。以实际邮件查询投影构造 NOTI 并编码，覆盖装扮和宠物，不能只测 ClaimAsync 存储往返。

### F2 / P1：装扮交付/领取投影丢失 AvatarDetail，提交后编码必抛；宠物领取无运行态同步

位置：`Server/src/Dfo.Server.Application/Mail/MailboxAssetDomainService.cs:30-35`、`Server/src/Dfo.Server.Protocol/Game/Mailbox/MailboxDelivery.cs:35-37`；同类路径 `Server/src/Dfo.Server.Protocol/Game/OpeningDelivery.cs:26-29`。

邮件资产投影只保存 `ToCore()`，之后构造 `InventoryItemState` 不带 `AvatarDetail`；礼包交付投影同样丢弃 `OpeningInventoryUpdate.Core.AvatarDetail`。`GameInventoryItemEntry.Write` 在 `GameInventoryDelivery.cs:77-78` 对 kind 8 明确抛出缺少 AvatarDetail。装扮邮件领取事务已经提交且附件已认领，随后网络编码失败，既不给正常资产更新，也无法重领。宠物领取另只发普通背包 delta，没有与礼包路径相同的宠物运行态同步。违反 RQ-006/007、SC-3/5。

最小修复：让资产投影保留装扮详情并传入 `InventoryItemState`；对宠物领取构造当前宠物运行态并通过既有宠物消息投影。礼包更新构造器也传递装扮详情。增加“交付/领取 -> Compose -> 真实 encoder”覆盖，断言成功领取后所有所需通知可编码。

### F3 / P1：开罐仍要求奖励全部进入装备栏，邮件回退被后置断言回滚

位置：`Server/src/Dfo.Server.Application/Inventory/LotteryOpeningDomainService.cs:81-83`；关联 `Server/src/Dfo.Server.Protocol/Game/LotteryOpening/LotteryOpeningSlice.cs:50-53`。

共享 GrantPreparedItemEffect 已允许满包邮件，但开罐仍要求装备栏 Updates 的数量等于 1/2 件。满包时发奖服务准备邮件后，这个断言抛出并回滚整笔用例；双倍罐只剩一格时同样失败。双件全邮件又被合并为一个展示行，Rewards.Length 检查也失败。因此该奖励入口仍不支持已承诺的满包交付。仅删断言还不够：encoder 通过 `receipt.Updates.Single(x => x.Slot == reward.Slot)` 取实例，邮件行 Slot=null 没有相应背包行，随后将变成提交后的编码异常。违反 RQ-004/005/007、SC-1/6。

最小修复：以总交付数量验证结算，保留可独立编码的奖励实例/展示详情，ACK 不从背包 Updates 反推邮件奖励。覆盖单罐满包、双倍罐一格与全满、成功编码以及重放不重复发奖。

### F4 / P1：售货机邮件奖励导致已提交后的 ACK 编码失败

位置：`Server/src/Dfo.Server.Protocol/Game/VendingMachine/VendingMachineSlice.cs:51-60`。

`slots` 将邮件奖励的 null Slot 计入 HashSet；`entries` 只来源于真实背包 Updates。只要售货机奖励存在邮件余量，entries.Length 就少于 slots.Count，固定触发 InvalidDataException。此时命令已提交，代币已消耗，邮件已发送，但成功 ACK 发不出。违反 RQ-004/007、SC-5。

最小修复：原生背包条目只包含有实际槽位的奖励，邮件奖励仍通过已有 ItemId/Count 展示；校验实际槽位集合而非含 null 的展示集合。增加全部邮件和部分邮件两种 receipt 的真实 encoder 回归。

### F5 / P2：共享材料有部分容量时仍将全量寄走

位置：`Server/src/Dfo.Server.Application/Inventory/ItemDeliveryDomainService.cs:95-108`；关联 `Server/src/Dfo.Server.Application/Inventory/ItemArrivalRuleService.cs:53-57`。

新服务将整个 pending.Count 传入既有 ForGrant。该规则对 SharedMaterial 采用整批容量检查，`current + count > StackLimit` 直接 NoRoom。例如共享材料当前 `int.MaxValue-2`，奖励 5，实际尚可入库 2，但新路径将 5 全寄邮件。普通材料支持部分堆叠，并不能证明共享材料满足相同约定。违反 RQ-002 和 SC-2 的“能装入的先入库，只邮件剩余”。

最小修复：交付路径对共享材料按可用额度请求本次入库数量，剩余维持原记录邮件；避免全局改变购买/拾取现有整批规则。增加共享材料边界 2 入库 + 3 邮件测试。

### F6 / P2：塔奖励邮件实例的展示 seed 与实际实例不一致

位置：`Server/src/Dfo.Server.Application/World/IllusionTower/IllusionTowerRewardDomainService.cs:119-121`；关联 `IllusionTowerRewardRuleService.cs` 的 Item 返回 `DisplayValue=0` 和 `Protocol/Game/IllusionTowerReward/IllusionTowerRewardSlice.cs:31`。

入库时显示实际 InstanceValue；全邮件时 receipt.Inventory 为空，回退到候选的 DisplayValue（当前装备候选固定为 0）。新交付服务已经为邮件装备生成随机 InstanceValue，但结算 NOTI 写出的仍是 0。玩家看到的装备属性种子与领取的装备不一致。违反 RQ-007/SC-5。

最小修复：回执包含实际交付实例的展示事实，或在调用交付前一次决定实例 seed 并用于入库/邮件和候选展示。覆盖满包塔装备结算值与实际邮件实例值相等。

## 反向扫描与最终复审范围

已从生产代码全仓扫描 `GrantAsync`、`GrantAllAsync`、`GrantAllPersistentAsync`、`ApplyPlannedAsync`、`PutNewAsync`、`PutAsync`、`CreateAsync`、`SendPreparedAsync`、`MailBuilder.FromSystem` 以及奖励 Effect 制造者。对迁移入口读取了原 HEAD 与当前源码差异；`git diff` 在本工具环境持续报 must be run in a work tree，改以 Python difflib 对 `git show HEAD:路径` 与当前内容生成差异。

确认保留独立语义的路径：地面拾取、临时塔库存、初始资产及建角、NPC/Cera 购买、装扮合成、史诗制作、图纸制作、邮件领取、容器移动/排序、镜像/正规化。共享 Effect 的制作调用使用 AllowMail=false；物理奖励 Effect 使用统一服务；史诗碎片地面拾取继续 EpicBook 专用领域；礼包权益/复活币继续对应专用 Effect。来源上的认领、开奖、支付和邮件失败回滚边界已检查，未从本轮静态检查发现额外确定的重复发放路径。

两处不可漏记的实际系统物品制造者仍绕过统一服务，须由最终迁移矩阵明确裁定，不能将它们误标为普通移动：

- `Achievements/TitleBookDomainService.cs:91-125`：AchievementCompleted 或 PrepareAsync 触发称号解锁后，在未持有称号时 CreateAsync + PutAsync 真正制造物理称号。目标是专属称号簿而非背包，需说明统一服务如何覆盖或契约为何允许保留该专属目的地。
- `Administration/GmOfficialMailDomainService.cs:121-145`：根据管理员配置快照直接创建物品后 SendPreparedAsync；属于显式系统邮件交付，带自定义文案、金币、期限及回执，不可仅因入口叫 GM 就从“全部系统奖励”矩阵中漏掉。若保留，应在范围中明确其独立邮件用例边界及理由。

矩阵尚未交付，本轮不对上述两项制造覆盖做最终批准。最终复审必须检查修复差异、完整迁移矩阵和实际测试结果，并验证没有把纯存储测试等同于协议编码或 ClientVerified。

## 本轮目标指纹

| 文件 | SHA-256 |
|---|---|
| ItemDeliveryDomainService.cs | D1D1E120B2E12EA616BC52AE08DC5BB39C68AF1625DE3441E80AF9BE485B419F |
| SqliteItemDomainStore.Mail.cs | E97A00B964A6ECFEDCF2C2E50FA365BD1A319B1A77FC30B5A7AF6DE76170E357 |
| MailItemTransfers.cs | D8207082184DAD445B1D3486DEEA2389D7AE818B132256383D41903F1F8208A4 |
| LotteryOpeningDomainService.cs | 9E436C8EE522D6834536AFE6D3EF05B412179AEFA1750BF8676C25B632A4F241 |

本轮没有执行测试；测试通过与否由主执行者提供新鲜证据。本文件保留 round1 发现；后续修复需要独立复审，不能将本轮变更请求直接改写成通过。

## review round2（当前结论）

```yaml
kind: rks-review
verdict: approved
```

目标：相同工作区与 HEAD `4aec17baa083271f322012043511f9f2845112db` 上的 **dirty diff / review round2**，2026-10-11。依据仍为工作区 AGENTS.md、TaskContract revision 1、spec.md RQ-001..008、reviewing-work 的 Code 标准；重新读取更新后的契约范围、reward-migration.md、migration-scan.md 与扫描脚本。独立检查修复差异和调用链；没有修改生产代码，没有运行 .NET，没有部署。

本轮代码及迁移完整性范围内没有未解决的阻断缺陷。此结论不代替主执行者的最新构建、广域测试及完成验证，也不构成 ClientVerified。

### 首轮问题处置

| 问题 | 当前代码证据（Server/src 下） | 裁定 |
|---|---|---|
| F1 | `Dfo.Server.Persistence/Mail/SqliteMailboxRepository.cs:93-114` 将 asset_kind 保留为托管类型，另按 LegacyKind 保存 avatar/creature 的 item_kind，并写入完整 Avatar/Creature detail_json；与既有邮件 codec 的类型分支/Avatar JSON 读取相符 | 已修复 |
| F2 | `Dfo.Server.Application/Mail/MailboxAssetDomainService.cs:36-45` 保留 AvatarDetail，领取宠物时读取并投影运行态，容量上限在提交前检查；`Dfo.Server.Protocol/Game/Mailbox/MailboxDelivery.cs:37-44` 传递详情及宠物消息；`Game/OpeningDelivery.cs:31` 同样传递装扮详情 | 已修复 |
| F3 | `Dfo.Server.Application/Effects/OpeningEffectHandlers.cs:106-110` 为每件邮件实例生成含 Snapshot 的独立展示行；`Inventory/LotteryOpeningDomainService.cs:81-83` 只验证完整奖励行而非背包更新数；`Dfo.Server.Protocol/Game/LotteryOpening/LotteryOpeningSlice.cs:51` 优先用奖励快照，邮件行无需反查背包 | 已修复 |
| F4 | `Dfo.Server.Protocol/Game/VendingMachine/VendingMachineSlice.cs:51` 只将有实际槽位的奖励纳入背包条目集合；邮件奖励仍由原奖励 ItemId/Count 展示 | 已修复 |
| F5 | `Dfo.Server.Application/Inventory/ItemDeliveryDomainService.cs:110-116` 对共享材料按兼容持有量计算剩余额度，再交给既有 arrival；循环将剩余记录寄出，未全局改变购买/拾取的原规则 | 已修复 |
| F6 | `Dfo.Server.Application/Inventory/ItemDeliveryDomainService.cs:159` 返回真实邮件快照；`World/IllusionTower/IllusionTowerRewardDomainService.cs:121-122` 从入库或邮件实例读取展示种子，保持与领取实例一致 | 已修复 |

### 新增约束和迁移完整性

`ItemDeliveryDomainService.cs:47-58` 先按 PVF 查找真实 placement，再验证约束只能用于 Interchangeable、库存空间相同、栏位上下界包含于原 PVF 范围，最终堆叠上限取两者较小值；0 按原语义视为未声明上限，仍不能放宽 PVF 的有限上限。`87` 行关闭受约束请求的快捷栏备用路线；`114-116` 行使用只在给定范围内找堆叠/空槽的 ForDailyRefill。已开放容量仍由容器的 OpenSlotCount 遮挡。生产调用只见 `DailyRefillDomainService.cs:61`；没有发现该参数引入额外库存路线。

每日补充在 `DailyRefillDomainService.cs:55-61` 将存活、未领取附件计入持有量，并在交付后写周期标记。配方制作与材料合成分别显式 AllowMail=false；购买、地面拾取及邮件领取仍保留各自容量失败语义。邮件领取失败沿用同一事务回滚，不会把附件再转寄；UID 托管排他校验和实际实例读取仍在持久化层执行。

在首轮全域反查基础上，重新核对直接 Create/Put/Grant/ApplyPlanned/SendPrepared 调用与共享 Effect 的制造和投影。只读复算扫描得到 **710 个排除 bin/obj 后的生产文件、64 个命中文件、0 个未分类文件**；原脚本的 722 是过滤生成文件前的枚举数，现已修正计数，扫描文件链接也已修正。文本分类不等于完整数据流证明，结论还结合代码语义复核。没有发现新增自动物理奖励入口遗漏。

称号簿 UnlockAsync 是真实制造者，但固定进入专属称号簿、受目录解锁与镜像关系管理；GM 官方邮件是真实制造者，但收件人、正文、附件状态和交付方式由管理员明确选择。更新后的契约、规格和矩阵已将两者正确列为例外；不能再将它们误标为移动，也没有理由将其改变为背包优先。

### 验证证据和边界

已阅读真实 DI 的 `Server/tests/Dfo.Server.Application.Tests/Inventory/ItemDeliveryTests.cs`，覆盖部分堆叠/邮件数量、23 件装备拆批与独立 UID、装备/装扮/宠物属性领取往返、邮件存储失败与来源扣费回滚、后续无效奖励使整批回滚、满包领取保留原邮件，以及真实 Mailbox handler -> projection -> encoder 的装扮/宠物扩展。每日补充测试还覆盖跨天未领邮件不重复补发且不借用空快捷栏。

已阅读 LotteryOpeningTests 的满装备栏单罐与一格双倍交付/编码测试、VendingMachineTests 的满包扣币与邮件/ACK 测试，以及 IllusionTowerDomainServiceTests 对实际邮件种子和重复结算的断言。Persistence 的老命令 fixture 使用 BagRewardDelivery 测试适配器，**不能当作生产邮件持久化、邮件事务或完整协议证据**。

截至此轮检查，主执行者报告补充最后两个测试前真实 DI ItemDeliveryTests 11 项通过；上一轮 Application 3649 通过、4 失败，其中 3 项地图 PVF 失败已在独立基线 4aec17b 原样复现，另 1 次生命周期 2 秒等待超时在隔离重跑完整 SelectCharacterLifecycleTests 后 29 项全过；Protocol 1443 通过、Persistence 468 通过。最终广域运行结果由主执行者补充。审查者未运行或独立重现这些测试，只独立检查测试代码及其能够支持的断言范围。

定稿前已独立读取补充的 `ItemDeliveryTests.Shared_material_fills_remaining_balance_and_mails_only_the_remainder`，使用真实 DI/存储明确断言共享材料 `int.MaxValue-2 + 5` 入库 2、邮件 3；以及 `LotteryOpeningTests.Full_double_reward_mails_both_instances_and_encodes_ack_without_inventory_reward_slots`，断言只抽取一次、只消耗一次、两件均邮寄、两行均无槽位并编码实际 ACK。原非阻断测试补强建议已落实。真实客户端仍需验证满包奖励展示、邮件提示、装扮/宠物邮件打开及领取。

### round2 目标指纹

| 文件 | SHA-256 |
|---|---|
| ItemDeliveryDomainService.cs | AD50B73D81BFD40AEACD0831D6BE98C67016DC7A6C8EC8396B643E7597D53619 |
| SqliteMailboxRepository.cs | 6474BF6323A6A9F06229E5889975FAD8BEFF2D2F20B4138BFD48B6FA872DF1D7 |
| MailboxAssetDomainService.cs | F5FA43F6E16A41FE7BF483CE90E4C884F99628FD9A08D970F852C72A5D9B0CB1 |
| OpeningEffectHandlers.cs | 0BEBDFB321F4A0D200179FBD64A07F968D617D03F17A73940422C637CE1D47D8 |
| reward-migration.md | 5ACF8690F7EA55586869801C3382C45FCCDDE08D3E84ED04C67495CA98DAB176 |
