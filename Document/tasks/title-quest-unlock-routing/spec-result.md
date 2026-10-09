> 落地状态（2026-10-09）：用户已接受当前称号系统并要求PR收尾。下文保留当时交付证据；最新协议与验收边界见 [称号簿流程](../../AchievementTitleBookFlow-20261009.md)。

# 任务型称号与被动称号路线重构

用户授权：重构后部署。工作区`task/abyss-research`，基线`9da9b83e`，保留前轮称号镜像的未提交修改；本次只修改服务端。

改动：PVF被动目录只收grade=title；grade=achievement恢复接受/进度/放弃/正式完成。真实cleared任务是任务型称号的唯一未激活解锁依据，旧自动计数不参与当前投影和依赖。未激活的污染依赖完成行在准备中重置；已经激活的称号及原物品保留。登录和当前状态快照都用成就领域的只读Get；静态数据库行仍保留。

完成链：任务事务提交→QuestCompleted→成就判定6501→AchievementCompleted→称号簿唯一26596。PVF title奖励不重复进背包，映射不明则拒绝交任务。客户端0200插件、EXE和PVF不变。

验证：quest-achievement-regression-green.trx 108/108；protocol-regression.trx 264/264。包含真实4303只在接受后推进、普通怪计数、放弃、目标0不自动交任务、正式完成、回滚、重放、重启、旧计数/旧事件污染守卫、登录补认与已激活保留。协议测试另覆盖登录/当前查询过滤旧4303且只读查询不执行prepare。

测试取证边界：首次完成测试在同一事务一次注入30次击杀时触发原有QuestLog批量stage乐观比较问题；实际EnemyDeath命令按一次死亡一事务处理，测试现按真实调用粒度覆盖完整30次击杀。批量写入问题不是本次引入，详见技术债D-006。称号簿补丁UI与新的任务受理/完成显示仍待实机验收。

独立审查approved。部署已切换：PID25424，`0.2.2+title-quest-routing.20261009.9da9b83e`，HTTP ready/六端口归属及新DLLhash核验通过。SQLite关键玩家表未被启动改写，客户端插件hash不变。最终回归108+264+86=458通过，1跳过；最终证据见verification/completion-evidence.md。
