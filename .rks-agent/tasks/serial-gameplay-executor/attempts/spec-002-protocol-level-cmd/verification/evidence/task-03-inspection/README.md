# TASK-03 证据入口

权威目标为B的两个probe工具、migration-baseline.json、migration-ledger.json、migration-ledger.md和TASK-03 Result。owned-artifacts-sha256.json绑定最终五制品；Task文件由主控追加审查，不放进会失效的源码hash。source-checks.json核对全部注册/source/protection/transaction原位hash及产品零diff。两次capture分别在相邻task-03-capture-01/02，含完整console/csproj/runtime/payload/source-registrations/crosscheck/manifest。

两轮本机require_escalated成功：10.832s、3.486s；payload SHA256相同。每轮只模块ConfigureServices，没有构造ServiceProvider/启动服务/执行业务。只读源码解析与运行时真实CLR逐条闭合；203handler不按短名字符串去重。Darwin children_maxrss原值bytes在manifest，只是子进程观测，套件时间/内存不设阈值。

最终校验为task-03-validate-final-03（原数据通过）及bad-data-final-03（8坏数据均被拒），各原命令/UTC/退出/耗时在logs同前缀json与log，机器结果在对应evidence json。中间轮次不删除：initialize-01重现了初版源码锚点方法名写错的ValueError（没有业务运行，不算RED）；02通过后自检补静态/gm/{**path}与Joust周期source、细分ordering/runtime同名噪音和BeginSnapshot，再重建尚未提交的基线；最后主控指出Recover锚点误指旧CMD80行，改完整handler行并验证位于handler声明之后，最终142。全部为本Task未提交数据纠正，不改封存历史或产品。

工具不解析业务语义调用图；message文件级command/helper候选与每业务original_callers/实际单元/participants/destination仍显式pending。85来源是本轮31非HTTP入口+54实际路由，其中12条OutsideGameplay明确保留；143源码命中及221事务扫描逐条分类不表示204事务已审定。2356测试适配代码位置是文件索引，不算生产CMD来源。最终业务159消息/204旧CMD/73入队来源未闭合，5保护/8过渡新归属待后续迁移。F132未修，API生产禁用；不把IL零引用当机制修复。

scripts/task03-run.py是实际命令日志runner。task03-doc.py是本轮初始索引生成记录，后续ledger维护不要盲目重跑覆盖人工更新。capture工具的initialize拒绝覆盖已有baseline/ledger；基线在检查点后冻结。后续注册API变化需在对应Task适配实时导出，不能重写冻结原行。

本Task新增0/删除0，数量账8365；Server/Tool/AGENTS/旧Task/父制品/A封存无diff，没有业务全量/性能运行。required独立快审与Git提交由主控完成，本说明不自批。
