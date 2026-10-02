---
kind: rks-review
verdict: approved
---

# Spec003 独立规格审查

- Target：`../spec.md`，`state: ready`，绑定 child TaskContract revision 1。
- Source baseline：`416f26c9919383b58b0bd0e7179a99734fe53b48`。
- Target SHA-256：`4D4349E0C10F6C90C4A1ACB13A082EEDB4896BDCC25BAA007B50891B74695169`；审查时已重新核对。
- Governing sources：根与 child `TaskContract.md` revision 1、工作区 `AGENTS.md`、本阶段 `sdd-execution.yaml`；采用 `reviewing-work/SKILL.md` 与 `references/spec.md` 的规格审查判据。
- Scope：结果与授权一致性、查询契约可实现性、验收是否必要且适度，以及当前 `spec-challenges.md` 四条挑战。审查者独立于规格生产者与挑战生成者。

## 结论与决定性证据

未发现进入实现前必须修正的规格缺陷。规格覆盖 child 的全服查询、运行事实和中文页面三项成功条件，贡献于根 SC-3/SC-6；既有管理员边界仅作为依赖和回归边界，未扩大为重做认证模块。查询、隔离验证和本地交付均在根授权范围内，未授权现服操作、真实玩家发放或根任务完成。

HTTP 契约明确输入、筛选语义、稳定顺序、总数、详情、字段白名单和失败反馈。AC-001..010 能区分库资料、当前会话及页面证据；不要求跨请求冻结名单或全局原子快照，也不预设容量指标。CommandBus/领域服务、窄持久化接缝和状态所有者要求来自仓库已有分层约束，没有强制额外框架、重复证明或文档形式。查询内部如何采样、处理失效以及实施分页仍有充分实现空间。

已检查实际源码：账号三币和角色身份/保存位置在 `CharacterDatabaseSchema.cs:258,289` 有来源；`SelectedSessionDomainService.cs:14-17` 提供当次会话副本；`SelectedSessionRuntimeDirectory.cs:17-20` 能核验精确发布；`CharacterPositionDomainService.cs:32-69` 提供当前位置、移动与精确清理。`ChannelEntryPolicy.cs:25-59` 已区分广告端口回退与本地监听顺序，规格保留其部署语义；`GameServerReadiness.cs:11-13` 提供真实进程就绪事实。既有 `GmAccessBoundary.cs:20-41` 支持权限、监听隔离、HTTPS 和 no-store。所需全服只读查询与已认证连接枚举可由原状态所有者扩展窄接缝，不需突破现有引用方向。

## 挑战裁决

| 挑战 | 裁决 | 证据与处理 |
|---|---|---|
| CH-1 发布标记与位置之间的窗口 | residual-risk | `SelectCharacterPhaseCommands.cs:243-244` 先绑定归属，`SelectCharacterLifecycleServices.cs:357-358` 先发布标记后 Place，窗口成立。RQ-005/006 与 AC-005/006 已要求已发布角色保持在线、无当前位置显示 unavailable，不能回填 saved。旧位置由替换前清理及 `ForgetExact` 控制（phase commands:89-106；lifecycle:150-154）。无需修改规格；实现验证覆盖发布后、Place 前的交错即可，不从 Find 按角色查询推导未证实的旧 owner 泄漏。 |
| CH-2 COALESCE 掩盖缺失姓名字节 | residual-risk | `CharacterEntryTables.cs:38-41,69-91` 确有兼容合成字节；schema:289 允许 NULL。RQ-004、HTTP 姓名投影及 AC-004 明确要求原始字节权威、missing/invalid 诊断和 ID 寻址。管理查询可独立读取原始列而不复用花名册兼容表达式。要求已充分，剩余是实现与隔离库判据。 |
| CH-3 在线集合变化导致 total/items 失配 | residual-risk | Snapshot 是当次复制，而 `OnlineStateRegistry.cs:74-100,121-147` 可随后断线或清理，变化真实可达。RQ-002、分页契约、AC-002 已要求同次查询按同一处理后筛选结果计算总数与页；RQ-005/006 和 AC-006 要求失效处理。全局非原子允许不同事实采样时刻，不允许先分页后过滤或改变同次分页名单。选择同一名单/失效策略是局部实现自由，不需要新增全局锁或更改规格。 |
| CH-4 复用入口写库或泄露快照 | residual-risk | `AccountProvisioning.cs:38-41` 的 GetOrCreate 会 Insert；`AccountModels.cs:11-15` 与 `OnlineUserModels.cs:31-37` 带密码或 Connection。RQ-001/003/008、响应白名单与 AC-001/003/008 已明确禁止这些路径和字段，并要求成功/空结果/失败均只读。使用 typed 窄投影及只读接缝即可控制；保持现有权限边界，无需扩张认证范围。 |

四条均为已被当前规格约束覆盖的低风险实现问题，未确认规格缺陷，也无进入实现所必需的证据缺口。实施者应在对应已有 AC 中落实这些场景，不额外创建规格阻塞或重复审查轮次。

## 验证边界

本次核对合同、规格、挑战与相关生产源码，未运行共享 .NET 构建/测试、现服或客户端。批准只适用于上述精确规格修订及 spec-review gate；不证明实现已通过、不代替独立完整结果审查或完成验证，也不批准根任务完成。推进和验收仍由主控决定。
