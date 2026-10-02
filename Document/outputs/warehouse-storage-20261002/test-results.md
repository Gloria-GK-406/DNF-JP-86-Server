# 仓库全量回归结果

2026-10-02；.NET SDK 10.0.302，真实A21 PVF已启用。最终各程序集结果：5429通过、0失败、2跳过。

| 测试程序集 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests.dll | 80 | 0 | 0 |
| Dfo.Server.Application.Tests.dll | 2690 | 0 | 0 |
| Dfo.Server.Host.Tests.dll | 118 | 0 | 0 |
| Dfo.Server.HttpApi.Tests.dll | 50 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests.dll | 766 | 0 | 2 |
| Dfo.Server.Networking.Tests.dll | 86 | 0 | 0 |
| Dfo.Server.Persistence.Tests.dll | 397 | 0 | 0 |
| Dfo.Server.Protocol.Tests.dll | 1242 | 0 | 0 |

证据：全解决方案复跑保存在 `artifacts/warehouse-replay-final-tests-20261002/`；该轮仅两项注册清单断言未包含新增Warehouse入口。同步明确清单后，Application全部2690项重新通过，结果为 `artifacts/warehouse-registration-final-tests-20261002/`。其他程序集的生产代码与测试未再变更，不重复运行。

跳过项（设置真实PVF后跳过的缺失档案场景，非新增仓库测试）：

- `Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
- `Dfo.Server.Infrastructure.Tests.Content.PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`

仓库新增范围不包含真实客户端验收，见[设计与Human Test List](../../WarehouseStorageDesign-20261002.md)。
