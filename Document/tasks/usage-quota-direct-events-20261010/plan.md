---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Plan：次数统一与同步事件

## Planning Choice

一份详细计划指导整体迁移；共享工作区写worker串行，主控保留协议、部署与验收上下文。无需Task Units。

## Approach and Shared Decisions

- 新的统一UsageQuota领域服务与存储契约承载次数读取/CAS更新；可使用兼容适配原表，保留已有row/key和时间戳，禁止清数据。
- 领域门面保留额度/资格/双倍罐续期业务判断，把原始计数操作委托中央服务。消耗品Effect现有计划若已决定expected/new，应转为事实直接订阅，由中央服务检查CAS，而不是再次消费。
- 同步次数事实只注册runtime订阅。可用不可变typed payload包含character/account、配额类型、业务时刻、预计状态等纯数据；不携带session/service。Direct失败向上传播，不吞失败继续确认业务。
- 计数写入后可enqueue LiveOnly变化事件做提交后权威投影；不得让Reliable异步订阅扣次。需立即投影时afterCommit回调只可直读服务，不重入CommandBus/外层锁。不要为此大改全局事务pipeline。
- 保留现有领域重置时刻（镜像午夜、消耗品/权益现有dailyreset）；统一引擎并不等于改产品规则。
- 镜像加载后权威0279校准本地自增；打开选图/登录补发。首字段需要证据和保守映射，第二字段confirmed。协议外部实现不复用container移动。

## Work and Dependencies

### T-001 Direct事件及次数核心（RQ-001至004）

调整Abstractions/DomainEvents和EventService，迁移runtime发布者/测试fake；建立配额Abstractions、实现及兼容存储；迁移消耗品Effect/ItemUsage、Premium所有次数路径（包括QuestHelperPuppetDomainService的quest_helper_puppets_checked每日发放标记）、Mirror入场与奖励。以真实事务回滚与既有规则测试保护，不只测试实现镜像。实现worker可覆盖Server下所有必要模块与测试，不能写协议文档/任务控制/部署文件，不提交。

### T-002 客户端同步（RQ-005至006，依赖T-001）

审查现有021E、036F与0279客户端；新增镜像快照DTO/codec/投影，适当登录/选图/加载ACK后同步，避免本地increment重复。收口消费后通知经EventService，保留已有日刷新与登录顺序。编写codec/投影/事务提交与离线测试。只在T-001写worker结束后修改Server。

### T-003 审查和验证（全部RQ，依赖T-002）

独立reviewer按spec对全diff检查，重点遗漏写表、重复扣、postcommit重入、权益续期和兼容。串行dotnet build/test（Application/Protocol/Persistence/Host及相关Architecture）；保留已知Infrastructure基线失败证据。修复发现后重新审查/必要验证。

### T-004 部署（RQ-007，依赖T-003）

Release publish新artifact；复核当前真实PID/路径/监听/DB。停止旧服后复制DB/wal/shm并quick_check，不替换权威数据。沿用现有bootstrap/GM/频道配置及PVF。Start-Process Hidden启动，核对hash/version与六监听/health/bootstrap。失败恢复旧服进程而不自动覆盖DB。保存deployment.json与测试清单。

## Verification

将实际命令与结果放实施报告/最终证据。SC-1至5覆盖Spec AC-001至005；自动验证与ClientVerified严格区分。确认所有改动只在当前worktree以及授权artifact目录，最终review和freshverification通过后一个本地acceptedresult提交，主干不变。
