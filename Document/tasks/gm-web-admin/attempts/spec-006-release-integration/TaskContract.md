---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-1
  - SC-6
depends_on: []
---

# TaskContract: GM 后台构建发布与验收集成

## Context

### Source Request

根授权“可以，你来写任务契约实施吧。不过最好是multispec，按搭建项目到设计各个模块一步一步分spec完成。”，要求后台加入启动并最终验收。

### Current Situation

aa5b6636已接受查询、权限审计、官方邮件和点券模块。独立开关/同Host已通过矩阵验证，但标准CI未准备前端，候选打包不能保证干净源码包含GM资源；最终统一验收清单尚缺。

## Goal

标准构建和候选发布包含可访问的GM页面，运行只需已发布Host；使用者按完整说明和逐项清单可接手验收。

## Scope

### In

- CI前端依赖、测试、构建与现有服务端验证顺序接入。
- 本地/CI候选包准备和校验GM资源、默认配置、独立HTTP启动及环境数据隔离。
- 完整运行/构建说明、统一最终验收表、既有核心流程回归。

### Out

- 新业务模块、改动账号资产/管理员授权逻辑、性能优化；真实现服切换、初始化真实管理员、真实玩家发放、远程CI执行、push/PR/merge或外部Release。

## Constraints

- 保留已有默认关闭、同Host和独立监听行为；候选包不包含玩家数据库、PVF、日志、部署秘密或Node运行时依赖。
- 只在隔离临时目录/端口验证，保持已接受模块和source main；Windows实测与Linux/云端未执行明确区分。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 标准CI和本地候选打包从源码准备后台资源；缺资源/构建失败明确失败，包中GM入口与引用资源可访问 | CI顺序检查、候选包资源核对与独立HTTP启动 |
| SC-2 | 候选包默认配置及内容边界正确，已接受后台和核心旧流程无回归 | 包内容/启动检查、前后端及服务端完整回归 |
| SC-3 | 完整构建运行说明、逐项最终验收表可接手，证据等级及待验边界明确 | 文档与实际行为独立审查 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | “可以，你来写任务契约实施吧。不过最好是multispec，按搭建项目到设计各个模块一步一步分spec完成。”（根授权） | 当前集成实施、隔离候选验证、独立审查和本地验收提交 |

## Assumptions

- 本机Windows验证候选包；Linux CI按既有矩阵接入但不代表远程已执行。真实A21功能由最终验收表交用户验证。
