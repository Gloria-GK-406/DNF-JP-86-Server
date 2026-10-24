# TASK-07 证据

范围为四名册协议，起点d2e07630。最终状态以source-freeze-final.json和artifact-manifest.json的SHA为准；生成脚本和原log/meta保留实际操作轮次，包含编译／新oracle前置错误，不保证全部脚本可幂等重跑。前置错误不计行为RED。

- mutations.json：ACK前提前读取名册的真实坏变异，2过／1红，finally恢复SHA后3过。
- preflight.json及task-07-mapping/old-*：原字节、BOM、行尾。source-checks-final核精确15源码路径、原codec/encoder bytes、Golden15、旧API0及仅8ledger行。
- formal-runs保首轮完整build／五full／两control实际命令；formal-summary选最终Host02，全库8465仅数量账，未冒称八项目全量。
- host-port-diagnosis保原channel-connect失败和归因界限；未新增允许失败基线，未宣称修复端口竞态。
- DFO01为五full＋两control（Host01有一条测试失败）；最终Host02另新鲜目录／独立分类，不拼接记录或control。exe0只为本次观测。
- preclose仅预检；Result R1后strict/selftest。required独立结果快审和commit仍由主控负责。
