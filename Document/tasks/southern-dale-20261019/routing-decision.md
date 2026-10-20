# Routing Decision

- TaskContract: `Document/tasks/southern-dale-20261019/TaskContract.md`
- Contract revision: 1；同目录独立审查approved，绑定绝对路径及本revision。

## SDD Readiness

用户已授权南部与共享69去重，调研已明确普通迷宫/准入/补货/奖励方向。剩余客户端效果保留用户验收，不用其替代本地实现完成条件。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 一个可独立验收的南部闭环。直接实现容易遗漏共享69的启停组合及跨06:00领取、重复击杀发奖；Spec固定这些边界，Plan先确定奖励幂等与事务接入点，再改入口/投影，独立全量结果审核防止只完成可进图。少量文档与一次审核的成本低于晚期修正玩家重复资产的成本。
- Reroute conditions: PVF/客户端证明需要独立玩法协议，或必须改变已接受的门票、开放、奖励语义时停在安全边界重新对齐；独立活动新增不混入本Spec。

## Controller Selection

- Controller: sdd-standard
- Profile source: planned-delivery
- Rationale: 奖励应与权威击杀提交同时成功，补票应复用持久领取记录；先计划这两个接入点可防止先改协议后返工。Lite无计划不足以保护该顺序；没有外部不可逆写入或无恢复的数据丢失路径，不增加Governed控制。

## Execution Configuration

- Profile: planned-delivery with sdd-standard; long-running: no
- Spec baseline: 35d890662e987c85ff7e05786ae72c068f81c187
- Gates: spec: none; plan: none; implementation: auto; review: auto; verification: auto
- Bound methods: implementation: test-driven-development（业务可观察回归）；review: code/mixed；其余none
- Artifact path overrides: none；所有制品以本目录为根
- Workspace: E:/Project/DNF/DNF-JP-86-Server，current-checkout，task/southern-dale-20261019
- Baseline evidence: 起始HEAD如上，tracked与staged均干净；原有未跟踪MarriageHousing/Opus/旧outputs/工具cache不在本任务所有权中，保留不提交。
