# TASK-04 首贯通实际单元映射

历史源码快照来自开工 HEAD 26d0e4d1；冻结 baseline original 不变。此文件只支持四行迁移，不能作为其余204旧命令的事务审计。

| 入口 | 原实际流程 | 新实际流程 | 参与者与保护 |
|---|---|---|---|
| ChannelScriptVersion / 旧Get | dispatcher一作业→handler查询Direct Get→ScriptVersion属性读→返回原response到原connection；旧UoW对Direct放行，无Begin/Commit/RequireRollback | dispatcher外薄handler→同一bus唯一作业→ChannelScriptVersionCommand领域读→新outbox登记同response/connection；不调用WorkUnits、无事务 | Channel强类型metadata、ProtocolNonEffect显式匿名权威；无account/selected/effect/main参与者；真实source提前outbox保证零登记失败关闭 |
| GM Bootstrap | PrepareBootstrapAsync在executor外→StartupRecovery新root/原startup:GmBootstrapService label且无source→legacy bus Transactional→UoW Begin→IGmAccessDomainService.BootstrapAsync→Commit；现存SQLite事务参与者 | 同样executor外Prepare→生产SendInternal同root/label/无source与queue/body token→同一bus新Internal→单个ordinary Write bootstrap-gm-administrator→同一个domain BootstrapAsync→同一DomainTransactionScope提交 | 无登录账号租约或selected session/effect/main；ordinary声明精确参与者空；实际SQLite仍真实参与；null prepared拒绝、重复启动不覆盖现有管理员 |

原Get command/result/handler及唯一注册被删除；原handler只前送原request/context，响应映射断言移至真实CMD和TCP，不删除语义。

首例证据：ProtocolCommandExecutionTests 的真实dispatcher单作业/精确连接与response，旧ChannelControlCommandHandlerTests的版本响应断言迁到新handler；原真实TCP ScriptVersion golden保持。ProtocolCommandHostTests使用真实GmTestHost/SQLite、credential/access装饰器检查hash offexecutor、一段环境事务、真实来源与privacy；旧GmAuthenticationTests保首次/重复启动行为。

普通workunit runner新增可执行区分：Write提交、ReadOnly拒写、RequireRollback正常回滚；失败/typed cleanup保原cause；多段提交后的普通错误逐精确参与者fence；未await的action与commit必须完整收口，action gate中scoped依赖在收口前不释放、退出后释放。真实原因保留错误core-tests-02红→03绿，scope-order-red-01红→green-01绿；六独立坏变异的断言红及逐字恢复见task-04-mutations/results.json。其它测试详见正式八全量日志及TaskResult。

F-129在本Channel迁移中提前实证：executor业务错误已记录后middleware Debug，停止executor未入队拒绝为Error。原NetworkPlugins字节保留，临时Debug→Error变异被真实TCP断言拒绝。P1概述中的Warning/Information是描述误记；未据其改变生产、Spec或A。

未交付的义务：其它158业务消息、202旧命令、72执行器来源仍pending；两种effect状态无可用工厂且注册/执行明确拒绝，MainSettlement在业务前拒绝。实际效果ledger/footprint/单例传播/一次主结算/Freeze/重放/Pending生命周期留首effect单元；本Task不是整个T003或Spec完成。四保护新归属仍pending；F132禁用原IL判据保留，未修机制。

过渡清理归T011：LegacyFineGrained/LegacyQueued、InlineAsync、已有DispatchDepth正legacy/负explicit防护、legacy四参数context允许缺Request但新路径拒绝、旧Capture(session)授权metadata重载、新ICommandBus默认SendInternal只NotSupported（兼容旧fake，不授信不转发）。Protocol新路由只能从显式request获权，Internal必须显式origin；Lifecycle已支持保Connection原链的显式Kind区分，但现真实生命周期仍legacy，只有GM启动来源完成迁移。

正式第一轮另暴露新Freeze对null绑定的legacy registration无条件with克隆，破坏原registry同实例39项断言；根因/原失败日志保留，最小修正只让null绑定返回this，有集合仍冻结只读快照。不改原测试、不改变legacy执行。
