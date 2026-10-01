---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification：完整装备事实与显式规则/解析失败

## Why and Outcome

合法首饰没有耐久字段应仍能提供等级、品级、稀有度、类型；装备事实不能被强化资格绑架。非法内容必须可定位，不返回业务 UnsupportedContent 掩盖内部错误。

## Scope

迁移当前普通强化、异次元、锻造和失败结果消费者，保留已接受基线的状态与结算行为。穿戴/修理/出售的独立内容源不扩展；增幅升级交易、晶体 x0 后续单独处理。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 通用 EquipmentDefinition 与 IEquipmentDefinitionSource 只表达基础内容事实和原始禁止声明；ushort? 最大耐久，无 CanReinforce/费用索引 |
| RQ-002 | 合法无耐久为 null，零及 ushort 范围保留；非法/重复/越界/必需缺失有带 item/path/field 的异常，不负缓存为正常未找到 |
| RQ-003 | 一个统一升级规则服务提供普通强化报价、异次元决策、锻造决策与增幅资格查询；实例和领域保持状态/事务职责 |
| RQ-004 | 费用、等级/稀有度/称号限制、原始禁止内容、满耐久、保护券与失败材料保持；合法首饰可赋予 |
| RQ-005 | 解析异常经过现有命令 Error 日志与事务回滚；启动实际依赖的基本内容无效时不能 ready；不强制预解析全部装备 |
| RQ-006 | 当前方案自动验证、可部署产物和用户最终验收分开报告；最终清单覆盖首饰、武器防具、失败不扣与重登 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001,RQ-003 | 生产路径无旧基本信息类型和预计算能力/费用索引；统一服务正确接线，四条消费者均使用通用事实 |
| AC-002 | RQ-002,RQ-004 | 真实 PVF 100310498 手镯为完整信息/无耐久；武器正常耐久；首饰赋予成功与原资格拒绝矩阵通过 |
| AC-003 | RQ-002 | 构造非法内容对各字段、重复声明、ushort 边界抛异常且原因具体；重复读取非法条目仍抛异常 |
| AC-004 | RQ-004 | 原普通强化、保护券、失败材料、锻造、协议与持久化回归通过；有耐久必须满值，无耐久跳过耐久比较 |
| AC-005 | RQ-005 | 真实命令路径解析异常记录 Error、无成功回包/资产变化；启动实际准备环节失败不进入 ready；合法未找到仍是明确业务拒绝 |
| AC-006 | RQ-006 | 独立审查通过，当前验证记录明确人工待验；用户按清单最后确认后才关闭根任务 |

## Success Evidence

关联 TaskContract SC-1..SC-7；具体自动输出、构建与部署准备记录保存在主仓库 artifacts/equipment-definition-rules-20261001，人工项目在 Human-Test-List.md。
