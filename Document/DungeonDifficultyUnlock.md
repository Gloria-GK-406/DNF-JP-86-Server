# 通关解锁地下城难度

2026-10-07，CurrentImplementation。用户授权：有效通关即解锁下一档，不要求评分；账号共享。

- `GrantClearExperienceCommand` 的结算事务调用 `ICharacterDungeonDifficultyDomainService.UnlockAfterClearAsync`。
- 领域服务通过难度规则服务取得下一档，仅提高已解锁上界。重复、低档和最高档通关不重复提升。
- 规则服务读取地下城内容源投影的 PVF 难度声明；优先指定档位，其次 `[difficulty level]` 的非零位置，其次固定 `[difficulty]`。没有声明不假定五档。任务专属路径不参与普通难度递进。
- 新记录写 `account_dungeon_permissions`，主键为账号与地下城。角色接口通过角色所属账号访问共享权限；旧角色表只读兼容，合并同账号旧记录的最高值，不改写历史记录。登录和选图查询不执行写入。
- 事务提交、运行态结算确认后，权限发生提升才投影 `NOTI 0x0005`：`u16 count + N*(u32 dungeonId + u8 state)`。重新登录、重新选图读取共享权限。

该规则是自定义服务端政策，不宣称是 A21 官方评分政策。自动检查不替代实机验证；需检查低评分通关后的难度按钮、重登保持与同账号其他角色共享。
