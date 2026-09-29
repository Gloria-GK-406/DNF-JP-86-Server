# TASK-005 — 集成与实机准备

状态：进行中。覆盖全部RQ/AC，不提前将整项标完成。

1. 相关Application/Persistence/Protocol/Infrastructure/Host回归；独立复审已发现问题及新增转变出口。
2. 精确备份并修正三条样本UID错配；新增蛋必须走正式容器Add，不能复制core后只改数据库主键。
3. 发布到独立runtime，沿用现有数据库和配置；检查监听、HTTP引导和启动异常。
4. 玩家1123按序验收：带宠物登录→详情→脱穿/换宠/重登→饲料及改名→副本饱食和清房经验；宠物蛋右键确认捕获真实CMD补入口。
5. 每条证据分别标自动测试、70Reference、ClientStatic、WireCaptured和ClientVerified。孵化网络入口、特殊成长及技能路径未闭合时保持任务进行中。
