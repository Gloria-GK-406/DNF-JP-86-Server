---
kind: rks-task-result
state: accepted
contract:
  path: ./TaskContract.md
  revision: 2
review: ./reviews/accumulated-review.md
evidence: ./verification/completion-evidence.md
---

# Task Result: 响应式地下城同步与结算生命周期

地下城运行期现已形成一条共享实例驱动的闭环：权威拾取提交共享变化；剧情任务物品在同一事务更新背包与 derived Trigger；Boss 清关由共享 run 只开启一次；参与者各自结算、翻牌和回城，而共享牌面与接收者由 run 统一维护。

远端拾取动作仍属于客户端 P2P 表现边界，服务端只负责权威地面物消失和奖励，不制造第二动作真源。清关、牌面和离场投影均过滤 frozen connection generation，旧连接、replacement 和过期 timer 不会继承旧局通知或奖励。

四个子结果和根级累计复审均已接受。结果仅提交在隔离分支；按授权边界，尚未合并 `main`、部署或清理工作区。
