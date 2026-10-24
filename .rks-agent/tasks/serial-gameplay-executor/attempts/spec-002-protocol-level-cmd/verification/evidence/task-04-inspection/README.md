# TASK-04 验证复现边界

run-command.py / mutations.py / mapping-construction.py 是本轮真实运行的本机临时工具副本，记录实际工作区/日志命名，不是产品API。mapping构造只能在原未闭合账上使用，不要覆盖已审查结果；mutations仅Task白名单临时变异且每项finally原字节恢复，正式产品不得使用坏源。

所有native命令以require_escalated运行并用caffeinate，保原xUnit类并行/Skip/deadline和5m blame期限。log/json给出逐轮原命令、cwd、HEAD、UTC、exit与可得资源；Darwin children_maxrss是子进程观测，未作为内存门槛。没有运行游戏客户端/游戏exe、提交、推送或PR。

core-tests-01是编译适配错误，channel-failure-green-01是缺脚本fixture错误，均保日志且不计行为RED。core-tests-02是真实runner原因丢失断言红，03对应绿；scope-order-red-01是真实scoped资源提前释放断言红，green-01对应绿。六坏变异均是真断言红→恢复绿；见task-04-mutations/results.json；与其后的注册identity修复并非同一风险，不重造重复坏变异。

正式第一轮发现EffectSettlementRegistrationTests原39项Assert.Same全部失败，根因为Freeze无条件with克隆null绑定legacy record；保旧测试、只改为null直接返回原record。第一轮完整日志保留。最终正式轮与该修正后source SHA绑定，以其完整数量/新鲜七程序集DFO作为验收，不混用两轮计数。

纯签名与机械注册没有编译RED；两旧ScriptVersion测试驱动适配，原版本值/response/原connection断言迁往新CMD和真实TCP。其余旧断言、Golden、四保护/F132原IL判据保持；F129保Debug/拒入队Error真实事实，P1描述误记不据其改生产。
