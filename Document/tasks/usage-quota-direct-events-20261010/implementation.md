# 实施记录

## 已实施：次数核心

- `IEventService.PublishAsync` 改为 `DirectAsync`：直接依序等待 runtime 订阅者，继承 ambient transaction，不排队、不新建事务；失败及取消向上传播。原 `INotificationBus.PublishAsync` 保持基础设施职责。
- 新 `IUsageQuotaDomainService` / `IUsageQuotaStore` 与兼容存储适配。消耗品、权益、镜像的次数更新通过四种强类型业务事实进入统一订阅者，以预计旧值检查和 CAS 写入；批次先全部验证，再写入。
- 配额变化只在同一事务中追加 `UsageQuotaChanged`。扣次事实仅 runtime 注册，不能被持久化队列再次扣次。
- 原消耗品 Effect、权益服务、双倍罐续期、任务人偶每日发放标记、镜像入场和领奖均迁移。资格、上限、冷却和重置规则仍归各领域。
- 沿用原表、键、时间戳和镜像日期行；无需数据清理或计数迁移。
- 剩余 `ICharacterDailyCounterTable` 使用点为每日补发的 `daily_refill_attempt` / `pvf_daily_refill_item_*` 领取标记，不是消耗品使用、权益使用或地下城入场配额，保持原补发领域流程。

## 核心自检

首次 `--no-restore` 缺少四个项目 assets，已通过正常 restore 解决。随后 solution build 通过，零警告零错误。

- Application 相关测试：设置真实 PVF 后 163 passed、0 skipped。
- Persistence 全套：468 passed、0 skipped。
- 新真实 SQLite 回归：后续直接订阅者失败时次数及变化事件共同回滚；三类原表兼容重载；过期预计值批次不部分更新；无事务、只读事务、重复地址和溢出拒绝。
- TRX 位于当前工作区 `artifacts/usage-quota-direct-events-20261010/tests/`。这些是 T-001 自检，完整变更还须独立审查和最终验证。

## 客户端投影

- 新只读快照门面按配额类别读取最新状态；不会重入 CommandBus 或业务事务锁。
- `UsageQuotaChanged` 使用 LiveOnly 订阅及角色/类别合并键，提交后经 `EventService.ProjectAsync` 进入通知系统。离线时跳过，登录恢复。
- 投影读取前后检查准确会话 generation；消耗品复用 021E 完整快照与 00AC 有效冷却，权益保持 CMD036F，镜像 NOTI0279 发 `unknown=0 + entriesUsed`。
- 登录、成功打开选图、镜像加载释放后均补权威镜像快照。保留 NOTI001E-only，不增 CMD0025 ACK；在线午夜按原镜像日期规则刷新。
- 原消耗品/权益日刷新与权益到期0042清除保留；两条纯快照查询的执行模式改为 ReadOnly。
- T-002 自检：solution Debug build 0 warnings/errors；Protocol focused 45 passed；Application focused 125 passed；真实 SQLite quota 6 passed（提交前无投影、合并后读最新值、回滚无通知、投影失败不回滚业务、跨午夜查询不写）。TRX 同目录 `tests/usage-quota-projection*.trx`。
- `git -c core.whitespace=cr-at-eol diff --check` 通过。TitleBookProtocolTests 基线使用 CRLF，仅重命名方法；不为本任务改写整个文件换行。

静态证据和边界见 [client-evidence.md](./client-evidence.md)。完整 Release 回归、独立审查及部署验收以最终 verification 为准；完整登录/选图实机序列仍待用户测试。

## 审查修正

独立审查发现 AGENTS.md 仍要求已删除的 PublishAsync 事务外语义，已更新为 DirectAsync 的实际职责和调用约束；未因此改变马战通知时机。
