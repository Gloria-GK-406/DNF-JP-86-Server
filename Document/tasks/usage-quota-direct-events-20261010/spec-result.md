---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result：次数配额与直接事件投递

## Delivered

- DirectAsync 不入队、直接等待订阅者并继承调用方事务；消耗品、权益、镜像次数的写入归统一配额服务。
- 成功业务与扣次同事务，拒绝/后续失败不扣；原次数数据、冷却、领域重置时刻和双倍罐续期总量保留。
- 提交后的变化经 EventService 投影最新权威快照；登录、选图、镜像加载后及在线午夜恢复镜像显示。
- 已部署 `0.2.3+quota-direct.20261010.r1`，保留原数据库及完整回滚输入；客户端人工测试待用户执行。

## Verification

- [当前完成证据](./verification/completion-evidence.md)，包括已知PVF失败边界、实际运行身份和逐行次数对照。
- [独立审查](./reviews/change-review.md)：approved，无未解决阻塞项。

## Repository Result

- Changed paths：AGENTS.md；Server下Abstractions/Application/Persistence/Protocol与相关测试；protocol三条协议及catalog；本任务Document。
- accepted baseline：a0ef0f150dc8bf9d567177a007cd1766033ad022。完整结果通过接受操作作一次本地提交，其新SHA从Git返回，不在包含它的文件里自引用。
- Parent rollback required：no。分支 task/monster-declaration-parser 与现工作区保留，未推送/PR/合并/清理。

## Artifacts

- [实机测试集](./client-test-set.md)
- [客户端证据](./client-evidence.md)
- [实施记录](./implementation.md)
- 实际部署回执：E:/Project/DNF/DNF-JP-86-Server/artifacts/usage-quota-direct-events-20261010/deployment.json
