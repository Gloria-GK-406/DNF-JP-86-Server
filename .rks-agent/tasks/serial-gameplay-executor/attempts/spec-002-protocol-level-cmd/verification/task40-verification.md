---
kind: rks-verification
decision: pass
---

# TASK-40 verification

本结论仅覆盖 TASK-40 两个协议、两个 fine 与 F-88-recover；不代表 T-007 或整个 Spec 完成，也不替代独立快审。工作树 HEAD 为 `e8d9499f`，本次未提交。

| 必要条件 | 当前证据与结论 |
|---|---|
| 旧行为先钉住，纯准备可独立 | `logs/task40-old/default-final-r2-app/results.trx`：在旧 HEAD＋纯测试准备、默认实际注册识别、规定环境92/92。`logs/task40-old/pure-preparation.patch` 可供主控分段；原 Protocol 聚焦11/11。 |
| 结构／实际原边界 | `GameRecoverStaminaCommand`／`GameDailyReviveCoinCommand` 与薄 handler 唯一注册；fine 的生产／静态测试类型引用删除。新正式 flow 对真实声明断言 ProtocolNonEffect、ordinary、空 Participants，Recover 无足迹、Daily owners 精确等于原查询 session；现有业务／并发／周期／余额／SQL重试断言保持。 |
| F-88 会拒绝反例 | `logs/task40-new/mutation-build.log` 零警告错误；`mutation/results.trx` 单例因额外 Error4 帧红。guard已恢复，最终 focused/full 同一具名用例通过且金币／虚弱不变。 |
| VC-B9 与保持项 | 正式 `RecoveryAndReviveProtocolCommandFlowTests`：真实 action SQL／native deferred FK Commit、受控不可失败参与者 Commit／Rollback 第二因／fine 对应 whole DI scope、token 两种取消、独立 request错误；核资产、completion、cause、原 ACK、scope 帧序、生产 sender→真实 transport.CloseAsync 和原来源链／替换隔离。成功投影 encoder 两路保持原逐收件人隔离；普通空提交拒绝 direct encoder 断言旧 raw／新 PostCommitPublicationFailure，未扩无 session／online 分支。仅受控 transport，不冒实机。 |
| 冻结与正式运行 | `logs/task40-final-r2/build.log`：Release solution 0 warning／0 error。同目录 `focus-Application`94/94、`focus-Protocol`11/11；`full-Application`5462＝5444pass＋2原Dimension内容fail＋16skip，`full-Protocol`1781pass、`full-Networking`203pass。所有正式 full 均通过 require_escalated 原环境，原类并行、项目串行、原5m/detailed参数与独立DFO/TRX。 |
| N/D、失败身份与诊断 | 同目录 `Application-nd.json`：逐display＋status＋重数 N26/D0，仅本Task新用例；Protocol／Networking均0/0。两失败均7100/maze0/(1,0)缺Left map，与 TASK-39完全同名；其它既有状态保持。`dfo-{Application,Protocol,Networking}.log` parser分别退出0，总70＝原67反例＋3实际control；outside、exe、orphan均0。 |
| 账本与记录 | 仅五目标行变化，所有 original 与非目标行逐值相等；`strict.json`、`selftest.json`、`diff-check.log` 保存本次实际运行结果。AGENTS仅增清单、Spec仅追加VC-B9，未修订Plan或A制品。 |

首次误用 sandbox 的 full 仅作诊断：`logs/task40-final/full-Application`在原宿主timer用例达到5m非活动门后中止，无可用dump（mac task_for_pid权限/签名失败）；Protocol随后取消、该轮Net未启动。最终规定环境的该timer单例1/1、303ms通过；没有把环境对照写成具体FSEvents根因证明。其它五项目仍沿TASK-39历史证据，不称本轮fresh；T-007收口须再跑八full。
