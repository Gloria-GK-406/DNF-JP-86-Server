> 2026-09-30实机语义更正：此前将 `EnchantUpgradeCount` 当普通强化等级的解释错误。普通强化为 `Attribute & 0x1F`（`ReinforcementLevel`）；`EnchantUpgradeCount` 是独立附魔升级次数。以下早先实现/测试记录保留其当时范围，不证明客户端普通强化等级正确。TASK-004纠正读写和wire/存储oracle；升级珠子效果仍不在当前支持范围，拒绝行为保持。

# 当前自动验证与部署证据 — 2026-09-30

目标版本：基线6e60c4c6加本任务22个Server变更/新增/删除文件。SHA256源文件集合指纹 `6af2b4b5ed52615a84b3c99693b215d2f4ce102cd2943bf648fea36bee467333`；逐文件hash与删除标记在主仓库 `artifacts/equipment-instance-20260930/deployment-manifest.json`。当前未提交；没有PR或push。

## 自动检查

工作树 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap`，真实PVF `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`：SHA256 `F1FD58E6873F0A3EB050265BF912A01F2B2B0782A1BE17578BAF45B4D4BC2D41`。

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/86JP.Server.slnx -c Release --no-restore --logger 'trx;LogFileName=equipment-instance-final.trx' -v quiet
dotnet build Server/86JP.Server.slnx -c Release --no-restore -v quiet
```

全量test exit0：Application2309、Protocol1202、Persistence385、Infrastructure408、Networking72、Host24、HttpApi50、Pvf60，**合计4510通过，0失败，2跳过**。两skip分别为Town/Quest内容源missing-archive负向测试，真实PVF已设置时由其原条件跳过。没有其他skip。Release build exit0、0警告/错误。

原始输出：主仓库 `artifacts/equipment-instance-20260930/full-test.log`；各项目 `TestResults/equipment-instance-final.trx`。含新实例9个真实SQLite测试、全部现有养成/repair/durability/disjoint/swap、身份/分层架构、CommandBus事务、协议handler/codec和Host/TCP引导回归。

副本probe exit0：实际Infrastructure/Persistence/Application/Networking/Protocol模块、真实PVF、生产库在线备份副本组合。选角库存编码60条；UID339实际规则报价450金币/3037×10，固定draw成功到+1；公开Of/Find/Set/Decrease/Read跨两个句柄保留修改；提交后旧句柄拒绝，新事务仍读到同UID和两个字段。生产库没有执行这些实例修改。probe只证明规则/实例/真实组合，不冒充实际强化GUI验收或wire抓包。

probe源及日志：主仓库 `artifacts/equipment-instance-20260930/probe/`、`probe.log`、`probe.db`。该副本没有按玩家业务扣费，不用于验收资产基线。

## 调用覆盖和协议边界

五项养成+修理/自动修理/耐久所有字段修改均调用IEquipmentInstance；分解通过实例Destroy。旧snapshot EquipmentInstance.cs删除，真正EquipmentInstances.cs内实例没有内容源、概率/费用公式。强化残余`core with`为销毁材料空间模拟，分解Put/Clear为工具/产物；没有在这些业务保留装备字段的平行writer。

git diff中Protocol/Networking/Data wire及schema均无变更；未重新生成协议记录或将自动结果标ClientVerified。基础store仅给暂存读取投影结算会保留的世界身份，写入仍不接受UID。原分解领域直接内容准入依赖保持在本次适配旁支，不声称全部分解规则已重新分层。

## 当前运行与备份

发布exit0：新独立目录 `E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-instance-20260930/runtime`；沿用原appsettings/Resources和实际DB/PVF路径。切换时Game端口无已建立连接；验证旧PID33868实际exe路径后只停止该进程，新服务Hidden启动。

16:29:46 +08:00新Host启动；PID42104实际exe为新runtime。7001/10010/10011/18080监听都属42104，`GET http://127.0.0.1:18080/api/launcher/v1/bootstrap` 返回apiVersion1/status ready；stderr空。旧runtime保留，没有删除工作树或其他服务。

SQLite只读在线backup `before-deploy.db` quick_check=ok。只读基线与副本读取确认1123/Account4/Character2，金币179868、无色9806；皮靴UID339/模板100260176/+0/Dur20。`acceptance-baseline.json`包含原82字节core与当前位置。详见[最后验收](../Human-Test-List.md)。

## 尚需用户最后验收

SC-6的E-01结构确认、R-01当前重构版一次强化和R-02正常退出重登尚无当前用户反馈；因此本文件只支持自动及部署检查通过，不支持任务最终完成。
