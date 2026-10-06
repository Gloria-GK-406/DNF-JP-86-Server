# T-001 模型与 SQLite 身份结算结果

## 已完成

- 新增 `ItemRecord(Core, WorldClass)` 与仅保存 `SlotIndex + Item` 的 `ItemSlot`。模板、Traits、Count、IsPile 均只读投影；WithCount 检查正数和独立物品数量，WithCore 保留 UID/细节。
- `IItemDomainStoreSession` 使用 ItemSlot；删除 StoredItem / ItemTransferSource。
- SQLite 按 slot.kind 区分堆叠物与独立物品，读取全部时装六字段/宠物八字段。world UID 来自 items.item_uid；堆叠物不暴露内部 core_uid。
- 结算仅按记录的显式 UID 匹配，拒绝未知 UID、重复最终位置、未释放原位置、模板/类别不相容、不同角色直接移动、不相关角色账号移动。允许同所有者和角色所属账号之间移动。
- 更新完整 Core 与全部附加字段，保留 world 行、创建时间与 UID。时装旧 wire UID 不允许在更新时改变，新建有唯一性护栏；宠物新建将 Core.Value 写成分配 UID，已有宠物保留 UID 并支持既有孵化/进化模板变更。
- 删除内容/来源猜测、CarriedAvatars 回填。Flush 成功后清空 staged，防止 Creature facet 再次 Apply 导致新建 UID 重分配。
- 已迁移直接存储测试，移动/交换/更新从读取结果保留 Item；修正旧测试中“改变状态并移动应更换 UID”的旧契约。完整事实缺失的时装现在直接拒绝，原允许两条无事实时装的测试改为验证该漏洞关闭。
- 所有修改恢复 LF，纯行尾文件不再出现在 diff；git diff --check 通过。

## 验证

1. 改实现前，在 AvatarDetailFollowsTheIdentityTests 加入存储会话读取 AvatarDetail 的断言；运行单测，1/1 失败（原会话始终丢失读取细节）。此初始 RED 输出在任务工具记录，未单独落盘。
2. `dotnet build Server/src/Dfo.Server.Persistence --no-restore -v quiet`：0 警告、0 错误。日志：artifacts/unified-item-record/store-build.log。
3. `dotnet test Server/tests/Dfo.Server.Persistence.Tests --no-restore -p:BuildProjectReferences=false --filter '<七类直接存储测试>' -v normal`：66/66 通过。完整命令与测试项见 artifacts/unified-item-record/store-tests-green.log。使用既存 Application 编译输出，**不等于全解决方案集成通过**。
4. 新测试覆盖同 Core 装备交换并跨容器多跳改状态、伪造/复制/重复 UID、模板重绑定、角色与账号归属拒绝和回滚、时装完整更新、宠物完整更新及模板改变、pile 无 UID 与数量边界、直接 Flush 防重放。
5. 中间 Flush 防重放 RED（65 项中 1 失败）保存在 artifacts/unified-item-record/store-tests-final.log，随后修复并在最终 66 项中通过。此前 55 项通过日志为 store-tests-first.log。

## 主代理后续集成

- IItemContainer、IBusinessItemContainer、Application、Protocol 和其他测试尚未迁移；本阶段没有越过文件范围修复这些消费者。
- SqliteCreatureDomainStoreSession 仍有独立 staged CreatureState。当前 record 存储会完整覆盖 creature details，主代理须消除同事务 stale record 与该 facet 的先后顺序覆盖，并让 record 读取看见 pending creature state。这是 T-002 明确的集成工作。
- SQLite 无 PVF 分类来源：读取时先用 slot.kind 判 pile/world；world 再在持久化边界按 core.kind 8/5/其余恢复 Avatar/Creature/Equipment。生产 Save/新建工厂须仍验证模板的真实 WorldClass 和 Traits。
- Creature 模板变更允许的边界保留既有语义：原 core.kind=5、原 Core.Value=world UID、目标 kind=5/相同 Core.Value/相同 UID；具体合法孵化/进化模板仍由业务规则约束，存储无模板目录可自行判断。
- 新独立物品在 Flush 前 Identity=null；需要实例/客户端投影的消费者保留既有 Flush 边界。
- 未提交、未部署、未操作运行数据库；测试只使用临时 SQLite 文件。
