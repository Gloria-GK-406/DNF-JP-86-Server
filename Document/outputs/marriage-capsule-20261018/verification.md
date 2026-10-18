---
kind: rks-verification
decision: manual_required
---

# 成长胶囊与爱之摩伽陀验证

当前主张：服务端功能与协议接线已实现并通过自动化，准备部署供原生双端验收。未主张 ClientVerified。

| 验证 | 结果 | 日志（仓库根目录下） |
|---|---|---|
| 无PVF婚姻、商店、入口、选角、重放、经验相关回归 | 420通过，23条件跳过，0失败 | `artifacts/wedding-six-fix-20261017/capsule-final-app` |
| 真实PVF婚姻、胶囊普通效果、双人副本扣票与Boss房间 | 121通过，0跳过，0失败 | `artifacts/wedding-six-fix-20261017/capsule-final-real` |
| 协议项目全量 | 1551通过，60条件跳过，0失败 | `artifacts/wedding-six-fix-20261017/capsule-final-protocol2` |
| 真实及合成PVF地下城/胶囊/经验脚本 | 33通过，0跳过，0失败 | `artifacts/wedding-six-fix-20261017/capsule-final-infra` |
| 独立审核 | approved；已修复配偶通知依赖发起者ACK的缺陷 | 独立审核员 `capsule_entry_review` 的本轮结论 |

核心回归包含：双方门票检查前置、任一人缺票时均不扣票、双方各扣3且疲劳不变、真实Boss64762加载、经验胶囊30000普通effect、满包回滚、丢失ACK重发不重复制作/赠送、离线礼物持久状态、通关去重和满能量时已处理run防重放、旧通知读取当前值、旧会话隔离、胶囊查询与通关订阅不回到婚姻写链。

## 部署证据

当前数据库的在线一致性快照已通过integrity_check与foreign_key_check。部署在独立目录进行，停服后再作最终备份，并按全部既有婚姻、物品、角色、任务、邮件表逐表核对指纹；仅允许新增两张空胶囊表。脚本内含旧运行时与停服备份恢复路径。

实际部署是否成功及当前运行身份，以 `artifacts/marriage-capsule-20261018/deployment-receipt.json`、`final-runtime-audit.json`、`final-database-audit.json` 为准。发布构建须来自本轮已验证的唯一结果提交；此文不把计划中的发布视为已部署。

## 尚待用户验证

按[验收步骤](../../MarriageCapsules-LoveMagatha-20261018.md)检查地图可见及实际夫妻入场、双端机关与通关、胶囊页按钮/提示、离线赠送重登提示及领取。原生客户端画面和真实战斗行为不能由服务端测试替代。
