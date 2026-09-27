---
kind: rks-verification
decision: pass
---

# 完成验证

验证日期：2026-09-27。对象：基线 d32ecfc9 上的 host-component-composition 最终变更，含独立审查要求的环境配置复制修正。

## 条件与证据

| 条件 | 结果与决定性证据 |
|---|---|
| SC-1 组合边界 | Host 架构断言、HttpApi 架构断言及独立审查 approved；Host 仅共同模块+两组件，无第二个 provider |
| SC-2 同进程票据 | Host 真实 HTTP→Channel→Game 用例通过；认证相关 Application/HttpApi 全套通过；票据实现未改 |
| SC-3 开关/关闭 | ComponentLifecycleTests 四组开关启动、readiness、停止重绑定；HTTP-only 引导/注册成功、登录503 |
| SC-4 预热屏障 | 阻塞/失败准备期间各端口关闭；真实 PVF Host 全套24通过；缺PVF Host仍可启动 |
| SC-5 兼容 | Windows 全套3638通过、457跳过、0失败；环境JSON临时探针在build/publish中逐字节一致，运行采用其日志配置；Linux最终结果见下 |
| SC-6 双平台发布 | win-x64/linux-x64自包含发布及闭网启动通过；两平台发布程序在独立DB/端口/工作目录运行，HTTP200、Channel0B→0C、两个Game端口可达 |
| SC-7 隔离交付 | 独立任务分支，任务契约/规格/计划/审查/结果齐全；提交前diff检查与提交后状态检查由收口门执行 |

## 可复核执行

- Windows：`dotnet test Server/86JP.Server.slnx -c Release --no-restore`；TRX `artifacts/full-final/`，3638 passed / 457 skipped / 0 failed。
- Windows真实PVF：设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` 后测试 Host.Tests；`artifacts/pvf-host/` 为24/24，最终复核见 `artifacts/verified-host/`。
- Windows发布：`RELEASE_VERSION=0.0.0-components RELEASE_RID=win-x64` 的 release.py package；以及审查修正后的 `dotnet publish ... -r win-x64 --self-contained true -o artifacts/env-probe`。
- Linux：WSL Ubuntu，SDK10.0.401，独立 `/var/tmp/dfo-host-component-20260927/source`；`dotnet test Server/86JP.Server.slnx -c Release --no-restore -m:1 --logger trx`。最终全量3638 passed / 457 skipped / 0 failed，TRX/日志复制至 `artifacts/linux-final/`。
- Linux发布：`dotnet publish Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj -c Release -r linux-x64 --self-contained true -o /var/tmp/dfo-host-component-20260927/publish -p:NuGetAudit=false`；离线还原仅此隔离检查禁用NuGet审计，未改CI设置。
- 两平台发布入口探测：`artifacts/published-network-smoke.py <publish-dir>`；证据 `artifacts/windows-network-smoke.log`、`artifacts/linux-network-smoke.log`。仅网络探测，无真实游戏客户端操作。
- 环境配置探针：`artifacts/env-probe.py`、`artifacts/env-probe-result.log`；临时源文件已移除。

## 异常与边界

- Windows首轮旧Protocol共享数据库夹具曾出现 duplicate column seria_luck_value，第二轮完整通过；没有改动该夹具或声称消除既有并发迁移风险。
- Linux首轮旧频道测试读取0B回复达到原15秒预算；单独Host24/24及最终完整解决方案复跑均通过，未修改超时、跳过测试或协议处理。暂不能归因其偶发原因；通过结果不抹去首轮异常。
- 457项缺PVF测试明确跳过，不计作通过。补跑有PVF的Host生命周期/登录覆盖不等于全量内容实机验收。
- 原Windows服务PID4248与Docker容器dnf-server-linux-test继续运行；主干仍d32ecfc9且干净。本次未替换部署、未推送或合并。
