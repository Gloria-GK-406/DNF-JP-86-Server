---
kind: rks-plan
spec: ./spec.md
state: ready
mode: direct
---

# 实施计划

单一直接计划维护接口与迁移矩阵。串行执行写代码的工作者，主代理负责集成与部署；只读调查/审查可并行。

1. T-001 状态与资格：Quest统一facts/evaluation及三个查询口；迁移登录、实时、提交Effective、材料需求；可接规则搬入Quest，World/Cycle/Replay保留薄适配。先补共享0旧副本、多通道、查询只读回归。覆盖RQ-001/002/003。
2. T-002 计划与执行：普通Finish改只读Prepare＋显式cost/reward；Application/Settlement编排预演、完整Apply、事件。镜像Prepare/Commit任务记录分离，外部权益Direct事件由编排执行；完成券任务变化生成迁入Quest；接取放弃审计迁移重复路由。覆盖RQ-004/005。
3. T-003 集成：编译及既有普通/镜像/完成券/登录/Cycle/Replay测试；完整扫描外部asset写与重复条件。独立review针对SC-1..5，修复后重验。覆盖RQ-006。
4. T-004 接受与部署：保存实现/审查/验证证据，单次本地结果提交；新artifact发布，复用权威DB/PVF配置，校验旧PID路径哈希，停服备份SQLite，切换检查监听/health/bootstrap/版本。启动失败回旧runtime，不自动恢复数据库。列出实机测试。

## 接口与执行边界

- 统一只读查询接口与纯有效状态规则由T-001实现，其他服务消费同一结果；外部资产读取可经只读facts collector，不能在查询中隐式写进度。
- 普通完成保留现有Effect模型和预演，Prepare返回计划，不调用apply或完成事件；结算入口保留CMD事务与EffectOperationContext。
- 镜像不写普通30槽log；本局任务记录／权益仍同一事务，重复领奖不再执行奖品变动。
- 所有写入在当前工作区，保留既有调查目录。检查/构建串行，环境PVF_ARCHIVE_PATH指向权威档案。
