---
kind: rks-verification
decision: manual_required
---
# 宠物重构阶段验证 — 2026-09-29

本次只确认已实现常规链可部署实测，**不宣布完整TaskContract完成**。独立初审和复审提出的运行问题已修复，部署就绪复审 `../deployment-readiness-review.md` 为approved（限定范围静态复审）。

## 自动验证
- 全解决方案build：0 warning / 0 error。
- Application：1988通过、171跳过、0失败；包含提交窗口入队、事务回滚、同UID升级进化。
- Protocol：1103通过、60跳过、0失败；包含完整带宠物登录编码、可见广播与脱下后的过期位置回执保护。
- Persistence：385通过、0失败；Host：24通过、0失败。
- 指定真实A21 debug PVF：宠物定义/规则相关18通过、0跳过。
- 最初并行回归出现契约测试一例失败，单独3例及后续整套均通过；未修改契约实现。新增RoomReward成员名称触发架构约束，已改为CreatureKey，整套验证通过。
- `git diff --check` 通过。协议更新以语义TSV为输入重建；生成器同时同步了既有01DE正确语义，不是新增通知业务。

## 实际部署与数据
- runtime：`E:/Project/DNF/DNF-JP-86-Server/artifacts/creature-system-20260929/runtime`
- 启动：2026-09-29 22:37，本次PID3380；HTTP bootstrap返回200，TCP监听与启动日志正常，stderr空。
- 数据库保持原路径，未替换为副本。备份：原数据库旁 `dfo-server.db.pet-identity-20260929-223718.bak`。
- 精确修正角色1123（CharacterId2）UID948/949/950的core.Value；未改其他物品。启动后只读核验五只宠物/蛋的core键与真实UID全部一致。
- 正式容器服务新增蛋63022×2，UID959/960，宠物栏内部0/3；非复制core造样本。副本演练发现并修复了蛋模板按文件名映射失败，优先使用PVF `[creature species]`。
- 服务日志：`artifacts/creature-system-20260929/server.stdout.log`、`server.stderr.log`（主仓库下）。

## 未验证条件
- SC-2/3 实机：登录面板、穿戴换宠、重登、喂食改名、饱食度和清房经验，用户尚未验收。
- SC-4：孵化领域动作已实现，但A21 C2S入口缺真实请求；已准备蛋，等待用户右键确认一次以取证。
- 任务/多选/道具成长型进化、自动喂食客户端触发及宠物回应/脚本通知受众仍未闭合；0065/007A仅严格解析记录，不伪造成功响应。普通规则不套到物品成长宠物或无疲劳地图。
- 尚未推送、合并或收尾分支；保留当前工作树继续补齐。
