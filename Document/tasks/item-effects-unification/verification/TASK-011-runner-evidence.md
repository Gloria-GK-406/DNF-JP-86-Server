# TASK-011 effective runner证据

8项目按Application→Protocol→Networking→Persistence→Infrastructure→HttpApi→Host→Pvf串行，无filter；runner均有实际collection parallel off诊断。有效testhost RunSettings包含ParallelizeAssembly=false、ParallelizeTestCollections=false、MaxParallelThreads1，VSTest engine初始化MaxCpuCount1。

| 项目 | actual Starting日志行（collections off） | 最大同时非零长度test intervals | effective settings/engine |
|---|---|---:|---|
| application | TASK-011-application.txt:13 | 1 | TASK-011-application.diag.txt:settings 293 / engine 265 |
| protocol | TASK-011-protocol.txt:9 | 1 | TASK-011-protocol.diag.txt:settings 293 / engine 265 |
| networking | TASK-011-networking.txt:9 | 1 | TASK-011-networking.diag.txt:settings 293 / engine 265 |
| persistence | TASK-011-persistence.txt:9 | 1 | TASK-011-persistence.diag.txt:settings 293 / engine 265 |
| infrastructure | TASK-011-infrastructure.txt:10 | 1 | TASK-011-infrastructure.diag.txt:settings 293 / engine 265 |
| httpapi | TASK-011-httpapi.txt:9 | 1 | TASK-011-httpapi.diag.txt:settings 293 / engine 265 |
| host | TASK-011-host.txt:9 | 1 | TASK-011-host.diag.txt:settings 293 / engine 265 |
| pvf | TASK-011-pvf.txt:9 | 1 | TASK-011-pvf.diag.txt:settings 293 / engine 265 |

TRX区间按startTime/endTime扫描，结束与下一开始同timestamp时先处理结束，略过零长度interval；最大1是本次实际执行证据，不把它推广为所有内部异步业务都没有并行。MaxParallelThreads1与assembly/collection配置由effective payload确认，actual Start及区间补充验证adapter接受。

raw vstest console/testhost diag、完整TRX与stdout保留在同verification目录；未删原失败证据。
