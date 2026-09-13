# 组队表层工作树收口记录（2026-09-13）

## 收口基线

- 主工作树：`E:\Project\DNF\DNF-JP-86-Server`
- 清理前 `main`：`ea3961ec64b904a3235b8813297b32ed4116e247`
- 本次仅做历史状态收口、协议文档勘误与旧工作树清理，不改产品代码。

## 清理对象

| 工作树 | 分支 | 清理前 HEAD | 相对 main 的独有提交 | 处置依据 |
|---|---|---|---:|---|
| `entries-are-what-the-client-reads` | `entries-are-what-the-client-reads` | `f5c00fb8` | 0 | 唯一差异是执行状态未从 `in_progress` 收成 `completed`；该状态已带回主线。 |
| `party-mode-rules` | `task/party-mode-rules` | `9a3a0b49` | 0 | 剩余修改/未跟踪项仅属于旧 `spec-004-public-party-directory` 调查；其 `0x023C` 阻塞结论已被后续 `party-directory-completion` 的原生目录实测取代。 |
| `party-directory-client-attach-recovery` | `task/party-directory-client-attach-recovery` | `9a3a0b49` | 0 | 仅有任务本地抓取与恢复材料，无产品、测试、协议或客户端提交；成果已由后续目录任务覆盖。 |

以上三个分支的 HEAD 均为 `main` 的祖先。旧调查工作树内未提交的原始材料不会进入 Git；删除后不可通过 Git 恢复，但其有效结论已由主线中的验收任务与本记录保留。

## 明确保留

- `party-mode-story-cycle`：保留，不清理。下一步先做**剧情地下城**，并把现有的剧情地下城进入异常纳入同一任务；`cycle` 延后。
- `client-debug-lab`：按用户要求完全排除，本次不读取其内容、不修改、不清理。

## 协议文档收口

- `0x0010 SELECT_DUNGEON` 不再标为“未实现”：A21 教学/正式副本成功入场及 `DungeonNotFound` 拒绝已经实机验证。
- 历史调查文档保留当时证据，但显式标明其“无 handler/流程中断”结论是 2026-08-17 快照。
- 当前缺口为准入规则，以及下一任务要处理的剧情地下城目标解析/进入异常。

## 最终处置

- 三个清理对象均已通过 `git worktree remove --force` 移除，并执行 `git worktree prune`。
- 对应三个本地分支均已通过安全删除（`git branch -d`）移除。
- 删除后复核：三个路径、三个分支均不存在；`party-mode-story-cycle` 与 `client-debug-lab` 仍注册为工作树。
