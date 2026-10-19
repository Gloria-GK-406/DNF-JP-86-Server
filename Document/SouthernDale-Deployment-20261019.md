---
kind: rks-verification
decision: pass
---

# 南部溪谷合并与本地部署

## 2026-10-19 后续用户验收

用户在右上角提示PVF补正后明确确认“已经没有显示了，南部溪谷可以当验收成功了”。本轮基础闭环验收通过，GitHub #153 已评论并以 completed 关闭，#170 和旧总览 #150 对应条目已完成。提示补正和服务重启记录见 [提示PVF修改](ScheduledDungeonNotice-PvfFix-20261019.md)。下文部署时的“待验收”不代表最新状态；本次反馈不扩大为所有异常边界均逐项实测。

2026-10-19，用户明确要求“合并回 task/next-round-20261019，部署，然后我测试一下看看”。本记录对应这次合并部署，接续已封存的本地实现；不修改原Spec的历史授权或验收结论。

## 合并与构建

- `task/next-round-20261019` 从 `35d890662e987c85ff7e05786ae72c068f81c187` 快进至 `5297d9b00b5add4bd5c4dc9b1812951c6bfe2779`，保留planning、implementation、remediation、seal四个检查点。
- 主检出当前分支为 `task/next-round-20261019`。原有未跟踪调查文档、outputs及工具cache保持原样；未推送、创建PR或发布版本。任务分支保留，未执行未要求的删除。
- `dotnet publish Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj -c Release --no-restore --self-contained false -o artifacts/southern-dale-deploy-20261019/runtime` 退出0。
- 合并后真实PVF的SouthernDale、EventInformationIdentity及TauKingdom回归30项通过、0失败、0跳过，见 `artifacts/southern-dale-deploy-20261019/post-merge-tests/`；此前完整实现检查见[完成核验](tasks/southern-dale-20261019/verification/completion-evidence.md)。

## 数据保护与演练

- 原数据库继续使用 `Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`，没有初始化新库或发放测试物品。
- 切换前用SQLite backup API生成停止时备份 `artifacts/southern-dale-deploy-20261019/stopped.db`，`integrity_check=ok`，外键检查为空。
- 新版本先在数据库副本上无网络启动演练，再切换实际服务。演练和实际启动后都逐表比较：原106张表的行数与内容摘要不变，仅新增空表 `southern_dale_clear_rewards`、`southern_dale_daily_quest`。
- 旧运行目录 `artifacts/collection-box-deploy-20261019/runtime` 保留作为可恢复的运行产物；备份与配置身份均保留在本次artifacts目录。失败回退重开兼容旧实例时保留当前数据库，不自动覆盖可能新产生的数据。
- 新运行目录的appsettings与旧实例逐字相同，SHA-256 `E1F991F5A79DFC231E0C5AAB9171028FC30409DA43E1576D27BD7E8675E5EEDE`。PVF继续使用收集箱常驻文案归档，SHA-256 `7E679699E814F60CE2B9315E661DF62A31F7D784DD49FE221FB9F2BE66EA425B`；未改客户端或PVF。

## 切换后验证

- 运行构建提交：`5297d9b00b5add4bd5c4dc9b1812951c6bfe2779`。
- 实际版本：`0.2.7+5297d9b00b5add4bd5c4dc9b1812951c6bfe2779`。
- 进程PID：`38644`，唯一Host，路径 `E:/Project/DNF/DNF-JP-86-Server/artifacts/southern-dale-deploy-20261019/runtime/Dfo.Server.Host.exe`。
- 12个Dfo程序集逐项版本与SHA-256对照发布清单一致；31个TCP及2个UDP监听与旧实例完全一致。
- Launcher bootstrap、GM health均为ready，GM页面HTTP 200；启动stderr为0字节，日志无fail/crit/Host终止。
- 原始证据：`artifacts/southern-dale-deploy-20261019/source-manifest.json`、`deployment-receipt.json`、`final-verification.json`、`post-start-comparison.json`与启动日志。

## 用户实机验收

部署和运行就绪已验证，客户端验收仍待用户完成，issue #153保持开放。重登后先检查山麓南部/牛头入口与邀请函补足2，再检查单人/组队扣票、通关钥匙、每日任务及罐子。金币罐仍按PVF另消耗2枚钥匙。完整步骤见[实现说明](SouthernDale-20261019.md)。
