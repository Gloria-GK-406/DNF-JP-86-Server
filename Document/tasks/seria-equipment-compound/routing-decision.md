# Routing Decision

- TaskContract path `./TaskContract.md`
- Contract revision `3`
- 独立审查 [task-contract-review.md](task-contract-review.md)

## SDD Readiness

目标、PVF 驱动的配方、单底材协议边界、资产一致性和用户实机交接都已定义。传承权重采用有来源的候选策略并明示日服实服概率未知。

## Structural Selection

- Selected workflow `single-spec-workflow`
- 单个玩家完成一笔合成即可验收所有组成能力。不同长度误解或只扣一侧资产可逃过简单的正常路径测试。持久规格和独立结果审查能以较低开销守住这两处分支。
- 若用户另行要求恢复八字节入口或更改实际经济策略，重新判断范围和证据。

## Controller Selection

- Controller `sdd-lite`
- Profile source `focused-delivery`
- 结算依赖本仓已有原子效果与请求重放。规格固定关键行为后，可直接复用现有入口并验证，无需单列计划阶段。

## Execution Configuration

- Profile `focused-delivery` with `sdd-lite`，long-running `no`
- Spec baseline `818cbeed7391a9fb6079270a06766fecac2a5ff6`
- Gates `implementation: auto`、`review: auto`、`verification: auto`
- Bound methods `implementation: test-driven-development`
- Artifact path overrides none
