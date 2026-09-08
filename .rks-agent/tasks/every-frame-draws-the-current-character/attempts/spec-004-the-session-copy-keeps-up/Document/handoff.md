# 接手与整改

2026-09-08，用户授权：「接手一下86 jp server的除了crap那个以外的另一个rsk agent sdd任务」。

复用当前任务 worktree `every-frame-draws-the-current-character`，HEAD `1d887c59a84845525963b3e8f7a6ab0ba0654b55`。主仓 main 为 `addbeb52`。原有两处产品/测试修改、根 map 修改和 spec-004 目录与评审列出的变更一致，均属于未验收的第四个子任务；不回滚原有工作。

恢复检查：根 phase=child_execution、last_transition=child-created，与转移表一致；唯一 running 行 spec-004；前三行 sealed；子契约 parent/贡献条目匹配，depends_on=[]；子 execution revision=1、parent_controller 匹配，尚无 sealed spec-result。恢复没有错误。

处置：task-rework。评审 BF-1..BF-7 属于既定范围内的验证、记录与正确性整改；当前子任务保持未封存。使用会话级执行清单，不另建 Task 单元。测试首先保护移动后副本选择帧的实际装备；原有测试只看快照或本次响应，不能证明消费端接线。并发结论单独核查。
