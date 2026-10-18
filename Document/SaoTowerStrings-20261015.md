# SAO标题与通关横幅占位文字修复

## 来源与定位

玩家实机报告：右上角显示`dnf_38379`，通关横幅显示`dnf_38386`。这是当前客户端`dstr.dat`的中文FILE/102、语言1042资源直接保存的文本；不是DGN缺少name，也不是服务端把占位文字发给客户端。

同档日文FILE/104分别为`%s 第 %d階`和`%d階が攻略されました。`，英文FILE/105分别为`%s %dF`和`%dF has been cleared.`。保留格式参数类型、数量和顺序，仅补齐中文。

| ID | 原中文内容 | 修复内容 |
| --- | --- | --- |
| 38379 | dnf_38379 | %s 第 %d 层 |
| 38386 | dnf_38386 | 第 %d 层已被通关。 |

证据级别：原文字及资源归属为ClientStatic；玩家报告确认两个UI实际显示对应占位键。修复后渲染和实际楼层数仍待玩家验收。

## 生成和验证

工具：[patch_sao_strings.py](../Tool/ClientStringPatch/patch_sao_strings.py)。Windows运行，需Python及pefile；通过Win32资源更新API生成新文件，不原地编辑源文件。SC01解码使用DWORD差分XOR种子0x5819AF17，按原始长度读取UTF-8正文。原始输入必须恰好各有一个占位条目，已修复或陌生版本拒绝生成。

```powershell
python Tool/ClientStringPatch/patch_sao_strings.py <原dstr.dat> <新dstr.dat>
```

已完成：编码解码一致性、生成后独立PE资源读回、完整中文正文逐行比较仅2行变化、其他全部语言资源字节一致、非资源段字节一致、对已修复输入再次运行在写文件前拒绝。

- 原档SHA-256：`60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10`
- 新档SHA-256：`DA151F228267746221F13F598A388479266A8ABF21A0AB542E62753EB5DF43D7`

## 同步与回退

已同步DNFClient、DNFClient-A21-debug、DNFClient-A21-protocol、DNFClient-A21-protocol2，各自回读哈希相同。上游对照与OneKey发行包未修改；PVF、exe、插件及服务器程序未改。本次同步前无游戏进程；服务端保持原PID 36932及版本0.2.5-sao-core.20261015+f67ce8bc运行，没有重启。

产物、来源哈希与同步回证：`artifacts/sao-text-20261015/dstr.dat.json`、`sync-receipt.json`；各客户端原档保存在`artifacts/sao-text-20261015/rollback/<客户端名>/dstr.dat`。回退需先正常退出游戏，再将相应备份覆盖到对应客户端。

验收：重新启动客户端，进入SAO后确认右上角名称带实际层数；通关确认横幅带已通关层数。其他未汉化SAO文本仍保留原状，不扩大本次修改范围。
