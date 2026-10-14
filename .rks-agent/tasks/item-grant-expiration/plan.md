---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---
# Implementation Plan

1. 在已有物品模板解析面携带只读期限声明，独立期限规则以显式时间与DailyResetRuleService计算结果，不读写世界状态。先写三种规则与真实PVF读取测试。
2. 物品生命周期增加新建前准备接口，Create保底；发放/容器Add/预检在堆叠判定前准备，检查其他规划创建点，显式期限不重算。修复元数据差异导致的不正确堆叠。
3. 实际投递/溢出邮件/领取/同期限叠加/异期限分离、永久、登录清理回归；运行Application/Infrastructure及必要Protocol/Persistence回归。
4. 先只读预检历史数据，生成基于实际PVF的授权目标清单；操作前再次确认角色范围和runtime/DB身份。发布独立runtime，停服并备份SQLite，校验清单，原子改期后对比所有字节/其他字段，无直接删除；启动新runtime并验证健康、监听、程序集、DB。
5. 保存postcheck（保留/恢复/停止决定）、独立完整结果审查与最终核验，一次本地接受提交。

若原子改期失败则事务回滚；若启动失败，恢复旧runtime，不猜测全库恢复，不丢现场备份。改期完成后不会主动触发选角，保留目标给用户测试。默认1123范围若收到新选择，数据库操作前同步契约/spec/计划。
