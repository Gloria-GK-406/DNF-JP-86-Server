# A21 商城反馈复现与修复（2026-09-09）

## 身份与观察边界

- 客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- 静态镜像：`E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260831-083553-079-22620/DNF-memory-rva.bin`，SHA-256 `9B24CC177E489CDCAA0A2B27E742ACEF5C45FFBD0C89EDB5AFE8D61C0121E459`，VA 基址 `00400000`；下文地址均为 RVA。
- PVF：`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，SHA-256 `19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A`。
- 实机服务端日志：`Server/src/Dfo.Server.Host/bin/Release/net10.0/Logs/server-20260909-192001.stdout.log`（运行产物）。用户反馈：装扮选择期限/属性后失败，宠物蛋成功，黑钻无反馈，此后无法关闭商城。本次未保存原始全帧，不能声称已重放用户完整装扮请求。

## 装扮与宠物

日志记录商品 `105016` 成功，character=1、cera=80200、gold=66332、slots=1；随后商品 `630019` 被 `UnsupportedPayment` 拒绝：`Negative-price avatar also declares catalog payment fields.`。宠物蛋成功为用户反馈与服务端提交日志的一致观察，不推广为所有商品双向验证完成。

PVF `etc/cerashop.etc` 的 `[avatar]` 段第 206042..206047 个 token 为 `630019, 413550012, 2, 1, 0, -1`（PvfProbe tokens 输出编号）。该段共 34734 行，其中 1398 行尾部为 `1,0,-1`，其余 33336 为 `0,0,-1`。旧服务端把第四列套用普通商品金币列并一律拒绝非零，构成本次失败的确定原因。此标记的完整业务含义仍为 Hypothesis，仅支持当前档案观察到的 0/1，未知值继续拒绝。

装备 `equipment/character/knight/avatar/cap/chn_equ_upper_avatar_knight_cap_413550012.equ` 的 `[avatar type select]` 声明 7天/300、30天/660、永久/1980 点券；相邻商品 `630018/630019/630020` 分别取三档。价格来自装备档位，数量=1，不把上述标记计入金币。第四个带插槽档位仍不支持。可选属性 ID 0..4，分别对应 MAGICAL_ATTACK/MAGICAL_DEFENSE/HP_REGENRATE/MP_REGENRATE/CAST_SPEED。以上 PVF 数据为 Confirmed；跨其他档案不外推。

回归使用真实 PVF 和独立 SQLite 数据库，三档选择不同属性，断言扣款、金币保持、到货数量、持久化属性与期限；不修改玩家数据库。

## 黑钻请求与反馈

日志在上述测试后记录 `Unhandled CMD 0041 seq=40 body=0`。构包 `0108F32A..0108F33A` 只写 cmd=1/type=0041 的 header 后发送；前面的确认弹窗路径不写请求字段。handler 注册 `00D44E98..00D44EA1` 将 `00D1A0F0` 注册为 CMD0041。请求为空是 Confirmed。

通用 CMD 接收路径 `0123BF26..0123BF73` 先读 success，调用 `0234D880` 处理待应答计数，再按失败读 error；`0234D8F1` 为计数递减指令。`0123C078` 调分发，`0123770C` 调具体 handler。0041 handler 的失败分支直接返回，不显示错误；成功分支读 dstr + u32 并进入网页窗口。故采用明确失败 `00 01`，再发 NOTI01F7 mode0 文本，避免伪造成功网页票据。新实现补齐缺失应答，TCP 自动化证实随后 EXIT 正常；客户端内部商城标记是否全部解除仍需实机确认。

NOTI01F7 直接 reader 与布局见 [协议记录](../../../game/server-to-client/NOTI-0x01F7.md)。参照服 `TicketHandler` 的假票据成功实现带有鼠标消失注释，未采用。

## 回归证据

原始 TRX 位于 [Document/Verification/CeraShop-Feedback-20260909](../../../../Document/Verification/CeraShop-Feedback-20260909)。`red-avatar` 为修复前 3 项实际 PVF 装扮购买失败；`red-ticket` 为未实现0041造成 TCP 测试超时；`green` 为首次定向通过；`full` 为最终完整回归。检查各 TRX 的 UnitTestResult，不把无匹配项目的空 TRX 当成测试通过。
