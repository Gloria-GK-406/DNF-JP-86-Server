# TASK-003 — 集成验证、部署及最后验收准备

## Task Unit Contract
- Revision: 1
- Outcome and references: 将TASK-001/002实际产出整合为可供最终验收的版本；覆盖T-003、RQ-004至006、AC-004至006的自动证据和人工入口。
- Owned boundary: Document任务及示例文档、相关原强化验收准备链接、必要集成缺陷修复；新部署工件在主仓库artifacts/equipment-instance-20260930。不造生产装备或调整角色资产，不更改协议布局，不提交/推送。
- Required checks: 全量Application/Protocol/Persistence Release回归，真实PVF的相关Infrastructure，Networking/Host兼容；Release构建及diff检查；生产模块+真实PVF+数据库副本登录和实例/规则验证；旧writer检索；运行就绪和备份证据。
- Dependencies: TASK-001真实SQLite身份与事务行为；TASK-002公共报价/规则/实例接口及全部既有调用迁移。

## Detailed Plan
1. 接收TASK-002真实结果，核对调用覆盖、旧snapshot移除、DI和行为回归，不把结构改动当作新的wire证据。
2. 设置真实PVF档案路径，在当前源版本执行完整Release验证。失败须确定是否基线问题或当前回归；有修复才重复相关检查。
3. 使用实际模块组合、生产SQLite只读备份副本、真实PVF，验证选角库存编码与公开规则报价；在副本的真实CommandBus事务内调用装备实例，核对持久化身份和两句柄修改。生产数据仅只读。
4. 保存典型代码例子和责任/调用覆盖表；基于实际接口明确报价用于准入，TryReinforce用于结果，领域服务结算后调用Set/Destroy，实例有事务生命周期。
5. SQLite在线备份并quick_check；发布独立新runtime，保留旧runtime；验证旧PID路径后仅停止该服务并以Hidden启动新服务。核对启动、四个监听端口、bootstrap ready、stderr。
6. 只读读取当前角色、金币/无色、UID339装备基线；列出E-01结构确认、R-01单次+0→+1和R-02正常退出重登。原五项完整GUI清单仍未验，旧环境文档标为历史。
7. 所有产出齐备后进入独立全结果review；当前审批通过后作新鲜证据核对。最终用户反馈前verification为manual_required，SC-6待验，不sealed spec-result、不commit。不登记纯等待用户的todo。

## Task Unit Result
- 实现与集成准备完成：完整Release4510通过/0失败/2skip，build0警告/错误；真实模块+PVF+生产库副本公开规则/实例及选角编码通过。
- 原库在线备份quick_check=ok，沿用原数据库，PID42104新Host ready，四端口监听正确，stderr空。旧服务目录保留。
- [典型例子](../implementation-examples.md)、[最后验收清单](../Human-Test-List.md)、[原始证据索引](../verification/automated-and-deployment.md)已齐全；旧强化准备文档明确标历史入口。
- 本Task的结果是可供独立复核和人工验收的版本；SC-6用户最后反馈仍待验，不作为纯等待todo、不提交或声明契约完成。后续review及verification由控制器推进。
