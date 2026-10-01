# TASK-005 — 强化保护券闭环与验收准备

## Task Unit Contract
- Revision: 1
- Outcome: 对应Plan T-005、RQ-007/008/009，验收资金/材料到账；普通/超级强化保护在适用失败时保装备和扣券，成功不扣券；即时、重登、回滚/重放一致，最终用户验收必需。
- Owned boundary: 现有强化保护规则/领域/handler/notification及直接回归；PVF/参考依据、发放记录、保护Human清单及必要部署。实例只应用Attribute低5位等级，不加入保护规则。
- Dependencies: TASK-004正确字段/UID边界；TaskContract rev2授权；真实PVF7804/10007840动作及描述、现有保护分支。
- Required checks: PVF保护动作与资格；普通/超级、优先级、成功/不适用处罚不扣、过期/错券拒绝；原始core/券计数；真实CommandBus/SQLite及注册encoder的即时通知/重读；回滚/重放；相关回归及Release；独立完整审查和用户最后门。

## Detailed Plan
1. 资金/材料通过角色AddGold、账号材料Add及离线容器Add，在同一CommandBus事务发放。备份副本试跑验证容量与完整数量，离线时仅一次生产发放，核对原装备/既有物品不变；不改券PVF期限或生产强化等级/概率。
2. 普通券7804 `protect equipment`使原破坏失败回0；超级10007840 `new protect equipment 10`按描述使适用失败回10、共存优先超级，仅普通强化。核对QuickSlot/Material自动扫描、实例ExpireTime和显式券槽；历史PVF日期与实例期限区别留证，客户端可用性人测，不以旧服务端测试自证。
3. 补缺失的真实存储/明确byte回归：普通回0、超级回10、共存优先、成功/非破坏失败不扣、过期/错券/边界；handler即时券余量/清空、强化位段及重读；提交失败与重放不多扣。复用已有正确覆盖。
4. 对复现缺陷先RED再最小修复，规则决定/领域资产/实例setter分工保持。无缺陷则保留正确分支、补有价值的覆盖。不得增加未知wire字段、改变概率/费用或引入生产强制失败开关。
5. 相关回归和Release，记录当前源码及准确测试目标；独立完整mixed复核、备份部署ready。旧field-fix review仅作未变基础引用。
6. 人测顺序：成功不扣、普通保护失败回0、超级失败回10且优先、即时数量、正常退出重登。用户原有可丢弃装备自然强化；未出现分支待验，不造通过。增幅材料/券已准备，增幅功能另续。

## Readiness
Contract rev2、Spec和Plan ready。继承原未提交产物指纹b60d0068...，HEAD仍6e60c4c6；原任务未seal，继续同一工作区，不嵌套worktree或先提交未验结果。

## Result — 代码/测试 worker，2026-09-30

- Changed: 现有保护规则/领域/实例/handler行为核对正确，产品代码零修改。三个直接相关测试文件补普通/超级、跨空间优先、成功/非破坏不扣、过期/错券/资格/越权、UID及raw core、实际CommandBus/SQLite/注册encoder余量与清空、账号无色、snapshot重读、commit rollback和保护replay覆盖。详细归属、当前源码SHA和命令见[保护代码证据](../verification/protection-code-tests.md)。
- Why: 自动消费券已进入receipt.Changes并有正确NOTI路径，新增测试证明即时与重读一致；ACK可选券回显仍是协议Unknown，保留已有request回显，不用自动消费槽改未证wire。没有实际产品行为失败，故补高价值回归，不制造RED或更改正确分支。
- Checks: 最终Application强化范围84/84、真实PVF12/12、既有Protocol wire8/8，均0 skip/exit0；Release solution构建0警告/0错误；三测试文件diff检查通过。跨角色新增fixture的重复容器注册失败已修正设置，保留原TRX，非产品RED。
- Remaining: controller两变更程序集全量及fresh完整mixedreview/部署/最后用户门仍须完成；本worker不宣称SC-6/SC-8人测或整个任务完成、不更改registry状态、不提交。

## Result — controller集成

- 物资发放和独立资产差分已完成，见supplies-grant.md；原装备保持。root两个变更程序集完整复验Application2356、Infrastructure408/2条件skip，exit0；其余未变目标保留TASK-004完整证据，最终所需汇总4557pass/0fail/2skip。
- 完整目标33文件指纹及7DLL一致性已核对；因为生产代码零修改，沿用ready PID41000。详见[当前证据](../verification/protection-current.md)。Task实现/自检查交付done；后续[独立完整review](../reviews/change-review.md)已approved，控制器再次核对证据并转入verification/manual_required，清单可验。用户最终门尚待反馈，未提交。
