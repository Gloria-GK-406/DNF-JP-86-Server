# 关联内容基础与马戏团取证验证

代码验证基线为`c7c85e04f6480d8c3a8d8d33d1947510725ae26d`，已合入main的`14f156dd`。
本记录为验证说明，不改变生产代码；最终PR当前提交仍须通过云端CI与Codex审核。

| 检查 | 结果 |
|---|---|
| Windows服务端完整Category=Unit及报告判据 | 1309通过，0失败，0跳过；无匹配Unit的项目不计为测试通过数量 |
| 客户端D45C…AEF84档案：关联解析与既有地下城读取 | 50通过，0失败，0跳过；包含遍历档案内全部关联块 |
| 原主干3822…05C6B档案：同一回归 | 50通过，0失败，0跳过 |
| 探查代理替身 | 默认分支、显式气球调度差分、强制天幕分支范围及去重通过；不代替客户端证明 |
| 实际客户端 | 本轮受控单人happy path通过；范围见[验收记录](happy-path-acceptance.md) |

命令（仓库根目录，PowerShell）：

```powershell
dotnet test Server/86JP.Server.slnx -c Release -m:1 -nr:false -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true --filter Category=Unit --logger trx --results-directory artifacts/circus-closeout/units
python Tool/Release/release.py summary artifacts/circus-closeout/units --unit-only
$env:PVF_ARCHIVE_PATH='E:\Project\DNF\DNFClient-A21-protocol2\Script.pvf'
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~PvfLinkedDungeonTests|FullyQualifiedName~PvfDungeonDefinitionSourceTests'
$env:PVF_ARCHIVE_PATH='E:\Project\DNF\DNFClient-A21-protocol\Script.pvf'
# 同上，对第二档案独立运行。
python Document/tasks/circus/verify_probe_proxy.py
```

原始关闭前验证输出保留在任务私有`artifacts/circus-closeout*`；清理工作区前与探查运行资料一并保存。
这次不运行占用默认端口的Host集成测试，不声明其通过。PR云端CI还会执行GM前端、报告判据和Linux Unit门。
