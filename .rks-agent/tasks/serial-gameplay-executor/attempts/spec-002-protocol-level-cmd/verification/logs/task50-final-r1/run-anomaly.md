# TASK-50 正式调度异常

Application r1 已于 2026-10-25 16:14:40 UTC 自然结束，其 5659 个结果和源码身份有效。调度者误以为子 driver 的 App exit1 会传给父 script；`task48-run.py` 实际只记录并打印 child.exit，自身返回0，父 script 因而继续 Protocol→Networking。

原 Protocol 首轮在工具输出中为1786通过；调度者在父 session57693 尚运行 Networking 时，又启动 session91246 的 Protocol，同用 r1/Protocol 路径，覆盖原 Protocol 的日志、TRX 和 run.json。**原首轮记录不能恢复，不能将当前 r1/Protocol 冒称原记录。** 当前该目录是重复轮，其时间为16:14:53.749136～16:14:59.640193 UTC；与 r1/Networking（16:14:47.685797开始）重叠。两者均自然结束，未 kill，未改产品／测试／类并行或5m。

r1 Protocol／Networking 均排除最终串行通过证据；保存其现存材料及此说明。全部 runner 结束后，仅在独立 `task50-final-r2` 目录严格 Protocol→Networking 重跑，核每项 run.end 后才运行下一项。最终采用 App r1＋Protocol r2＋Networking r2；完整逐例比较记录该一次已披露的重叠，而不宣称所有历史 runner 均无重叠。
