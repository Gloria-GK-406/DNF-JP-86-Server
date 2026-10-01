# TASK-004 当前验证与部署 — 2026-09-30

基线6e60c4c6，32个Server变更/新增/删除文件集合SHA256 `b60d00685bacb7127918aa9c314bb45ca96fb248e3cfb95a9d318abb5a47a3b0`。manifest主仓库`artifacts/equipment-reinforcement-field-fix-20260930/deployment-manifest.json`。当前未提交；早先6af2...版本的自动验证和review不证明修复后的代码。

## 行为RED/GREEN及修复

新增handler测试使用真实CommandBus/SQLite，明确检查core9、core17、注册NOTI0E encoder以及重新Load后的注册NOTI0D完整快照encoder的entry+0A/+12。不是getter或依赖shape镜像。首次有效RED：core9 expected161/0xA1、actual160/0xA0，1fail/exit1；internal codec访问的初次编译错误已消除，不计RED。GREEN同测试1pass/exit0。

原始TRX在Application.Tests/TestResults/equipment-field-red.trx和equipment-field-green.trx。命令：
```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests -c Release --no-restore --filter 'FullyQualifiedName~Reinforcement_is_committed_and_projected_in_attribute_bits' -v minimal --logger 'trx;LogFileName=equipment-field-green.trx'
```

Data.Inventory core只读ReinforcementLevel=Attribute&0x1F；实例setter保留高3位和独立附魔次数，合法0..31。强化Domain/Rule、RepairRule、Outworld等级读者修复；珠子拒绝/独立codec字段/DrawnAppearance映射保持。补+30→31、修理113/226和独立附魔次数、失败/保护原始byte断言；相关solution filter319pass/0fail/0skip，Releasebuild0warn/0error。

## 完整程序集复验

完整solution Release --no-build --no-restore运行：Application2314、Protocol1202、Infrastructure408、Networking72、Host24、HttpApi50、Pvf60均通过；Persistence首次383pass/2fail，其原完整记录dump会枚举新增只读派生ReinforcementLevel，因此两个文字golden需要补列。只补6个既定seed的派生值11/9/13/17/25/5，所有原字段/seed/扫描仍保持，没有忽略字段或改实际存储。Persistence完整复跑385pass/0fail，exit0。

八程序集最终所需证据汇总**4515pass/0fail/2skip**；2skip仍为真实PVF配置下Town/Quest missing-archive负向条件。不是声称首次solution命令exit0：其exit1已如实保留；测试golden修正后使用完整Persistence新证据替代旧失败结果，其他未变且已通过的程序集不机械重复。

日志新工件`full-test.log`、`persistence-final.log`；TRX各项目equipment-field-final.trx，Persistence使用equipment-field-persistence-final.trx。Release最终build0警告/错误，git diff --check通过。

## 真实组合与部署

新工件probe/Probe.csproj使用实际模块、真实PVF和本次实际库副本；选角编码60条，UID339报价450金币/3037×10、固定draw+1，两句柄修改及跨事务身份持久化通过。此副本只修改UID339，不修改生产，不使用已受损UID689作为+0样本。

新独立runtime发布exit0，沿用原配置/Resources/原数据库/PVF。用户已明确正常退出，Game端口无建立连接；验证旧PID42104路径后仅停止旧Host，以Hidden启动PID41000。四监听端口均属41000，bootstrap apiVersion1/status ready，stderr0字节。旧runtime均保留。

当前原库UID689仍是byte9=0/byte17=1，已准备准确恢复提案和隔离dry-run，未执行生产--apply；金币89516、无色9796保持，备份quick_check=ok。新代码没有自动推断整库转换，用户明确同意后才考虑恢复这件已付费装备。

## 最后门

当前新鲜独立修复审查已经approved。用户明确同意后UID689已恢复且独立核对通过，见[恢复结果](asset-recovery.md)；上文未恢复记录保留部署时历史状态。用户[最后验收](../Human-Test-List.md)的E01结构反馈、修复版R01即时强化、R02正常退出重登仍尚未通过；不提前seal/commit/ClientVerified。
