# WireSmoke

通过真实 TCP 驱动注册后的建角、选角与任务接受流程，保存帧和数据库行。
它验证当前服务端链路，不替代 A21 客户端验收。

## 运行

先构建服务端，然后指定真实 PVF：

```powershell
 dotnet build Server/86JP.Server.slnx
 python Tool/WireSmoke/capture.py after-change artifacts/wire-after --pvf E:/Project/DNF/DNFClient-A21-protocol/Script.pvf
```

也可设置 `PVF_ARCHIVE_PATH`。Bash 入口 `capture.sh` 转发相同参数。
`--host` 可指定其他构建的 `Dfo.Server.Host.dll`；`--name`、`--job` 指定测试角色。
需要 Python 3 和可运行该构建的 .NET Runtime，不需要第三方 Python 包。

每次启动独立临时数据库，并选择四个空闲端口供 Channel、两个 Game listener 和启动器 HTTP 使用。
工具不删除或修改构建目录里的数据库，不停止已有服务；端口若在启动前被抢占，本次运行失败。
UDP discovery 与 GM Web 在测试宿主中关闭。宿主进程在正常结束或异常时退出，临时目录随后清理。

每轮通过 `/api/launcher/v1/register` 注册临时账号，每条 Game 连接前重新调用 `/api/launcher/v1/login`。
Game 登录使用响应中的六位 `mId` 与 32 位大写十六进制票据；不再使用旧 MD5 登录。
凭据通过子进程环境传递，登录请求体不写入帧归档。

## 产物与失败

- `entry-batch-<label>.txt`：选角帧；`entry-probe-<label>.log`：探测摘要。
- `quest-accept-<label>.txt`：任务 1016、101 的接受响应及重复接受拒绝。
- `quest-rows-after-entry/accept-<label>.txt`：相应数据库任务行。
- `host-entry/accept-<label>.log`、`create-entry/accept-<label>.txt`：启动与建角诊断。

启动、注册、登录、建角、选角、任务探测、读库或已请求的比较失败，都返回非零退出码并保留诊断文件。
不同 label 可用于改动前后捕获；`framediff.py` 可比较两份帧文件。身份、时间及角色内容可能变化，
具体差异须结合当前实现判断，不再把旧版本的固定差异数量当成自动通过标准。

## 历史参考比较

默认只捕获当前服务端，不假定本机有外部参照仓库。比较历史 A21 日志时显式提供文件：

```powershell
 python Tool/WireSmoke/cmpentry2.py artifacts/wire-after/entry-batch-after-change.txt comparison.txt --reference <packet_log.reference-20260823.txt>
```

`--enums <PacketTypesA21.cs>` 可选，仅补充协议名称。也可把这两个参数传给 `capture.py`。
输入不存在或参考中不足 66 帧会失败；帧数、协议顺序、长度或正文不同均返回 1，报告仍会写出。
这里的 66 帧窗口专门对应历史 SELECT_CHARACTER 响应批次，不声称当前版本必须仍有 66 帧。
历史“66 对 66、60 帧相同”结论见
[2026-08-25 入场比较](../../protocol/patterns/data/traces/a21-entry-batch-diff-20260825.md)，不作为当前验收结论。

直接运行单个探测器时，先设置启动器刚签发的 `SMOKE_MID`、`SMOKE_TICKET`，然后使用：

```text
wireclient.py --port <port> [--name Probe] [--job 0]
entryprobe.py --port <port> --out <capture.txt> [--index 0]
questprobe.py --port <port> [--index 0]
questrows.py <database.db>
```

工具自身回归：`python -m unittest discover -s Tool/WireSmoke -p "test_*.py"`，已接入 CI。
