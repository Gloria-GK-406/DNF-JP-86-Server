---
kind: rks-review
verdict: approved
---

# Change Review: 地下城失败虚弱与自然恢复（Round 1）

## 评审目标与范围

- 目标：基线 `88741631d4f104fe63d35945f896dd492d647c50` 上当前完整未提交工作树。
- 治理来源：`TaskContract.md` revision 1、`spec.md` ready。
- 评审准则：`reviewing-work/references/code.md`；同时核对本次新增说明文档与协议资料是否和实现一致。
- 范围：内存虚弱状态、失败/成功退图、断线和全灭、自然恢复、登录/回城/在线投影、DI 与配置、现有生产出口接线。付费恢复、豁免资格和持久化不在范围内。

## 结论

当前目标在已批准范围内没有未解决的阻塞缺陷。此前“先移除 run、后异步读取等级”可能在读取失败时永久漏罚并中断全灭回城；当前实现已用内存待施加对象修复：退出先登记待施加项，读取失败保留并让退出完成，统一周期后续重试；对象身份比较阻止旧读取覆盖 `Recover` 或后一次失败，暂停/恢复也同时作用于待施加项。

## 关键证据

1. **规则与状态符合契约。** `WeaknessRuleService` 校验等级门槛、初值、周期和增量；`WeaknessDomainService` 只保存 `Value`、恢复开关及暂态待施加对象，满 100 删除记录，无记录读 100。`SetWeakness()` 无数值参数，`Recover()` 同时清除当前值和待施加项。
2. **恢复与异步补偿顺序正确。** `AdvanceWeaknessRecoveryCommandHandler` 先推进既有恢复项，再重试待施加项，避免本周期刚补出的 10 立即变为 19；补偿结果并入当轮投影集合。待施加读取逐角色隔离，一个成员失败不会阻断其他成员或 run 退出。
3. **入本、退图和幂等边界完整。** `PreparePartyEntry` 对实际参与者暂停恢复；`DetachParticipant` 恢复计时。`EndAsync` 仅接受当前角色的 exact run id，按 `ClearOpen` 区分失败与通关，并在成功 detach 后至多登记一次失败处罚；全灭使用同一异步入口。旧 run 的重复退出不能影响新 run。
4. **现有生产出口已统一。** 主动放弃、EPLP 离本、返回村庄、离队、踢队员、选角/断线清理和全灭计时均调用 `EndAsync`/`TryEndPartyWipeAsync`；源码中没有生产调用遗留的同步 `End(...)` 或同步 `TryEndPartyWipe(...)`。
5. **会话投影有当前性约束。** 登录投影在选角生命周期独占锁内发送；回城经 `TownAreaProjectionDelivery` 发送；周期投影取得账户 transition gate。`WeaknessProjectionDelivery` 同时复核角色与连接仍指向同一个 `SelectedSessionIdentity`，并串行化读值与发送，旧连接不能覆盖新会话。
6. **DI 没有扩大无角色启动依赖。** `IWeaknessDomainService` 通过 `Func<ICharacterDomainService>` 在实际施加时才解析角色服务；真实 Host/网络空场景不会因注册虚弱服务而提前构造完整角色读取链。日志依赖由容器提供。
7. **文档没有提高证据等级。** `Document/地下城虚弱与自然恢复.md`、`NOTI-0x0021.md` 和 `protocol/catalog.md` 明确区分已实机确认的登录 32/100 单值实验与尚未实机验收的完整生命周期，并保持付费恢复和免罚资格延期。

## 检查结果

- 评审者 fresh 运行 `WeaknessTests`：18/18 通过。
- 评审者 fresh 运行 `WeaknessProjectionTests`：4/4 通过。
- 当前提交方证据：Application 1669/1669、Protocol 1007/1007、Host 17/17、Release 构建 0 warning/0 error。本批准不替代后续 completion verification。
- Networking 当前 52 通过、14 失败；同一基线复现的失败测试集合完全相同，根因是既有无角色 fixture 缺 `ICharacterEntryTable`，不是本次惰性虚弱依赖引入的回归，因此不阻塞本评审。
- A21 完整生命周期实机验收尚未执行，资料已如实标注，按 TaskContract 留给用户后续验证。
