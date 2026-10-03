# 宠物与 ItemRecord 互通实现

## 实现

- CreatureFacet 删除完整状态 staged。WriteAsync 按 UID 和角色解析最新位置，校验模板、类型和 UID，以最新 ItemRecord 保存 CreatureDetail；普通容器和宠物不再各暂存一套细节。
- StageAsync 六处调用已接入。CreatureLocation 保留完整记录并按 Identity 定位；孵化/进化先 Save 新模板 Core，再 Stage 新状态，保留 UID 和其他 Core 字段。
- 条件复活仍使用独立受限 SQL，保留原账号/角色/删除标志/仅死亡时更新守卫及与完整状态写入互斥。条件应用后立即消费，ItemRead/ReadAll 仅在存在待决条件时触发屏障，避免普通交换的中途读取提前应用布局。ItemFacet 在 CreatureFacet 注册之前加入，避免 Prepare 中增加 facet。
- 在线计时增加已知存储整数基准和单 UID 重置时间。完整记录修改 Field04 后不复用旧小数或旧耗时；改名字等细节继续保留小数。宠物 runtime 孵化选择也按 Identity 定位。

## 验证

- Application、Persistence 生产项目构建通过，0 警告/0 错误。
- Persistence 测试筛选 `FullyQualifiedName~Creature|FullyQualifiedName~SqliteItemDomainStoreTests`：58 通过，0 失败；日志 `artifacts/unified-item-record/pet-persistence-tests.log`。
- 新增真实 SQLite 场景：宠物与 record 交替修改、移动后修改模板并保 UID/细节、提交或回滚；条件复活读屏障、record 再保存、两种 store 使用顺序及新建不重复落库。
- Application 测试尝试因本轮尚未迁移的其他旧容器夹具而编译失败，不能视为通过；日志 `artifacts/unified-item-record/pet-application-test-attempt.log`。新增 runtime 整数修改/名字修改小数保留测试源待主任务完成夹具迁移后执行。

## 测试文件交接

- `CreatureDomainStoreTests.cs`：完整迁移并已执行通过（11 个用例）。
- `TestCreatureCollection.cs`：WriteAsync 接口迁移完成，无旧容器类型。
- `CreatureRuntimeDomainServiceTests.cs`：仅新增两个 runtime 互通用例及 using；该文件无旧 Put/StoredItem/ContainerSlotItem 定义，但依赖其他尚待迁移的 FakeMainInventory 等。
- `CreatureTransformationDomainServiceTests.cs`：仅将 SqliteCreatureAdapter.SaveAsync 接到 WriteAsync；其余旧容器夹具与两处 PutAsync 调用尚待主代理统一迁移，不能排除此文件。

未提交。恢复所有本子任务改动的已有文件至各自 HEAD 换行风格，相关代码 diff --check 通过。
