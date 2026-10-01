# TASK-005 保护券代码与回归证据

本轮为 Contract rev2 / TASK-005 rev1 的有界代码 worker 结果。HEAD 为 `6e60c4c67417ce06b88e9e99277fb1e4c0239726`，继承 TASK-004 未提交代码。未操作生产数据库、服务、部署或提交。

## 实际结论及范围

现有 `EquipmentReinforcementRuleService`、领域结算、实例 setter、handler 和 completed 投影已具备本轮所需业务行为，新增回归未复现产品缺陷，故产品代码 **零修改**。只补三个直接相关测试文件的保护分支；未改变费用、概率、wire、PVF 或实例字段映射。

| 当前源码核对 | 结论及边界 |
|---|---|
| `ProtectionAsync` / PVF source | 普通 `protect equipment` 返回归零；超级 `new protect equipment 10` 返回保留 10；真实档案的 10004511 / 10007841 增幅券不识别为普通强化保护 |
| `SelectAutomaticProtectionAsync` / `TryReinforce` | 原处罚为破坏才选用/生效；超级仅在当前等级大于保留级时适用，共存优先超级；成功、保级和普通降级不扣券 |
| domain automatic scan | 只扫描本角色 QuickSlot / Material；实例 `ExpireTime > 0 && <= now` 排除，显式过期/错券/缺失槽拒绝；资格判断与实例期限不代替客户端对历史 PVF 日期的可用性判断 |
| receipt / completed | 实际扣券位置进入 `ProtectionSlot` 和最终 `Changes`；提交成功后按 Changes 编码余量或空槽，装备等级写 Attribute 低 5 位，保留高 3 位及独立附魔次数 |
| ACK optional ticket | `protocol/game/server-to-client/CMD-0x0050.md` 明确将“可选券是否应回显请求槽位”列为待真实帧/GUI确认；保留原 request 回显，不将 receipt 的自动消费槽擅自改进未证 wire 字段。自动用券库存正确性由 NOTI 和重读验证 |
| replay | 保留原同连接/同序列仅补 ACK 的契约，保护结算不会再次扣券/材料/金币；可靠 TCP 下尚无证据支持扩展重放库存投影 |

## 回归价值与实际覆盖

受保护结果是装备和资源的最终可持久化状态及已提交通知。现实回归风险是把普通/超级分支混用、成功误扣、过期/增幅券误用，或自动选券只写库而漏发余量/清空，用户可能损失装备或资源。已有测试只覆盖单个合成超级券的领域分支和无保护 handler 成功，不能验证这些组合。新增判据直接读取 SQLite 的 UID / `item_core` / 余额，并经实际 CommandBus、通知总线和注册 encoder 检查最终字节；对内部依赖重构保持容忍。

| 测试文件 | 新增覆盖 |
|---|---|
| `EquipmentReinforcementDomainTests.cs` | 普通回0/超级回10、QuickSlot/Material自动发现及显式券槽；跨空间的共存超级优先；全core原始字节除Attribute低5位外不变、UID不变、重读一致；两种券成功/保级/降级不扣；实例期限0/未来/到期/过期；增幅券拒绝/自动略过；+10超级不适用并不替代普通；非拥有、缺失、非携带及与目标/材料冲突的券槽不扣资产 |
| `EquipmentReinforcementHandlerTests.cs` | 实际CommandBus+SQLite，提交后才能发ACK/NOTI；两券各1/2张时余量/清空byte及主库存snapshot一致；实际账号虚拟无色槽358的扣除/通知；两券保护重放只结算一次；装备core SQLite flush失败整体回滚券/材料/金币/装备、无成功通知、原sequence可重试 |
| `PvfEquipmentReinforcementDefinitionSourceTests.cs` | 既有真实7804/10007840 action断言保留，增加真实10004511/10007841普通保护识别为null |

所有新增行为测试在正确现有实现上通过，属于补回归证据，**不宣称 TDD RED/GREEN 或产品修复**。第二轮新增跨角色测试初跑 83 通过 / 1 失败，是 `ShopFixture.Select` 重复注册已存在角色容器导致的 fixture 错误；按照 fixture 的 Clear → Select 生命周期修正测试设置，产品代码未动。原失败 TRX 保留为 `task005-protection-application-final.trx`，不作产品行为 RED。

## 命令与结果

每次 PVF 测试进程显式设置 `$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'`。测试结果目录为 `Document/tasks/equipment-instance-refactor/verification/task005-tests`。

| 命令 | exit / 结果 | 证据 |
|---|---|---|
| `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter 'FullyQualifiedName~EquipmentReinforcement' --logger 'trx;LogFileName=task005-protection-application-green.trx' --results-directory Document/tasks/equipment-instance-refactor/verification/task005-tests` | 0；84 passed / 0 failed / 0 skipped | `task005-tests/task005-protection-application-green.trx`，最终测试源码 |
| `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --filter 'FullyQualifiedName~PvfEquipmentReinforcementDefinitionSourceTests' --logger 'trx;LogFileName=task005-protection-pvf.trx' --results-directory Document/tasks/equipment-instance-refactor/verification/task005-tests` | 0；12 passed / 0 failed / 0 skipped，真实A21档案测试已执行 | `task005-tests/task005-protection-pvf.trx` |
| `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-build --filter 'FullyQualifiedName~ReinforceItemWireTests' --logger 'trx;LogFileName=task005-protection-protocol.trx' --results-directory Document/tasks/equipment-instance-refactor/verification/task005-tests` | 0；8 passed / 0 failed / 0 skipped；使用已有Debug程序集，Protocol源码/测试本轮未改，后续fresh DLL身份由controller核对 | `task005-tests/task005-protection-protocol.trx` |
| `dotnet build Server/86JP.Server.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false` | 0；0 warnings / 0 errors，最终源码 | `task005-tests/task005-release-build.log` |
| `git diff --check -- Server/tests/Dfo.Server.Application.Tests/Inventory/EquipmentReinforcementDomainTests.cs Server/tests/Dfo.Server.Application.Tests/Inventory/EquipmentReinforcementHandlerTests.cs Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfEquipmentReinforcementDefinitionSourceTests.cs` | 0；无空白错误 | 本轮自检 |

Release 输出由 `Tee-Object -FilePath .../task005-release-build.log` 留存，并 `exit $LASTEXITCODE` 返回真实dotnet状态。此前范围较小的首轮 77/77 TRX `task005-protection-application.trx` 保留，但最终判据用上述84项。

## 源码身份与后续门

| 本轮测试文件 | SHA-256 |
|---|---|
| `Server/tests/Dfo.Server.Application.Tests/Inventory/EquipmentReinforcementDomainTests.cs` | `5620B1F69F4FBF10B025F3BD5C63385B1901EBA7A2A2F3E9EAD5C8E1047D33E0` |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/EquipmentReinforcementHandlerTests.cs` | `679D01FA7E4C26F20A4A5340C13AF189E96B68C3FBEF7169A683BD489EF81B90` |
| `Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfEquipmentReinforcementDefinitionSourceTests.cs` | `BEBB1ED0B035FA193278F8CBD48E715D6383A9A8F62B425BA7A234CA254CC415` |

这三文件包含先前已存在的TASK-004改动；本轮归属是其中新增保护测试、直接相关fixture参数和两条PVF断言，不能把整个相对HEAD diff算成本轮新改动。controller继续组织改变测试的Application/Infrastructure两程序集全量、fresh完整mixed review、部署身份和最后用户保护/强化/正常退出重登验收。本报告仅建立CurrentImplementation自动证据，不标ClientVerified，不改变任务终态。
