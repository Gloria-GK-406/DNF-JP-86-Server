# 阶段2：马战窗口打开及重开边界复核

2026-10-06；ClientStatic / Confirmed（限下述指令路径）。没有启动客户端，未取得 WireCaptured / ClientVerified。固定dump SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，base `00400000`。

## 可确认的调用顺序

结果UI 1FC constructor 为 RVA `000F9D40` / VA `004F9D40`；主虚表 VA `0306BBEC`，`+18` 为 RVA `000F6050` / VA `004F6050`。投注UI 1FB主虚表 VA `0306B94C`，`+18` 为 RVA `000F4B10` / VA `004F4B10`。

当进入结果UI的 `004F6050` 显示初始化，且前置全局mode gate未拒绝时：

1. 取 EventManager 中 ID45 马战manager，dynamic cast到马战类型。
2. **manager存在时**，`004F60C8` 调 `004D21A0` 发04E6，然后 `004F60CF` 调 `02846D50` 立即读取已有 `manager+4`。这里没有等待ACK。
3. 若上述state为0，显示字符串 `12BC0` / UI209，返回false；非0继续。若manager缺失，`004F60C4` 直接跳到 `004F610C`，也继续后段。因此“必须state非0”只适用于manager存在的分支。前置 `004F6097` 的mode结果==1则直接返回false，且不发04E6。
4. `004F610E` 调 `004F5CC0`；其 `004F5DE3..004F5DE8` 调 `02304C70(1FB,0,0)`，之后调整窗口位置。
5. 若通用open实际走到调用1FB虚表`+18`的保留主体，则执行 `004F4B10`。它取得manager、构建材料控件；manager不存在时返回false；存在时在 `004F4CE1` 构包发04E8。

查询ACK成功、B9和BA均能ensure 1FB；这些reader没有写manager state，也不直接调用显示初始化。历史ACK更新客户端历史容器属于查询结果投影，不能据此推断服务器查询有写入副作用。

## ensure、构造与缓存

RVA `01F060F0` / VA `023060F0`：优先返回active表已有对象；否则返回cache首对象；cache为空才调用factory `023BD910`，把新对象放到cache并返回。此函数没有显示初始化`virtual+18`调用。

1FC构造器在 `004F9F09` 写 `scene(+218)=0`，`004F9F16` 写 `counter(+220)=-1`。**这是新对象构造赋值，不是每次ensure赋值。** active/cache命中路径不重新构造。不能在模型中把每次B8/B9/ensure或每次“重开”都无条件重置到0/-1。

通用open `02304C70` 的保留主体包含已有对象复用分支，也包含factory新建分支：`02304E2D` 判断是否已有对象，`02304EFB` 才调用factory；两路随后进入 `02304F0A`。其 `02304F9F..02304FAE` 调当前对象虚表`+18`。因此**保留主体证明复用对象也可能重新执行显示初始化**，执行该路径则重复发04E6/04E8；不能从对象复用推断“一生只发一次”。

但 `02304C70` 入口 `02304C70→0579F153` 和早期 `02304CA4→04F6B205` 受保护。没有闭合所有受保护分支，也没有实帧，不能把上述能力推广为“每次点击打开/关闭重开必然重复init”。关闭后是否保留、何时销毁或重新构造、已显示窗口再次请求打开的完整行为仍Unknown。

## show不等于重新消费当前scene

RVA `000FA1B0` / VA `004FA1B0` 才是scene setter。它在 `004FA1ED` 把传入参数写到 `UI+218`，之后 `004FA50C` 重新读**传入参数**，`004FA518` 按0..12派发。它没有“新值等于旧scene则跳过”的早退出。

已确认：

- setter消费scene0/1的共同分支 `004FA528`：manager存在且 `02301820(1FC)` 判定窗口已显示时，`004FA542` 再发04E6。
- setter消费scene12的 `004FB369` 分支会寻找决赛赢者，走冠军字符串 `12BBF` 公告路径；本地tick11→12或再次消费scene12可以重复公告。
- `004F6050` 和 `004F5CC0` 的完整函数本体没有调用 `004FA1B0`，也不从结果UI `+218` 取当前scene重新派发。前段 `023B4170(0xF)` 仅写通用UI字段；不是马战scene setter。
- 通用open保留主体 `02304F9F` 调的是`virtual+18`；对1FC对应 `004F6050`，对1FB对应 `004F4B10`，不是 `004FA1B0`。

所以，不应在模拟“打开/重开窗口”动作上自动触发scene0/1额外query，或scene12冠军公告。需要明确调用scene setter的包/本地tick路径才投影这些副作用。受保护通用open及其他虚表回调仍没有全程序排除证明，因此结论为“当前已确认打开路径不证明此副作用”，不是“重开绝无公告”。

## 给阶段2模型的边界

| 模型动作 | 可直接采用的静态规则 | 仍须标为选择/Unknown |
|---|---|---|
| ensure结果窗口 | 对象不存在才构造scene0/counter-1；已有对象保持字段 | 真实关闭/删除后是否仍属同一对象 |
| 执行结果窗显示初始化 | mode允许且manager存在：先发E6再读已有state；0拒绝，非0后续open1FB | 每次重开是否都到此初始化 |
| 执行投注窗显示初始化 | manager存在则材料投影后发E8 | 受保护open所有路径/实时调度 |
| 再次消费scene0/1 | 窗口已显示且manager存在则额外E6 | 不能由单纯ensure/show代替setter调用 |
| 再次消费scene12 | 重走冠军公告消费，缺少scene去重 | 单纯重开未证会重新消费12 |

若模型提供“打开”按钮，宜把它定义为**演示执行一次已定位的显示初始化路径**，标清不是对任意实际重开的保证；保留既有scene/counter，不自动执行当前scene公告。

原始指令证据：[opening-raw.txt](opening-raw.txt)、[opening-extra-raw.txt](opening-extra-raw.txt)、[opening-scene-raw.txt](opening-scene-raw.txt)、[opening-show-raw.txt](opening-show-raw.txt)、[opening-virtual-raw.txt](opening-virtual-raw.txt)。上一轮scene全体消费依据：[scenes.md](../joust-client-reanalysis-20261006/scenes.md)。本轮只新增opening报告及证据，没有修改产品或他人目录文件。
