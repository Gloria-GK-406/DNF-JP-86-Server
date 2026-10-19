---
kind: rks-verification
decision: pass
---

# 封印的藏宝地服务端交付验证

验证对象：`28f6aba6e02d0868fc5e65ab6a0a2d0f48531c9f`，契约revision1，独立完整结果审查approved。审查后未修改产品或测试文件，复验完成后HEAD仍为该实现提交。

本次通过声明限于契约约定的服务端实现、自动验证和人工验收文档；不包含部署或A21实机验收。

## 条件与证据

| 契约条件 | 决定性证据 | 结果 |
|---|---|---|
| SC-1 全类标记免疲劳 | 通用PVF解析spicies存在即ConsumesFatigue=false；真实3557确认，普通70仍true。零剩余角色准入、首次新房与重访已用疲劳不变。`SealedTreasureTests`、`PvfDungeonDefinitionSourceTests`。 | pass |
| SC-2 活动与准入 | 独立2331配置投影为3557 ScheduledDungeonState，活动关闭拒绝；70级、模式、练习、单人、指定难度及已有运行拒绝矩阵通过。消息沿用既有033F encoder，原生显示待验。 | pass |
| SC-3 票据替代与事务 | 四种持票组合、活动组不完整回退、整组AND、批次重复消费、回调拒绝与建局回滚通过。既有多材料与南部/牛头专用路径关联回归通过。 | pass |
| SC-4 元素与跨房 | 四组精灵分别验证对应路线与石头；其他三路仍关闭；未提交死亡不解锁、跨房返回保留、新局无继承。状态由已提交Kills派生，无第二份可变缓存。 | pass |
| SC-5 宝箱与池 | 销毁前及回滚后拾取拒绝，销毁提交后仅领取一次，重复销毁拒绝；无全元素条件。真实PVF25图工厂发现15150，两个白金箱均读取1抽、147项、总权重100000。 | pass |
| SC-6 生产工厂与回归 | 25图生产工厂创建成功，257怪物放置、47模板和四精灵/四封印石齐全；真实PVF相关149项与解析88项通过；完整无PVF解决方案8/8测试项目通过，6664 pass / 873按约定skip / 0fail。 | pass |

## 实际命令与结果

所有命令从 `E:/Project/DNF/DNF-JP-86-Server` 运行。真实PVF路径均为 `artifacts/scheduled-notice-pvf-20261019/Script.server.pvf`，SHA256 `C5CCE5346F60D21128D756A086EEF96E70F75F2F2FE063B6C61C00506C588F06`，实施前后相同。

1. 行为RED（生产源码为planning基线9907b3b3）：`dotnet test Server/tests/Dfo.Server.Application.Tests --filter FullyQualifiedName~SealedTreasureRunTests --no-restore -v minimal`，exit1，5项失败。决定性输出是未解封移动返回非null，以及宝箱销毁前地面claim返回非null，均为缺失行为而非编译失败。实现后同5项全部通过。沙箱首次dotnet静默退出未计作RED，获准使用正常本地测试运行环境后才获得上述行为证据。
2. 真实PVF相关：`dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~SealedTreasure|FullyQualifiedName~DungeonEntryMaterialTests|FullyQualifiedName~SouthernDaleTests|FullyQualifiedName~TauKingdomTests|FullyQualifiedName~DungeonObjectQueueTests|FullyQualifiedName~DungeonVisitDomainServiceTests|FullyQualifiedName~DungeonRunDomainServiceTests" --no-restore -v minimal --logger "trx;LogFileName=sealed-related.trx" --results-directory artifacts/sealed-treasure-tests`，exit0，149 pass / 0skip / 0fail。
3. 真实PVF解析：`dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --filter "FullyQualifiedName~PvfDungeonDefinitionSourceTests|FullyQualifiedName~PvfTypedDungeonAndMagicSealTests" --no-restore -v minimal --logger "trx;LogFileName=sealed-pvf.trx" --results-directory artifacts/sealed-treasure-tests`，exit0，88 pass / 0skip / 0fail。
4. 清空 `PVF_ARCHIVE_PATH`：`dotnet test Server/86JP.Server.slnx --no-restore -v minimal --logger trx --results-directory artifacts/sealed-treasure-tests/no-pvf`，exit0。解析目录内8份TRX，和8个测试csproj一一对应：7537总计、6664通过、873资源测试跳过、0失败；包含解决方案编译。
5. 独立审查后的末次复验：Application使用步骤2的筛选缩为 `FullyQualifiedName~SealedTreasure|FullyQualifiedName~DungeonEntryMaterialTests`，加 `--no-build --no-restore -v quiet`，输出 `sealed-final.trx`，exit0，28 pass；Infrastructure使用步骤3相同筛选，加相同选项，输出 `sealed-pvf-final.trx`，exit0，88 pass。两者均使用同一真实PVF。
6. `git diff --check`、产品/测试无未提交差异、实现提交parent与17文件边界、本地Markdown链接、服务PVF哈希及源分支仍为研究提交2764030f均已检查通过。

原始TRX保存在仓库 `artifacts/sealed-treasure-tests/`。早期 `sealed-focused.trx` 的无票组合因测试未注册Material容器导致辅助Count失败，已修正并由后续28/149及全量测试覆盖。牛头关联回归曾因原PVF重复practice字段失败，经原始dump确认0/1重复后兼容最终声明，后续149/88通过。两项均不作为当前失败遗留。

## 证据限制

原生A21的3557入口显示、ACT解封和提示、宝箱显示时序、完整通关、放弃及断线仍为待人工验收项；没有新增WireCaptured或ClientVerified。147项奖励池校验并不代表逐项开箱实测。未启动、重启或部署生产服务，未写生产DB、未修改客户端/PVF，也未修改GitHub；Host测试使用隔离测试宿主。

现有技术债D-001..D-006没有被本次修改解决或扩大；无新增技术债行。客户端人工验收与历史运营规则选择在契约中明确分阶段，不用技术债冒充已交付行为。
