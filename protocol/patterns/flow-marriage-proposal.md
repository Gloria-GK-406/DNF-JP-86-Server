# A21 求婚与普通婚礼预约的申请、答复和超时

更新：2026-10-16。服务端基准 `c9acc87326448c900020f15f8a7d2c3b9e497a9e`，客户端补丁基准 `f717197eaadc20e5179b351b2b80f31f76fd4c08`。本页描述交互编排；字节布局以各协议页为准。

## 协议与正常顺序

| 阶段 | 发起者 → 服务端 | 服务端 → 发起者 | 服务端 → 对方 |
|---|---|---|---|
| 申请 | [C2S03F0](../game/client-to-server/0x03F0.md)，21B | [CMD03F0](../game/server-to-client/CMD-0x03F0.md)，12B | [NOTI03CD](../game/server-to-client/NOTI-0x03CD.md)，11B |
| 答复 | 接收者发[C2S03F1](../game/client-to-server/0x03F1.md)，22B | 接收者收到[CMD03F1](../game/server-to-client/CMD-0x03F1.md)，13B | 申请者收到[NOTI03CE](../game/server-to-client/NOTI-0x03CE.md)，12B |

以上均为正文长度。CMD回包包含1B结果信封，NOTI不含信封。C2S的14B opaque结构前缀仍属于正文，不能扣除或作为第二个帧头解析。

ClientStatic / Confirmed：原生UI以action区分求婚1与婚礼预约2；答复outcome为0接受、1拒绝、2超时取消。申请的tier、waitIndex和邀请的tier属于不同字段，不可混用。构包、接收RVA与正文证据见协议页。

CurrentImplementation：申请先提交Pending，再确认申请方CMD03F0，随后向精确接收会话发送NOTI03CD。答复提交事务后先向答复者发送CMD03F1，再向申请者发送NOTI03CE；成功接受时还向双方投影[NOTI03D0](../game/server-to-client/NOTI-0x03D0.md)。此发送顺序是当前实现事实，不能仅凭代码声称实帧完整归档。

接收者的target=0表示当前有效邀请上下文；服务端仍检查接收者、申请者、双方精确会话及关系。不能将0作为免校验路径。已匹配的有效婚礼邀请在接受重校验失败时，也向申请者发NOTI03CE/status=-3收尾；只发答复者CMD不能结束另一端等待。

## 自动超时与普通拒绝

ClientStatic / Confirmed：A21未修正的共享窗口超时dispatcher实际匹配action1/4/7，婚礼action2落到本地提示/关闭分支，跳过已有CMD03F1超时sender。不能从“窗口自动关闭”推定另一端收到答复。定向地址、字节和日志摘录见[超时证据](../../Document/MarriageTimeoutProtocolEvidence-20261016.md)。

当前客户端统一玩法兼容插件完成两项修正：恢复超时action1/2/7路由；将婚礼action2超时sender的outcome由2改为1。接收者保留原生本地超时提示与关窗，申请者收到普通拒绝NOTI03CE，走与手动点“否”相同的提示分支。线上outcome枚举本身未改变，action1/7的超时答复不受此修正影响。

```text
申请者：03F0(action2) → CMD03F0确认 → 等待
接收者：NOTI03CD(action2) → 原生本地超时
接收者：03F1(action2, target0, outcome1) → CMD03F1确认
申请者：NOTI03CE(action2, outcome1) → 关闭等待窗、普通拒绝提示
```

这条兼容路径复用现有服务器拒绝分支，没有增加服务端超时定时任务，也没有改变服务端有效期。CurrentImplementation仍校验预约有效期：过期后收到非outcome2答复会返回Stale，不能承诺任意网络延迟下仍是成功拒绝。已有一次自动答复日志距申请约6秒，仅能说明该次时序，不能据此定义客户端规范超时秒数或保证所有场景。

## 当前业务映射

| action / outcome | 当前实现结果 | 客户端验收边界 |
|---|---|---|
| action1 / 0 | 持久化订婚关系Engaged，投影状态与可接任务 | 用户已确认双端订婚成功；不是已婚 |
| action1 / 1、2 | 拒绝或超时，释放Pending占位 | 手动拒绝已确认；自动超时未据本次婚礼测试扩大验收 |
| action2 / 0 | 独立预约Reserved，投影03D0阶段2 | 用户报告普通档接受正常；本轮后续流程尚待实机 |
| action2 / 1 | 预约Rejected，结束邀请 | 手动拒绝已确认；用户亦确认兼容补丁自动发送1后普通拒绝文本与等待窗收尾正常 |
| action2 / 2 | 预约Expired，结束邀请 | 旧版路由补丁已观察到服务端成功处理；当前自动拒绝补丁改发1 |

CurrentImplementation的重登恢复来自订婚、预约与婚礼结算的持久事实；本轮普通档已扩展到自动入教堂、请柬/礼金、到点原生动画与结算、缺席退款。阶段4只由成功结算产生，铂金/豪华档继续拒绝。详见[完整流程与测试](../../Document/MarriageWeddingFlow-20261016.md)，旧[预约说明](../../Document/MarriageReservation-20261016.md)保留为历史设计。

## 证据状态

| 项目 | 已有证据 | 尚未覆盖 |
|---|---|---|
| 21/22B请求、12/13B CMD、11/12B NOTI布局 | ClientStatic / Confirmed，原生发送/接收链与绑定哈希 | 本次完整逐帧正文样本归档 |
| 求婚拒绝、接受、成功提示 | 有限范围ClientVerified：用户明确反馈双端操作成功 | 未测变体及每个字节的动态差分 |
| 普通婚礼预约手动拒绝、接受 | 有限范围ClientVerified：用户报告两项正常 | 所有档位、重登恢复的独立验收 |
| 旧版路由补丁自动发outcome2 | ObservedMetadata / Confirmed：接收日志、结果与回包metadata | 不能用该记录证明新版outcome1的提示效果 |
| 当前自动发送普通拒绝outcome1 | ClientStatic / Confirmed：构包前缀与手动拒绝一致；CurrentImplementation：13项客户端回归通过、两端DLL已替换 | 用户已明确确认普通拒绝提示和等待窗关闭，该路径有限范围ClientVerified；尚无本次完整正文归档 |

自动超时的实机结论来自用户明确反馈，没有以自动化通过或服务端成功日志代替。预约后普通新人进入教堂已另接入CurrentImplementation，详见[准备与入场流程](../../Document/MarriageChurchEntry-20261016.md)；原生入场尚待双端验收。
