# 封印的藏宝地合并与部署

> 后续状态（2026-10-19）：用户确认功能验收，并确认封印墙阻挡符合预期，要求恢复原双向封印限制。以下为首次部署的历史记录；恢复后的运行身份与验证见 `artifacts/sealed-treasure-acceptance-20261019/deployment-receipt.json`，当前规则见 [实现说明](SealedTreasure-20261019.md)。

2026-10-19，用户授权：“合并然后部署吧，我测试看看效果”。本记录接续已封存的服务端实现，不改写原任务契约的历史授权或人工验收状态。

## 合并结果

- `task/next-round-20261019` 从研究提交 `2764030f4504c8e4f08c4780f75d230ee8add39a` 快进至 `19be8f3214dbe703e6351c1b0d1ef4f42e124768`。
- 完整保留 planning `9907b3b3`、implementation `28f6aba6`、seal `19be8f32`，没有 squash、rebase 或改写历史。
- 当前检出 `task/next-round-20261019`；实现分支 `task/sealed-treasure-20261019` 保留。未推送、创建 PR、打 tag、发布 Release 或清理其他工作区。
- 18 个任务外未跟踪文件的 SHA256 与部署前一致。

## 合并后检查与发布

- 完整无 PVF 回归：`dotnet test Server/86JP.Server.slnx --no-restore --no-build -v quiet --logger trx --results-directory artifacts/sealed-treasure-deploy-20261019/post-merge-tests`，退出0；8个测试项目共6664通过、873资源测试跳过、0失败。
- 使用实际服务PVF，藏宝地、南部溪谷、牛头怪乐园、票据、运行聚合和物件队列相关回归149通过；PVF解析兼容88通过，均退出0且无跳过。
- `dotnet publish Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj -c Release --no-restore --self-contained false -o artifacts/sealed-treasure-deploy-20261019/runtime`，退出0。
- 新旧运行目录的 `appsettings.json` 逐字一致，SHA256 `B48818D03767091DDDB8C982268D5CB3AA72FF8E326AC96D80220D32FA8E0226`；`experience.json` 哈希一致。

## 数据与回退输入

- 沿用数据库 `Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`，没有初始化新库、发放玩家物品或修改活动配置。
- 先通过SQLite backup API建立 `artifacts/sealed-treasure-deploy-20261019/online-backup.db`，再在副本 `rehearsal.db` 上禁用游戏网络、Launcher HTTP和GM HTTP进行启动演练，原服务此时继续运行。
- 演练启动成功、未监听网络，108张表的行数与逐行内容摘要全部保持一致，无新增表；完整性检查为ok，外键检查为空。
- 停止已核实路径/版本/程序集哈希的旧PID40784后，建立 `stopped.db` 停服备份并重新验证完整性；切换后生成 `post-start.db`。前后108张表内容摘要相同，无新增表。
- 旧运行目录 `artifacts/southern-dale-deploy-20261019/runtime`、旧配置和全部备份保留。若需回退运行版本，应保留当前数据库，不自动覆盖测试后新产生的数据。
- 服务PVF继续使用 `artifacts/scheduled-notice-pvf-20261019/Script.server.pvf`，SHA256 `C5CCE5346F60D21128D756A086EEF96E70F75F2F2FE063B6C61C00506C588F06`。
- 两个客户端PVF分别保持 `3822A9ED183CD17F74DFEE9C11B8F2E84AF596B23972A737846022148FB05C6B` 与 `D45CF58B1A2B3D16D6B35ABE0D4E302E052F0B6BC43E9D3E98D815FED54AEF84`，未替换客户端文件。

## 当前运行身份

- 构建提交：`19be8f3214dbe703e6351c1b0d1ef4f42e124768`。
- 版本：`0.2.7+19be8f3214dbe703e6351c1b0d1ef4f42e124768`。
- PID：`15408`，唯一Host实例。
- 路径：`E:/Project/DNF/DNF-JP-86-Server/artifacts/sealed-treasure-deploy-20261019/runtime/Dfo.Server.Host.exe`。
- 12个Dfo程序集的版本与SHA256全部匹配发布清单；31个TCP及2个UDP监听与旧实例完全一致。
- `http://127.0.0.1:18080/api/launcher/v1/bootstrap` 与 `http://127.0.0.1:18081/api/gm/v1/health` 均ready，GM页面HTTP200。
- 启动stderr为0字节，日志无fail/crit/Host终止。最终独立核验时间为2026-10-19 16:08:02（UTC+8）。
- 原始记录位于 `artifacts/sealed-treasure-deploy-20261019/`：`source-manifest.json`、`deployment-receipt.json`、`final-verification.json`、`post-start-comparison.json`、测试TRX及启动日志。

## 用户实机验收

部署和运行就绪已验证，DGN3557的原生客户端行为仍待用户测试，GitHub #154没有关闭。重新登录后优先验证山麓入口、免费票优先与只扣一张、零疲劳入场及新房不扣疲劳、四元素分别解封、中央宝箱与白金箱开启。完整矩阵见 [实现说明](SealedTreasure-20261019.md)。本记录不把服务健康或自动回归升级为ClientVerified。
