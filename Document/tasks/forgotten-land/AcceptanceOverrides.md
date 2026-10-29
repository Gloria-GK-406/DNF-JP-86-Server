---
kind: rks-verification
decision: manual_required
---

# 遗忘之地时限与验收开关

当前运行候选已按用户要求关闭不限次，恢复正常入场额度；每局20分钟保留。关闭时没有重置真实次数、疲劳、库存，也不补扣不限次验收期间的入场。历史不限次配置只用于已完成的连续玩法验收，不代表当前状态。

## 正常配置

```json
{
  "LostLand": {
    "LimitSecondsOverride": 1200,
    "UnlimitedEntriesForTesting": false
  }
}
```

不限次默认false。`LostLandRuleService`读取该游戏规则配置，结合调用方传入的累计疲劳与已用次数返回`LostLandQuotaDecision`：开启时为最大1、剩余1的虚拟视图且无准入额度成本；关闭时为真实视图及应执行的额度限制。业务服务负责读取事实与执行结果，不读取这个开关。验收开关不修改历史账本，不能把恢复正常规则误写成将所有角色重置到1/1。

时限覆盖独立于次数策略：入场时将1200秒冻结到本局计划，倒计时通知与服务端截止共用该值；PVF原始首字段为600秒。白天、夜晚各120秒保持原值，不将PVF第二字段1200解释为延期上限。关闭不限次不移除用户批准的20分钟时限。

## 验证与运行边界

- 正常次数、每累计消耗100疲劳加次、重建服务保持与游戏日刷新，由`LostLandDungeonTests`覆盖；关闭开关恢复真实耗尽额度也有现成行为测试。
- 用户已确认森林红色晶石修复结果符合预期，允许进入PR准备；其他实机范围见[主玩法验收](Human-Test.md)。
- 关闭不限次前核实旧进程、命令行、创建时间、默认监听及真实数据库环境。新的私有运行目录`private/host-normal-quota-20261029`沿用森林修复程序，74个文件中仅appsettings配置改变；未部署变基后的源码。
- SQLite backup API一致备份、无监听副本启动及真实库启动对账均通过，115张表无变化；默认监听、bootstrap ready及GM200通过。证据为`evidence/normal-quota-deployment-prepared.json`、`normal-quota-smoke.json`、`normal-quota-smoke-rows.json`、`live-normal-quota-ready.json`和`live-normal-quota-assets.json`。
- 玩家库、备份、环境、凭据及运行产物保留在忽略目录。历史PID不能作为下次停服授权或身份依据。

## Unverified Conditions

正常规则的实机扣次／耗尽、100疲劳边界和自然跨日仍未逐项反馈，不由自动测试或关闭开关替代。1分钟原生自动结束已获实机确认；完整20分钟到期未单独验收。其余边界见[剩余清单](Remaining-Acceptance.md)。
