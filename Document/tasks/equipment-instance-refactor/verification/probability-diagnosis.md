# 连续强化成功诊断 — 尚未确认现场根因

## 用户现象与现场事实

2026-09-30用户反馈一直成功，随后确认已经到+15。当前服务PID41000，18:44:34启动，实际程序位于主仓库`artifacts/equipment-reinforcement-field-fix-20260930/runtime`。启动日志确认PVF为`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，当前配置一致。

19:45至19:47日志有角色2/绝对槽9的14次请求，sequence46至78，old1→new2直至old14→new15，全部Succeeded。当时冻结的只读快照为UID689长剑强化15、独立附魔次数0；金币5007846、无色58456。这是历史快照，随后用户继续操作，当前资产见下段。用户本次操作保留，不恢复+1、不回滚或额外扣费。原始日志副本和历史快照保存于主仓库`artifacts/equipment-reinforcement-probability-20260930/before-diagnostic.stdout.log`、`observed-snapshot.json`。

调查期间用户又发送sequence123，旧PID41000的真实提交日志为old15→new10、outcome=Downgraded。root独立只读复核UID689强化10/独立附魔0，金币2992701、无色58236；普通强化券50、超级强化券49、两种增幅券各50。实机发生一次保护失败，证据位于`natural-failure.stdout.log`、`natural-failure.json`。这排除了“旧服务无条件成功”的断言，并证明本次超级保护失败已结算；仍需用户确认即时显示与正常退出重登。没有旧draw，不能确认此前连续成功的原因。

旧日志只有提交结果，没有实际draw或运行规则阈值。**已观察旧服务会失败，但当前没有连续成功源于具体代码缺陷的证据，也不能用一次旁路抽样正常证明实际进程的每次抽样。**

## 有限的排除证据

直接读取真实PVF，普通表失败阈值分别为：目标+5为10000，+6为20000，+7为40000，+8为20000，+9为10000，+10为50000，+11为70000，+12为80000，+13为82000，+14为83000，+15为84000。当前判定在0..99999抽样，draw小于阈值失败；+1至+4阈值0。

独立Probe引用当前运行目录DLL，而非替换随机源或自行复制判定。实际Infrastructure/Application模块解析出`EquipmentReinforcementRollSource`、`EquipmentReinforcementRuleService`和真实内容源。每个目标+2至+15遍历全部100000个draw，成功数量严格等于100000减阈值；每级另用实际Random.Shared包装器抽样100000次，非零失败等级正常出现失败，比例与表相符。没有注册Persistence、会话或调用领域结算，不访问生产数据库。

Probe副本与部署Application DLL SHA256相同：`67d40efcd36f224fc0c5dc3f4428a7e6868a185ce6447a715b2f92c72227de32`。代码、精确阈值数量和抽样结果见主仓库工件`Probe/`及`deployed-pure-probe.json`。本证据排除所检查DLL中固定成功及该PVF普通表全零，仍不是旧进程每次请求的draw证据。

按该表和独立均匀抽样假设，+1连续至+15的概率为`0.0000456855552`，约0.00457%。这是模型推算；罕见值得追查，不能单独证明或排除运行缺陷。

## 最小诊断增量

仅规则服务添加结构化`REINFORCEMENT_ROLL`信息日志：模板、old/target、draw、failureThreshold、100000scale、penalty、protectionRetain、calculatedOutcome/calculatedLevel及consumeProtection。规则、抽样、费用、保护、结算、wire及资产均不变。

该日志表达计算结果，可能随后被拒绝或事务回滚；必须配对提交后协议`UPGRADE_ITEM`及只读资产，不能把计算日志当提交成功。正常实际请求下一次日志可直接检查`draw < failureThreshold`与结果是否矛盾，以及实际运行阈值是否正确。

对TASK-005已approved结果仅一个产品文件改变，完整33文件指纹为`93fad50a72ab3fe88cc2ed55b3134f3755d3e0de4915bcd9e4412ed22f1ddd75`。新runtime已Release publish，7主DLL与当前Host Release输出相同；同原配置/原数据库路径，尚未切换。manifest：主仓库`artifacts/equipment-reinforcement-probability-20260930/diagnostic-manifest.json`。

## 自动检查与边界

Release solution build通过，0警告/0错误；publish exit0；源码diff检查通过。本轮两次完整Application各2355通过/1失败，原始TRX保留：首次宠物fixture在Dispose删除SQLite文件时IOException占用；第二次技能fixtureSQLite SafeHandle已释放。两处测试源及相应产品未变，疑似fixture全局ClearAllPools/并行清理干扰，不是强化概率断言失败。为核对该假设改用串行执行完整相同程序集，未修改产品或测试源码；串行通过支持并行干扰假设，但未确认其完整根因。

完整串行重跑Application2356通过/0失败/0skip，exit0，原始结果为`tests/application-serial.trx`、`application-serial.log`。其他七个未变目标沿用TASK-005完整证据；当前必要合计仍4557通过/0失败/2条件skip，不声称并行失败命令通过或本轮整个solution测试。

串行参数依据[xUnit官方VSTest配置](https://xunit.net/docs/config-runsettings)：`xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`。原失败不是被删除或改为skip。

## 待现场条件

- 自动检查、完整独立复核通过后，用户正常退出游戏，再切换诊断服务；未退出时维持旧服务。
- 新服务下一次实际请求的draw/阈值/计算结果配对提交结果；若出现矛盾再按证据修复，不强制失败或改变概率。
- 两种保护的实际失败与重登仍待验。根因未确认、整个契约未完成、未提交或标ClientVerified。
