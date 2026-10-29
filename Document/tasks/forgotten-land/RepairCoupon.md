---
kind: rks-verification
decision: manual_required
---

# 遗忘之地修理券

修理券扣量与单件耐久修复已通过离线验证和A21实机总体验收，用户反馈“修理券测试没问题”。日志对应角色8两次成功操作：目标装备12、17，材料源槽108，结果None。未单独反馈的边界不扩为逐项实机通过。
分支为`codex/77-forgotten-land`；整项功能仍在验收，保留未提交改动，不创建PR或调整版本。
修理规则唯一正文见[物品与容器](../../ItemContainers.md#装备修理与修理券)。部署沿用原玩家库、保留10分钟；部署核对时2026-10-29游戏日自然剩余1/1，角色8有7张券，没有重置次数或发放物品。这些数值不代表验收后的余额。

## 原因与证据

- CurrentImplementation：原CMD0017将非`-1`材料槽统一拒绝，没有消费修理券的业务分支。
- ClientStatic：A21映像SHA256为`6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`。
  VA023EC800构包第三段u16来自全局03992CAC；VA02319AD2–02319AD8调用该构包时尾部模式参数为0。
  成功接收函数VA01122C40读取金币、目标列表／槽和耐久更新控制字段；不把最后一个u16改成修理券源槽。
  [有界指令摘录](evidence/client-repair-coupon-paths.txt)保留来源和地址，未附加客户端进程。
- ClientStatic／内容：10093456为`[repair coupon]`，限定地下城4000和`[dungeon]`，可以免费修理一件装备。
  [脚本原文](evidence/chaser-other-items.txt)与实际PVF内容测试一致。
- 服务日志：上一轮三次CMD0017失败响应，修理券未被消耗。没有成对原始请求／响应体，不能将静态字段核对当成WireCaptured。

## 离线验证

所有资产测试使用临时SQLite和项目真实领域／持久化／协议调用链，不使用玩家库。

| 检查 | 结果 | 证据 |
|---|---|---|
| 原修理入口的失败基线 | 用最终测试设置及HEAD原CMD复核，背包／快捷栏／背包装备三种成功用例均因券请求被拒绝而失败；切回候选后通过。测试准备异常不计为业务RED | `evidence/repair-coupon-original-command-red.log` |
| 普通消耗入口的失败基线 | 原普通消耗入口将修理券判为Used，复现只扣券不修复 | `evidence/repair-coupon-plain-use-red.log` |
| 修理与物品使用回归 | 95项通过、无失败或跳过；覆盖背包／快捷栏、最后一张券、满耐久及错误动作拒绝、错图／城镇／加载／到期拒绝、锁定过期券、重复请求和物理帧重放、消费或耐久写入失败回滚，以及原NPC／自动修理和普通物品使用 | `evidence/repair-coupon-regression.log` |
| 实际PVF内容 | 33项通过、无跳过；包括修理券动作、4000限制、既有物品使用及遗忘之地内容 | `evidence/repair-coupon-content.log` |
| 协议Unit | 503项通过、无失败或跳过 | `evidence/repair-coupon-protocol.log` |
| 最终应用及结构集合 | 844项通过、无失败或跳过，包含修理、物品使用、应用Unit及CMD／通知／状态结构检查 | `evidence/repair-coupon-final-application.log` |
| 完整Release构建 | 0警告、0错误，仅编译，未运行Host | `evidence/repair-coupon-build.log` |
| 发布及副本启动 | Release发布完成；一致备份副本启动无监听，113张表指纹均保持，quick_check=ok；沿用原已部署GM静态资源，逐文件哈希一致 | `evidence/repair-coupon-publish.log`、`repair-coupon-smoke.json`、`repair-coupon-smoke-rows.json`、`repair-coupon-static-assets.json` |
| 默认服务切换 | PID36288已就绪，默认7001／10011／18080／18081监听、bootstrap ready、GM200；实际环境无时限覆盖，600秒；停服前后113张表无变化，角色8自然剩余1/1、券7张 | `evidence/live-repair-coupon-ready.json`、`live-repair-coupon-assets.json`，私有最终备份与恢复信息 |

不同测试集合存在重叠，不相加宣称独立测试总数。

主要命令：`dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --filter FullyQualifiedName~RepairCouponTests --no-restore`。
正式基线对比仅将CMD源码替换为HEAD版本并通过finally恢复候选；玩家库和服务不参与。
内容检查设置`PVF_ARCHIVE_PATH`为实际A21档案；完整构建使用`dotnet build Server/86JP.Server.slnx -c Release --no-restore`。

职责复核：Protocol只映射槽位、分流模式和投影；修理业务仍在原owner，券规则复用物品可用性判定，
消费复用材料及物品生命周期，耐久复用装备实例。没有新增资产副本、运行状态或存储表，也没有增加CMD基础设施例外。

## 实机步骤（候选部署后）

1. 用符合入场条件的角色，记录修理券、金币和一件未满耐久装备的当前值，再进入遗忘之地。
2. 从背包使用一张修理券，选择该装备：应只扣1张，目标耐久回满，金币不变，仍能移动／击杀／换房。
3. 对已修满的装备再尝试一次：应拒绝且不扣券。不要连续点击或故意构造请求。
4. 方便时将券放到快捷栏，用另一件未满耐久装备再测一次；最后一张券用尽时槽位应清空。
5. 回城重登：核对券数量、金币和装备耐久保持。城镇中的遗忘之地专用券应不能使用。

## Unverified Conditions

- 用户总体确认修理券通过；快捷栏、最后一张券及各拒绝情形没有逐项实机反馈，保留自动测试与实机总体结论的区别。PID和端口证据只代表部署核对时刻，后续操作须重新识别。
- 未捕获此次客户端修理器的原始8字节请求；候选增加材料槽、目标及拒绝原因日志，便于复测关联。
- 当前只验收PVF明确的10093456，不扩大为所有修理券或快速修理模式均已通过。
- 三排自动物品展示属于独立的[结果对账调查](ResultDiagnosis.md)，奖励语义未关闭；修理券不修改掉落或结算逻辑。
