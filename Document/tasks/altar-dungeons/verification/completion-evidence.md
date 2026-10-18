---
kind: rks-verification
decision: pass
---

# 两祭坛交付验证（2026-10-13）

## 精确声明

当前实现及部署已完成，可供用户用A21客户端实机测试。不宣称原生UI、完整多人矩阵、D1按钮或本期线包已验收。

| 条件 | 结果及证据 |
|---|---|
| SC-1 | [查证与实现记录](../research-and-implementation.md)及protocol人工区记录当前PVF、A21和70来源，校正D3/D1/D2；用户最新回答支持按查证采用全天默认、每日3次，未声称未知时段是原生事实。 |
| SC-2 | Application专项16项通过，包含真实无尽15轮/3房、极限7轮手选与超时、动态Cell延迟提交/清房阻塞、迟到generation/房间、全员加载后才D7/D2。见`artifacts/altar-dungeons-20261013/verification/altar-lifecycle.trx`，真实内容2项通过见`altar-content.trx`。 |
| SC-3 | 真PVF金币不足拒绝、事务回滚、3次持久化/日切、重复结算和部分收益回滚测试通过。奖励复用InventoryThenMail，完整Application回归包括ItemDeliveryTests满包邮寄及direct-only保护；首次/重复领取受事务状态去重。 |
| SC-4 | [独立审查approved](../reviews/change-review.md)。Application3750通过、Protocol1484通过、Infrastructure876通过/2跳过、Persistence468通过，0失败。含真实002A处理器先结算后移除、真实PartyService成员放弃/断线后剩余继续、普通地下城/大会及分层/命令构造器回归。两项跳过为“缺少PVF时报错”测试，因为本次显式提供PVF，不是祭坛跳过。 |
| SC-5 | 发布版本`0.2.5-altar.20261013`，PID39268，exe=`artifacts/altar-dungeons-20261013/runtime/Dfo.Server.Host.exe`。bootstrap ready，GM HTTP200；Channel7001、HTTP18080/18081、全部28 Game端口归该进程，缺失0，旧PID2252已停。SQLite备份与当前库integrity_check均ok、sqlite_master结构相同，配置SHA256与旧配置一致。研究记录提供逐项实机测试集。 |

## 自动化和产物身份

- 解决方案Release构建、发布均exit0，无编译警告/错误；四个完整相关项目合计6578通过、2跳过、0失败。
- 所有测试设置`PVF_ARCHIVE_PATH=E:/Project/DNF/DNF-JP-86-Server/artifacts/avatar-socket-text-20261011/Script.server.pvf`。`.NET`构建测试串行。
- 原始TRX与`test-summary.json`位于`artifacts/altar-dungeons-20261013/verification/`；本记录为受版本控制的结论，不将构建缓存纳入Git。
- 发布时HEAD仍为基线`a375a148d7fe8a711a64f56959bd41067b4cf217`，InformationalVersion带基线后缀；不能把它宣称为随后接受提交的精确源码号。`source-manifest.json`记录1357个实际源码/构建输入SHA256，复核当前文件全部一致；manifest SHA256=`f2d830cc7acb2e23c0d9a03e14753447a2928c36f2f47c057a79087bb8d6c0e4`，明确BuiltFromUncommittedReviewedSource=true。
- Application DLL SHA256=`F04AD3F0754BC15A925C1B1851885BF380C10AACB83C58B2A574B4F9612D8BC1`，其他核心程序集、时间、端口和配置身份见`deployment-verification.json`。
- 配置SHA256=`96370E95F7F834D8EEF68E39F86C2C5FC5772CA23CC21B96CC3646226F8E1869`，原数据库路径不变；无破坏性迁移，不改变1123角色数据以绕过测试门禁。

## 回滚输入和实际范围

旧运行目录保留：`artifacts/client-settings-blobs-20261013/runtime`。新目录rollback下保留`appsettings.previous.json`及`database-before-altars.db`，备份通过SQLite backup API生成，含未checkpoint的WAL内容。回滚程序时可停止核实过的新进程再启动旧exe；不要自动用旧库覆盖已产生的新游戏进度，数据库恢复应另行授权。

当前默认全天，每祭坛每日3次，已有06:00日切规则。无尽Lv86三次22000/29200/46000金币，不扣疲劳。历史最佳纪录未持久化。断线不补发未结算收益，已入账不回退。实机验收仍由用户进行，本期未改客户端/PVF。
