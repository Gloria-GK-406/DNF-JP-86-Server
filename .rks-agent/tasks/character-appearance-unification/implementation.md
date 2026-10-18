# Implementation

基准1e146ba1；当前工作区task/next-round-20261013。

- Changed：人物候选接口、纯外貌规则与读取领域编排；Roster共用编码；登录/当前状态/职业刷新/支援/称号/重试及设置事件消费统一结果。源码与协议矩阵见Document/CharacterAppearance-20261014.md。
- RED：`dotnet test Server/tests/Dfo.Server.Protocol.Tests --filter FullyQualifiedName~CharacterAppearanceInitializationTests --logger "console;verbosity=normal" --verbosity quiet`，四个职业阶段均期望25实际0，退出1；此前缺失类型导致的编译失败已更正，不作为行为RED。相同命令修改后四项通过，退出0。
- Checks：真实PVF完整应用3832通过，协议1519通过。首次无PVF应用两项塔夹具隐式依赖PVF失败；真实PVF首轮通知50ms时间预算在满载下失败，独立复跑通过，后续完整应用3832全部通过。完整结果均保留于artifacts/appearance-unification-20261014/verification。
- Review remediation：独立审查发现关闭原生装扮时选Career而非Default，已修正并新增四职业阶段回归，最新30项规则/身份/真实设置回归通过；最新协议1519通过。完整应用3832发生于此项小修正之前，不能称为3836项完整应用回归。
- Deployment：运行0.2.5-appearance-unification.20261014+snapshot.556392de83cbd60e，源码快照manifest与发布源一致，备份与运行身份见deployment-verification.json。实现与部署完成，客户端显示待用户验收。
