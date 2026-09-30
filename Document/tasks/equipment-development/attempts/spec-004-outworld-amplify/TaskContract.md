---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-4
depends_on: []
---

# TaskContract：异界气息与异次元属性

## Context

### Source Request

用户授权在 `task/solo-play-roadmap` 分支按一个内聚功能一个 Spec 完成五项装备改造，要求 70 服务端、上游、A21 客户端及 PVF 探查、设计、实现，快速验证并逐项提交；排除附魔师功能。本子项为根任务 SC-4。

### Current Situation

接受基线 `4dbd73b4` 已持久化并投影装备的 `AmplifyType/AmplifyValue`，普通强化对非零 `AmplifyType` 明确拒绝。`0x00CC`/`0x00CD` 有 A21 静态构包及响应 handler，尚无实帧/GUI 验收；现有协议文档对动态尾部标记为部分布局。

## Goal

使角色拥有的异界气息装备可按客户端支持的材料路径净化，并使异次元属性选择/扭转可按真实 PVF 规则处理；状态、材料和装备身份原子持久，客户端结果可见，增幅准入边界清楚。

## Scope

### In

- 对照 70、上游、A21 静态与真实 PVF，裁定异界气息状态、净化/清除、属性选择/扭转及增幅准入。
- 实现闭合路径的所有权、状态、材料、锁定、过期等校验及原子结算；保留 UID 和无关属性。
- 接入客户端已证实的入口、结果与权威刷新，补自动验证和 Human Test List。

### Out

- 实际增幅升级概率与等级结算（仅阐明并测试其准入关系）；锻造、附魔师路径；推送/合并/部署和未执行的 `ClientVerified` 声明。

## Constraints

- 70 与上游仅为参照，A21/PVF 是本服协议和内容边界。未闭合的动态尾部或材料身份不得猜测性扣物。
- 净化/扭转的装备、材料变更在一个事务；失败、重试、持久化失败不能半结算。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 异界气息与异次元属性入口、状态及材料规则可追溯；歧义显式列出。 | 70/上游/A21/PVF 对照及 `protocol/` 人工记录。 |
| SC-2 | 可用路径完成净化/属性选择或扭转，状态与材料原子持久，拒绝与回滚不改资产。 | 真实 PVF 样本、确定性领域/SQLite 测试。 |
| SC-3 | 客户端可获得相符响应、材料与装备刷新；普通强化和未来增幅入口的准入状态一致。 | 字节/handler 测试、重读测试及准入断言。 |
| SC-4 | 用户能对固定样本做净化、扭转、拒绝与重登验收。 | Markdown Human Test List，分明自动与未实测。 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根契约记录五项逐项实现；用户补充“继续完成全部功能……快速开发，快速验证……提交后继续工作。这次不包括附魔师相关功能。” | 当前分支本地探查、设计、实现、验证和一项接受提交，不包括外部发布。 |

## Assumptions

- 本轮自动接受与用户后续集中 A21 实机验收分离；未取得实机帧时如实标记静态证据。
