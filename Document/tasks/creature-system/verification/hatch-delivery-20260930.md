---
kind: rks-verification
decision: pass
---
# 宠物孵化接线交付 — 2026-09-30

## 已交付的实现范围

用户授权：“先完成宠物的孵化功能吧，结束后和我说”。本轮完成TASK-004的孵化接线，覆盖RQ-004/006、AC-004/005中的孵化部分；不宣布完整宠物TaskContract完成。工作基线`bbeb98ef`，本轮Task Unit改动保留在现有工作树，供完整结果评审及统一提交。

- 接入实际捕获的Game CMD0066，严格解析3B `u8 list, i16LE slot`。只接list7宠物本体槽0–139，其他列表/槽位失败，不派发业务写。解析长度异常遵循现有解码器坏包策略，不用宽松offset扫描。
- `HatchCreatureHandler → HatchCreatureCommand → ICreatureRuntimeDomainService.HatchAsync → ICreatureTransformationDomainService.HatchAsync`。既有领域方法检查当前会话、拥有关系、期限、蛋/产物定义及species映射；core和详情在同事务修改。
- 63022→63023保留原UID和槽位，等级1、经验0、饱食100、清空名字及模式字段，按产物PVF重算期限。成功将该UID旧的在线饱食余额在同事务作废，下一次结算从新状态起算；回滚同时恢复详情、core和在线余额。
- 提交成功后给主人依次发CMD0066 `[01]`、NOTI000E原槽更新、NOTI0069同UID详情。失败仅发CMD0066 `[00 02]`；错误码2为本服通用拒绝选择，未声称原服所有孵化失败原因也使用2。重复对已孵化宠物请求被拒绝。
- 不为背包孵化广播场景进化动画，也不混用NOTI0066经验通知。所有业务变化经命令事务，协议handler仅解析/编排投影。

## 新鲜验证

日志目录：`E:/Project/DNF/DNF-JP-86-Server/artifacts/creature-hatch-20260930/`。

| 验证 | 结果/证据 |
|---|---|
| Release全解决方案构建 | 0 warning、0 error |
| TCP行为RED | 基线缺入口，新增`CreatureHatchFrameIntegrationTests`捕获请求测试1失败，未收到响应；GREEN为1通过；后续网络回归包含此例 |
| Protocol完整回归 | 1137通过、60跳过、0失败；`protocol-tests.log`。新增请求/响应向量、坏长度、非法位置、成功更新身份、失败无刷新、提交异常不发成功 |
| Application相关回归 | 79通过、0跳过/失败；`application-tests.log`。覆盖孵化提交/回滚、重复、非蛋/过期/旧会话/坏槽/坏产物及现有宠物、登录和命令架构 |
| Networking相关回归 | 19通过、0跳过/失败；`networking-tests.log`。包括真实TCP请求接线、帧适配/编码及发送失败 |
| Persistence相关回归 | 22通过、0跳过/失败；`persistence-tests.log`。宠物身份、复活及跨载体事务 |
| 生产模块+真实PVF+当前库副本重放 | `hatch-probe.log`最终退出码0。生产decoder/dispatcher/CommandBus/SQLite完整处理`070000`，实际编码CMD0066 body01、NOTI000E和NOTI0069；UID959原槽0→63023；重复拒绝；下一时钟饱食100；登录库存重新加载仍同UID/槽 |

副本探针源码在`probe/Program.cs`和`probe/Probe.csproj`，副本数据库`hatch-probe.db`。探针早期在事务外读取业务store导致失败，已改为SQLite只读核验，再重新生成副本重放通过；不涉及生产库写入。仅TCP入口测试按RED/GREEN执行，之后补充的测试为实现后回归证据。

## 实际部署

- runtime：`E:/Project/DNF/DNF-JP-86-Server/artifacts/creature-hatch-20260930/runtime`；完整当前分支发布，沿用先前服务配置、PVF和真实数据库。
- 已停止此前登录修复runtime PID25932，新服务PID13728，启动时间14:05:02；bootstrap返回HTTP200、`status=ready`，监听18080/7001/10010/10011，stderr空。
- 部署前SQLite在线备份`before-deploy.db`，`quick_check=ok`。生产库中的UID959/960两枚63022蛋没有被自动孵化，留供实机测试。
- 发布DLL哈希与当前Release构建一致：Protocol `3dc532e557657af556fb58314231a87699e72742cda15484a7b1844c4800dc14`，Application `a436cc885c99853801f0472ff986cd6f0c88899e25ae29cca9c5b2ec42d34822`；本轮没有push、merge、分支清理或生产样本修改。

## 实机验收结果

用户随后明确反馈“ok，验收没问题”，P10按验收列表通过（ClientVerified，限普通63022蛋路径）。服务14:11:27已处理HatchCreatureCommand并依次发送CMD0066、NOTI000E、NOTI0069。生产库只读核对UID959为63023、core键959、level1/exp0/饱食100，UID960仍63022。GUI和重登依据用户确认，日志与数据库仅支持服务处理及持久化部分。

本文件pass仅针对本轮孵化交付；完整宠物TaskContract仍进行中，特殊成长/任务/多选/主动技能及原两项真实PVFCreatureGoesBackOnTests失败继续保留原边界。
