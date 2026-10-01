---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan：装备内容与规则边界迁移

## Planning Choice

一个完整详细计划能维护所有依赖，采用 direct，不建立 Task Unit registry。内容源可独立实施，统一规则与消费者由主代理完成；所有共享输出构建串行，末尾由独立审查者核对整个差量。

## Approach and Shared Decisions

- 新 EquipmentDefinition：MinimumLevel/Grade/Rarity、原始归一化 EquipmentType 字符串、ushort? MaxDurability、原始 ImpossibleContents 声明。类型到费用索引由规则映射；字符串保留合法但当前不支持的类型供规则拒绝，而不是解析器猜测其非法。
- 新 IEquipmentDefinitionSource 独立于强化表/保护券源；强化源移除 GetEquipmentAsync 与旧 record，旧装备读取代码彻底关闭。
- PvfEquipmentDefinitionSource 负责装备索引与字段严格解析。索引中没有模板可返回 null；索引/已有模板字段非法抛 InvalidDataException，消息含路径/模板/字段。合法 absence 耐久 null 与非法类型不同。禁止声明非字符串视为解析错误。
- IEquipmentUpgradeRuleService：QuoteReinforcementAsync、DecideOutworldAsync、DecideForgingAsync、CheckAmplificationAsync。一个 partial 实现类按功能分文件，保留可读性与一个 DI 服务身份；增幅查询只判断资格，不伪装为升级交易。
- 强化失败结果服务继续独立，但改读通用信息；费用与破坏算法不修改。所有测试夹具分离通用事实与强化表源。
- 启动准备只验证实际必需索引，不预解析全部模板；具体装备合法性首次使用时严格读取。已有命令日志/事务管线承担 Error 与回滚。

## Work and Dependencies

| Work | Covers | Executable work |
|---|---|---|
| T-001 | RQ-001,RQ-002 | 提取基本内容源与契约，迁移 Infrastructure 接线及基本信息/真实 PVF/非法内容用例 |
| T-002 | RQ-003,RQ-004 | 合并三个规则身份，迁移领域调用与失败结果/费用类型映射，更新夹具与规则矩阵 |
| T-003 | RQ-005 | 验证生产命令解析异常 Error/rollback/无 ACK；按实际 startup 内容准备新增索引验证及失败检查 |
| T-004 | RQ-006 | 串行构建、相关回归和真实内容验证；独立完整审查，修复发现，再生成最终人工清单和可部署产物 |

T-002 使用 T-001 的新契约，实施可在互不写同一文件时进行；T-003/T-004 必须在完整迁移后运行。用户此前基线提交 38f71bd2，工作区 clean，现有服务不切换。

## Verification

内容缺失/零/最大/错误/重复、未知模板与合法不支持类型；三类首饰与武器/防具真实 PVF；业务资格/满耐久/保护券/失败回报；SQLite 异常回滚与 Error 日志；协议编码/注册、启动失败及 ready 边界。已有基线失败须单独报告，不通过修改无关业务使其消失。

## Delivery Boundary

本任务实现后仍等待用户按 Human-Test-List 最后验收。增幅升级和晶体 x0 不在本次代码范围，不关闭这两个缺口；实际部署需正常退出游戏后切换，保留生产数据库与三个 PVF。
