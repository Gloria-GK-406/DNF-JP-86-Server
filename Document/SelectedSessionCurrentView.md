# 选角结果与运行期当前视图

`SelectCharacterResult` 是选角回复的一次性组包值，不是会话运行态。发布后，
`ISelectedSessionRuntimeDirectory` 只记录精确连接/代际的发布标记与位置持久化策略；
它不保存角色、背包、任务、金币或装备耐久。

运行期需要较完整的角色投影时，调用 `ISelectedCharacterCurrentViewDomainService.TryLoadAsync`：

1. 用连接 ID、角色 ID 和代际确认这是当前已发布会话。
2. 从角色、物品、账号、任务、世界规则及运行期位置各自的主人重新读取并组装。
   这条只读路径不再执行登录恢复、技能调整或物品清理等选角写入。
3. 读取完成后再次检查精确会话，避免旧连接向 replacement connection 投影。

物品移动、修理及耐久消耗直接修改物品领域持久化状态。副本选择、进图、回城、
查看角色和外观通知都重新取当前物品；不会把登录时的旧耐久再灌回客户端。
入场物品租约的内存副本、脏标记、退场写回链路已从生产流程移除。
旧的 `EntryInventoryLeaseSnapshot`/持久化接口仅为底层对账测试保留，
不在服务组合根注册，也不由会话生命周期调用。

位置仍由 `ICharacterPositionDomainService` 持有，队伍血量百分比由
`ICharacterVitalRuntimeDomainService` 按精确会话持有；旧连接清理不能移除新连接的记录。
这些短期运行态不复制整份 `SelectCharacterResult`。

这次变更只经过自动构建与回归测试。客户端实际显示的修理后进图、组队及回城效果，
待合并部署后再做实机验收；自动测试不替代 A21 实机证据。
