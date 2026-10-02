---
kind: rks-review
verdict: approved
---

# Spec003 完整结果独立审查

2026-10-02。本 reviewer 独立于 producer、drift reviewer 和 change-challenge 作者，采用 `reviewing-work/SKILL.md`、`references/code.md` 与 `references/deliverable.md`。结论：当前完整混合交付没有未解决的阻断缺陷，可以通过 final-review 门；本结论不替代 fresh completion verification 或根任务完成判断。

## Target 与 loaded criteria

- 工作树：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin`；实际 HEAD / 接受基线均为 `416f26c9919383b58b0bd0e7179a99734fe53b48`。审查相对该基线的实际 tracked diff 与 untracked 新增源码/测试，并核对交付文档和浏览器证据。
- 实际 27 个 `Server/`、`Web/` 变更路径集合指纹为 `98016BD0667F676E724FD252C1F2E27DF42FA0F3937539199E9AA127E736A124`。采用 `git ls-files --modified --others --exclude-standard -- Server Web` 排序去重，对各路径形成 `SHA256  path` 行，LF 连接后对 UTF-8 字节取 SHA-256；与派发冻结目标一致。
- child `spec.md` SHA-256 为 `4D4349E0C10F6C90C4A1ACB13A082EEDB4896BDCC25BAA007B50891B74695169`，根和 child TaskContract 均为 revision 1。
- 已载入适用 AGENTS、根 TaskContract、child TaskContract/spec/sdd-execution、drift-review 与不可变 change-challenges；按 child SC-1..SC-3、RQ-001..RQ-009、AC-001..AC-010 检查。配置无 Plan/Task Unit。本 child 仅贡献根 SC-3/SC-6；官方邮件、点券写入、最终 CI 与根验收不是本门的产品预期。
- 完整结果范围包括五类 GET、typed 持久化与运行态查询、领域/命令注册、旧 fixture 的兼容调整、中文查询页面、文档及页面证据。implementation-report 只作为证据位置指针，未用 drift approved 或生产者报告替代产品检查。

## Decisive evidence

| 验收面 | 实际检查与判断 |
|---|---|
| 权限、输入和最小字段 | `GmPlayerDirectoryEndpoints.cs:17-43` 五个 GET 均声明 QueryPlayers；沿用 `GmAccessBoundary.cs` 的当前持久化身份、独立监听、远程 HTTPS、默认封闭和 no-store。`TryFilter:59-76` 拒绝重复已知字段、越界/非法 ID、分页、布尔和过长 q；详情分别返回 400/404。Data 投影及平铺角色详情只含明确白名单，AccountCeraBalances 只有三币整数。异常返回通用中文 500，不把失败包成空结果。`EveryQueryUsesCurrentPermission...` 实际遍历五类 HTTP 的玩家票据、允许、撤权、过期和远程明文；既有监听矩阵与访问/审计判据仍有通过日志。 |
| 全服分页、姓名和余额 | `SqliteGmPlayerDirectoryPersistence.cs:15-55` 对同一筛选表达式、captured 在线 ID 参数先 COUNT 再按稳定 ID 分页；五条命令为 Transactional，`SqliteDatabase.ReadAsync` 使用同一 deferred SQLite 事务，不存在分页后过滤或先读一页再算 total。姓名字节先严格 UTF-8 判合法，再用 instr 做大小写敏感字面匹配；不读取兼容 name 作替代。`GmPlayerDirectoryDomainService.cs:54-62` 保留 missing/invalid 与 hex。实际 HTTP 判据含数据库后页在线对象、相邻/重复页、组合过滤、空库、越末页、特殊字符、冲突 name、异常字节、无角色账号、软删除及 int.MaxValue；只读前后对比覆盖成功、无结果和故意错误。 |
| 当前在线、频道、位置与 readiness | `OnlineStateRegistry.cs:160-180` 在原所有者锁内提供去重的已认证 Game 账号及 exact active 核验；没有第二份在线注册表。`GmPlayerRuntimeQuery.cs:24-40` 在位置读取前后组合 current selection、exact publication 和在线归属，使用实时位置及既有 ChannelEntryPolicy。发布前后 generation 不错误作数值相等比较。已追到实际生产 `SelectCharacterPhaseCommands.cs:49-109`，旧会话清理先于新选择，位置所有者 `ForgetExact` 不会清掉替代会话。生命周期与 sampling HTTP 判据分别覆盖未选角/准备/发布、移动换区、不同 local/public port、未知频道、旧 exact 清理、读取中失效和读取后断线的同次名单。NetworkEnabled/IsReady 分别来自真实宿主事实，GM-only 未谎报游戏就绪。 |
| 只读分层及回归 | HTTP 无 Persistence/Protocol/Networking 引用或 SQL；命令 handler 只注入 IGmPlayerDirectoryDomainService，领域经 typed IGmPlayerDirectoryPersistence 查询。新增持久化路径仅 SELECT，不创建账号、不恢复选角、不保存位置、不发通知。Application/Persistence 接线复用单 Host/DI 与原状态所有者。HttpApi ReadyGame 和 Protocol module-only readiness 调整仅补齐宿主接缝，未放宽产品断言。实际架构、认证/审计及相关生命周期回归日志支持这些边界。 |
| 中文交互与旧响应保护 | `PlayerDirectoryView.tsx` 实现搜索/在线过滤重置页码、分页、ID关联、详情关闭及刷新；姓名/登录名由 React 文本渲染。`useGmRead.ts:7-24` 以请求 key、effect 生命周期排除旧响应；`AdminApp.tsx:30-36,61,108-109` 在 403 后移除 QueryPlayers、推进 generation 并卸载资料视图，旧 identity poll 不能恢复被撤权限。新前端判据覆盖 400/401/403/404/500、网络失败、查询乱序、退出和权限变化，原十条管理员交互判据保留。 |
| 文档与实际页面 | `Document/gm-web-admin.md` 的查询契约、保存/当前位置、采样、权限及隔离 fixture 说明与代码一致。已实际查看 verification 的五张截图：概览、第二页、在线角色详情、编码异常、无角色账号；中文标签、数值、位置来源、余额和详情可读，宽表格有横向滚动。browser-verification 记录实际 bundle 在隔离 Host 上的搜索、分页前在线过滤、ID关联、刷新、输入拒绝及退出；其操作与 `browser-acceptance-host.log` 的查询/退出 HTTP 记录一致。截图证据与模拟运行态/HTTP 判据保持区分，没有升级为真实客户端验证。 |

## 挑战与发现

冻结 change-challenges 保留 0 个 CH；不存在待裁决条目。本 reviewer 另行检查了真实代码、输入/失败路径及跨层交互，没有发现需要 minimum fix 的 material defect，也没有增加形式证明、全局原子快照或未来业务范围要求。

## 验证证据边界与交接

已直接检查 `artifacts/gm-tests/spec003/` 的原始最终输出：Host 54通过，Application 2445通过/186跳过，Persistence 396通过，Protocol 1167通过/59跳过，HttpApi 50通过；前端25通过、生产构建成功，Release 构建0警告/0错误。PVF条件跳过仍为跳过，既有 chunk-size 提示不影响本范围的可使用结果。这些是已有运行证据，本 reviewer 没有重跑 .NET、Host 或浏览器。

实际浏览器长驻 runner 被应用中断且不可恢复，没有证明其 exit 0；browser-verification 已如实注明。独立 VERIFY_ONLY 的既有自检日志有正常结束，但主控当前阶段仍须 fresh VERIFY_ONLY 和所需回归，不把尚待 fresh verification 当成产品缺陷，也不以本 approved 结论宣称全部 success conditions 已完成。

本轮只新增本审查文件，未修改产品、测试、根控制或执行 cursor，未执行 Git mutation，未触碰现服、真实数据库或客户端。主控消费本门后继续按当前 execution 的 verification 阶段取得 fresh completion evidence。
