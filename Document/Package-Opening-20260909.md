# 三类礼包开启交付记录

2026-09-09。基线 `b59448e2`，开发分支 `main`。授权范围：实现独立的魔盒、普通礼包、装扮礼包领域服务，并更新协议文档；包含在现有本地服务端上准备可复测版本。

## 结果

新增 `IMagicBoxOpeningDomainService`、`IPackageOpeningDomainService`、`IAvatarPackageOpeningDomainService`，分别控制三类业务规则，共享库存结算机制和 PVF 奖励规则。接通 CMD 0450、00D0、00A0、0207 的 typed decoder、事务命令、handler 与完整响应。魔盒支持来源/材料校验、批量、独立赛丽亚幸运值；普通礼包支持固定/随机/明确自选奖励；装扮礼包保留所选属性与期限，创建完整实例。

来源消耗、全部物品/宠物实例、账户期限和幸运值同事务提交。失败回滚且不发送成功提示；成功先 CMD，再背包绝对更新，再期限或宠物通知。原生魔盒成功包对客户端有扣减副作用，通知不自动重试，旧会话收件人会被过滤。

协议条目：[0450](../protocol/game/client-to-server/0x0450.md)、[00D0](../protocol/game/client-to-server/0x00D0.md)、[00A0](../protocol/game/client-to-server/0x00A0.md)、[0207](../protocol/game/client-to-server/0x0207.md)、[NOTI 0042](../protocol/game/server-to-client/NOTI-0x0042.md)。[流程文档](../protocol/patterns/flow-package-opening.md)记录事务顺序、业务策略、PVF 边界与待实机项。机器区保持不变，人工区纠正 0450 错误长度和 0207 头部错误语义；清单均标为“实现服务端功能，待实机”。

## 验证

使用真实 `DNFClient-A21-protocol/Script.pvf`；所有成功开启测试使用隔离 SQLite，不消费用户背包。

| 范围 | 结果 |
|---|---:|
| 本次领域/事务测试 | 27 通过 |
| 本次协议字节测试 | 19 通过 |
| 本次真实 TCP 拒绝/畸形请求测试 | 4 通过 |
| 完整服务端解决方案 | 2730 通过，0 失败，2 跳过 |
| Release 构建 | 0 警告，0 错误 |

两项跳过均是“无 PVF 时应报错”的基础设施用例，因为本轮确实配置了可访问档案。按每条 `UnitTestResult` 统计，非仅引用聚合 Counters；明细见 [results-summary.json](Verification/Package-Opening-20260909/results-summary.json) 和 [完整最终 TRX](Verification/Package-Opening-20260909/full-final)。

覆盖：真实第一层魔盒 60 次（120 传送药剂、60 赛丽亚魔盒）、真实赛丽亚池所有奖励的创建定义/期限检查及确定种子 60 次结算、0/7/8 幸运边界、钥匙不匹配/不足、满包、错误选项、过期或缺失子奖励、真实装扮/普通随机宠物礼包、装备/装扮/宠物实例、虚拟复活币、期限续期、使用规则/冷却、持久化失败后的整体回滚、并发不足来源单赢家、旧会话通知过滤与 CMD 在 NOTI 前。

复现：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/86JP.Server.slnx --no-restore --logger trx --results-directory Document/Verification/Package-Opening-20260909/recheck -v q
dotnet build Server/86JP.Server.slnx -c Release --no-restore -v q
```

## 诊断保留与审查

`red/` 保留未接线测试与缺少 decoder 时四条 TCP 超时的失败证据；后者是真实注册缺失引起的行为失败。`diagnostics/` 保留内容探测和逐步扩展测试。`targeted/opening-codecs.trx` 中两处失败来自测试对 ImmutableArray 的相等性重载，已改为逐条断言；`opening-codecs-green.trx` 为其通过记录。编译错误不作为行为 RED 证据。

执行中发现赛丽亚材料 `201 ×0`、虚拟复活币和期限奖励，需要按真实声明分别处理；真实旧装扮包还存在缺失模板和过期子奖励。已支持明确有效语义；未支持或失效声明拒绝且不消费来源，没有替换 PVF 或吞掉失败奖励。历史装扮测试使用历史时间的事实单独标明，未声称旧活动奖励今天仍可领取。

完成前复核了账户/角色归属、容器边界、随机奖励数量来源、装备/装扮/宠物事实、跨库存与账户状态回滚、消息收件人代数和响应顺序。Application 未增加 Infrastructure 引用，三个领域服务不以共用协议号合并成一个开箱服务。参考实现的幸运 3 倍策略与原生回包业务语义保持 High；客户端实际接受情况待复测。

## 本地运行状态

更新前确认 DNF 客户端已退出，用 SQLite backup API 备份数据库，随后停止旧 Host、构建 Release 并后台启动新版。实际数据库仍为 `Server/src/Dfo.Server.Host/bin/Debug/net10.0/Data/dfo-server.db`；备份路径在 [runtime-before.json](Verification/Package-Opening-20260909/runtime-before.json)。

仓库持久化服务为惰性实例化，Host 仅启动监听并不触发表迁移；通过现有 `PersistenceModule` 的 `IAccountTable.FindSeriaLuckAsync` 读取完成实际库初始化。新增 `seria_luck_value INTEGER NOT NULL DEFAULT 0`，账户 1 为 0。

新版 Host PID `35512`，端口 7001、10010、10011；两个 Game 端口实际接收未选角色的 0450 请求并返回 `00 02`。这只证明部署和拒绝路径，不证明成功开箱 UI。启动参数/日志位置见 [runtime-host.json](Verification/Package-Opening-20260909/runtime-host.json)。

初始化后比对 `items`、`item_slots`、`account_premiums` 全表摘要与钱包，均未改变：[runtime-after.json](Verification/Package-Opening-20260909/runtime-after.json)。账户点券仍为 44310；角色 1 的 slot 65 保留 `10007476 ×1`，slot 66 为 `10007368 ×60`，slot 122 为 `10007367 ×60`。

仍需用户实机打开现有魔盒、赛丽亚魔盒和装扮礼包，确认弹窗、幸运效果、属性/期限与重登状态；此前不标记 ClientVerified。
