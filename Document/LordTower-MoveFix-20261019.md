# 领主之塔原生换层标记兼容修复

2026-10-19。分支`task/lord-tower-20261019`；修复前代码`911b73cf22ae2692d372bd5bda213baf3cf1c44a`。对应领主塔#165；绝望之塔100层后循环问题另记[#174](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/174)，本次不改绝望之塔。

## 原因与修正

原生客户端20:44:26发来002D的目标为(255,255)，当时第一层已Cleared、阻挡怪0、死亡3/3。服务端只接受(0,0)，以TargetCoordinateMismatch静默拒绝，没有发第二层0334。接收日志与实际客户端哈希已[归档](../protocol/patterns/data/traces/a21-boss-tower-move-20261019.log)，属于服务端解码和状态证据，并非完整原始帧抓包。

目标判定收进IBossTowerRuleService/BossTowerRuleService，接受双255专用标记，保留此前parity坐标兼容；不接受单边255或任意坐标。领域继续要求清层、队长、当前run和roomVersion及精确会话，每次只进下一层；不改掉落、经验、门票、奖励和回城规则。换层成功仍只发0334，所有有效成员0025后才发001E。

## 验证与实机边界

同一八层行为测试参数化双255及parity两种形式；修复前双255真实失败、parity通过，修复后两种均通过。用例同时验证清层前拒绝、混合255拒绝、旧房间请求不再推进；多人用例增加清层后非队长双255请求拒绝。真实TouchSocket测试改为FF FF前缀002D，验证0334→0025→001E。

日志/TRX在`artifacts/lord-tower-move-fix-20261019/`：`move-sentinel-red.trx`为修复前1失败/1通过，`move-sentinel-green.trx`为修复后17通过/1资源跳过。Release构建和准入/占用、完整Protocol、完整Networking及真实PVF专项回归另有各自原始记录。

这次会话崩溃后现服进程已停止，部署按无旧进程场景核对前次runtime、同一配置/PVF和数据库，备份并在副本上禁用监听演练；运行身份、备份与启动后数据对比以同目录`source-manifest.json`、`deployment-receipt.json`及审计文件为准，不自动恢复旧数据库。

用户复测：重新登录入塔，清空第一层后走门，确认第二层加载、怪物可战斗，再继续换层；若仍失败，提供操作时间。保留新增换层请求、拒绝原因和结果日志。修复后的原生换层及八层全程仍待用户验收。
