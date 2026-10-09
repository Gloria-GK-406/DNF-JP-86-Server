---
kind: rks-verification
decision: pass
---

# 服务端重构与部署验证

本判定范围为任务路线的CurrentImplementation、相关协议回归、独立审查与运行就绪，不声称新的ClientVerified。用户要求“重构后部署供测试”已经交付。

| 条件 | 当前证据 |
|---|---|
| SC1/2/3/4 | quest-achievement-final.trx：108/108；真实4303受理后计数、普通怪、放弃、正式完成、回滚、唯一称号、事件重放/重启、被动等级与旧计数污染守卫。 |
| SC5协议/内容 | protocol-regression.trx：264/264，包括登录/当前快照过滤旧任务计数与只读查询；content-regression.trx：86通过、1跳过（环境已有PVF，缺失档案场景未运行）。 |
| 独立审查 | ../reviews/change-review.md approved；reviewed-target-manifest.json标识当前代码，review-static-title-mapping.json确认真实PVF63个称号奖励均有映射，任务掉落19个入口保留。 |
| 部署/数据 | deployment.json与final-verification.json：PID25424，实际新路径，版本0.2.2+title-quest-routing.20261009.9da9b83e，6个端口归属，Launcher/GM均ready。runtime/live-audit.json中角色、任务、物品、称号、邮件关键表启动前后变化为空。SQLite一致备份完整性ok。 |
| 客户端边界 | 当前EXE、PVF和0200_TitleBookMirror.dll未改；插件SHA256仍025BF6FEC0AEAAD3995C8EFD50A16F7EF24AEDAB5B466AF14F6182EE70A0ADB2。 |

运行及回退输入位于`E:/Project/DNF/DNF-JP-86-Server/artifacts/title-quest-unlock-routing-20261009`，旧runtime完整保留。服务端已经切换到新runtime；未提交/推送Git，也未清理用户工作分支。

## 人工测试清单

重新登录角色1123，在赛丽亚处接受怪兽猎杀者4303，确认初始30、击杀普通怪减少、放弃停止计数，目标完成后正式交任务并检查6501解锁与26596仅一件。已有被动等级称号应保持。称号簿镜像UI沿用前轮补丁，其剩余展示行为不由本次服务端测试宣布已修好。D-006为既有同事务批量任务写入边界，不影响当前单死亡命令流程。
