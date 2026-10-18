---
kind: rks-review
verdict: approved
---
# Item grant expiration 完整结果审查

审查人：独立 reviewer `item_expiration_review`。基准：`df570d8700b369632aa819878511f650d22a9419`；目标：当前工作树全部本任务变更（含未跟踪源文件、测试、只读模板工具、Document 文档及受控历史改期脚本），源码 snapshot `0c66b709624d4bef`。治理：TaskContract revision 1、spec.md、plan.md、preflight.md、postcheck.md；应用 reviewing-work 的 Code 与 Deliverable criteria，遵守仓库 AGENTS.md。reviewer 没有修改生产源、测试或数据库，没有启动清理或停止服务。

结论 approved：已报问题均在实际操作前修复，最终代码、必要回归、受控数据操作与部署 postcheck 均完成独立核对；本审查范围内没有未解决的阻断缺陷。当前结果支持保留新 runtime 与限定改期，不替代用户真实客户端登录验收或控制器的最终完成核验。

## 已查明并解决的阻断问题

| 问题 | 后果 | 当前修正与复审 |
|---|---|---|
| 历史目标查询把 world 槽的 s.id 当模板 ID | world 的 id 是实例 UID，模板审计失真，可漏掉限时装备或把其他同号模板政策用于永久装备 | 脚本现从 core 偏移1解码模板 ID，并校验 world id=UID / template id=模板 ID；输入和 dry-run 已重建。独立只读重算182槽：19目标、162无期限/永久项、1类型差异排除；目标 UID 集合与 dry-run 完全一致。旧67排除结论作废。 |
| 开包正值期限绕过 daily delete | 模板或包裹天数已生成正值时每日截止不参与最早期限，Lifecycle 又保留该正值 | 开包现先算模板声明期限，再与默认/包裹天数及绝对期限取最早；新增两个组合边界回归。固定日期内容源显式使用 UTC+8，避免主机时区分叉。 |
| Cera 商城在叠加规划之后才准备模板期限 | 旧0堆被计划合并后应用失败；同日有限期堆被误判不可叠加并占额外槽位 | Expand/Quote 现准备最终期限后形成 grant，再进入 Plan；新增实际购买两次及旧0堆分离的领域/SQLite 回归。 |

## 独立检查结论

- PVF 三种期限的严格解析、日界与固定 UTC+8、正值/永久哨兵保留、多个模板约束取最早、int.MaxValue 永久哨兵隔离均与 spec 一致。
- Delivery 仅对 Instance=null 准备新实例；业务容器 Add 与商店在叠加选择前准备；CanStackWith 比较期限及其他实例元数据。
- 既有邮件领取、容器移动及分拆只传递/拆分原 ItemRecord，不调用新物品准备，不续期。溢出邮件保留已准备期限。
- 原 ExpiredCharacterItemCleanupDomainService 未改，继续读取普通 core.ExpireTime / 装扮 ExpireDate，并跳过0和 int.MaxValue。本任务没有新增定时删除入口或 schema 变更。
- 数据脚本仅选 owner_scope=character、owner_id=2，确认名称1123，排除 list19..23；更新只涉及目标 items 截止字段，事务内逐行校验其余字段、其他 items、所有其他表及 sqlite_master 不变。备份不覆盖、PVF 路径和 SHA-256 锁定、清单漂移拒绝、共享引用拒绝、完整性检查与回滚路径均已审查。
- 唯一剩余排除为 UID1592：core 模板1、存储 kind0、PVF kind2（虚拟金币形状）；未进入改期集合。
- Persistence golden 六行 ResealCount 是既有 Attribute 右移5位的派生观察值（139→4、169→5、109→3、49→1、89→2、69→2），生产字段已于67fce4d2加入；没有修改 seed 或业务字节消除失败。
- Tool/ItemLifetimeAudit 仅访问 PVF 输入ID数组，不访问数据库；已显式读取未跟踪 csproj 与 Program.cs。Document 清楚区分自动回归与客户端验收，没有把模板审计或代码标为 ClientVerified。

## 实际操作 postcheck 独立核对

- postcheck.md 决定 retain；旧 runtime 和配置保留，未发生需要恢复的失败。
- 已独立只读打开停服后的改前备份及改后启动前快照，两份 PRAGMA integrity_check 均 ok；SHA-256 分别为 `7ba38128ea2680b3877d84b7dbcd200cd9edf93d78ec7382149111dd3c369b2b`、`ad7222e49dee1dff6e77ace9bec055951bfccc26ee6177b63c69bf684edf3e43`，与 historical-change.json 一致。
- 对两份快照逐表、逐行及 items 字节独立比较：78张非items表完全一致，sqlite_master 完全一致，items UID集合和行数不变；只有19个 dry-run 授权 UID 的 item_core 第41..44字节变化，所有其他字节/列不变。19个期限均为1791820800，即 UTC+8 2026-10-13 00:00。没有直接删除。账号仓库、邮件、称号簿、其他角色及所有永久物品均无本次改期差分。
- 启动后又独立只读确认19实例全部仍在，且唯一槽位所有者均为角色2/1123、期限均1791820800；live DB完整性ok。用户登录前的测试目标已保留。
- 独立取得 PID52176 的实际 exe 路径为 `artifacts/item-grant-expiration-20261014/runtime/Dfo.Server.Host.exe`；Host ProductVersion 为 `0.2.5-item-expiration.20261014+snapshot.0c66b709624d4bef`。Application、Protocol、配置哈希与 deployment-verification.json 一致，部署 PVF 与模板审计哈希一致。
- 实际 bootstrap ready、GM HTTP200；PID52176 的31个监听端口集合与部署证据一致。DB和PVF路径符合既定目标，stderr为空。
- 本次没有代用户选角或调用真实角色清理；真实客户端正常登录后的删除与永久物品保持情况，仍由用户验收。运行中的正常后台业务可继续写其他表，因此以上完整差分结论严格基于停服快照，而非承诺运行数据库永久不变。

## 验证证据复审

- source-manifest.json 的27个文件逐项 SHA-256 与当前文件全部相符，snapshot 标识为 `0c66b709624d4bef`。
- application-verified.trx：3875/3875通过；infrastructure-reviewed.trx：900通过、2未执行；persistence-final.trx：468/468；protocol-verified.trx：1519/1519；host-verified.trx：11/11。真实 PVF 的固定日期、7天、每日删除以及永久样例均实际通过，没有跳过。
- application-reviewed.trx 曾有两条墙钟/线程调度失败：通知的两次50ms截止耗时5958ms超过断言2.5s；EffectOperationReplay 的 Task.Run 进入信号超过5s。完整错误身份已审查、原失败证据保留；timing-isolated.trx 两条实际通过（163ms/184ms），随后独立全量 application-verified 全绿，未扩大原测试预算。
- 原登录清理的 SQLite 幂等、永久哨兵和错误账号拒绝测试均包含于 Persistence 通过集合。自动回归不替代用户实机登录验收。
