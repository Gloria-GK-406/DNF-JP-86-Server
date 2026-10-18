# 礼包同格同步与魔王契约组合包修复

日期：2026-10-15；调查/实现基线：`99eb3c1c5d8276848243f301dcd629113caf6475`；分支：`task/next-round-20261013`。

用户授权：修复同格同步，检查共享路径，修复魔王契约组合包，测试后部署；用户已确认正常退出客户端。

## 实现

- `OpeningInventoryDomainService` 从已应用消耗中识别耗尽后又被占用的格子，保留在 `OpeningInventoryReceipt.Replaced`，不再只保留格子最终值。
- `OpeningCompletedHandler` 保持成功 ACK 在先，之后清除复用格，再发新物品绝对值，最后清除其他空格。普通礼包不会依赖魔盒 ACK 自行扣物品的假设。共享材料保留原有投影边界。
- 商城读取 `[charac premium package]` 的价格及 30/60 天期限，从包装道具的声明解析组成服务，并用 `[selectable character premium]` 目录映射到服务槽0..7。不存在按篮子或组合包商品 ID 特判的修复。
- 60天包装 `2682006` 使用 `[package data]`；30天包装 `10007846` 使用 `[booster info]/[cera]` 固定列表。两种均严格解析，未知/重复/含选择项的服务声明拒绝购买。
- 商品100625（60天）和103156（30天）各按目录扣3880点券，一次事务开通8项服务；有效权益在原期限后续期，已过期权益从现在开始。复用已有权益快照 `NOTI036F`，不把580..587这些存储键当作普通 premium wire type 发送。
- 第7格已有延期券留在数据库中；部署不补发篮子、不修改用户物品或余额。重登加载真实背包可清除之前的客户端残影。

## 共享路径核查

| 路径 | 复用条件与处理 |
| --- | --- |
| 普通固定/随机/自选礼包 | 共用消耗→发奖→OpeningCompleted，统一先清后填；含最后一个源物品的接收分支模型回归 |
| 装扮礼包 | 同一结算和通知路径；装扮进入独立容器，附带普通奖励复用源格也保留 Replaced |
| 魔盒单开/批量 | 共用修复；ACK 先行扣源物品/材料，再清复用格及发绝对值，避免二次扣减；覆盖两个耗尽格、多奖励 |
| 配方合成 | 共用结算及通知；新增源图纸/材料耗尽后多奖励复用两格的回归，修复前失败、修复后收敛 |
| 自动售货机 | 硬币消耗后奖励可能复用格，走相同的 OpeningCompleted，包含在修复范围；既有内容与事务测试通过 |
| 普通袖珍罐 | 当前产物限定普通装备，源为堆叠物，落点不同；仍经共享修复，并保留专用 ACK |
| 升等券、疲劳药、任务完成券、遗忘河之水 | 当前计划消耗道具并改变角色/任务/技能状态，没有同次实体物品奖励落格；原有耗尽清格逻辑保留 |
| 史诗制作 | 使用共享结算但专用投递；当前仅消耗材料/消耗品，产物限定 CarriedEquipment，格子空间不交叠，不能产生本问题的同格复用；没有更改专用 ACK/材料清空规则 |
| 商城普通物品投递 | 仅并入兼容堆叠或空格，购买本身不消耗背包礼包，不存在本次源格耗尽复用条件 |

这是已检查实现的边界，不代表客户端全部礼包类型已验收。跨请求后续物品操作、重试与未实现内容不以本次测试概括。

## 验证

新同步测试经生产命令、隔离SQLite、生产通知和编码器，应用固定A21静态接收分支模型。该模型只覆盖主背包class=1堆叠物；不是原生客户端回放。魔王契约测试经过生产商城handler/命令，校验扣款、8项到期值、036F编码期限、续期、余额不足及中途写入失败全量回滚。

| 检查 | 结果 | 本机证据（仓库根 artifacts/opening-sync-contract-20261015/tests） |
| --- | --- | --- |
| 修复前行为RED | 9项中6项按缺陷失败、3项通过 | red.trx |
| 最终针对性真实PVF | 17通过、0失败 | real-pvf-focused-final.trx |
| 应用层库存/结算/技能/架构真实PVF回归 | 1602通过、0失败；生产代码与最终版本相同，后续仅补测试夹具与拒绝用例 | application-real-pvf.trx |
| 最终同范围无PVF回归 | 1363通过、188依赖档案用例跳过、0失败 | application-no-pvf-final.trx |
| 协议全项目回归 | 1460通过、60档案相关用例跳过、0失败 | protocol.trx |
| 商城/开包内容解析真实PVF | 35通过、0失败 | infrastructure-real-pvf.trx |
| 商城与开包真实TCP集成 | 5通过、0失败 | networking.trx |
| Release解决方案构建 | 0警告、0错误 | dotnet build Server/86JP.Server.slnx -c Release --no-restore -m:1 -nodeReuse:false /p:BuildInParallel=false /p:UseSharedCompilation=false |

以上测试存在覆盖重叠，不汇总为互不重复用例总数。首次无PVF广范围测试暴露新增夹具对契约配额PVF的间接依赖，补入独立定义后通过；同次既有附魔replay测试5秒屏障超时，单独复测及随后整个无PVF范围重跑均通过，未修改该测试或其生产逻辑。

## 部署与回滚

候选路径：`artifacts/opening-sync-contract-20261015/runtime`；版本后缀 `opening-sync-contract.20261015`，InformationalVersion 附本次最终提交 SHA。

沿用旧运行时 `artifacts/tau-schedule-20261015/runtime/appsettings.json`；数据库仍为 `Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`。PVF 保持 `artifacts/sao-ticket-20261014/Script.server.pvf`，SHA-256=`91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4`。

部署脚本在停服前用SQLite backup生成一致快照并做 `PRAGMA integrity_check`，确认旧PID/路径/哈希、客户端已退出后才替换服务；停服后再留最终快照。新服务验证 launcher bootstrap、GM health、GM mail页面、全部配置监听端口所有者、程序集版本与哈希、库存及钱包/权益关键表摘要。没有数据库迁移或数据补丁。

具体部署成败、实际PID、版本、备份、哈希和检查值以 `artifacts/opening-sync-contract-20261015/deployment.json` 为准；失败则重启保留的旧运行时，并保留诊断日志。本文件在发布前记录实现与验证，不替代运行时部署凭据。

## 实机观察

1. 重登后第7格应显示收集箱延期券，原篮子残影消失。
2. 最后一个篮子/其他礼包打开后，旧图标消失；同格奖励即时出现，不需重登。奖励并入已有堆叠也应数量正确。
3. 魔王契约组合包购买成功、只扣对应价格，8项服务期限即时刷新；续购应累计期限。
4. 继续观察随机/自选/多奖励礼包与配方。若复现，保留操作时间和格子信息，便于核对日志。

验收状态：CurrentImplementation 已测试；A21静态依据见 [同格复用探针](../protocol/patterns/data/probes/a21-opening-slot-reuse-20261015.md)；修复后的 ClientVerified 待用户实机确认。
