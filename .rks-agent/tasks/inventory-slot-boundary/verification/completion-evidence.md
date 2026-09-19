---
kind: rks-verification
decision: pass
---

# Completion verification: 库存槽位边界单源化

## Claim

TaskContract revision 1 的 SC-1～SC-8 已由当前工作树结果满足；结果可形成一个本地验收提交。
本次授权不包含合并、推送、部署或清理工作区。

## Success-condition evidence

| Conditions | Decisive evidence |
|---|---|
| SC-1, SC-2 | 六条 decoder/handler/command 回归与 `InventorySlotBoundaryArchitectureTests` 证明请求先形成 `GameSlot`、ICommand 前转换为 `BusinessSlot`，命令、领域接口及回执不携带协议坐标。 |
| SC-3 | 六条 codec 样本与响应/通知测试通过；新增 `PersonalCargo/-1` 字节测试证明 repair-all 保留 wire space；输出统一经映射或只读投影服务形成 `GameSlot`。 |
| SC-4 | `InventorySlotMappingRuleServiceTests` 覆盖 Mapping Contract 每个区段首尾、双向往返，以及未知、保留、虚拟、负数和越界地址的携源异常。 |
| SC-5 | Move、QuickSlot、跨容器、穿戴/卸下及分解组合测试通过；生产扫描确认协议绝对偏移只存在于唯一 mapper，PVF 原始区段只存在于 Infrastructure 私有读取模型。 |
| SC-6 | Persistence 355 项通过；新增业务绑定往返覆盖 `Main/70`、`Equipment/13`、`PersonalCargo/7`，数据库格式无破坏性迁移。 |
| SC-7 | `InventorySlotMappingFailureIntegrationTests` 证明异常被记录并隔离到失败连接，后续独立连接仍可服务；六条业务 handler 不把映射异常伪装成普通拒绝。 |
| SC-8 | 独立 complete-code-change 复审 verdict 为 `approved`；Fresh Release build 0 warnings/0 errors；设置真实 A21 PVF 后串行完整测试 3519 passed、0 failed、2 skipped。 |

## Structural checks

- `git diff --check`：通过。
- Application/Abstractions 中不存在 `ItemPlacement.SlotStart/SlotEnd`；剩余同名字段仅是
  Infrastructure 的私有 PVF 原始 DTO，并在返回内容模型前转换。
- `IBusinessInventorySlotDomainService` 不暴露 `GameSlot`；反向转换位于 Protocol/投影或
  backing container/persistence 适配边界。
- 映射 API 无 `TryXxx`、默认值或业务拒绝降级入口。

## Diagnostic note

首次并行运行解决方案测试时未向各测试进程显式提供 `PVF_ARCHIVE_PATH`，Protocol 的真实内容
用例因此报告缺少 `Script.pvf`，实时 Socket 用例也随宿主断开。显式设置
`E:\Project\DNF\DNFClient-A21-debug\Script.pvf` 并按项目串行复跑后，完整套件 3519/3519
通过；两个 skip 是“提供 PVF 时不执行缺失归档负例”的条件测试，不属于功能缺口。
