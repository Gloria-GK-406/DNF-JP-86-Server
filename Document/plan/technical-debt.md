# Technical Debt Tracking

This table records only active technical debt. Delete the row when the debt is resolved.

| ID | Debt Item | Content and Risk | Introduced By Task | Resolution Stage |
| --- | --- | --- | --- | --- |
| D-001 | 区域切换期间账号门持有到网络投递结束 | `SetUserAreaHandler` 为保证范围变化与 Party 投影的一致性，在捕获并投递全部网络消息前一直持有账号 transition gate。慢连接可能延长同账号 replacement 登录等待；当前以正确性优先，后续可在投影计划完全冻结后把网络发送移出门。 | `.rks-agent/tasks/party-service-directory-scope-refactor` | |
| D-002 | Presence 每角色顺序锁不回收 | `SessionPresenceDomainService` 为防止旧 presence callback 覆盖新范围，给每个曾出现的角色保留一个顺序锁；长时间运行且角色基数持续增长时会产生小幅无界内存增长。当前单对象开销小且不影响正确性，后续可在确认无在途回调后安全回收。 | `.rks-agent/tasks/party-service-directory-scope-refactor` | |
