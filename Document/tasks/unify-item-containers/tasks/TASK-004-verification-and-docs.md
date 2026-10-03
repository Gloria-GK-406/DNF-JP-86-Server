# TASK-004 — 完整回归与交付说明

## Task Unit Contract

- Revision: 1
- Outcome: T-004；RQ-001..007、AC-001..007。核实全部入口已收敛，补真实 PVF 金库存取证据、模型与迁移说明、集中实机清单。
- Dependencies: TASK-001..003 的统一 ContainerOwner、账号 SharedMaterial/240、0..10 固定槽、完整入口与协议投影。TASK-003 交接并释放生产写入及 dotnet 后开始执行。
- Owned boundary: 补充有实际风险价值的回归测试及发现缺陷的修复；Document、相关 protocol 实现记录；检查与证据。不部署、不修改线上库、不重启客户端或服务、不提交（最终提交由控制器完成）。
- Checks: 同一真实 A21 PVF 下顺序运行完整 Application、Persistence、Protocol、Infrastructure、Networking 与 Host 测试；构建与 diff 检查；旧类型/账号列读写审计。保留失败原始证据并诊断，不以删断言或跳过代替修复。

## Detailed Plan

1. 检查 TASK-003 result、实际 diff 和自测结果；核对完整数量、账号归属、目的空间准入、迁移幂等/回滚、旧余额停止读写及消费来源范围。
2. 加入真实 PVF 集成覆盖：11 种材料逐个验证默认固定槽、实际 ItemKind，向个人/账号金库拆分、再次合并及取回，总量不变；复用既有边界测试验证错误槽/超限/越权/故障回滚。不再使用合成 kind 代替真实内容证据。
3. 设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf`，依次运行所列项目，日志及 TRX 保存在 `artifacts/unify-item-containers/`。失败先局部复现，再对受影响项目做必要完整回归。
4. 将 `Document/AccountMaterialBag.md` 更新为现行普通容器模型与迁移说明，保留 2026-09-20 验收历史；补共享材料 human test list；更新 0013/000D/000E 的 CurrentImplementation 边界，记录用户已经确认的新手教程验收，不扩大证据范围。
5. 输出本单元结果及验证索引。完整结果再进入已配置的独立评审与完成验证，不能用本单元自检替代。

## Result — 2026-10-03

- **Changed:** 增加 `SharedMaterialPvfTests`：真实 PVF 下11种材料分别与两类金库拆分、合并、全部取回；实际 kind、默认固定槽、数量及空槽全部断言。模型、迁移说明和 human test list 已完成，0013/000D/000E 与 catalog 更新 CurrentImplementation，教程页按用户原话收口既有客户端效果。
- **Why:** T-002 中间结果的统一 kind=3 假设被实际内容推翻，T-003 已改为晶体11/灵魂3；本单元直接使用真实内容源，避免合成测试重复该错误。原始探针在 `artifacts/shared-material-pvf/`，其中金库拒绝列是修复前行为，不作为当前结果。
- **Checks:** 同一 A21 PVF（SHA256 `3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06`）依次完整运行 Application 2805、Persistence 406、Protocol 1246、Infrastructure 766、Networking 86、Host 118 通过，零失败。Infrastructure 的两个“缺 PVF”用例因当前提供 PVF 主动跳过，随后移除该进程环境变量定向补跑，2/2 通过。独立用例总数为 2805+406+1246+768+86+118=5429；真实PVF矩阵已包含在Application中，不重复相加。
- **Checks:** 首轮 Persistence 2失败：新标记表漏计与前任务 `tutorial_completed` 列未回填黄金结构；修正声明断言并保留严格整表检查，完整重跑406通过。首轮 Host 3失败：GM测试内容伪造kind10，以及真实邮件领取成功后仍查旧余额列；修正为真实kind11、查询普通账号物品槽，并断言旧列仍0，完整重跑118通过。没有为这些失败更改生产规则。
- **Checks:** `git diff --check` 通过；旧接口/状态/结果分叉生产引用为0，11个旧材料列仅出现在 Schema 与迁移。143个本次改变的源/测试路径哈希清单确认审查期间未变。命令、原失败、最终TRX与审计JSON保存在 `artifacts/unify-item-containers/`；完整路径与验收映射交由最终 verification 统一引用。
- **Remaining:** 实现单元无未完成项；控制器继续独立完整结果评审与新鲜完成核验后一次本地提交。部署、线上迁移和新客户端验收按契约留出。
