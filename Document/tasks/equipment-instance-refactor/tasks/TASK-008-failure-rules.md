# TASK-008 — 强化失败规则与完整损毁产物

## Task Unit Contract
- Revision: 1
- Outcome: GetResult(equ,context)规划保持/降级/保护/销毁，销毁产生70基础分解及按原等级追加奖励，统一事务结算并供最后实机验收。
- Owned boundary: 强化规则/失败规则契约及适配、相关内容和纯分解计算、领域多产物规划/结算/通知兼容、相关回归与部署文档；不修改NPC分解行为或其他养成，不重发/恢复历史资产。
- Dependencies: root rev4授权、现有instance+事务+容器、已运行诊断包和当前PVF，70 ELF证据；现有改动全部保留。
- Required checks: 缺产物和原等级数量的真实存储RED/GREEN，白装/稀有、多奖励/虚拟无色、保护、满包、提交/中途发放失败回滚、重放/通知；候选真实PVF+实际DI、副本；Release、相关全量、独立完整review；正常退出后备份/部署ready、人测损毁和重登。

## Detailed Plan
1. 保存当前完整源身份快照及运行基线，限定本Task源diff，保留原未提交结果及其他改动。补充70 ELF `_ProcPenalty` 0x0854714f取DisJoint::GetResult、0x0854723b追加、0x085472d3批量入包；旧等级085470d9/原等级奖励08546938；GetResult VA084733f6、附加084732b4。证据与A21参数分开。
2. 先追加真实SQLite白装损毁有无色和稀有原等级奖励回归，观察旧实现预期失败；其他保护/成功边界不变。以最终资产与receipt作oracle，不测试构造器形状。
3. 强化规则只负责报价/准入/成功抽样；新失败规则拥有处罚/保护决定和完整奖励列表。公开instance入口通过薄只读适配冻结facts，计算实现只依赖facts/静态规则，显式上下文传入保护与奖励随机输入，无隐式库存查询。关闭旧处罚/单奖励计算入口。
4. 70基础GetResult只用基础无色及品质附加结果，不直接复用NPC整个服务/expand。复用静态分解数据和适当纯计算层，核对1.1价值修正、售卖率、grade及截断；追加奖励原等级、品质权重/grade修正。PVF缺失/非法数据明确拒绝，不销毁后少发。
5. 领域先规划全部物理/账号产物，模拟本次扣材、叠堆和各奖励之间容量占用，再在同一命令事务扣费/扣券/实例修改/多奖励发放。同步receipt全部槽/账号变化及现有ACK通知，不扩线布局。规则结果只能在本次结算应用一次。
6. 定向及相关程序集完整测试，真实PVF副本生产DI验证；完成实例/失败典型例子及F人测。publish候选并核对源/包身份，供新鲜独立完整review判断；批准后、游戏正常退出才备份生产并切换，保留当前PVF/资产，ready后新鲜核对。人工反馈前根契约保持manual_required。

## Readiness and Attention Check
ready：新增SC10直接修补用户损毁无材料观察，同时兑现已授权失败服务设计。没有增加增幅/改概率或制造验收样本；本Task分解计算和多产物事务较集中，按executing-tasks Standard将生产交给有界worker，root保留原契约/资产/独立review和部署判断。遗留范围及用户最后门保持。

## 实现交回与root检查

生产实现及局部返修见[TASK008结果](TASK-008-failure-rules-result.md)。root接受代码交回：44文件身份a0cda7a88dc3f7d24ec4b8a49bba6ac77aeaf3f5b0bf889ea6ed9a3b88b447dd、17项Task增量；Release/真实DI副本/69文件候选及关联回归具备，F1长剑嵌套字段已修并在真实UID689非拒绝断言中通过。完整review第2轮、部署后核验和人测由根workflow下游继续，不能以Task代码交回宣告SC5/SC6/SC10人测已通过。

根workflow后续已执行：第2轮review approved，部署脚本exit0；正常退出后新鲜备份、PID40292 ready、四端口归属/正确PVF/空stderr核对，源44/包69及47业务表/三PVF保持再次读回。当前verification/manual_required，F实机材料与重登尚待用户，不提前提交或封存。

