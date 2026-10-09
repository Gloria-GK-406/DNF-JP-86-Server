# 0.2.3 交付与验收边界

日期：2026-10-09。基准：main 81f4373f、深渊工作分支9da9b83e及后续称号修订。服务端版本0.2.3；配套ClientHack 0.1.1（ABI2，0200 TBM1 v1）。

本分支纳入已完成的地下城规则/难度、主动深渊与奖励、史诗图鉴、CH100与切频道、升级券、城镇旅店记录、GM邮件批量附件、统一事件投递及成就/称号簿。称号协议与客户端兼容的当前答案见[流程说明](AchievementTitleBookFlow-20261009.md)。

## 新鲜验证

- `dotnet build Server/86JP.Server.slnx -c Release --no-restore -m:1 -p:ContinuousIntegrationBuild=true`：0警告、0错误。
- `dotnet test Server/86JP.Server.slnx -c Release --no-build --no-restore -m:1 --logger trx --blame-hang-timeout 3m --blame-hang-dump-type none`：8个测试程序集，5628通过、650跳过、0失败。未提供PVF时真实内容测试按标记跳过；不能称为6278项全通过。
- 使用真实PVF定向测试成就/称号簿、任务型称号与难度：59通过、0跳过、0失败。
- GM前端：148测试通过，TypeScript检查与Vite生产构建通过；现有大chunk提示非阻塞。
- WireSmoke 5项、Release 11项通过；协议实现覆盖对账和self-test均0失败。
- ClientHack：Win32 Release全部构建、stage及A21插件通过，CTest12/12；21处dump和1处独立实启签名一致。

启动拒绝测试会传入故意损坏的配置。此前异常逃出Host入口导致Windows弹出dotnet异常框；入口现在保留完整标准错误并返回退出码1，正常退出码0。21项启动相关测试通过；错误配置仍不得监听端口，不通过关闭系统WER掩盖异常。

## 验收与保留项

用户接受当前称号玩家闭环（配套0200）、CH100频道切换、史诗碎片进入图鉴。此前各步骤的验收按各协议页限定，不把整体接受扩大到全部多人、跨日、冷启动或崩溃边界。

称号功能以[任务#141](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/141)收尾，父#58/#90的收藏/每日每周范围继续开放；随机深渊#140、ClientHack稳定性#1和技术债D-006保留。当前部署保持原运行身份，本次版本升级是仓库版本；没有重新部署、打tag或发布Release。
