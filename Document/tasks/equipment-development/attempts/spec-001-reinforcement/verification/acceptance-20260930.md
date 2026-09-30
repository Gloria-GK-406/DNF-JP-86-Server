# 普通强化验收准备 — 2026-09-30

状态：**待用户执行R-01、R-02**。宠物常规收尾完成后接续；没有据自动测试标记强化ClientVerified。完整清单仍在[Human-Test-List](../Human-Test-List.md)。

## 当前环境与只读基线

- Host生产源码`923f3c28`；运行目录`E:/Project/DNF/DNF-JP-86-Server/artifacts/creature-closeout-20260930/runtime`，PID33868；登录入口ready，端口7001/10010/10011/18080正常。
- 数据库未造装备/未改资产；切换前备份在同工件目录`before-acceptance.db`，quick_check=ok；JSON记录`acceptance-baseline.json`保存原82字节item_core，后续可排除强化字段以外的变化。
- 真实PVF：`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，SHA256 `F1FD58E6873F0A3EB050265BF912A01F2B2B0782A1BE17578BAF45B4D4BC2D41`，与原强化准备使用的档案一致。
- 角色1123，CharacterId2 / AccountId4，等级80。当前金币179868，无色小晶块9806。只读生产DI+PVF+SQLite副本确认普通+0候选；选角库存可编码60条消息。
- 推荐现有装备：**女王御赐皮靴**，模板100260176，UID339，主背包绝对槽37（程序槽位，不要求按UI第37格找）；+0，耐久20/20，未封印/锁定，无增幅属性。名称来自PVF `[name]`，不是自行命名。
- 普通强化首行失败阈值0：本服计算金币450、无色小晶块3037×10。操作前以强化UI显示确认；若不匹配，保留界面/日志并调查。

## 先执行这两项

| ID | 操作 | 本次检查点 | 状态 |
|---|---|---|---|
| R-01 | 登录1123，找到上述皮靴，打开NPC普通强化，执行一次+0→+1 | 显示+1；只扣450金币和10无色；若期间没有其他资产消费，余额179418 / 无色9796。UID保持339，其他属性不变，日志仅一条成功提交 | 待验 |
| R-02 | 正常退出并重登1123，查看同一件皮靴及资产 | +1及R-01实际提交后余额保留，重登不重复扣费 | 待验 |

只先操作一次R-01；R-02结束后用户报告编号与可见结果，再由服务端日志/只读数据库核对。若背包位置改变，按UID追踪当前槽，不把旧slot37当固定身份。若金币基线已经因其他操作改变，记录点击前余额并核对差值450，不能机械套用旧绝对余额。

后续R-03降级、R-04保护、R-05销毁、R-06拒绝、R-07受控重放继续按原清单准备，尚未执行，不自动触发破坏或重放。

## 采证

本轮`server.stdout.log`记录`UPGRADE_ITEM character=... seq=... slot=... old=... new=... outcome=...`，`server.stderr.log`保存异常。A21-protocol目录ClientHack.ini中FrameCapture已Enabled=1、LogFile=clienthack-frame-capture.log；目前没有据此宣称已获得本次强化原始帧。若实际使用另一客户端目录，需要核对其抓帧设置；只有真正产生的请求/响应hex才可补WireCaptured。

本轮自动验证：Application宠物/架构/普通强化126通过，真实内容42通过，Protocol1137通过/60跳过，Release整套构建0警告错误。它们是CurrentImplementation，不能替代上述GUI验收。
