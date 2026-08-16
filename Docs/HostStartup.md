# 86JP Server 启动与资源约定

## 稳定运行根目录

`Dfo.Server.Host` 始终以可执行程序集所在目录作为 Content Root。进程从仓库根目录、Host
项目目录、临时目录或发布目录启动时，配置和相对资源路径的解释方式相同，不再依赖 shell
当前目录。

编译输出和发布输出必须同时包含：

```text
appsettings.json
Resources/Channel/channel_info.etc
```

默认频道脚本配置为：

```json
"Channel": {
  "ScriptPath": "Resources/Channel/channel_info.etc"
}
```

相对 `ScriptPath` 从 Host 可执行目录解析。测试、容器或运维部署仍可配置绝对路径；绝对路径
不会再次拼接到 Host 目录。

## 启动期强校验

频道脚本是必需资源。Host 在启动任何 Channel/Game listener 前读取并验证脚本，将非空内容
保存为本次进程的只读快照。以下情况会让 Host 直接启动失败：

- 路径不存在；
- 路径不是可读普通文件；
- 文件内容为空。

异常会包含最终解析出的绝对路径。不要通过在输出目录根部复制第二份 `channel_info.etc` 来
掩盖错误；应修复项目资源复制、发布内容或 `Channel:ScriptPath` 配置。

启动成功后，即使进程当前目录改变或磁盘上的源文件被替换，客户端的 `0x09` 请求也只读取
已验证快照。这避免连接建立后才出现 `FileNotFoundException`。

## 有效配置摘要

Host 启动时记录以下非敏感值：

- Content Root；
- 频道脚本绝对路径；
- Channel 与 Game 对外地址；
- Channel listener 与全部 Game listener 端口。

日志不会写入频道 AES 密钥、账号信息或报文负载。看到 `Application started` 代表必需频道
资源校验已经通过；若资源无效，listener 不会先行启动。

## 本地联调示例

以下命令可以从任意目录执行，不需要额外设置 `Channel__ScriptPath`：

```bash
PVF_ARCHIVE_PATH=/absolute/path/Script.pvf \
Networking__Enabled=true \
Channel__PublicServerIp=100.100.51.178 \
Game__EndpointHost=100.100.51.178 \
dotnet /absolute/path/to/Dfo.Server.Host.dll
```

开发阶段也可以使用：

```bash
PVF_ARCHIVE_PATH=/absolute/path/Script.pvf \
Networking__Enabled=true \
Channel__PublicServerIp=100.100.51.178 \
Game__EndpointHost=100.100.51.178 \
dotnet run --project /absolute/path/to/Dfo.Server.Host.csproj
```

完整客户端 Channel bootstrap 仍为代码反推的
`0x0B → 0x0C → 0x09 → 0x0A → 0x11 → 0x12`。启动与资源修复只保证必需资源可靠可用，
不把该协议的字段语义升级为抓包确认事实。
