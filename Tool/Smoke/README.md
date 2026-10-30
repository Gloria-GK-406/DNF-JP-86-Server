# 基础设施冒烟测试

此处保留原先由代码、协议文档和旧工具推导的 6 项网络测试，使用脚本自动创建的数据库。
没有本次真实 A21 操作、人工签收和正式报文基线，**不属于正式 E2E，不计入 348 项台账的实现或通过数**。
`SMOKE-*` 编号与正式 caseId 分开；结果 manifest 固定 `formalE2E=false`。

使用既有生产 Host、`Tool/E2E/harness.py` 的网络进程工具和本地 PVF；原测试库配方仅供冒烟。

```bash
export PATH="$HOME/dnf-e2e/dotnet:$PATH"
export PVF_ARCHIVE_PATH="$HOME/dnf-e2e/assets/Script.pvf"
export PVF_SHA256="$(cut -d ' ' -f1 "$HOME/dnf-e2e/assets/Script.pvf.sha256")"
dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj -c Release
python3 Tool/Smoke/run.py
```

结果在 `artifacts/smoke/`。独立冒烟 workflow 为 `e2e-smoke.yml`，仅手动运行。
旧运行 [41009354129](https://github.com/Gloria-GK-406/DNF-JP-86-Server/actions/runs/41009354129)
也只证明基础设施与自编回归能运行，撤回其正式 E2E 通过含义；不删除原始历史证据。
