---
kind: rks-review
target: tasks/TASK-20.md
verdict: approved
---

# TASK-20 结果快速审查（测量闸 M-1，独立审查者）

- 审查对象：`tasks/TASK-20.md` Revision 1，第 1 轮执行（含 S3 补测与两条主控方裁定），基准 `5f7dba11`；改动为相对 HEAD 的未提交差异。评审者只读，本文件由主控方按评审回报落盘。
- 适用标准：reviewing-work 的 Task Unit 快速审查，加 Deliverable 与 Code 标准。
- 复算：评审者另写只读脚本（scratchpad `t20-review/`），从原始 `samples.csv` 与 `host.log` 独立复算关键数字。

## 结论

判定诚实，关键数字能从原始数据独立复算；没有被漏判的越线，也没有越线样本被归为「受污染」而排除；两条主控方裁定都有事实基础。

## 发现（均不阻断）

- **L1**：1188 包/秒是 18 秒一档内的上限，接近饱和：1200 档 04:19:10～:25 执行器作业执行时长之和 13.3 s（占用约 89%），档末 5 秒发出滞后 max 423、381、530、457、652 ms 呈上升；800 档滞后 40～195 ms 无趋势；1600 档占用约 99%。S3 结论不受影响（800 档已稳定满足 ≥ 400），但不宜作为余量依据。
- **L2**：`judge.py` 的「预期，不计」规则只匹配同时含 `Gameplay `、` failed: ` 与 `Writing is not allowed after writer was completed` 的作业失败行，不吞其他失败；本次数据下没有误吞（12 次运行只有这 8 行）。但它没有时间窗，M-2 复用时可能吞掉服务端主动关连接引起的同类异常。
- **L3**：「与基线同类」是读代码得出的推断，基线上观测不到（基线构成 A 爬坡第一档被 SQLite 停顿卡住，构成 B 收尾队列为空）。代码依据逐项成立：`TouchSocketNetworkMessageSender` 自 `a02bce46` 只包了 `ExecutorSendDeadline.AwaitAsync`、异常原样上抛；TouchSocket 一直是 4.3.1；8 个作业排队 25～28 ms 期间 writer 已 complete，连接关闭不等接收回调结束。
- **L4（核实结果）**：失败限于单个作业（`Job<T>.RunAsync` 的 catch 只设失败结果），日志中 `program error`、`crit` 为 0；执行器继续运行；`GameConnectionCheck` 不碰状态，`SetUserPositionHandler` 先内存更新坐标再投影发送、不开事务。唯一新增代价是积压时已关连接的排队作业照常执行。
- **I1**：(f) 构成 A 冷启动最长停顿 1189.6 ms 是目标 250 ms 的 4.8 倍，§1 只写了「约 2 s 线的 0.6 倍」；向用户转述时应一并说明。
- **I2**：Result Checks 6 写 27 个路径，日志为 28 个；每次运行收尾有 54～267 行 `Connection … is not active` 警告（关连接那一秒），summary 只在 ramp-A-2s 提到；A-def-2 有 3 行 timed_out 在所有窗口之外。

## 已核实无问题

| 项 | 结果 |
|---|---|
| S1 作业层 max | A-dbg-1 236.1、A-dbg-2 229.5、B-dbg-2 233.7、ramp-A-2s 227.7 ms，都是 `inbound:GameSelectCharacter` |
| S2 入队等待 max | A-dbg-1 1074.5、A-dbg-2 1245.8、B-dbg-2 234.9 ms；1074.5 那一刻前约 1.1 s 作业执行时长之和 1172 ms（选角 1067 ms） |
| S2 (f) 与 ≥ 2 s 样本 | 冷启动 max A 921.2／1189.6、B 133.0／132.4 ms；四次默认级别运行判定与参考序列 ≥ 2000 ms 的样本都是 0 |
| 2 s 口径爬坡 | 400／800 档完全一致；1200 档 max 668.5 一致、p99 456.0 对 462.5（口径差）；判据代码与裁定一致，档位到 25000 |
| S5 信号 | 12 次运行复核；只有 ramp-A-2s 有 8 个作业失败，时间、异常、作业类型与记录一致 |
| P1～P4 | 没有越线被归为受污染而排除；受污染的 B-dbg-1 两个窗口都没有越线 |
| 工具只做加法 | 删除的 12 行都是替换；默认 `p99:50` 分支的判据、输出格式与原来相同 |
| 归档 dll | `bin/m1-base/GameplayLoad.dll` SHA-256 与 `raw/tool-version.txt` 相同；新 dll `deb158a5…` 与源码哈希一致 |
| 基线对照 | 「M-1 预警」8 行都在；写明 2.73～2.79 ms 是基线构成 B，同构成对照 5～7 倍 |
| F 编号 | F-02～F-10 去向与裁定一致；F-77～F-83 数字准确；AC-005／AC-021 未写成已满足 |
| 加法与边界 | 选角补给命令份额等加法可复算；`git status` 只含 Owned boundary 路径，`Server/` 无 diff |

## 主控方处置

见 `tasks/TASK-20.md`「主控方处置（评审后，提交前）」：L1 补记到 F-77，L2、L3 补记到 F-83，I1 向用户汇报时说明，I2 改正路径数。
