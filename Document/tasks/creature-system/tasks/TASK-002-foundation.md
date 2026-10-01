---
kind: rks-task
id: TASK-002
revision: 1
---
# 宠物身份、本体store与登录基础
## Contract
覆盖T-002、RQ-001/002/003；依赖TASK-001当前实现证据。主agent修登录及样本repair设计，worker负责typed store持久化/事务/测试（无生产操作）。
## Detailed Plan
1. 登录完整序列回归加入已穿戴宠物，先观察missing marker失败，再将当前角色与支援角色统一调用期限投影。
2. ICreatureDomainService.GetStore(character)提供typed宠物操作/读取与原业务容器；新增事务session facet读取/更新本体，复用items身份和明细，校验拥有者。
3. 语义字段与opaque wire字段分开；状态转换使用时刻锚点以便后续规则接入，不在此单元猜数值。
4. 修复样本使用严格模板/UID/旧value条件和备份；不整库恢复、不删除宠物，不在尚未部署前扰动在线实例。
## Checks
登录真实构造结果能编码整条消息流；store所有权校验、修改后重读、与道具共同提交/回滚、换位保留身份。评审新旧状态写入口避免两个真源。
