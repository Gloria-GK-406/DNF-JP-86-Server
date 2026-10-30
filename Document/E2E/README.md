# E2E 场景收集与本地 CI

关联 [#212](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/212)。[场景台账](场景台账.md)维护原目录的 348 个 caseId；步骤定义仍引用 [验收目录](../E2E-验收全测试集-20261027.md)。

## 当前状态与证据门槛

**正式 E2E：已签收实机样本 0，已固化基线 0，正式执行 0，正式通过 0。**
六项由代码推导的测试已移至 [独立基础设施冒烟集](../../Tool/Smoke/README.md)，正式台账恢复为待实机采集、未固化、未实现。脚本新建的 synthetic-smoke-v1 数据库不是实机场景库，不能导入正式基线。
旧 [CI 41009354129](https://github.com/Gloria-GK-406/DNF-JP-86-Server/actions/runs/41009354129)仅证明基础设施和自编冒烟能运行，撤回其“正式 E2E 六项通过”的含义；原始历史记录保留。

正式 case 必须关联：实际客户端/补丁/PVF/Host 身份、操作步骤、完整双向报文、准备阶段场景库、步骤前后数据库一致快照、人工观察与签收、固定/动态字段依据及已批准预期。仅有服务端当前输出不能代替这些材料，不得自动将实际输出写成预期。

采集顺序：**真实 A21 操作 → 人工确认行为 → 归档报文和数据库 → 审核并固化基线 → 编写自动执行与断言**。首批准备流程见 [登录、建角、入城实机采集](首批实机采集流程.md)。

## 保留的框架

- Tool/E2E/harness.py：独立 Host/模拟客户端、A21 帧与只读 SQL 工具；尚未适配正式采集库消费。
- Tool/E2E/report.py：JSON、JUnit 与 Markdown 报告，零 case/失败/阻塞不报通过。
- Tool/E2E/run.py：正式入口目前仅报告缺少已签收实机样本，不创建账号、不生成数据库、不执行旧六项，退出码 1。
- Tool/Smoke/：保留合成账号、脚本生成数据库和旧六项，编号 SMOKE-*，仅供基础设施诊断。

```bash
python3 -m unittest discover -s Tool/E2E -p 'test_*.py' -v
# 当前预期为 blocked 和非零退出，不是 E2E 通过
python3 Tool/E2E/run.py
```

采集完成前不恢复自动执行旧 case。未来正式执行必须复制经采集确认的 baseline，使用真实网络与完整生产 Host，动态票据本轮获取，运行中不补库帮助通过。

## CI 与 runner

[正式 workflow](../../.github/workflows/e2e.yml)：Server E2E (real-client baselines)，目前仅手动触发，缺样本时明确失败并保存就绪报告。
[独立冒烟 workflow](../../.github/workflows/e2e-smoke.yml)：Server infrastructure smoke (local PVF)，也仅手动触发；结果写入 artifacts/smoke/，不混入正式通过数。

runner dnf-e2e-ubuntu-210 位于 Ubuntu 24.04 x64 主机 192.168.8.210，Tailscale 名称 lan-ubuntu-210。标签 self-hosted, Linux, X64, dnf-e2e；systemd 开机启动保留。两个 CI 共用 concurrency；实机采集前仍须确认没有冒烟进程占用固定 Game 端口，不停止他人进程。

| 内容 | 部署路径 |
|---|---|
| runner | ~/dnf-e2e/runner |
| .NET SDK | ~/dnf-e2e/dotnet |
| 本地 PVF 与哈希 | ~/dnf-e2e/assets/Script.pvf、Script.pvf.sha256 |
| CI 工作目录 | ~/dnf-e2e/runner/_work |

当前本地 PVF 为 73,085,159 字节，SHA-256 为 01f1be685d46647961df65207a36304777db20ef5a3d03c68efaa92f8b0c5189。它是待锁定采集素材的当前快照，不因为冒烟通过就成为正式实机基线。
PVF、SQLite、密码和票据不进 Git、不上传公开报告。原始实机证据存私有采集目录，分享前另制脱敏副本。

前置数据 / 人工实机 / 原生采集 / 正式基线 / 自动化分别升级；每轮结果独立保存，不覆盖前次失败。实现、实机接受与官方原服协议证据始终分开。
