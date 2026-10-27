# 选角结果与运行期当前视图

`SelectCharacterResult` 是选角回复的一次性组包值，不是会话运行态。发布后，
会话进入世界只是在线身份（`OnlineStateRegistry`）上选中会话记录的阶段变化
（`ISelectedSessionDomainService.EnterWorld` / `IsInWorld`）；是否落库位置不存储，拆除时由频道推导。
在线身份不保存角色、背包、任务、金币或装备耐久。

运行期需要较完整的角色投影时，调用 `ISelectedCharacterCurrentViewDomainService.TryLoadAsync`：

1. 用连接 ID、角色 ID 和代际确认这是当前已发布会话。
2. 通过独立的 `ICharacterCurrentStateQueryDomainService.ReadAsync`，从角色、物品、账号、
   任务、世界规则及运行期位置各自的主人重新读取并组装。在线查询不调用选角准备服务，
   不执行登录恢复、技能调整、物品清理或出生点计算。当前位置同时用于角色状态、
   世界位置投影及邻接状态查询；没有在线位置时明确报错，不使用登录落点代替。
3. 读取完成后再次检查精确会话，避免旧连接向 replacement connection 投影。

物品移动、修理及耐久消耗直接修改物品领域持久化状态。副本选择、进图、回城、
查看角色和外观通知都重新取当前物品；不会把登录时的旧耐久再灌回客户端。
入场物品租约的内存副本、脏标记、退场写回链路已从生产流程移除。
旧的 `EntryInventoryLeaseSnapshot` 及配套持久化接口也已删除。
当前库存投影由入场读取链路组装，物品修改通过引用、取出记录与生命周期服务完成；
见 [物品生命周期规格](tasks/item-container-lifecycle/spec.md)。

位置、队伍血量百分比、物品冷却与灵药加成由 `IOnlineCharacterRuntimeDomainService` 的同一条
运行时记录持有，记录属于开始它的会话代数；旧连接的迟到写入与清理不能改到或移除新连接的记录。
这些短期运行态不复制整份 `SelectCharacterResult`。

登录落点只在选角准备阶段通过 `IEntryPlacementRuleService.ResolveAsync` 计算。
普通频道的上次城镇不存在、没有到达门或门坐标无法编码时，回退到配置的初始城镇到达门。
初始城镇同样无有效到达门时仍报告配置错误；内容读取异常不被兜底吞掉。
受限频道继续使用自身指定区域的临时落点，避免跨到不允许的城镇。
落点由已有 `PlaceAtSpawnAsync` 和会话发布流程放置、持久化并投影，不增加额外位置同步。

2026-10-07 的时空之门断线：Windows Application 事件 198442 记录选图请求 `CMD 0x000F`
在读取当前角色状态时触发 `Town 12 declares no arrival gate`，选图回包尚未发送。
这次拆分消除在线查询对登录到达门的依赖；深渊入场协议与掉落功能不属于本次修复。

自动构建、回归与实际 PVF 测试不替代 A21 实机验收；客户端选图、进图、组队及回城
表现需要在部署后分别验证。
