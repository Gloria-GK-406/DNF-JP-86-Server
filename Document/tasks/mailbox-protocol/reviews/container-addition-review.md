---
kind: rks-review
verdict: approved
---

# 容器统一添加增量独立审查

审查范围为 `ICharacterContainerDomainService.AddAsync`、`CharacterContainerAddition`、`PersistentItemContainer` 的可空快照源，以及 `MailStackTransfers.GrantAsync` 的新入口调用和领取后的资产投影。此结论不评价整个邮箱任务是否完成；当前生产代码未作修改。

## 已修正的阻断

**跨角色持久包写入已阻断。** 初审发现 `CharacterContainerDomainService.Register` 不校验注册容器的 owner，而持久 store 的 `OwnerOf` 仅从容器 ID 推断实际写入角色。现 `CharacterContainerAddition.AddAsync` 在首个 `bag.AddAsync` 前校验可解析的角色容器 ID，其 owner/type 必须等于请求角色/目标类型；错配抛异常并回滚。新增负向测试把 B 的持久包注册给 A，验证拒绝且两人均无新增槽位。此修补覆盖当前生产唯一的持久容器实现。

**账号材料不再依赖普通物品 placement。** `accountRules.TryGetByItemId` 现在先于 `placements.PlacementForAsync`。新测试以已登记的 `3037` 且无普通 placement 的 fixture 调用统一入口，断言账号材料 receipt 和角色无物品槽位。建议增加账号表 `cube_clear` 的直接余额断言，强化持久化验证；现有账号材料 bag 测试另覆盖余额提交和失败回滚。

## 已核对与剩余边界

- 普通物品的 `AddAsync` 捕获同一当前容器，反复调用 `IItemContainer.AddAsync` 直到全部数量落位，记录每步的实际槽位和数量；`NoRoom`、`UnknownItem` 和非法进度触发异常。持久容器每步在同一领域事务和 SQLite 参与者内，部分落位后失败会回滚；新测试覆盖 19 件只能放一部分时回滚为零，以及未知 ID 拒绝。
- 在线路径取注册表的当前容器；离线路径临时构建同角色、同类型的持久容器，不注册为在线包。后者的 `snapshots=null` 只禁用无快照源时的 `SnapshotAsync`，不影响 `AddAsync` 的 store 读写；现有离线测试验证入库后仍无在线注册。
- 新的 `MailAssetChange.ContainerId` 让邮件领取投影按实际 ID 回读，解决非默认**持久**容器被错误回读为默认包的问题。`CharacterContainerAddition` 现在在首个写入前拒绝非 `StoreBackedItemContainer`，不会假称任意内存包也能事务回滚。当前生产只有 `PersistentItemContainer`；支持未来非持久/临时当前包需独立设计其事务参与、回读与客户端投影。

**结论限定于当前持久容器与邮箱采样增量，通过。** 本审查为静态独立检查，没有与执行者并发运行同项目测试。执行者报告本轮 43 个 mail/container 测试通过；实机 A21 资产投影和整个邮箱任务仍待后续验证。
