---
kind: rks-verification
decision: manual_required
---

# 婚礼六项修复验证

声明范围：服务端六项补正已通过自动化，可进入备份部署与原生双端验收。代码和协议测试不能证明客户端动画、模型及双端显示均已通过。

| 条件 | 结果与证据 |
|---|---|
| 礼炮真实dispatcher与结算回滚/重发 | RED确认主命令缺失异常，GREEN及专项通过；fireworks-red.log、final-targeted.log |
| 男女任一方预约均按新娘、新郎投影 | 两种发起方用真实身份测试；final-targeted.log |
| 开场PVF坐标保存且在03D7前广播 | 位置持久化、事务rollback及重登回归通过；当前坐标读PVF通过；原版镜头与坐标性别顺序仍须实机/历史资料确认 |
| 婚龄完成时间持久与秒级协议 | 完成/缺席取消、重开数据库、原生偏移向量通过；age-red.log、final-targeted.log、protocol-final.log |
| 心意通事务、关系、上限、重放 | 未婚不耗、事务回滚、上限不耗、另一端读取共享点、数据库重开、真实dispatcher已提交丢ACK后重试不重扣均通过；final-targeted.log |
| 配偶等级/职业字段 | 00892D20/00892DCE与13A6E31职业查表闭合；骑士level85/job12协议向量通过；protocol-final.log |
| 全量业务/协议/Persistence | Application3541通过426跳过；Protocol1512通过60跳过；Persistence469全部通过。初次失败的注册/表数量清单已更新到既有婚礼命令/表与本轮入口，未弱化事务/选择会话约束 |
| 当前PVF与TCP | 婚礼及物品解析25通过；婚姻TCP集成1通过 |
| 源码一致性 | git diff --check通过；一次本地结果提交后publish固定来源版本；部署回执另存artifacts |

全部日志位于 `artifacts/wedding-six-fix-20261017/`。最后一次生产代码调整为返回与持久化一致的完成时刻，最终专项75通过3跳过。

## Unverified Conditions

- 用户原生双端验收：礼炮不掉线、000模型、当天婚龄、心意点实时同步及重登保持。
- 新一场婚礼：角色问答和开场站位；原版是否含行进镜头仍保留Unknown。
- 部署在代码验收提交后执行，健康检查、PID/路径/哈希、监听、当前数据库备份/回填及回滚输入见同目录 deployment-receipt.json，不以本文件提前宣告部署成功。

保持当前已婚数据，历史回填使用生产日志首次Completed时间2026-10-17T11:03:15.530+08:00。未授权重新恢复预约前备份，因此本轮不重置角色婚姻进度。
