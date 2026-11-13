# 泡点（在线时长奖励）

Issue #321，父级 #100「签到/在线/热时」。角色在世界内每满一个周期，给账号发点券／代币，
刷新客户端钱包并弹服务器提示。

## 配置

部署配置 `OnlineTimeReward`（`Server/src/Dfo.Server.Host/appsettings.json`，缺省值见
`ServerConfigService`），只由 `OnlineTimeRewardRuleService` 读取：

| 键 | 缺省 | 约束 |
|---|---|---|
| `Enabled` | `false` | 关闭时其余键不校验 |
| `IntervalMinutes` | `5` | 1–1440 |
| `Cera` | `0` | 0–1,000,000，每周期点券 |
| `TokenCera` | `0` | 0–1,000,000，每周期代币 |
| `Notice` | `true` | 是否弹服务器提示 |

开启而两种货币都为 0、或数值越界时启动失败。部署时用环境变量覆盖，例如
`OnlineTimeReward__Enabled=true`、`OnlineTimeReward__TokenCera=30`。

## 行为与边界

- 计时单位是会话（`SelectedSessionIdentity`，含连接代数）：进入世界后第一轮记起点，满一个周期发一次。
  重登、换角、换连接都重新计时；城镇和副本内都算在线。
- 定时任务 `activity.online-time-reward` 每分钟一轮，周期精度为分钟。一轮中断或停顿后只补发一次，
  不按错过的周期累计。
- 计时只在内存，停服丢弃未满的周期。到期时间在事务提交前推进，发放回滚时丢失该周期而不重复发放，
  任务按失败记录。
- 发放经 `IAccountWalletDomainService.AddAsync`；某种货币到达余额上限时该种跳过，另一种照发。
- 发放事件 `OnlineTimeRewardGranted` 在同一事务入队，`LiveOnly` 订阅者只投给赚到它的那次会话：
  先发钱包刷新（与 GM 发点券相同的 `CeraBalanceUpdateMessage`），再按配置发 NOTI `0x01F7` mode 0 提示
  「在线奖励：获得 N 点券、M 代币。」。会话已离开则跳过，下次登录读到新余额。

## 参照与证据

| 来源 | 内容 | 适用 |
|---|---|---|
| 70Reference：1995chen-dnf `plugin/70s2_dp_enhance` 的 `df_game_r.js` `enable_online_reward` | hook `CUser::WorkPerFiveMin`（0x8652F0C），每 5 分钟按本次登录分钟数 × 3 充点券并发系统消息 | 采用"在线周期发货币"的语义；不采用按累计分钟递增 |
| ServerS4A21 `Game/Events/PcRoomTimePoint/` | 客户端原生 PC 房时间点数活动（EVENT 228，`etc/pcroomtimepoint.etc`，四段日奖励＋周期奖励，领奖协议） | 另一种玩法，需单独取证，本任务不实现 |
| 第三方 S4A21 v3.3 `DfoServer.WebAdmin/OnlineReward*` | 按日／周／月／一次性时间点给当时在线角色发邮件 | 定点发放，不是时长累计 |
| 本仓 NOTI `0x01F7` | mode 0 布局 Confirmed（ClientStatic）；已用于黑钻提示 | 提示文本 UI 未实机观察 |

未覆盖：日上限、递增奖励、道具／金币奖励、GM 页面配置、A21 实机验收。
