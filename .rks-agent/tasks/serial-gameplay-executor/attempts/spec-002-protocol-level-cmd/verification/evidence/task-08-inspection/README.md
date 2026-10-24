# TASK-08 executor evidence

formal-summary.json区分三full／四actualcontrols与全库inventory；原log/meta保留所有轮次。mutation坏变异只新CMD，源finally恢复；原source／两个旧Fact body／Golden逐字节守护。

Task内python脚本为实际实施步骤与检查快照，部分实施脚本记录当时前置状态，不作为可无条件重跑的产品工具；不要对已交回或已提交任务执行source/test/ledger生成步骤。run/formal/check脚本仅供核验命令与证据追踪，原日志/original快照不可改。

Result R1后strict/selftest及manifest完成才交回；manifest排除Task全文件，另保存R1前缀bytes/SHA供主控追加独立review。无执行者自批或commit。
