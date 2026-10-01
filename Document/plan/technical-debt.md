# Technical Debt Tracking

This table records only active technical debt. Delete the row when the debt is resolved.

| ID | Debt Item | Content and Risk | Introduced By Task | Resolution Stage |
| --- | --- | --- | --- | --- |
| D-001 | 区域切换期间账号门持有到网络投递结束 | `SetUserAreaHandler` 为保证范围变化与 Party 投影的一致性，在捕获并投递全部网络消息前一直持有账号 transition gate。慢连接可能延长同账号 replacement 登录等待；当前以正确性优先，后续可在投影计划完全冻结后把网络发送移出门。 | `.rks-agent/tasks/party-service-directory-scope-refactor` | |
| D-002 | Presence 每角色顺序锁不回收 | `SessionPresenceDomainService` 为防止旧 presence callback 覆盖新范围，给每个曾出现的角色保留一个顺序锁；长时间运行且角色基数持续增长时会产生小幅无界内存增长。当前单对象开销小且不影响正确性，后续可在确认无在途回调后安全回收。 | `.rks-agent/tasks/party-service-directory-scope-refactor` | |
| D-003 | 协议覆盖检查依赖已知注册 API 名称 | 同步检查器已对当前调用形状、空提取和漏登记设置失败门，但若未来新增不同名称的协议注册 API，且旧注册仍存在，新路径可能不进入清单而不被发现；增加注册入口时须同时扩展提取器或改用可编译的注册清单。 | `.rks-agent/tasks/sync-protocol-documentation` | |
| D-004 | 异界气息污染装备尚无自然产出路径 | `spec-004-outworld-amplify` 已能处理拥有的 `AmplifyType=0x80` 装备，但当前掉落/赠予链没有已证实的污染标志生成；净化 UI 的自然玩家入口可能无法触发，只能先用隔离测试样本验收。应在掉落实例化的证据/概率规则闭合后接通，不可给所有装备通用赠予链随机加污染。 | `Document/tasks/equipment-development/attempts/spec-004-outworld-amplify` | 掉落实例化与 A21 实机验收阶段 |
| D-005 | 00CD action2 动态尾和纯金增幅路径隔离 | A21 `0x00CD` action2 会追加动态字符串，当前未确认尾部及材料/结果语义，服务端安全拒绝而不扣资产；若客户端 UI 可进入该路径，用户无法完成该操作。待抓帧与静态接收/真实 PVF 联合闭合后再实现。 | `Document/tasks/equipment-development/attempts/spec-004-outworld-amplify` | A21 action2 专项取证阶段 |
