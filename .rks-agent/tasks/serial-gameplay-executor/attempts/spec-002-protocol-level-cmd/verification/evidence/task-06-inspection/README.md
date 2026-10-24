# TASK-06 执行与核验证据

当前结果按 Task r4 及独立 attention 后的主控恢复裁定收口；r1中一次性票据与普通兑换加入SQLite两项假设已纠正，不能从历史编辑脚本或首轮错误oracle推回最终合同。原日志和HEAD276a4338的三个源码快照保留。执行者不作结果独立批准、不提交。

## 实际产品边界

GameLoginHandler只带同一bus、原request/context；Protocol route由总线入队一次。GameLoginCommand唯一普通Write只调用原认证域；正常兑换只有LoginSessionRegistry、OnlineStateRegistry两个runtime participant（均CommitCanFail=false），不是无事务，也没有SQLite enlist。SQLite夹具的注册/Launcher发票及后续设置查询不能混作兑换的持久化提交。旧可失败提交测试是显式测试TicketParticipant；真实Net prepare fault同样是故障参与者取证。

原身份复核→设置→listener/environment/endpoints→成功CMD＋六NOTI次序保持；hotkeys后、auction0前直接域激活profile0。提交后的设置/部署失败保绑定，实际Disconnected后按原规则同凭据成功重连。原settings内只采集状态/当前事务，关键断言在CMD外；真实认证调用计数避免同CMD重复认证。单元测试DirectWorkUnits/发送驱动只保原观察断言，真实事务证明来自fixed typed Internal probe、真实auth flow及原TCP。

真实ChannelEntryDomainService直接吸收原mapping/Unknown/KindOf/Refuse，同singleton旧policy alias继续供未迁消费者；typed Infra仅持既有只读snapshot，ctor零Get、environment短路、ConnectionPort→Host→PortA→PortB、原转换/非strict规则保留。Activate旧命令因Select caller仍pending。

## 区分与诊断

机械编译失败不计RED。directed-app-01的4失败中，两项是新增fixture前置不足，另两项是错误“不可复用”验收被旧实际合同证伪，不是产品缺陷RED；原日志保留。构图与正确oracle后directed-app-02为74/74，再加入3条分类/原cause真路径后directed-app-03为77/77；Infra新来源6/6。

两轮有价值坏变异均在finally恢复原bytes/SHA：提前eager default使两个override用例在真实getter失败，恢复2/2绿；将部署读取搬到settings前使两个拒绝路径外部计数断言失败（3pass/2fail），恢复5/5绿。mutations.json与四对独立log/meta保存真实命令、exit、时间和哈希；编译/夹具错误未冒作有效RED。

完整Net首轮唯一失败是Lifecycle祖先名字仍期待旧inbound:GameLogin；真实FIN/清理已过。r4只将一个expected表达式改为完整类型nameof(GameLoginCommand)，保其余链/Source/Root/Parent/Depth/顺序/FIN/deadline断言。完整Build02及Net02验证此最小迁移，无生产链/关闭修改。

## 正式核验与复现

所有dotnet正式运行native require_escalated/caffeinate，原xUnit类并行、Skip和5m hang deadline、-m:1保持。完整Build01/02均0warning/0error；八项目首轮全跑（含Net旧标签1fail），最终Net02全量199/199替代其首轮。其它七程序集没有后续代码变化，按r4复用首轮。选定结果8446＝8426pass＋两准确旧Dimension fail＋18原skip，N22/D0；formal-summary.json逐项目绑定log，原formal-runs.json保第一轮完整历史。资源为Darwin子进程观测，不作性能门槛。

fresh01七程序集各唯一control，原74expected全匹配；fresh02仅Net、唯一control、7expected全匹配，各外/exe/orphan均零。两个DFO目录独立保存，不拼接。完整Net02中表征10逐名pass/0skip；15Golden对f01dc226与Task05零差异。无额外表征定向重复。capture只ConfigureServices：173消息、205commands＝Protocol5/Internal1/Legacy199，Kind168Tx/24Direct/9RO/4W。baseline和original、其它closed行、其余源及A封存文件保持。

source-freeze.json/source-checks.json是首轮源冻结证据，保持原值；source-freeze-final.json/source-checks-final.json是r4文档与精确新增测试路径后的最终证据。两者差异明示，不用初轮冒充最终。28个物理源/测试/AGENTS路径（--no-renames，3删除null）为执行修改范围，另两份开放ledger及当前Task/专属证据。行尾/BOM前后在final checks，所有被触及文件原本LF、未转换CRLF或混合文件。

实际运行命令与UTC/cwd/HEAD/exit/耗时/环境在同名log/json。编辑/变异脚本是实际历史，只为审计；其中早期脚本含已被r3/r4纠正的假设，不能重新执行覆盖当前交付。当前源码、Task文末裁定和最终证据为准。manifest排除当前Task，另绑定R1完整字节前缀与SHA供主控追加review后复核。

本结果只到TASK-06执行与检查；required结果快审/检查点由主控，未启动TASK-07，未完成T003/整个Spec、延迟2秒全量验收、T011清零或Astra/用户收尾门。没有push/PR/部署/外部写入。
