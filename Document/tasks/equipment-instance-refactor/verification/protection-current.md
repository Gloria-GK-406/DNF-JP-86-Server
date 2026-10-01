# TASK-005当前自动证据与运行状态

契约revision2，HEAD6e60c4c6，完整33个Server变更/新增/删除文件集合SHA256 `5c90145124ed6e703b514505c21b569524ff29e9a74e818131a04045cc4dd087`，主仓库`artifacts/equipment-protection-20260930/current-manifest.json`。对TASK-004仅有三个测试文件变化，产品代码、wire、费用/概率与7个部署主DLL均保持。

## 自动结果

保护专项Application84、真实PVF12、Protocol wire8均通过，Release solution0警告/错误。详见[worker原始证据](protection-code-tests.md)，初始新增fixture失败已保留，未声称业务RED或修复不存在的缺陷。

root对两个变更测试程序集执行完整Release检查，均exit0：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests -c Release --no-build --no-restore --results-directory E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-protection-20260930/Application --logger 'trx;LogFileName=protection-application-full.trx' -v quiet
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -c Release --no-build --no-restore --results-directory E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-protection-20260930/Infrastructure --logger 'trx;LogFileName=protection-infrastructure-full.trx' -v quiet
```

Application2356/0fail/0skip，Infrastructure408/0fail/2skip；两个skip仍为真实PVF配置下missing-archive原条件。其余未变六程序集沿用TASK-004完整通过的Protocol1202、Persistence385、Networking72、Host24、HttpApi50、Pvf60。因此当前所需证据汇总4557pass/0fail/2skip，**不是称本轮执行过全solution test命令**。

新增明确原始core/UID、券余量/清空、注册NOTI0E encoder及重读完整NOTI0D、虚拟无色、提交失败整体回滚与保护重放。ACK optionalTicketSlot语义仍Unknown，未改request回显；P-02/P-03客户端保护结果仍待人测。

## 资金与运行版本

用户授权发放已完成：金币10089516、无色59796；完整材料及四券各50见[发放记录](supplies-grant.md)。领域服务事务、副本试跑、原库备份及只读差分均完成，原装备/已有core/world UID和位置保持。仅新增堆叠模板载体，不制造保护失败装备。

TASK-005不改变产品代码；独立逐字节比较当前Host Release输出与运行中的7主DLL一致，故沿用PID41000现有field-fix runtime，不重启或生成相同包。manifest明确该决定，不把测试源码变更当新运行行为。最后fresh端口/bootstrap/stderr检查由controller和完整review保留。

当前自动/运行交付已通过[rev2独立完整mixed review](../reviews/change-review.md)，控制器消费approved后重新核对源码、原始TRX、部署DLL与当前资产，exit0，现场端口/bootstrap/stderr正常。执行阶段转入verification/manual_required；[保护人测](../Protection-Human-Test-List.md)与原[结构/强化/重登门](../Human-Test-List.md)已可执行，继续待实际反馈，不seal/commit/标ClientVerified。
