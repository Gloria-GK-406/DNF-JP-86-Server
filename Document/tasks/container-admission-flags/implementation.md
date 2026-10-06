# Implementation — round 1

2026-10-03；owned diff = 基准 `c2d647e2e46395d4df542fa565d742be2c81470a` 之后当前工作区的准入重构及本目录工件。

- `ItemTraits`：8 个粗特性，宠物装备/消耗品、副职业材料、时装徽章使用组合；`InventoryItemCoreState.Traits` 只读派生。`ItemPlacement.ItemId` 保留模板身份，使共享标志不依赖目标位置。
- `ItemContainerAdmission`：单一接受表和非空子集谓词；业务容器公开 `AcceptedTraits`。槽位、仓库及全部快捷栏候选路径复用此表；未知 kind 不再被排除式快捷栏规则放行。
- 业务容器新写入优先读取模板 placement 的特性；已有堆仅递减且其余 core 完全一致不视作准入，复用现有关闭槽取出条件。模板缺失/无提供者时保留既有 kind 投影回退；core 不是新的可写权限字段。共享固定槽仍在底层核验模板、kind、数量、所有权与范围。
- 保留世界分类、UID、交易与锁定、容量、物理槽、事务和协议编码；不新增数据库字段或迁移。不运行生产服务，不操作现有数据库。

## Checks and diagnosis

- TDD 价值：未知 kind 的快捷栏误入有直接业务结果，旧测试只覆盖已知五类排除，故先加行为测试。`dotnet test Server/tests/Dfo.Server.Application.Tests --no-restore --filter FullyQualifiedName~A_quick_slot_refuses_unclassified_items --logger "trx;LogFileName=red.trx" --results-directory artifacts/container-admission-flags`：基准代码上 3 个断言按预期失败（kind 0/12/255 被允许），exit 1。首批迁移后 focused 72/72 通过，含上述 3 项及真实 PVF 共享材料拆分/归还。
- 首次全量应用回归 2777 通过/56 失败；保存 `application.trx`。追溯确认新增写入检查也拦截了已有堆的纯消耗，导致多个故意简化模板的行为测试提前退出；最小修正为只对准入而非递减重新筛选。针对失败链路回归 236/238，剩余两项来自 InventorySide.MoveAsync 缺少模板 provider，错误回退到历史 core kind。为夹具注入其已有模板规则，保持 PVF 权威测试。
- 时装/宠物测试过去构造 CarriedEquipment 目标并期望允许，与当前 PVF adapter 返回独立 Avatar/CreatureInventory 范围不符；更新范围及对应主背包拒绝断言，没有改变当前真实模板的玩法。
- 新增核心类别/子集契约检查；真实 PVF 药品与礼盒同为 Consumable、共享材料不同位置 Traits 不变。
- 当前完整回归及最终验证见后续 `verification/completion-evidence.md`；原始命令结果统一保留 `artifacts/container-admission-flags/`。这些是 CurrentImplementation 自动验证，不是 ClientVerified。

## Review remediation — round 2

- Persistence 初轮 404/406：仅反射 golden 未包含新增 Traits；补五行 Equipment、一行 Avatar，保留所有原字段，最终 406/406。
- Protocol 初轮 1205/1246：三个夹具共 41 项失败。CardReward 的 kind 2 物品原落装备槽 12，改为消耗品槽 65；两个时装 SlotBook 原统一 kind 1 / Equipment，改为给已声明时装模板 kind 8 / Avatar。原行为与协议字段断言保留，生产代码未改。首轮修复漏了第二件时装 101500690（1245/1246），补齐后最终 1246/1246。独立评审 R1 及复审裁定见 `reviews/change-review.md`。
- Application 最终 2833/2833；Infrastructure 有档案运行 768 通过、两个无档案前提测试跳过；移除当前测试进程的 PVF_ARCHIVE_PATH 后只补跑这两项，2/2 通过。全部是本次当前代码验证。
