---
kind: rks-verification
decision: pass
---

# 完成验证

声明：主动深渊冻结奖励、组末一次性结算、既有死亡投影及测试部署完成；不声称客户端奖励显示/拾取或官方爆率已经验收。

| 条件 | 当前证据 | 结果 |
|---|---|---|
| SC-1 | HellRewardSettlementTests 覆盖两种击杀顺序、重复 claim、独立组、空奖、不合资格、放弃 claim 重试、无效数量不半提交；DungeonCellFactoryTests 覆盖重复 GroupIndex 跨 order 冻结一份组奖。 | pass |
| SC-2 | 实际 A21 PVF 在75/85级验证奖励次数、七列累计阈值、20444/440标记、真实装备池和生成模板可落位；纯规则测试覆盖概率零值/满值、空池、不跨稀有度回退、六个累计边界。 | pass |
| SC-3 | 全量 solution 检查后重跑最终 Application：合计6094通过、0失败、2跳过；发布构建退出0；缺票专用拒绝已包含；五个端口归属新PID34512，bootstrap/GM ready，GM mail HTTP200，备份 quick_check=ok。 | pass |

## 检查与身份

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/86JP.Server.slnx -c Release --no-restore --disable-build-servers -m:1 -nr:false --nologo --logger trx --results-directory Document/tasks/manual-hell-rewards/verification/test-results
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-restore --disable-build-servers -m:1 -nr:false --nologo --logger trx --results-directory Document/tasks/manual-hell-rewards/verification/final-test-results
dotnet publish Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj -c Release --no-restore --disable-build-servers -m:1 -nr:false -p:InformationalVersion=0.2.2+hell-rewards.20261007.3ef6b379b3ce -p:IncludeSourceRevisionInInformationalVersion=false -o E:/Project/DNF/DNF-JP-86-Server/artifacts/manual-hell-rewards-20261007/runtime --nologo
git diff --check
```

均退出0。最终 Application 回归3134项，替代稀有度边界修正前的3128项结果；其他项目完整结果保持。逐项目结果和原始TRX SHA-256见 [汇总](test-summary.json)，原始TRX保留在本机并忽略。两项既有跳过为缺归档失败测试，因为此工作区可读取实际PVF，深渊测试没有跳过。

独立审查 [approved](../reviews/change-review.md) 对应最终18个C#文件指纹；审查后未再修改产品/测试源码。发布按该源码指纹设置可复核程序集身份，构建于本地接受提交前；部署保存SourceBaseline和逐文件SHA-256，接受提交后外部记录绑定完全相同的产品树。

运行版本 `0.2.2+hell-rewards.20261007.3ef6b379b3ce`；Application SHA-256 `95EFB90424838890E2C0B866BDEF59E757A40C279A0F30B7B3CC5F8E9A120429`。
部署证据在 `E:/Project/DNF/DNF-JP-86-Server/artifacts/manual-hell-rewards-20261007/attempt-2/deployment.json`。
备份为同目录 `before-stop.db`，SHA-256 `a228f3fdc321b893a16748389ac153c02773c62575047800c8efa48e18999308`，quick_check为ok。
旧版本位于 `artifacts/manual-hell-entry-20261007-313ca14b/runtime`，完整保留。

首次切换已通过新进程/监听及ready检查，但 Windows PowerShell5 的 Invoke-WebRequest 页面检查发生NullReference；脚本恢复旧运行版本。只读确认旧服ready及UseBasicParsing成功后，在新的attempt-2目录重试并核验通过；未覆盖首次失败日志/备份，未回滚数据库。无生产源码修正。

## 实机边界

装备/碎片地面显示和拾取、碎片在客户端的具体归属表现、缺票后切地图/难度尚待用户验收。史诗药和Luck加成的未还原边界见 [实现说明](../implementation.md)。这些不被上述自动验证升级为ClientVerified。
