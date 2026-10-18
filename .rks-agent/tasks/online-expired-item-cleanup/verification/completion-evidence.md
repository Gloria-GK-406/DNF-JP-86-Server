---
kind: rks-verification
decision: pass
---
# 完成验证

验证目标是服务端在线清理实现与部署就绪；不宣称真实客户端已跨六点验收。

- SC-1：实际ApplicationModule注册的调度器在受控UTC+8六点触发。OnlineExpiredItemScheduleTests与在线SQLite测试证明离线不删除、排队发布状态变化及旧会话跳过。
- SC-2：登录和在线均调用ExpiredCharacterItemCleanupDomainService.CleanupAsync；Persistence的装扮/普通过期与永久测试保留，新增在线未来期限/永久及事务回滚回归通过。无历史物品期限修改、无Schema修改。
- SC-3：真实SQLite事件在提交前不投影，回滚无事件；同槽后来新物品在NOTI000D中仍保留。Protocol测试验证穿戴清理本人/可见玩家收到当前外观且旧generation不发送。独立审查approved。
- SC-4：失败隔离和重复幂等回归通过；有限重试来自既有调度器已覆盖测试。部署数据库备份/integrity与schema对比ok，bootstrapready、GM200、31个监听端口归属新PID25892。

当前最终证据：artifacts/online-expired-item-cleanup-20261014/application-verified.trx（3882/3882）、persistence-verified.trx（468/468）、protocol-verified.trx（1520/1520）、host-verified.trx（25/25）。以上均exit0，无跳过。

初始行为RED：online-cleanup-red.trx因注册集合为空失败，后续focused75通过。application-full.trx保留首次架构问题证据，已修复后全量重跑通过。

部署：deployment-verification.json、source-manifest.json。ProductVersion=0.2.5-online-expiry.20261014+snapshot.105b3d1c91c1ea54；SourceIdentity与当前源码哈希逐项复核一致。原item-grant-expiration运行产物与rollback/database-before-online-expiry.db保留。

验收边界：下一次真实北京时间06:00保持在线，观察到期物品与穿戴刷新；离线角色下一次登录清理。现有LiveOnly通知是尽力提示，持久化删除由重新登录快照恢复，不承诺跨进程恰好一次提示。
