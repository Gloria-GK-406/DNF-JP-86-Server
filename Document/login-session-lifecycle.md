# 登录会话与TCP连接生命周期

## 规则（CurrentImplementation）

一次启动器密码登录签发一对随机 mId/ticket，保留既有HTTP字段及Game两个DString布局。凭证只在内存中保存摘要，关联稳定AccountId，不写数据库。新登录替换旧会话，进程重启使所有凭证失效。

| 状态/事件 | 行为 |
|---|---|
| 尚未首次接入 | 签发后180秒内有效，截止时刻拒绝 |
| Game LOGIN成功 | 事务提交时绑定连接，原180秒截止解除 |
| 在线 | 凭证保持有效 |
| 换频道/新Game连接 | 同一凭证可以交接；提交后原连接立即标记不可接收新命令，并排队清理身份、关闭传输 |
| Game断开 | 仅当前绑定的连接可启动300秒宽限 |
| 宽限期内接入 | 绑定新连接，清除截止时间 |
| 宽限到期 | 校验直接拒绝，无需等待清理任务 |
| 新密码登录 | 替换旧凭证，旧连接被标记失权并异步关闭 |
| EXIT 0003 | 撤销当前连接的凭证，仍ACK并允许客户端退出flush |
| crash | 按断线处理；重开启动器登录立刻替换旧凭证 |

返回频道页面若没有Game连接，停留超过5分钟需要重新登录启动器。Channel连接不保活Game登录会话。

配置：`LoginSession:InitialLifetimeSeconds=180`、`LoginSession:ReconnectGraceSeconds=300`，必须为正整数，启动时读取。

## 实现边界

- `LoginSessionRegistry` 管会话与当前连接索引；事务准备阶段预约绑定，失败回滚释放预约，提交后才交接。重复断线不延长宽限，旧连接断线或退出不影响新连接。
- 网络分发捕获连接绑定的授权代次，命令取得账户/事务锁后再次校验；排队中的旧写命令不能因新登录发生在排队期间而越过撤权。请求结束即释放ambient授权，后台任务不会永久继承这次网络请求的限制。
- `IAuthenticationDomainService.BindSessionAsync` 校验并绑定凭证；`IGameLoginAuthenticationDomainService.AuthenticateSessionAsync` 在同一事务准备在线身份。
- `LoginSessionConnectionObserver` 处理传输断线，包括登录回复发送失败后的断线；提交间丢失目标连接时进入宽限，避免形成永久在线凭证。
- `LoginSessionMaintenanceService` 每进程一个30秒过期清理循环，另一个异步队列消费者发起旧连接关闭。无每角色timer。
- 角色、队伍、副本等清理由既有OnlineUserConnectionObserver完成；关闭请求返回不代表所有清理回调已经结束。
- token验证、过期判断不依赖后台循环是否恰好运行。EXIT不强关TCP，保留历史flush时序。

## 自动化与实机边界

可控时钟覆盖在线超过初始期限、299/300/301秒边界、新登录替换、旧断线/退出、重复断线、事务回滚与提交间断线。
真实Host测试覆盖HTTP登录、两个游戏监听端口交接、断开再接入、新登录关闭旧连接、旧凭证拒绝、EXIT撤销；网络测试覆盖登录回包失败后重接。

仍需游戏客户端实测：返回频道后重新进入、停留5分钟超时、退出客户端与切换频道是否使用不同的退出序列。这次未修改客户端，不新增ClientVerified标记。

## 本轮验证（2026-09-27）

- Windows Release全解决方案：3648通过、457跳过、0失败；日志 `artifacts/login-verified.log`。
- Linux（WSL Ubuntu）Release认证/Login相关筛选：108通过、0失败；日志 `artifacts/login-linux.log`。
- 独立审查通过；审查发现的旧连接排队写入竞态已修复，并以真实SQLite和命令管道回归覆盖。
- 本轮仅本地实现和提交，未部署、推送或合并；客户端实机验收仍按上述边界进行。
