# 邮箱核心服务

> 历史行为说明，正文保留 `8923ea26` 时的接口与落地边界。
> 当前已接入 A21 邮箱协议处理链，进度及证据见
> [邮箱协议实现状态](tasks/mailbox-protocol/implementation-status.md)；不能从服务实现推断实机验收。
> 物品流转已采用 `ItemReference` → `Take` → `ItemRecord` → `Put`，销毁使用显式 `DeleteItem`；
> 下文旧 `Clear` 及共享材料账本描述不再是现行接口。当前材料走业务容器，
> 见 [物品生命周期规格](tasks/item-container-lifecycle/spec.md) 与
> [槽位坐标边界](inventory-slot-coordinate-boundaries.md)。

2026-09-24；实现基线 `8923ea26`。这是服务级行为说明，新增邮件操作尚未接入 A21 CMD/NOTI，也未做客户端验收。

## 调用

```csharp
var mail = new MailBuilder(MailSource.FromCharacter(senderId), recipientId)
    .WithText("寄送物品", "请查收")
    .AddEquipment(existingEquipmentIdentity)
    .AddStackable(materialTemplateId, 10)
    .AddGold(100)
    .ExpiresAt(expiresAt)
    .Build();
var sent = await mailbox.SendAsync(mail, cancellationToken);
var inbox = await mailbox.InboxAsync(recipientId, cancellationToken);
var read = await mailbox.ReadAsync(recipientId, sent.MailId, cancellationToken);
var claimed = await mailbox.ClaimAsync(recipientId, read.Attachments[0].Id, cancellationToken);
```

`Mail` 内容不可变，Builder 后续修改不改变已构造对象。邮件 ID 与附件 ID 各自稳定，数字恰好相同不代表同一对象。每个附件是一个独立领取单位；金币也是附件，可分次领取。领取不删除正文。

`MailSource.FromSystem(name)` 只供可信服务内部调用，免扣玩家资产；装备仍引用由物品系统合法创建的既有实例，要求无背包位置、无其他未领取邮件引用。它不是客户端可选择的权限。协议接入必须先鉴权，不能把客户端传入的发件人或系统来源直接当成授权。

## 资产与事务

没有外层事务时，邮箱操作自行创建并提交；已有命令事务时加入它，回执在外层提交后才能用来通知客户端。任何操作异常均使当前事务回滚，调用者不能捕获失败后再提交部分资产。

- 装备寄出仅删除原背包位置，原 `items` 行及附属列保留；领取将同一 UID 放入收件角色装备背包。禁止用模板重新生成装备。普通容器 Clear 会销毁未幸存实例，不适合邮件寄存。
- 普通堆叠物按模板与数量扣除，领取复用物品分类、槽位映射与到达规则；不足或完整数量放不下则回滚。账号材料进入账号材料袋，回执包含该账本的变化。
- 金币复用角色扣款/增加能力；若增加被携带上限截断，整个领取回滚，附件仍未领取。
- 写操作先取得 SQLite 写入资格，再读取可变事实；领取标记条件更新。重复或竞争领取最多一笔成功，不以客户端序号作为资产幂等依据。

## 可寄送性

角色装备来源限主背包中的普通装备；穿戴中装备、时装、宠物不是此接口支持的装备附件。实例装备锁和交易限制会拒绝。寄出前还读取 PVF `[attach type]`：

| 字段 | 当前服务规则 |
|---|---|
| `[free]` | 允许 |
| `[sealing]` | 仅已封装实例允许 |
| `[account]` | 仅同账号角色间允许 |
| `[trade]`、`[trade delete]`、`[sealing trade]` | 拒绝 |
| `[trade limit]` | 暂拒绝；模板+数量附件无法保存剩余交易次数 |
| 缺失、未知、异常声明 | 拒绝玩家寄送 |

字段取值有本地 A21 PVF 静态证据；交易解释采用参照服 `MailboxSendPolicy` 并与开源 PVF 字段映射交叉核对，**不标 ClientVerified**。系统奖励仍校验合法模板/实例及收件人，但不要求奖励可由玩家再次交易。

## 查询、删除与兼容

到期由注入时钟统一裁定，`now >= expiresAt` 时不可领取，登录收件箱不展示；无限期输入不设置到期时间。普通删除拒绝仍有未领取且未过期附件的邮件。物理清理、销毁或退信政策另行设计。

保留原邮箱三表，增量增加附件种类、领取状态、金币额及未领取装备唯一约束。旧消息金币迁移为附件，已领取状态保留；旧 `9999` 到期哨兵仍视为无限期。升级不清空原数据。

无法证明原装备身份/资产状态的旧附件标记 `LegacyUnsupported`，查询与登录保留，领取明确拒绝。无限期旧信因此也不能普通删除，需以后人工修复迁移；不会用重新生成装备或清空附件掩盖缺失事实。过期旧附件仍保留可追溯记录。

登录邮箱加载使用相同可见性、期限和领取事实，保留既有最早20封、页内逆序的选择方式。原线格式映射（例如附件编号编码和金币展示）仅保留现状，不作为新核心领取 ID 的线级定义。

## 后续协议边界

依据 [70流程与A21核对](邮箱系统-70流程与A21核对.md)，后续接入发信、打开/读取、逐附件领取、删除和通知；重点校对 A21 0x005E/0x005F/0x0060/0x0086 及 NOTI 0x0061/0x0062/0x0063。当前不能声称邮箱UI已可用，也尚未接任务助手人偶、溢出投递等生产者。
