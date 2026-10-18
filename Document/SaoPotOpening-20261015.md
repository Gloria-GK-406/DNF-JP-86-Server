# 封弊者袖珍罐取消附加材料要求（2026-10-15）

玩家使用10004331时发送普通开罐CMD00A0，现用服务记录MissingMaterial（This entry supplies no material）。原始及现用PVF均有`[need material] 10086503 6 [/need material]`，但当前物品目录未定义该材料。网上未能确认其原始名称，不能据此认定原设计错误。

用户明确选择：删除该罐子的材料条件，保留奖池；主动开罐仍遵循背包容量检查，容量不足不消耗、不邮寄。

## 修改和范围

Tool/PvfSaoTowerEntrancePatch增加`--sao-pot`，仅接受已确认的10004331脚本、名称及精确10086503×6条件。删除这一组4个token；不改随机奖池、物品说明、图标、其他物品条件或通用开罐服务。

每个开发客户端及服务端分别从自身现用PVF生成。每档712010条文件记录完整校验，只有1条目标记录改变，其他712009条不变；原有名称/哈希池及压缩内容块保持一致。已同步DNFClient、A21-debug、A21-protocol、A21-protocol2与现用服务端PVF，上游对照和OneKey发行包保持不变。

## 验证

- 工具Release构建0警告、0错误。
- 服务端真实内容源读取结果：Package，MaterialCount=0，SourceCount=1；原奖池1次抽取，6种可能物品均Supported。
- 真实补丁PVF下PackageOpeningTests与OpeningCapacityRegressionTests共35/35通过。
- 各现用PVF回读哈希与生成档一致。
- 原数据库已备份，启动前后integrity_check=ok、schema完全一致；未主动修改角色物品或共享塔进度。
- 重启后bootstrap=ready、GM HTTP200、31个预期端口均由新进程持有。

本次只部署内容，服务程序集仍为0.2.5-sao-core.20261015+f67ce8bc。实机开罐结果仍待玩家验收，建议确认罐子扣1、奖励入包，以及满包拒绝行为。

## 部署与回退

产物、补丁报告、prepared.json、deployment-verification.json与运行日志在`artifacts/sao-pot-20261015/`。rollback目录保留五份原PVF、配置和SQLite备份；回退PVF需要退出客户端并停止当前服务器，按各档原路径恢复，再启动服务器。内容回退无需恢复数据库。

| 档案 | 部署SHA-256 |
| --- | --- |
| server | `91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4` |
| client | `573388731C394A71D5E75ED7DD215124B3D04383631D93116625ED353E06ACAB` |
| debug | `DC250D42A7DE1123F7CF7813260EA9359695E71A05DF6BC0B5341E8E250F27CF` |
| protocol | `573388731C394A71D5E75ED7DD215124B3D04383631D93116625ED353E06ACAB` |
| protocol2 | `FDDBB938361D4CA817598AE479E2F951B47E1DB64E096943167D20014A97AF98` |
