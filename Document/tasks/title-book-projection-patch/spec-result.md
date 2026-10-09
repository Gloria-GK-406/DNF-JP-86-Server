---
kind: rks-spec-result
state: delivered-for-client-acceptance
---

> 落地状态（2026-10-09）：用户已接受当前称号系统并要求PR收尾。下文保留当时交付证据；最新协议与验收边界见 [称号簿流程](../../AchievementTitleBookFlow-20261009.md)。

# 交付结果

任务契约revision1的研究、双仓实现、独立审查、自动验证及本地配套部署已交付。窗口、实际队列消费和保护函数调用仍待用户实机验收，不标作ClientVerified。

## 改动

服务器保留全量称号簿原件，根据MirrorSourceUid输出唯一来源。佩戴只创建镜像，取消匹配来源，佩戴原件不能取出/覆盖，同源重复操作保留UID及外观。装备投影后使用静默0166及TBM1尾部，移除000E全量书回填，不伪造0168。

ClientHack独立0200插件校验21个目标，书佩戴/取消走原生writer五i32，专用ACK只消费本请求pending；setter/rebuild按准确来源显示装备中。拖出佩戴原件拒绝；Main op6移出镜像转换取消。真实称号替换镜像为显式兼容019D单事务，失败不删除旧镜像，普通限时物品保留期限。0239外观独立。

审查期间修正actor VA/RVA错误、Main op类型过宽、普通真实称号替换的旧路由、等待快照永久busy、两阶段替换失败不变问题及真实限时称号的额外永久限制。最终实现已独立批准，见reviews/change-review.md。

## 验证与部署

- Application完整3294/3294；之后最后限时增量的TitleBook相关54/54。完整TRX不包含新增第7个限时案例，54定向TRX证明该案例。
- 最终Protocol完整1397/1397；插件Release构建无警告，C策略1/1通过；启动修订后20个目标签名匹配权威dump，Flush的1处匹配独立实启内存，双仓diff检查通过。
- 首次尝试保留异常审计并自动回退：赛马生命周期结算旧期1791460800、新增下一场1791468000，使四张赛程表变化；角色/物品/邮件/称号表没有变化。没有恢复数据库来覆盖结果，也没有放宽全表检查。第二次以当前数据新建一致备份，启动前后所有既有表内容不变。
- 最终运行：PID45676，`0.2.2+title-book-mirror.20261008.9da9b83e`，目录 `E:/Project/DNF/DNF-JP-86-Server/artifacts/title-book-mirror-patch-20261008/runtime`。
- 部署记录/两数据库备份/插件配置备份：`E:/Project/DNF/DNF-JP-86-Server/artifacts/title-book-mirror-patch-20261008/attempt2/`；首次备份与赛程差异证据保留在父目录。
- protocol2/plugins/0200_TitleBookMirror.dll 修订后SHA256=`025BF6FEC0AEAAD3995C8EFD50A16F7EF24AEDAB5B466AF14F6182EE70A0ADB2`，原EXE/PVF/其他插件不变。实启日志确认0200初始化、全部插件完成、游戏线程放行；尚未验证游戏内行为。

### 启动故障修订

首次用户启动失败来自新增0200对Flush RVA0234E6D0的精确签名使用错误：历史dump仍是保护入口，而同SHA256客户端实启后已展开为原生入口。独立只读采样19个函数，只有该项不同。改用实启16B精确签名，不放宽或跳过校验，并增加失败RVA/状态日志。重建、C策略测试与20个dump+1个live目标签名验证通过，替换实际protocol2 DLL。

自建启动验证PID56796，无登录参数，8秒后日志显示`TitleBookMirror plugin initialized`、`ordered plugin initialization completed`、`client thread released after startup initialization`；随后仅停止该自建进程。原始失败DLL/日志、独立live-sites.json、corrected-startup.log和新旧部署身份保存在`artifacts/title-book-mirror-patch-20261008/startup-recovery/`。服务端代码和运行进程未变，当前PID45676与GMready再次核实。该证据只证明加载和初始化，不替代称号簿实机验收。

## 用户验收

1. 原生与自由存入称号连续换三次、取消再佩戴：书原件一直可见、只有当前来源显示装备中，没有背包闪现或获得刷屏。
2. 佩戴原件拖到背包被拒；取消后自由称号可以取出。装备镜像拖到背包只取消，目标已有物品保持原样。
3. 背包真实称号替换镜像，普通卸下回背包；锁定/不符合资格的新称号替换失败时旧镜像和外观不变。
4. Shift右键换外观、重登录，属性来源与外观分别保留。重复ID只有实际来源行勾选。

若游戏无法启动，先检查ClientHack插件日志中的0200目标失配；退出游戏后移走0200并切回匹配旧服务发布目录。回退只恢复程序/插件，不覆盖数据库中新产生的数据。

## 2026-10-09 实机回归与修订

用户报告游戏崩溃及整页称号动画丢失。原客户端实现清cache+8误删展示ANI的缺陷已确认；崩溃栈吻合原生提示借用旧佩戴对象路径。修订保留同ID展示资源、按分类刷新，并在提示入口核对借用对象身份；独立审查另补同槽不同ID更换资源的边界。修订插件于09:02部署至protocol2，SHA256=`868186A0478F03199CA00C94122418FA6752149F1A552BE2D1941D21645125BA`；本节替代此前DLL当前部署身份。详见 `render-recovery-20261009.md`、相应独立审查及验证记录。原版本“待验收”没有转为ClientVerified，新修订仍待用户实机测试。
