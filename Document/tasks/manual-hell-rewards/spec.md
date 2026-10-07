---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 主动深渊奖励

## Why and Outcome

玩家击杀深渊演员后得到真实地面奖励，可按普通拾取流程领取。不会因重复请求、查询或回到房间重新抽奖。

## Scope

主动深渊演员独立掉落及 PVF 支持的整组奖励，复用 Cell 和地面物品体系。随机深渊和客户端修改不在此交付内。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 创建 Cell 时由奖励领域服务冻结所有奖励；领域服务取玩家加成事实，规则服务读当前 PVF。 |
| RQ-002 | 每个演员自身奖励在自身死亡释放；按 GroupIndex 合并重复组，整组只在末怪死亡后结算一次。末怪资格不符或空奖励亦标记完成。 |
| RQ-003 | 死亡、组状态和地面物品原子提交；保留原有所有者、序号和重复死亡防护。 |
| RQ-004 | 装备奖励依据当前 PVF 奖励次数、概率、稀有度、等级范围及真实物品池；未知官方参数明确区分。 |
| RQ-005 | 普通地下城和拾取行为不回归；部署同时包含缺票拒绝修复，保留回退输入。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001, RQ-004 | 规则测试覆盖解析、边界与真实资源；反复访问不会重新抽取。 |
| AC-002 | RQ-002, RQ-003 | 不同死亡顺序、独立组、重复 ID、重复死亡、空奖励、不合资格末怪及事务回退验证。 |
| AC-003 | RQ-003, RQ-005 | 现有死亡投影与普通掉落、拾取回归通过；无新客户端协议或补丁。 |
| AC-004 | RQ-005 | 发布构建、DB 备份、进程路径/程序集身份、监听与 HTTP 健康核验；用户实机显示与拾取另行验收，不以自动测试替代。 |

## Evidence

70 与上游资料见 `../../abyss-research-20261007/70-reference.md`、`../../abyss-research-20261007/upstream.md`；实现结论单列 CurrentImplementation，不能据此声明 ClientVerified 或官方爆率。
