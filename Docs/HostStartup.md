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

### 一处待办：这道校验发生得比它该发生的晚

`ChannelScriptStartupService` 是一个 `IHostedService`，所以「读脚本、拒绝、退出」排在容器建好
之后、并且排在此前每一个 hosted service 之后——包括打开 70MB 档案的内容源与建库。**「在 listener
起来之前失败」是真的，但「早失败」不是**：一次拒绝要走完整个冷启动。

代价先落在测试上。`ChannelHostStartupTests.InvalidRequiredChannelScript_FailsBeforeListenersStart`
等的是一整个进程冷启动，单跑两三秒，七个测试程序集并行时越过十秒、又越过三十秒，每次都让一条
干净的基线读起来像回归。当前预算三分钟，并在超时时打印主机已经输出的内容——数字不是重点，能
诊断才是。

把这道校验搬到建图期（配置绑定处直接读一次），拒绝就会发生在任何昂贵启动工作之前，保证更强、
测试也不必再等冷启动。属结构改动，未做。

## 频道与经验加成

`channel_info.etc` 不只是发给客户端的一份脚本，它还声明了**哪个频道为哪些副本多发经验**：
`[server]` 段每一行给出 `频道号 / 显示名 / 频道类型 / 副本组名 / 经验加成百分比`，`[dungeon]`
段给出每个组含哪些副本。服务端从 2026-08-29 起按这两段发放频道奖励——**只有本局副本属于所连
频道声明的那个组时才发**，客户端把它画成「赛丽亚的祝福奖励」那一行。

比率不在 `experience.json` 里，也不在任何服务端配置里。要改就改这个文件。

默认部署因此配了两个频道，好让这条规则正反两面都能在实机上看见：

| 频道 | 端口 | 绑定的副本组 | 组内副本 | 加成 |
|---|---|---|---|---|
| 10 | 10010 | `[granfloris]` | 144–149、2009、240 | 5% |
| 11 | 10011 | `[sky_catle]` | 156–160、2010、242 | 5% |

同一张图，换频道结果不同：

* 在**频道 10** 打副本 **144**，通告里多出一行「赛丽亚的祝福奖励」，金额为基数的 5%。
* 在**频道 11** 打同一个副本 144，没有这一行，只有基础值——144 不在 `[sky_catle]` 组里。

`[server]` 段里不是每一行都发钱：绑 `[deathtower]` 与 `[trade]` 的两行写的是 `0`，而决斗、
PVP 一类绑 `[none]` 的十来行同样是 `0`（它们连副本组都没有）。一行读不懂（字段缺失、百分比不是
数或超出 0–100）、一个频道号或组名被声明两次，也都按不发处理：档案自相矛盾时由服务端替它选一
个答案，玩家是看不出来的。

脚本整体解析不出任何频道行时，服务端照常启动、经验照常发放，只是没有频道奖励，日志里会记一
行。脚本文件本身读不到是另一回事，见上一节，那会直接拒绝启动。

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
