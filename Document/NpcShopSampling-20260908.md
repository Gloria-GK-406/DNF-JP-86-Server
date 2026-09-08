# NPC 商店支付方式采样（2026-09-08）

用户要求给角色增加 100,000 金币及 100 个预约硬币，分别测试金币购买和材料兑换。
本次仅准备资源与记录请求，尚未实现购买处理。

## 已发放

- 账号 1、角色 1，名称 `000`，守护者，等级 2。
- `character_stats.carried_gold`：0 → 100,000。
- `490003528`「Lv预约硬币」：新增 100 个；主背包材料段 `list_type=0 / slot_index=121`。
- 物品行 `item_uid=61`，槽位身份为 template，槽位 count 与 item_core.Value 均为 100。
- 发放时客户端未运行；写入在 SQLite 事务内完成，随后核对数量和 `PRAGMA integrity_check=ok`。
- 发放前完整备份：`Server/src/Dfo.Server.Host/bin/Debug/net10.0/Data/backups/before-shop-sampling-20260908-233124.db`。

## 为什么是这款硬币

PVF 中有六款名称带“预约硬币”的物品，不能仅凭显示名选择。
用户说明测试赛丽亚商店后，按当前 `Script.pvf` 逐级定位：

1. `npc/Seria.npc`：NPC 2，`[role] [item shop] 15`。
2. `itemshop/itemshop.lst`：15 → `EquipmentShop7.shp`；归档实际含 `(r)equipmentshop7.shp` 和 `(f)equipmentshop7.shp` 两份。
3. `itemshop/(r)equipmentshop7.shp`：“全职Lv预约得好礼”页列出 490003530、490003529 等商品。
4. `stackable/chn_490003529.stk`：闪亮的远古宝箱，`[need material] 490003528 60`。
5. `stackable/chn_490003530.stk`：绝望之塔直达券 (70层)，`[need material] 490003528 80`。
6. `stackable/chn_490003528.stk`：Lv预约硬币，`[stackable type] [material] 0`，stack limit 1000。

上述声明由本仓 `Tool/PvfProbe` 直接读取当前客户端 PVF。
这证明商品可以要求材料支付；还不能据此给 `BUY_ITEM` 请求的后八字节赋予“货币 ID”等含义。

## 采样范围

- 运行中 Host PID 47028，继续使用原数据库。
- 源日志：`Server/src/Dfo.Server.Host/bin/Debug/net10.0/Logs/server-20260908-232038.stdout.log`。
- 本轮起始文件偏移：14787 字节，准备时间 23:33:20。
- 未实现请求由 `GameNetworkPlugin` 记录 selector、type、sequence、完整 body 长度及十六进制。
  这是服务端解析后的请求记录，不是完整原始 TCP 帧；不会伪造未记录的 checksum 或帧头。
- 客户端 `0100_NetworkHook.dll` 原本位于 plugins-disabled，本轮保持该运行配置。
- 用户分别测试金币商品与硬币商品，并提供商品名、数量；实际是否都是 `0x0015` 由日志判定。

实际样本待本轮测试产生后归档到 `protocol/`。
