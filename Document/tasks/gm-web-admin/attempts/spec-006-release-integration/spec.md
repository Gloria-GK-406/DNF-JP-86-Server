---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: GM 标准构建发布与最终验收

## Why and Outcome

从干净源码走CI/候选打包可得到完整后台页面；已发布Host无需Node即可提供GM。操作者能根据统一说明和验收表接手查询、邮件、点券与认证检查。

## Scope

接入现有Windows/Linux CI矩阵、Python标准候选打包与GM运行说明；验证本机Windows候选及已接受模块。保留版本0.2.0、tag/main历史校验、原资源/数据排除和组件开关行为。真实部署、管理员初始化、玩家资产、云端CI/Release与性能调优不包含。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | CI在.NET构建前安装指定Node、基于lockfile npm ci、运行全部前端测试及TypeScript/Vite build；任一失败失败，不沿用开发机dist。保留原Windows/Linux矩阵、服务端和release guard验证。 |
| RQ-002 | 标准本地/CI package入口总是从lockfile重新准备并构建前端；失败停止发布。候选包含HTML及其本地引用JS/CSS，文件与本次dist身份一致；缺失/越界/错误引用明确失败，不接受遗留dist掩盖失败。 |
| RQ-003 | 候选仍默认GM/Launcher/Game关闭；原环境资源排除继续有效，增加.env/部署秘密配置校验。包不含玩家DB/PVF/log/生产配置/管理员bootstrap凭据；release元数据标明本次GM资源身份，带运行和验收说明，不带Node运行时要求。 |
| RQ-004 | 冒烟在候选副本、fresh临时SQLite和本机临时端口启用GM，明确关闭Launcher/Game，清除继承bootstrap输入；验证GM页面/引用资源HTTP、无管理员initialized=false及私有API401。只结束自己进程，正常释放目录/端口，不污染archive。 |
| RQ-005 | 统一最终验收表按准备→登录权限→查询/在线→邮件→三币→重试/记录排序，列编号、操作及预期；已有自动/Chrome证据和A21待验清楚区分。运行说明包含构建/候选打包、独立开关和管理员部署秘密注入，以及真实现服仍原版本的边界。 |
| RQ-006 | 已接受后台与核心旧流程保持正常；完整前端及.NET回归、Python工具测试、当前构建与实际Windows自包含候选通过。Linux/远程CI只做配置/静态核对，不宣称已执行。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001,002 | 从lockfile重新npm ci/test/build成功；CI顺序和固定Action身份可检查。标准package重建dist并成功核对包内引用资源；缺引用资源、错误/越界引用及前端失败均明确拒绝，不形成合格候选。 |
| AC-002 | RQ-003,004 | 实际Windows自包含候选通过资源/默认配置/无部署数据检查，临时HTTP得到HTML/JS/CSS对应内容、initialized=false和私有401；自己进程退出、端口释放；zip内无冒烟数据库/日志。恶意或意外.env/非空bootstrap配置被拒。原tag guard测试不退化。 |
| AC-003 | RQ-005 | 独立文档审查确认所有根功能与顺序/预期齐全，A21真实邮件和三币待验、未执行远程平台以及当前运行版本均明确；部署示例不带真实凭据。 |
| AC-004 | RQ-006 | 全部前端、完整.NET（显式只读PVF）及Python工具测试通过，当前构建与候选实测退出码成功。现有HTTP独立开关矩阵、认证/分层、事务/领取/并发资产回归继续正常；source/main及PVF身份未变。 |

## Success Evidence

EV-001：CI静态顺序、Python行为回归、当前npm/.NET输出及本机候选包HTTP/内容/identity检查日志。
EV-002：Document运行/最终验收说明、独立完整结果审查；证据只支持CurrentImplementation及后台页面，A21依清单由用户验收。
