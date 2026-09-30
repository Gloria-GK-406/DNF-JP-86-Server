# 带宠物选角断线修复

日期：2026-09-30。修复基线：`982f8d7e`，分支 `task/solo-play-roadmap`。证据类别为 `CurrentImplementation` 与运行日志；A21实际登录成功待用户复验。

## 失败与原因

用户登录1123后选角失败。2026-09-30 13:15:15 +08:00的服务日志记录 `SelectCharacterHandler` 抛出 `InvalidDataException`：`SupportEquipment.Noti2CreatureMarker[slot 25]` 不可用。异常出现在 `ExtendedUserStateMessage.EncodeBody()`，在发送选角接受批次前触发补偿并关闭连接。原始日志保留在 `E:/Project/DNF/DNF-JP-86-Server/artifacts/creature-system-20260929/acceptance-20260930-130957.stdout.log`。

`SelectCharacterPreparationService` 已为当前角色的穿戴宠物生成期限投影，但 `PrepareSelectedCharacterStateCommandHandler` 随后调用库存绑定。`PrepareLeaseAsync` 再次读取原始库存，并覆盖整份 `CharacterInventory`，导致非持久字段 `Noti2CreatureMarker` 丢失。每日补货后重新加载库存也存在同样问题。原回归只编码首次准备结果，没有覆盖库存绑定和补货步骤。

## 修复

将现有宠物期限投影提取为共享 `SupportCreatureMarkerProjection.CompleteAsync`，首次准备、支援投影、库存绑定重载及补货重载共同调用。重载后依当前物品模板、期限和此次准备时间重新计算，不复用可能已变更的旧装备字段；期限优先级和线格式保持原实现。没有修改正式库中的宠物或装备数据。

## 新鲜验证

- 将原带宠物完整登录编码回归扩展到真实库存绑定、补货与完整消息编码：永久/900秒限时 × 无/有补货，共4例。修复前4例均复现同一个缺字段异常，修复后4例通过。
- Release解决方案构建：0警告、0错误。
- Protocol全量：1126通过、60跳过、0失败。
- Application定向：65通过，覆盖选角生命周期、库存绑定/补货、宠物期限算法、宠物运行事务及命令架构。
- Host全量首次：23通过、1失败；失败为非法LegacyMd5配置场景的进程退出等待20秒超时。再次单独运行Host全量：24/24通过。超时根因未确定，不删去首次失败。
- 正式库的SQLite备份副本上，使用生产模块、生产 `PrepareSelectedCharacterStateCommand` 和登录消息工厂验证角色2：61条消息全部编码成功；穿戴宠物UID949、core键949、期限marker为0。探针只读正式库取副本；其事务仅写副本。探针与输出在 `E:/Project/DNF/DNF-JP-86-Server/artifacts/creature-login-fix-20260930/probe`、`login-probe.log`。
- 测试完整日志保留在 `artifacts/creature-system-20260929/login-fix-*-tests*.log`；未宣称真实PVF应用全量或完整宠物功能通过。

## 部署与复验

完整发布当前任务分支至 `E:/Project/DNF/DNF-JP-86-Server/artifacts/creature-login-fix-20260930/runtime`，继承原运行配置和数据库路径，因此也包含该分支之前已自动交付的装备功能。部署前备份：`before-deploy-20260930-132313.db`，`quick_check=ok`。原运行包及失败日志保留。

用户先复验P-01：登录1123进入城镇、打开宠物栏；随后P-04验证带宠物退出重登。自动回归与副本验证不等于客户端已验收；孵化与特殊成长仍保持原未完成边界。
