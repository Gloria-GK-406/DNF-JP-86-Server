# Implementation Result — round 1

## Changed

Host 现在组合共享模块、GameServer 与 HttpApi。HTTP 类库/测试项目重命名，HTTP 装配下沉；游戏装配和 PVF 预热进入 GameServer。共用一次 Host.Build，无新票据表或第二套 provider。新增只读游戏就绪契约，HTTP 单开可引导/注册，游戏未就绪时登录返回既有 503 形状。

配置在组合前统一加载供开关决策与最终 DI 使用；新增启动配置辅助类属于组合根。现有路径、监听配置键和 experience 覆盖优先级保留。发布脚本只补充组件说明文档，仍发一个 Host。

## Checks

- 原 Host 17 项基线通过。
- 新增 HTTP-only 用例在基线出现预期 RED（bootstrap 为 503）；迁移后 green。
- Host 18 项回归先通过；新增生命周期 5 项通过；依赖约束由静态架构测试维护。
- 全解决方案最终 Windows 3638 passed / 457 skipped / 0 failed（artifacts/full-final）；新增架构 1 项、HTTP-only 1 项、生命周期 5 项。
- 第一轮新增同进程测试清理遇到 SQLite 池持有文件，现已只释放测试数据库池；测试重复 StopAsync 改为一次。第一轮 Protocol 旧夹具遇到 duplicate column seria_luck_value，未改动相应生产或测试代码，完整第二轮通过；该旧夹具共享默认测试数据库，不能据此声称消除其首启并发风险。
- Windows/Linux 自包含程序均已发布并在独立数据、端口、工作目录运行：HTTP bootstrap 200、Channel 0B→0C、两个 Game 端口可达。发布闭网 smoke 也通过。
- 独立审查发现普通 SDK 不再自动复制环境配置，现已补齐 appsettings.*.json 的 build/publish 规则；临时环境文件在输出/发布中逐字节一致，并在运行时生效，探针源文件已删除。复审 approved。
- Linux 首轮全量中旧频道握手用例在读回复时达到原 15 秒超时；单独 Host 24 项复跑通过。未改超时或协议实现，最终全量结果及边界见 verification。

## Remaining

实现与独立审查完成；最终双平台证据见 verification/completion-evidence.md，本地提交状态见 spec-result.md。未修改现有用户实例或数据库。
