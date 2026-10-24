# 最终核验中的环境中断与恢复

产品冻结`0ecb9af3`。第一轮logs/spec-001-final-raw.txt构建0警告0错误，但测试跨机器低电量休眠5516秒（10:14:45～11:46:41 Asia/Shanghai），直接pmset证据logs/spec-001-final-environment.txt；恢复时load73～83，swap约9GB，多程序集无关用例同步长暂停。该轮SIGINT中断，测试退出1、取证汇总退出1，不用于完成判断。

独立systematic-debugging诊断a_verify_diagnosis：Networking回调替身在释放后10秒延续等待超时（ConnectionCallbackBoundaryTests:72）；Host在PVF预热30秒启动token取消（ComponentLifecycleTests:31/37）；端到端45秒bootstrap和CardReward30秒启动也受影响。TASK35对这些测试、Networking、Infrastructure、GameServer diff为空，对Joust启动路径亦未改。没有因此改产品或调大deadline。不能把所有失败个别因果都说成已证明；能够确证的是本轮跨休眠且有广泛环境干扰。

恢复实验：确认旧测试和子宿主退出，使用随进程结束的caffeinate -i -s防idle/AC休眠；同冻结产品重新完整构建和全量，-m:1仅串行项目，xUnit程序集内并行、原断言/超时不变，不回避F130。新日志spec-001-serial-*，最终结论仅由completion-evidence.md决定。两轮运行脚本随evidence/scripts保存，原轮输出与取证不删除。

第二轮serial也未闭合，App完成记录停于03:52:38，84个未报告例全属Scheduler非并行集合；load已回落3.09。只读样本logs/spec-001-serial-testhost-sample.txt记录FSEventStreamCreate/Start与fsevent_realpath的深调用链，未见SQLite忙热点；候选是默认Host builder配置监视器。系统服务沙箱限制仅为待验证假设，尚不称已证明。2026-10-24T03:58:03.825871+00:00中断该轮自有测试，按相同产品/断言/期限/项目串行方式在获准本机环境重跑；不将不完整serial当pass。

最小本机对比：同一TimerSourceJobFailureTests.A_timer_cmd_that_fails_with_a_program_error_does_not_stop_the_host，以相同原期限在获准本机环境运行，1/1通过，用例324ms，runner1.2973s，exit0（logs/spec-001-native-host-probe.txt）。这支持环境限制假设，但单例不是完整run。最终完整验证绑定spec-001-native-*及evidence/spec-001-native-ev，命令仅在允许系统服务访问的本机环境运行，断言/期限/-m:1均不变。
