# ClientHack

这是一个与服务端工程解耦的 Windows x86 纯 C 客户端补丁项目。游戏根目录只保留一个主动入口 `Az.dll`；兼容 `ijl15.dll` 不负责注入，功能按数字前缀从 `plugins` 目录加载。

## 启动链

1. `DNF.exe` 加载根目录的 `Az.dll`。
2. Az 在工作线程中读取同目录 `DNF.exe`，计算完整 SHA-256；当前只接受 `F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`。
3. 哈希通过后，Az 在解包启动点拦住游戏主线程，逐槽验证采集到的瞬时 IAT 状态，并修复尚未解析的槽位。
4. 游戏主线程保持阻塞；Az 验证功能地址、初始化集中式补丁/Hook 服务，并按顺序加载全部 required 插件。
5. 只有五个 required 插件全部初始化成功后才释放游戏主线程。此顺序是硬性约束，不能改成先释放主线程再异步打补丁。
6. 任一步失败都会记录日志、回滚本次 Host 持有的补丁/Hook，并用 `MessageBoxW` 警告后终止进程。

插件文件名必须符合 `数字_名称.dll`。排序先比较数字，再比较不区分大小写的文件名。required 插件还会同时校验固定文件名、描述符名称和 `CH_PLUGIN_REQUIRED` 标志。

## 产物布局

```text
stage/
├── Az.dll
├── ijl15.dll
└── plugins/
    ├── 0010_DisableGameGuard.dll
    ├── 0020_ClientCompatibility.dll
    ├── 0100_NetworkHook.dll
    ├── 0110_ServerRedirect.dll
    └── 0900_PreventMinimize.dll
```

不会生成根目录 `GameGaurd.dll` 或 `86JP.dll`。构建只写入本项目的 `out`，不会部署到 `D:\project\DNFClient`。

## 构建与测试

要求 Visual Studio x86 C 工具链和 CMake 3.25 以上：

```powershell
cmake --preset win32-release
cmake --build --preset win32-release --target clienthack_stage
ctest --preset win32-release --output-on-failure
```

正式产物位于 `out/win32-release/stage`。工程仅启用 C17，并静态链接 MSVC 运行库。`AzCapture` 是证据采集目标，不属于正式 stage。

macOS 上可安装 CMake 与 MinGW-w64，对全部 Host、插件、兼容 DLL 和测试程序执行 Win32 交叉编译：

```bash
brew install cmake mingw-w64
cmake --preset mingw32-release
cmake --build --preset mingw32-release --target all -- -j4
```

该构建用于及早发现 C 语法、链接、DEF 导出和 PE32 架构问题；MinGW 生成的测试 EXE 不能由 macOS 直接执行，也不替代 Windows MSVC 构建及真实客户端回归。

## 插件 ABI

插件导出未修饰名称 `ClientHackPlugin_Query`，返回 `ch_plugin_descriptor`。ABI 位于 `include/clienthack/plugin_api.h`。插件的 `DllMain` 不承担功能初始化；内存补丁、函数 Hook、日志和生命周期所有权统一通过 Az 提供的 `ch_host_api` 完成。

源码按插件优先级分目录。`plugins/plugin_entry.c` 是所有插件共用的唯一 ABI 入口，插件目录只实现 `ch_plugin_initialize` 逻辑；CMake 在构建每个 DLL 时注入描述符名称和 flags：

```text
plugins/
├── plugin_entry.c
├── plugin_logic.h
├── 0010_disable_gameguard/disable_gameguard.c
├── 0020_client_compatibility/client_compatibility.c
├── 0100_network_hook/network_hook.c
├── 0110_server_redirect/server_redirect.c
└── 0900_prevent_minimize/prevent_minimize.c
```

新增插件时应新建一个数字前缀目录和逻辑 `.c` 文件，再通过 `clienthack_plugin(...)` 注册；不要复制入口实现，也不要在入口中 `#include` 逻辑 `.c`。

直接补丁必须同时声明原始字节和替换字节。Host 只接受“仍是原始字节”或“已经是替换字节”两种状态。函数 Hook 必须携带已采集的入口签名，且统一由 Host 内置的 MinHook 管理。required 插件失败时，Host 按检查点回滚本轮修改。

## IJL 兼容层

仓库生成的 `ijl15.dll` 是被动兼容桩，只提供标准 IJL 导出，不创建线程、不修改内存、不加载插件，也不依赖 `86JP.dll`。它用于替换当前客户端目录中已被修改、且依赖 `86JP.dll` 的同名文件。

实验中也核验过 Intel IJL 1.51.12.44，但该第三方二进制不随仓库分发。若使用自行取得的原版 IJL，应先核对其 SHA-256 和六个标准导出，不要把旧的篡改版 `ijl15.dll` 与新 Az 混用。

## 服务器地址重定向

`0110_ServerRedirect.dll` 只替换 DNF 自身已修复的 `inet_addr` IAT 槽；它不会全局 Hook `ws2_32.dll`。默认配置不启用。客户端与服务端位于不同机器时，修改 stage 根目录的 `ClientHack.ini`：

```ini
[ServerRedirect]
Enabled=1
Address=192.168.1.20
```

插件只接受严格的 IPv4 地址，也只把 DNF 传入的 `127.0.0.1`（以及遗留兼容形式 `127.0.0.1/`）替换为目标地址。配置无效时 required 插件会失败关闭，避免静默连向错误主机。

初始化时 `ClientHack.log` 会记录配置目标；DNF 第一次真正使用重定向后，还会追加一次 `loopback endpoint replacement was used`。后者可作为实机确认调用路径已命中的直接证据。

参考补丁中的角色名输入兼容和内部 GameLog Hook 仍需先取得 `0x01D1D190`、`0x01DB3800`、`0x01CF9700`、`0x01CF9800` 的两次一致运行时入口签名。本仓库的 `AzCapture` 已包含这些目标；在签名审查前不会用无验证 Hook 强行启用它们。

## 运行时证据

- 两次独立实验副本采集保存在 `runtime/captures`，`tools/compare-captures.ps1` 会验证目标字节与 406 个 IAT 槽位的一致性。
- 生产 Host 使用 `src/az/iat_gate_policy.h` 逐槽接受“采集到的瞬时值”或“当前解析目标”。这允许初始、部分已解析和完全已解析状态幂等通过；唯一无法通过名称解析的 MSVCP 数据导出按模块 RVA `0x60F0` 验证并保留。
- Release CTest 同时执行采集一致性/先于修改的顺序检查、406 槽生产策略的混合与幂等测试，以及部分写入失败后的事务回滚测试。
- 原四插件组合已在隔离副本中按 `0010 → 0020 → 0100 → 0900` 初始化并进入游戏窗口；新增 `0110` 需要本次 Windows 实机回归。
- R6002 的根因是游戏主线程在 required 插件初始化前被过早释放；修正启动顺序后，最小配置稳定超过 130 秒，完整组合需作为每次发布的运行回归项。

## 边界

- 只支持上述 DNF.exe 哈希和采集到的运行时状态；其他版本会失败关闭。
- 不支持热卸载。插件建立活跃 Hook 后应保持加载到进程退出。
- 构建与部署必须在独立实验副本中完成；源目录 `D:\project\DNFClient` 保持只读且不得启动。
- 外部登录、账号和服务器可用性不属于本项目的客户端补丁保证范围。

## 第三方组件

Host 内置 MinHook v1.3.4，固定到提交 `c3fcafdc10146beb5919319d0683e44e3c30d537`。许可证和作者文件保留在 `third_party/minhook`。
