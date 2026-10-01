---
kind: rks-verification
decision: manual_required
---
# TASK-007保护券期限修正及部署证据

契约rev3；工件根目录`E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-protection-expiry-20260930`。用户明确授权四券去过期字段、三个客户端替换、重启服务及额外5000万金币。当前已完成生产部署与后端新鲜核对，用户GUI门仍待验。

## 已验证的候选

- 四券7804/10007840/10004511/10007841的静态脚本各精确删除一个`[expiration date]`和其日期值，共10字节；不改其他token、保护action、概率、限制。以各自原PVF为模板，PvfLib overlay/SaveAs生成候选；独立生产Dfo.Pvf逐个解码所有712010文件，路径/类型/内容仅四个目标raw按预期不同。每个客户端独立receipt保存全检查结果。
- debug/protocol原SHA F1FD58E6873F0A3EB050265BF912A01F2B2B0782A1BE17578BAF45B4D4BC2D41，候选3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06；protocol2原4315D3B4E8B189D34CBAEFADBC81620438A242814139B479C059E4226AE10417，候选3847DAEE513DED7039AEFB2896FF1EB03E6FE321E40214BF12CE093BE57DE111。保留protocol2既有差异。
- 四券现有实例ExpireTime全部0，位置以生产InventoryItemCore.cs序列化byte41..44为准。未修改core或通用期限规则。增幅券仅备料，不新增增幅玩法。
- 候选protocol PVF环境下Infrastructure强化内容测试12 Passed、Application EquipmentReinforcement专项84 Passed，0失败/0跳过；原始TRX在test-results。继承未变源整体证据及TASK-006独立完整review，不声称重跑整个solution。
- Grant通过真实Application/Infrastructure/Persistence模块、Transactional CommandBus及CharacterDomainService.AddGoldAsync对副本追加50000000：2992701→52992701。独立SQLite全业务表逐列逐行比较，仅character_stats角色2carried_gold变化；dry-run-receipt.json、dry-run-verified.json。生产guard要求原余额且receipt不存在，防重发；当前生产尚未追加。
- preflight.py核对当前Server33文件集合及每文件SHA，与TASK-006已approved指纹93fad50a72ab3fe88cc2ed55b3134f3755d3e0de4915bcd9e4412ed22f1ddd75一致；七runtime主DLL与当前Release匹配。现有生产库所有业务表仍与备份完全一致，各客户端仍原SHA，候选与receipt一致。

## 准备的生产操作

deploy.ps1复查无DNF/无Game Established、PID41000路径身份、原PVF/候选/源/数据库身份；仅停确切旧进程后File.Replace分别替换三PVF。追加金币一次并全业务差分后，以Hidden启动已复核诊断runtime，核对bootstrap与四端口归属，保存deployment.json及stdout/stderr。失败恢复仅本次PVF/服务，绝不恢复整库或重发金币。旧/新目录见TASK-006 review。

## 未验证条件

用户需登录核对四券不再过期、实际余额，以及普通/超级失败保护和正常重登。整个根契约人工门保留，不seal、不提交、不标ClientVerified。

## 首次部署故障与恢复

首次deploy退出1：PowerShell将File.Replace第三个string参数`$null`绑定为空串，报`The path is empty (path)`；第一debug替换尚未发生、金钱发放尚未调用。原服务已按脚本恢复为相同旧runtime PID1088，bootstrap ready；重新preflight逐文件/全业务表证实三客户端和生产库完全原baseline。仅清理hash与候选一致的debug临时副本。

最小修正：Replace明确使用工件中atomic-before备份路径，回退明确failed-candidate路径；ExpectedOldPid改为参数，始终额外严格核对旧runtime路径。工件目录中小文件真实Replace检查目标after/备份before通过，未动生产。该脚本改动重新交独立reviewer复核，批准后以ExpectedOldPid1088继续；不抹失败结果、不重复金币、不恢复全库。

## 实际部署及最终检查

[独立完整mixed审查](../reviews/change-review-expiry.md)两轮均approved，第2轮明确核对脚本整改和旧服务1088恢复身份。随后以ExpectedOldPid1088执行deploy.ps1，exit0：停旧进程后三客户端各File.Replace成功，金币一次事务2992701→52992701，生产47业务表比较仅角色2carried_gold增加50000000。原始production-receipt.json与production-verified.json保存。

新诊断runtime `E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-reinforcement-probability-20260930/runtime` 实际PID40060，路径已核对；7001/10010/10011/18080监听全部归该PID，bootstrap.status=ready，stderr空。启动日志确认读取`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，内容root为新runtime。既有独立掉落缺模板warning保留，不属于此次期限内容修改。诊断日志功能已部署，下一次真实请求才会产生draw/threshold记录，尚不声称已捕获新请求。

postcheck.py及部署后再次verify-funds.py均exit0；三目标实际SHA与各自候选完全一致，protocol2原差异保持。quick_check=ok、角色2金币52992701、UID689强化10/独立附魔0、普通券50/超级49/增幅普通50/超级50，四实例ExpireTime仍0，其他所有业务数据与原备份相同（无色仍58236）。工件deployment.json、postcheck.json和server.stdout/stderr提供现场证据。

结论：期限内容修正、三个客户端/服务部署及一次性资金追加已交付，允许用户继续实机验收；保护显示/普通失败/退出重登及结构认可仍manual_required。
