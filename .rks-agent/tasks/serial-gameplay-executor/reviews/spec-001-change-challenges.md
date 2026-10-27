# spec-001 完整交付挑战集（2026-10-24）

- 独立生成：a_final_challenges，generating-adversarial-challenges；目标a02bce46..9aeb638c加TASK35（现已按相同内容提交0ecb9af3）。根契约r4、子Ar2、SpecR4、Plan及实际源代码；聚焦跨单元失败传播，不把已移交B算A遗漏。未跑全量；挑战不是verdict。

## CH-1：首个结束挂钩失败后，后续挂钩被跳过

- Risk: low-risk
- Failure and impact: RunEndHooks注释承诺每个挂钩一次，但first ??= hook(end)会在first非空后跳过后续调用。未来多挂钩时可能漏收尾；当前Server/src唯一OnEnd调用是GameplayOutboxes.Create，生产单例正常只挂一个，未找到A当前生产受损路径，不足阻断。
- Evidence: Server/src/Dfo.Server.Abstractions/Execution/GameplayJob.cs:158-175（168行）；Application/Execution/Outbox/GameplayOutbox.cs:51-58及GameplayQueue单例注册。探针：首hook返回/抛异常、第二hook计数，现预计第二为0。
- Minimum correction: 可记录低风险残余；修时先无条件调用hook再保存首错，补返回/抛出两种用例，不改设计。

## CH-2：定时失败吸收仍依赖Warning提供器不抛异常

- Risk: low-risk
- Failure and impact: TickAsync吸收后LogWarning若被异常日志提供器抛错，仍可漏到BackgroundService并停宿主。需叠加故障提供器且无部署发生证据，属日志健壮性残余，不否定正常日志环境修复。
- Evidence: GameplayTimerWork.cs:48-55；测试RecordingExecutionLogger不抛错；GameplayExecutorRobustnessTests只覆盖执行器自己的包装。探针：Warning抛异常的logger配作业失败观察TickAsync。
- Minimum correction: 可记录残余；如要求日志故障不结束服务，只需防护此Warning并配区分用例，不做全仓日志重构。
