---
kind: rks-verification
decision: pass
---

# 完成证据

2026-10-10，独立审查 approved 后，主控对冻结源码完成 Release 构建、八个测试项目回归和实际部署核验。此判定针对契约要求的实现、自动验证与部署；本次客户端人工验收仍待用户执行，不标为 ClientVerified。

## 条件覆盖

| 契约 | 当前证据 | 结论 |
|---|---|---|
| SC-1 | DomainEventEntryTests 与 UsageQuotaTests；Application 3502 passed | DirectAsync 顺序等待/取消/失败停止/事务内外、后续订阅失败共同回滚；持久化调度回归通过 |
| SC-2 | 生产源码写入口搜索、UsageQuotaTests、ItemUsage/FatiguePotion/Premium/Mirror 全量回归 | 消耗品/权益/镜像写次数收口；同批预计值前置检查、CAS、失败回滚/拒绝、幂等和续期总量保护通过 |
| SC-3 | 原表重载与跨周期查询测试；quota-before-start.json 对 quota-after-start.json | 原表/键/时间戳和原重置规则保留；1条镜像日期记录与57条daily counter部署前后逐行相同，查询不落库清零 |
| SC-4 | UsageQuotaProjectionTests、DungeonLoadingProjectionDeliveryTests、真实SQLite提交后订阅测试；Protocol 1432 passed | 提交前/回滚不投影、合并读取最新值、投影失败不回滚业务、离线/替换会话保护、午夜刷新与加载后同步通过；0279首字段Unknown发0，第二字段used；021E/036F家族保持，未新增0025ACK |
| SC-5 | deployment.json、runtime-verified.json、publish.log；停旧服后DB/WAL/SHM备份quick_check=ok | 新服路径/程序集哈希与版本一致，六监听归属新PID，health/bootstrap ready、GM邮件页200；旧服已停止，回滚输入保留 |

## 构建与回归

执行命令存于授权artifact的 `verification-run.ps1`：Release solution build（0 warning / 0 error），随后对八个项目逐一 `dotnet test -c Release --no-build --no-restore`，真实PVF为 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。测试总计：**6631 passed、2 known baseline failures、2 intentional skips**，不宣称整套全绿。

各项目计数、失败名称、完整TRX路径及SHA256见 [test-summary.json](./test-summary.json)；构建与每个项目日志同目录。完整TRX另保留在 `E:/Project/DNF/DNF-JP-86-Server/artifacts/usage-quota-direct-events-20261010/verification/`，不依赖后续工作区是否保留。

Infrastructure两个失败仍是 PvfQuestEnemyRewardTests.A21_APC_object_and_repeated_candidate_declarations_are_preserved 与 PvfQuestDefinitionSourceTests.Projects_every_neutral_eligibility_input_from_the_real_archive，预期2814、实际3252；本轮完整套件只失败这两项。原reader在b9717c65已隔离复现，见 [原基线对照](../../mirror-arad-entry-20261010/verification/baseline-pvf-counts.log)。两个skip是配置了真实PVF时不适用的缺archive测试，不计为通过。没有新失败，没有修改旧断言掩盖资源差异。

代码差异卫生：`git -c core.whitespace=cr-at-eol diff --check` 通过；基线TitleBook测试保留CRLF，忽略的是CR-at-EOL而非实际尾随空格。

## 部署身份与数据

- runtime：`E:/Project/DNF/DNF-JP-86-Server/artifacts/usage-quota-direct-events-20261010/runtime/Dfo.Server.Host.exe`
- version：`0.2.3+quota-direct.20261010.r1`；PID：36176。
- Host DLL SHA256：`85C86A146316CEE81B2D361AEAD2646B99F3D3675B50BABA574FF410FA8E19A5`。
- 编译源码固定为基线 `a0ef0f150dc8bf9d567177a007cd1766033ad022` 加1988个Server源文件的manifest；发布前/停服前/部署后均核对当前源哈希一致。manifest：`E:/Project/DNF/DNF-JP-86-Server/artifacts/usage-quota-direct-events-20261010/source-manifest.json`，SHA256：`6D1FA120ADFFAABD92AB176DAD2760A3AAC353C0F9077F32647B58AFE4A2DF9A`。最终接受提交作为部署回执的外部状态另记，不自引用改写本证据。
- 数据库仍是原 `Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`；未用备份覆盖权威DB。
- backup：`E:/Project/DNF/DNF-JP-86-Server/artifacts/usage-quota-direct-events-20261010/database-before-20261010-131825`；DB及WAL/SHM输入哈希、旧服路径/配置见deployment.json。
- 新PID拥有7001、10010、10011、10100、18080、18081。实际HTTP再次读取health与bootstrap均ready，旧PID49556已不存在；stderr无启动错误。
- 部署后只读对照：角色2（1123）2026-10-10 entries=6、clears=1、daily_reward_claimed=0；预期每日上限10减6剩余4。人工后续消费会合法改变该值。

## 实机边界与工作区

用户按 [测试集](../client-test-set.md) 验证重新登录/选图恢复、成功入场只减1、拒绝不扣、领奖幂等、消耗品及权益次数。协议字段的静态确认和自动codec测试不等于本次实机验收，0279第一字段仍Unknown。

当前工作区 `task/monster-declaration-parser` 保留；main仍为 `d0427ce3ecc987ab76050f0e714fe919c7188fcf`。本次不推送、不合并、不清理工作区或分支。完整接受结果与证据一起作一次本地提交，提交身份在交付回复与部署回执外部记录。
