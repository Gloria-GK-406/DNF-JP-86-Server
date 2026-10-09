---
kind: rks-verification
decision: pass
---

# 完成验证

声明：任务契约 revision 1 的实现、独立审查、自动化验证及服务端部署完成。客户端交互效果待用户验收；不是 ClientVerified。

| 成功条件 | 当前证据 |
|---|---|
| SC-1 任务完成与最终职业，保留正式完成路线 | `state-events-final.trx`：95/95；含 QuestCompleted 最终职业及 TitleQuestUnlockRouting 回归。 |
| SC-2 统一物品事实及对应固定条件 | `application-final.trx`：3307/3307；`persistence-final.trx`：466/466。覆盖数量变化、仓库转入、共享材料多角色、装扮、穿戴、Creature facet 先打开的孵化及未初始化角色两级依赖。 |
| SC-3 幂等、只读、回滚与职责 | 同批边界测试及 `../reviews/change-review.md` approved；`git diff --check` 通过。事件同事务、同容器合并，查询不发事件，重复消费不重授予。 |
| SC-4 实际部署与数据保全 | `../deployment.md` 与根仓库 artifacts/achievement-state-events-20261009/deployment.json：版本身份、PID、所有监听和 HTTP ready；SQLite integrity ok；live-audit.json 的 changed=[]。 |

## 检查与证据边界

- PVF：`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，通过 PVF_ARCHIVE_PATH 提供，最终测试没有跳过。
- 最终应用全量 3307、持久化全量 466、称号协议 20，共 3793 项通过，0 失败；核心 95 项是应用测试的子集，不重复计数。
- `title-protocol-final.trx` 覆盖称号簿 mutation、称号外观、成就 wire；本次未改协议字节布局或客户端补丁。
- Application 首轮 3 个失败和复跑保留：两个超时隔离通过；NotificationRegistration 在 main 原样重现，正常日志权限的基线及当前全量通过。细节见 `../execution.md` 与对应 TRX。
- Release Host publish 成功；配置及 wwwroot/Resources 沿用旧运行目录。发布后资源复制遇到已存在目录，加入 Force 后补齐准备；旧进程在准备阶段未改动。
- 源码 manifest 与二进制 SHA-256 存在实际 deployment artifacts；部署前逐项核验已审查生产源码没有变化。
- 固定条件采用消费时的当前状态；不还原瞬时历史，不新增尚未支持的累计规则。

## 用户待测

登录后在本次会话完成转职/觉醒任务、取得所需物品数量、孵化宠物或穿戴条件装备，应即时解锁对应成就/称号。再次触发及重登不得重复授予；仅打开查询界面不应解锁。共享材料可检查同账号其他角色。
