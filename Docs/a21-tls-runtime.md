# `.tls` 腾讯安全运行时：身份、采集面与在本机上的惰性（实测结案）

日期：2026-08-31。输入：`DNF-memory-rva.bin`（dnf-dump-20260822-232754）、
`wrapper-apis-a21.csv`、boundary-1 的定性、两次完整游戏会话 + 一次 2 分钟
netstat 采样的运行时证据。本文回答三个问题：它是什么、它采集/外发什么、
在当前私服游玩环境里要不要摘除。

## 一句话结论

**它是 TenProtect/az 安全组件的采集与校验半体（不是遥测 SDK）；在日服客户端 +
本机的组合下它是惰性的——不落盘、不外发、握手失败被容忍。私服游玩无需摘除，
保持观察即可。**

## 身份：az 安全组件，不是 Beacon

明文字符串里没有任何 `beacon/qimei/appkey/report/upload` 字样。它的指纹是：

* `az.dll` 字符串 ×5 + 导出名 **`DllEntry4` / `DllEntry7`** —— 运行时回调
  az.dll（原版为腾讯保护宿主；现已被 ClientHack 的 Az.dll 顶替，后者的导出表
  里没有这两个名字，`GetProcAddress` 返回 NULL——实测客户端容忍）。
* `\Tencent\DeskUpdate`、`\Tencent\Desktop`、`\Global.db` —— 逐盘搜索腾讯
  安装根与共享库。
* `SbieDll.dll`（Sandboxie 沙箱检测）、`QTalk.exe` —— 反沙箱/反调试清单成员。
* SQLite3 + Crypto++ + zlib 1.2.8 + md5.c + Tencent TDR + msvcp60 STL
  （boundary-1 已确认），`.tls↔.vmp1` 双向 6118 条边——敏感逻辑被虚拟化进
  VM，可读部分是管道设施。

> 教训一条：`%APPDATA%\Tencent\beacon\` 里今天确实有 2 MB WAL 增长，但开库
> 全是**搜狗输入法**的事件（`pz_r: sogou_pinyin_96b`）——Beacon 是腾讯系共用
> 遥测框架，本机多个软件在写。凭"目录今天变过"归因会栽跟头，开库看行才知道主人。

## 采集面（静态全量）

673 个已命名包装器 + 10 个 `."2A` 直连指针（全是 msvcp60 数据导出）+ 明文
字符串锚点，合并后的行为词汇表：

| 类别 | 证据 |
| --- | --- |
| **原始磁盘序列号** | `\\.\PhysicalDrive%d`、`\\.\Scsi%d:`、`SCSIDISK`、`Drive%d` |
| 网卡指纹 | `GetAdaptersInfo/GetAdaptersAddresses`、`SendARP`、注册表 `{4D36E972-…}\Connection` 的 `MediaSubType`/`PnpInstanceID` |
| 网络配置 | `ipconfig /all`（外壳调用） |
| 注册表扫描 | `RegOpenKeyExA`×11、`RegEnumKeyExA/ValueA`、`RegQueryValueExA`×4、`RegCloseKey`×10 |
| 进程/模块枚举 | `EnumProcessModules`、`GetModuleFileNameExA`、`GetModuleInformation`（psapi） |
| 窗口枚举 | `EnumWindows`×3、`GetWindowTextA/W`、`GetClassNameA` 等（user32×19） |
| 机器画像 | `EnumDisplayDevicesA`、`GetSystemMetrics`、`GetFileVersionInfoA/VerQueryValueA`、`CoCreateGuid`、`ReadEventLogW` |
| 模块完整性清单 | az.dll、gdi32、user32、ws2_32、winmm、dsound、shell32、urlmon、bugtrap、kernel32、comdlg32、advapi32、ole32、oleaut32、wininet、d3d8、dinput8、imm32、ijl15、fmod、msvcr71、**SbieDll.dll**、QTalk.exe —— 即 DNF 真实导入集 + 反沙箱目标 |
| 汇总 | md5.c（`$Id: md5.c …$` 版本串）、Crypto++、TDR 序列化 |

**网络能力判读**：已命名包装器里**零**网络 API（无 wininet/winsock/Http/URL
类）；`urlmon.dll`/`wininet.dll` 字符串属于模块完整性清单，但也不能排除
`LoadLibrary+GetProcAddress` 动态绑定绕过包装层的可能——这条只能实测（见下）。

## 本机运行时实测（三层证据）

1. **磁盘零写入**：两次完整会话（24 分钟通关局 + 对照局）窗口内全盘时间戳
   扫描，DNF 可归因的落盘为零（客户端目录里只有它自己的 LagLog/CrashDNF2.cra
   与 ClientHack 产物）；`Tencent\` 目录树里**不存在任何 Global.db**——持久化
   管道从未跑通。
2. **网络零外发**：一次会话启动→登录→频道全程的 2 分钟 netstat 采样（每 10
   秒，含 ESTABLISHED/SYN_），非 127.0.0.1 的远端连接为**零**。netstat 看到
   的是全进程行为，虚拟化的 `.vmp1` 半体若发起连接同样会被计入。
3. **握手缺失被容忍**：我们的 Az.dll 无 `DllEntry4/7` 导出，客户端 24 分钟
   会话 + 完整退出 flush 无异常（与零补丁对照局逐项一致）。

惰性的机理未定（两个候选，均未证）：日服构建的功能门（`ClientArgumentParser`
里的 `MODE - TCLS`/TenProxy 是国服登录模式）未开；或 az.dll 握手 NULL 后静默
退出。对结论无影响。

## 对私服游玩的处置建议

**不摘除。** 它在本机不采集落盘、不外发；摘除（封 33 个 `.text` 入口或
VM 入口 `0x050006A2`）反而引入新变量。要加保险的话，最低成本的两条：

* 防火墙规则拒绝客户端出站非 127.0.0.1（一劳永逸，覆盖虚拟化半体的一切
  未来行为）；
* 运行期抽查 `Tencent\` 目录树有无新 `Global.db`/WAL 增长（一分钟脚本）。

## 与既有结论的关系

* boundary-1 把 `.tls` 判为 `PROTECT_RUNTIME`（腾讯侧运行时、非游戏代码）——
  本文补齐"它具体是什么、干什么"，判定不变。
* clean-exe 终局"保护层运行时彻底消除"清单里，`.tls` 的摘除属于低优先级：
  它是惰性的，而 `.-wI` 变形 CRT、`.eI4` 才是活体。若未来做段级摘除实验，
  本文的采集面清单就是 `.tls`（节 6）的风险登记表。

## 复现

```
# API 面
python - <<EOF  # wrapper-apis-a21.csv 里 source_section==.tls 的 module×function 分布
EOF
# 字符串锚点：.tls 范围 0x04F79000-0x051BB000 的 ASCII runs（本文全部地址）
# 运行时网络采样：launch_a21_b1.ps1 起客户端后
for i in $(seq 12); do netstat -ano | grep -E "ESTABLISHED|SYN_" \
  | grep -v 127.0.0.1 | grep " <pid>$"; sleep 10; done
# 磁盘证据：会话窗口内 LastWriteTime 全盘扫（client 目录 + %TEMP% + 各 Tencent 根）
```

## 未决（低优先）

* 33 个 `.text→.tls` 调用者逐个语义（boundary-1 已列清单；19 处落在 VM 入口
  `0x050006A2`，属 `.vmp1`，按既定决议不追）。
* 惰性机理（TCLS 功能门 vs 握手失败退出）——若哪天要安全审计可再挖。
