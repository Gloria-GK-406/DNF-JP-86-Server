# TASK-05 执行证据

本单元只迁剩余三个Channel协议和共享日期key算法，没有新事务、效果或骨干改动。三消息与两旧命令的原新对应在 `../task-05-mapping/workunit-map.md`；原源快照在那里及 `../task-05-preflight/`，冻结migration-baseline不改。

## 验证选择与独立oracle

薄handler、旧DTO和注册迁移是保行为的结构变化，使用已有断言适配、编译、注册闭合和真实TCP，不把机械编译失败当RED。密钥是消费者可观察的共享合同：UTC替代本地日期或漏InvariantCulture会只在跨日/非公历culture发生握手/解密失谐，普通固定UTC+8中午旧例不足；三个独立字面量向量覆盖原值、UTC±8跨日与作业内Thai culture。预期字面量不从helper生成，provider和实际Connect CMD分别对其断言，行为重构不影响该oracle。

实现正确算法抽取后先GREEN 3/3，再仅临时把Connect的GetLocalNow换成GetUtcNow，两跨日响应key断言RED（1pass/2fail），finally原字节恢复后GREEN 3/3。`task-05-key-red-01`、`key-green-01/02`原日志和metadata保存实际命令/UTC/HEAD/exit；mutation记录 `../task-05-preflight/key-mutation.json`。这是有价值的坏变异区分证据，不声称算法抽取前新增行为缺失的TDD RED。

原Connect日期值和原连接断言迁到真实CMD向量，原handler用例改验原request/context；Script/Info原bytes/catalog断言迁到真实CMD/outbox，旧返回DTO类型断言随API删除，原ScriptVersion用例保留。真实TCP在原控制序列前加0B→0C，检查classification/checksum/ack/36B独立body；后续版本独立ciphertext、脚本bytes/padding、频道字段及两个F129例均保持。

## 复现与边界

所有dotnet正式运行是native require_escalated/caffeinate，保原xUnit类并行、deadline和Skip。完整Release构建后只跑完整App/Networking/Protocol；Task04其余五项目没有本Task重跑声明。各轮原命令、环境、cwd/HEAD、exit、UTC/耗时和Darwin children_maxrss bytes在同名前缀log/json；资源只是子进程观测，不作为门槛。fresh DFO只要求本轮三个实际程序集control和原expected，不能混合其它轮。

复制的task05-*.py是实际编辑/运行历史，部分固定了本次绝对workspace或/private/tmp helper路径；不要盲目重跑编辑脚本覆盖后续代码。权威复现测试命令在log metadata，正式源码为当前仓库，key mutation只在隔离且无其它writer/构建时重现并finally恢复。该执行器不提交Git或自批结果。

## 首轮诊断与 r2 修正

首轮完整App与Protocol按预期，Networking的19新增失败同根：两个legacy probe夹具只替换ChannelConnect的消息注册，留下新Protocol command注册，反向闭合正确拒绝无thinroute；Host未启动后的StopAsync ArgumentNull为次生错误。经主控Task r2增补，仅在被probe替换的单消息Protocol绑定上同步移除对应command registration，其余注册、生产闭合规则、probe标签/断言/期限/teardown均不改。原19失败日志保留，不混入基线。最终完整build及Net独立fresh02重跑；App/Protocol与生产源未再变，复用首轮通过证据。首轮三程序集70取证与最终Net独立7取证分别核验，不复制/混合JSONL。

## 最终 R1 证据定位

最终 build02 为0warning/0error；App01为4683（4665pass、两准确原Dimension失败、16原skip），Protocol01为1756pass，Net02为199pass并包含逐名表征10pass。完整首轮Net01的19夹具接线失败仍保存，未声称通过。formal-summary.json保实际三项目选择轮；test-delta.json记N4/D0与全库数量账8424。fresh01三程序集70条、fresh02仅Net7条各自通过violations，分别有3/1个control，外/exe/orphan全零；两个目录独立保存。strict-final.json无待完成Result，bad-data-final.json八个坏副本均拒绝。

artifact-manifest.json绑定22个授权源/测试/文档/账本路径及task-05证据/log文件（2删除为null），排除manifest自身。Task是主控随后追加review的控制制品，因此不放在files列表，而单独绑定追加前完整r2＋Result R1的字节数与SHA；主控可对追加后的该字节前缀复核，无须改写R1源/证据。
