---
kind: rks-verification
decision: pass
---

# 设置blob实现与部署验证

此pass仅表示TaskContract revision1的服务端实现、部署及实机测试准备完成，不表示ClientVerified。
当前独立审查有效裁定为[change-review.md](../reviews/change-review.md)的Round3 approved。

| 条件 | 证据与结果 |
|---|---|
| SC-1 | 真实SQLite重开、原bytes、两个bank／profile、不同账号、事务提交／回滚、显式空及生产handler闭环通过；应用全量3311通过、360无PVF跳过 |
| SC-2 | 最终28项专项覆盖精确归属、过期generation／token、断线精确清理、替代连接保护、C0与事务注册名册，全部通过 |
| SC-3 | 协议全量1411通过、60无PVF跳过；最终真实TCP登录5通过，包含C5/C6/170上传、后续无额外ACK、重连AD／1C7原bytes恢复 |
| SC-4 | 持久化全量468通过；Release publish成功；原库备份integrity_check=ok；新进程50048路径确认、31监听无缺失、GM／bootstrap均ready |
| SC-5 | [acceptance.md](../acceptance.md)提供逐项操作及预期，实机全部待验收 |

## 命令与原始日志

项目测试均使用`dotnet test <csproj> --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false --logger console;verbosity=detailed`。
专项另加`--filter`，完整命令已在会话工具记录。下列路径均相对仓库根，保留原始失败身份和最终结果，不以摘要代替日志。

- `artifacts/settings-protocol-full.log`：完整Protocol项目，1471总数＝1411通过＋60跳过。
- `artifacts/settings-application-full.log`：完整Application项目，3671总数＝3311通过＋360跳过。最后断线清理补丁后由以下专项及TCP复核受影响路径。
- `artifacts/settings-final-targeted.log`：ClientSettingsBlobTests、CommonExperienceEffectTests、EffectSettlementRegistrationTests、CharacterOptionsTests，28通过。
- `artifacts/settings-persistence-full.log`：完整Persistence项目，468通过；78张表及新表列形状断言保留。
- `artifacts/settings-tcp-tests.log`：最终GameLoginIntegrationTests，5通过。
- `artifacts/settings-publish.log`：Release Host publish成功。

## 部署身份

运行目录：`artifacts/client-settings-blobs-20261013/runtime`；PID50048；版本0.2.5。
构建时尚未接受本地成果提交，InformationalVersion仍带基线314bd463，不能用该版本字符串单独判断本次代码。
由`artifacts/client-settings-blobs-20261013/runtime-verification.json`记录实际路径、程序集哈希和PVF哈希；
`source-files.json`记录构建源文件，`accepted-source.json`在成果接受后关联提交，不假称该提交已嵌入程序集。

Protocol DLL SHA256：`F18D14A0166E957CE44E8B3566545ECFE43970346EB6056133787C20070C9415`。
PVF SHA256：`49D4CAFA402DAA93E9C60EFAC8550C20789BB5BD9F99DD7B9235FD0F20666E7A`。
数据库继续使用`Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`，备份位于
`artifacts/client-settings-blobs-20261013/database-before-settings.db`；旧运行目录完整保留。
沿用原7001、18080／18081与30000＋频道ID端口配置。

## 实机未验证边界

- 系统界面每项设置是否上传、各字段语义、其他玩家外观效果不由自动化证明。
- 默认.co资源正常、键位配置实际恢复及跨角色／缔造者隔离待用户测试。
- 1D9及DNF.cfg本地设置不在本期；客户端未发送的内容不受服务端持久化保证。
