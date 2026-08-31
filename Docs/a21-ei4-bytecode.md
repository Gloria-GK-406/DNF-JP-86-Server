# `.eI4`：不是数据，是变形 CRT 解释器的字节码池（实测结案）

日期：2026-08-31。输入：两份跨 boot dump（20260822-232754、20260831-083553）、
PageGuardTrace 运行期首触普查（Sections=4、ArmDelayMs=0，共 268 页全布防）、
symeval 对读点的 11 跳具体求值、`tools/analyze_ei4.py`。

## 一句话结论

**`.eI4`（1.05 MB）是线程化解释器的字节码池：handler 代码在 `.-wI`（变形 CRT）里，
每条记录 ~4-5 字节，消耗一个 dword 载荷后经密钥编排与地址累积分派到下一 handler。
实机会话（启动+登录+进世界）只触碰 268 页中的 1 页——池子绝大部分处于休眠。
完整解码它等于给 CRT 做反虚拟化，与 `.vmp1` 同属"不做"一类。**

这纠正了此前对 `.eI4` 的两个说法：它不是"`.-wI` 的运行期数据源/配置"（clean-exe
文档用语），也不需要为阶段 2 包装器改写而解码（CRT 的解释执行与包装层互不相干）。

## 证据链

**1. 静态特性（`analyze_ei4.py`，两 dump 一致）**

* 跨 boot 逐字节相同 → boot 稳定，无会话密钥；内容是解包器从容器里确定性物化的。
* 熵 7.96、99.5% 非零；唯一低熵页是末页（尾部 ~1.5 KB 明文零区）。
* 全镜像 dword 引用普查 6,600 个指向 `.eI4` 的 dword，**低于随机期望**（1.05MB/4GB
  ≈ 7,800）——没有任何指针表，地址全是算出来的（与包装器 K 站点同一风格）。

**2. 运行期首触普查（PageGuardTrace，init 期布防、零延迟）**

268 页全部 PAGE_GUARD，客户端完整生命周期（启动、token 登录、频道、选角、
进世界）内**恰好一次首触**：

```
{"addr":"0x04EB6243","eip":"0x05C33BB0","access":0(read),"stack":[…,0x07A29810(VM堆),…]}
```

> 顺带更正：`clean-exe-section-removal.md` 里丢段实验记录的
> `at=0x05833BB0 / target=0x04AB6243` 与本记录**都恰好差 0x400000**——
> 是 next-measurements §0.1 记录过的"简报地址系统性偏低"问题，同一事件。

**3. 读点机制（真 EIP 反汇编 + symeval 11 跳）**

```
05C33B8E  mov ebp, esp
05C33B90  lea esp, [esp - 0xC0]       ; 私有栈帧（32 槽），与包装器同款
05C33B97  mov ebx, edi                ; 密钥种子 = 流指针
05C33BB0  mov edx, dword ptr [edi]    ; 读流（注意：非对齐地址）
05C33BB7  add edi, 4                  ; 流指针前进
05C33BBE  xor edx, ebx                ; 解码
05C33BC0  jmp <下一跳>                ; 变形链
```

11 跳的循环形状完全一致，每跳三件事：

```
xor ebx, ecx      ; 密钥编排：ebx 逐跳异或不同的常量/派生值
add esi, ecx      ; 分派地址累积：esi += 每跳常量
jmp esi / push esi; ret   ; 直接线索化分派（无中心分发器——与 .vmp1 VM 同族）
```

流指针每记录前进 4-5 字节；hex dump 里 `04` 标记字节以 ~5 字节步长周期出现
（0x04EB624C/6250/625C/6264/6270…），记录结构固定。

**4. 与既有结论的对账**

* 控制流矩阵"`.eI4` 零控制流、只被读"——与"字节码=数据、执行留在 .-wI"一致。
* `.vmp1` 的 VM：direct-threaded、字节码存加偏置的 handler 地址、~158 个 handler。
  `.eI4` 这台：同为 direct-threaded，但分派靠**加法累积**（esi += 常量），
  编码不同——两台不同的机器，不是同一套。
* clean-exe 文档的"runtime transform vs build-time transform"二分里，`.eI4` 的
  **物化**是 runtime（解包器写出），**内容**是 build-time 生成的字节码（boot 稳定）。

## 含义

1. **阶段 2 包装器改写**：不受影响，无需先解码 `.eI4`。CRT 的解释执行独立于包装层。
2. **干净 exe 终局**：`.eI4` + `.-wI` 的 handler 池就是"变形 CRT"的实体。替换 CRT
   时两者一起消失；在那之前它们原样保留即可（`.eI4` 不可单独丢——丢段实验已证
   启动期就有一次解释执行）。
3. **要不要做全量解码**：不做。它等于 CRT 反虚拟化，收益是读一段已经被定位为
   "远期重合成"的代码；且 1/268 的实测使用面说明大部分字节码在本私服场景根本
   不会被执行。

## 若将来要深挖（方法已备好）

* 布防普查：`[PageGuardTrace] Enabled=1 Sections=4 ArmDelayMs=0`（本客户端无
  `.eI4` 内挂钩，init 期布防安全；新会话注意先清 `clienthack-pageguard.log`）。
* 读点求值：symeval 从触发的 EIP 起步、`edi` 给具体流地址、`follow=True`，
  观察每跳的 `xor ebx,ecx / add esi,ecx`——密钥编排与分派累积全在这两个寄存器上。
* 批量解码需要先离线复原密钥编排（ebx 的逐跳常量来源），属 CRT 反虚拟化工程。

## 复现

```
python ClientHack/tools/analyze_ei4.py \
  --dump <dumpdir> --dump2 <other-dumpdir>     # 差分 + 结构 + 引用普查
# PageGuardTrace 测量局：客户端 ini 开 [PageGuardTrace] Sections=4 ArmDelayMs=0，
# 复制 stage/plugins/0001_PageGuardTrace.dll 到 plugins/，正常启动打一局
```
