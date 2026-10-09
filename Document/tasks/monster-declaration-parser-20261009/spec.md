---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification：基于 token 的怪物声明解析与房间生成接线

## Why and Outcome

消费方取得统一声明，而不是猜固定 token 宽度。资源解析保存字段和来源；房间规则决定生成和运行资格；协议只投影既定身份。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 对实际 MAP 全量盘点，区分普通、条件、APC、对象子模板和附属字段，并记录参考实现缺陷与未知语义。 |
| RQ-002 | 使用 typed token 游标解析变长记录，支持放置方式、dummy、NPC 绑定、精英特征尾部；保留坐标、等级模式、阵营、掉落声明及记录顺序。 |
| RQ-003 | 特殊对象子模板保留完整父对象与子项序号，不因空对象、item/trap/quest 子项过滤而重编号；条件信息不能混入无条件实体。 |
| RQ-004 | 输出统一声明供外部读取，现有房间创建使用该结果；dummy 不领取普通敌人清房、经验、奖励资格；有效记录仍保持客户端 MAP 索引。 |
| RQ-005 | 特殊对象模板以明确来源投影 START_MAP；已确认的场景波次不能被普通 Boss 死亡抹掉；普通/APC/深渊现有身份与奖励流程保持。 |
| RQ-006 | 未识别格式与损坏记录报告资源、section、记录和 token 位置，不能静默截断、猜普通类别或产生空房。 |
| RQ-007 | 构建、聚焦回归、独立审查后部署，保留旧 runtime、配置与数据库，给出运行身份及实机待验收范围。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001,RQ-002,RQ-006 | 当前 PVF 10,293 MAP 均扫描；已声明模式有解析回归；未知语义保留并列明而不升级为已实现行为。 |
| AC-002 | RQ-002,RQ-004 | 22036 五条 monster 均可解析，61150 与离屏 dummy64639 不混淆；普通/精英/NPC 变体后接记录仍正确。 |
| AC-003 | RQ-003,RQ-005 | 21314 的敌对 Boss APC 和三十子模板可取得；模板父序号0..8及各子项原序号对应；控制对象与item对象不影响序号。 |
| AC-004 | RQ-003,RQ-004,RQ-005 | 条件声明不会默认生成；dummy不阻塞或获奖；模板协议标志和后续死亡账本一致，Boss/APC与普通房间回归通过。 |
| AC-005 | RQ-006 | 截断、未知标记、错误类型、非法数量的输入明确失败且定位到实际记录。 |
| AC-006 | RQ-007 | 当前产物测试通过、审查无阻塞问题，HTTP/bootstrap ready且目标监听属于新进程。 |

## Success Evidence

调查记录写入 `survey.md`；测试与部署写入 `verification/completion-evidence.md`。ClientStatic / 70Reference / UpstreamReference / CurrentImplementation / 实机待验证分开。

## Boundary

条件脚本执行器及特殊地下城专属机制不在本次实现；其声明保留在统一结果中。OBJ int data 不能仅凭长度一律认定为怪物脚本，模板清房策略仅使用已查明的生成类型。
