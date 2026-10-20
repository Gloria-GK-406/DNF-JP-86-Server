---
kind: rks-verification
decision: pass
---

# 幸运星与装备租赁交付验证

验证声明：当前代码已完成授权功能、通过独立完整审查并部署为可供用户实机验收的测试服务端。此pass不宣称原生客户端验收通过。

- 代码目标：`c87e0a75b88e401fab5e010cf573906d05f2ce9f`；独立审查`../reviews/change-review.md`的target一致、approved，无未解决阻断。
- 任务分支：`task/lucky-star-rental-20261020`，正常checkout `E:/Project/DNF/DNF-JP-86-Server`。基线90c5678b，规划checkpoint a9496cb5，实现checkpoint c87e0a75；最后seal仅收证据，不改变运行代码。

| 条件 | 当前证据与结果 |
|---|---|
| SC-1 余额、购星、道具、通关和租赁 | 当前PVF规则、真实dispatcher和领域集成覆盖正常/拒绝/去重；real-pvf-final.trx 17/17通过，基础语义与报价覆盖14职业，数据只在临时测试库变更。 |
| SC-2 UID、期限与跨容器/重开/清理 | LuckyStarRentalTests验证真实UID移动至Equipment、仓库重开、到期清理及关系删除；期限以item_core.ExpireTime为准，自动回归通过。 |
| SC-3 原子性与重放 | 金币不足/超额、满包、SQL关联写失败、通关标记失败、真实道具消费失败回滚，以及dispatcher重复购星/租赁/道具只结算一次，均通过；旧999余额可消费。 |
| SC-4 A21协议与统一投影 | 非零14B冗余前缀、22B正文、ACK/公共失败、完整03C1模板和绝对期限codec覆盖；静态指令证据在protocol/patterns/data/probes/lucky-star-rental-20261020.md。 |
| SC-5 部署与数据保留 | publish退出0，部署脚本退出0，verify_deployment.py退出0；online和stopped备份integrity ok，现库integrity ok，两个新表与删除trigger存在；7个程序集及PVF哈希一致。原11张关键表所有既有列/行与停服备份一致，无玩家资产调整。 |
| SC-6 明确交付及待验收 | Document/LuckyStarRental.md记录规则、架构、客户端取证/当前实现边界、PVF职业冲突与实测清单；用户测试仍待执行。 |

## 自动验证

全方案各测试项目当前有效结果6763通过、898显式跳过、0未解决失败。证据`artifacts/lucky-star-rental-20261020/test-summary.json`指向每个TRX；包括Application3720通过、Persistence475通过、Protocol1592通过。全方案命令最后一次因表结构断言列序错误退出1，此测试修正后重新运行整个Persistence项目退出0，其余项目未受影响且当前通过。初轮既有登录测试2秒超时，在单独及完整Application复核均通过，没有修改其超时或生产登录代码。真实PVF17/17及wire-integration31/31用于补充无PVF跳过分支；没有将跳过计作通过。

## 运行与回退输入

- 部署时间：2026-10-20T12:12:47.3076377+08:00。
- 新进程PID `39432`，运行版本`0.2.7+c87e0a75b88e401fab5e010cf573906d05f2ce9f`，可执行路径`E:\Project\DNF\DNF-JP-86-Server\artifacts\lucky-star-rental-20261020\runtime\Dfo.Server.Host.exe`。
- 启动器bootstrap与GM health均ready，监听31 TCP/2 UDP与旧实例集合一致；独立进程路径核对通过。
- 测试数据库`E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Data\dfo-server.db`，未重置；旧runtime仍保留。
- 在线备份`E:\Project\DNF\DNF-JP-86-Server\artifacts\lucky-star-rental-20261020\before-restart-20261020-121240.db`；停服备份`E:\Project\DNF\DNF-JP-86-Server\artifacts\lucky-star-rental-20261020\stopped-20261020-121240.db`；均已完整性检查。
- PVF `E:\Project\DNF\DNF-JP-86-Server\artifacts\scheduled-notice-pvf-20261019\Script.server.pvf`，SHA256 `C5CCE5346F60D21128D756A086EEF96E70F75F2F2FE063B6C61C00506C588F06`，本轮未修改客户端/服务端PVF。
- 可复核产物：同任务artifacts下deployment-receipt.json、deployment-verification.json、live-verification.json、deployment.log、publish.log及stdout/stderr。

## 实机待验收与限制

购星实际界面/提示、武器穿戴战斗、同模板续租显示、角色切换账号余额、到期换频道/重登回收和通关/道具获得提示尚未ClientVerified。租期24h，在线不新增即时回收扫描，符合当前PVF描述。部分守护者目录商品与装备自身职业声明冲突，按装备限制拒绝；折扣未自动启用。旧租赁表目标库实际0条，保留表和登录兼容读取，不将此证明扩大为任意旧库迁移覆盖。

## 仓库处置

保留任务分支及正常checkout供用户验收，没有合并、推送、PR或清理。任务外已有untracked资料保持原状。seal只提交本任务审查、验证和结果文件。
