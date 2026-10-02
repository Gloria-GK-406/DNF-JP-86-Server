# Spec006 实现与自检

当前HEAD aa5b6636，完整7产品边界见verification/product-owned-manifest.md，主控作为Lite生产者直接实施，无产品委派。

CI固定Node22.19.0/官方setup-node v7提交82076278，lockfile ci→前端test/build→原.NET流水线；保留Windows/Linux矩阵及所有Action固定SHA。package每次npm ci/build，校验HTML引用/全部dist字节、默认关闭和环境文件；临时副本开启GM并清除bootstrap继承输入，HTTP逐资源SHA/MIME、initialized=false、私有401。包带GM说明/验收表和链接目标，README仅引导到标准说明，未复制重复全文。

TDD价值：候选缺JS仍成功是可逃到交付的实际失败；自动测试直接观察无效候选被拒及无archive，不依赖协作者身份，维护成本低于每次手测无效包。旧源码实际RED exit1（应拒绝而未抛），旧工具真的生成缺资源zip；日志red-missing-asset.log。最小资源校验后GREEN，扩展有效/过期字节、越界/外部引用、环境文件/DB sidecar、默认关闭与大小写/flat bootstrap校验；10项Python含原5tag/main历史守卫正常。扩展测试不是伪称全部TDD。

npm ci实际重新安装168包且audit0漏洞，全部前端54通过，TS/Vite build成功。CI顺序/矩阵/固定SHA静态核对通过。第一次真实Windows自包含候选HTTP成功，补随包README/链接目标后重打当前版本；当前日志package-final.log及zip核对另见verification/package-verification.md。

服务端C#产品与已接受aa5b6636完全一致；最近完整5373通过/0失败/2预期skip是Spec005主控证据。当前独立review后将按Spec006执行新鲜完整解决方案检查。当前package在未提交工作区生成，release.commit表示当时基线；根收尾另生成干净接受提交的候选。现服、真实管理员/玩家、客户端和PVF未操作；版本保持0.2.0。Linux/远程CI仅配置未执行，A21验收按Document/gm-acceptance-list.md。

独立审查F-001发现配置环境别名可突破自测隔离；原报告保留reviews/change-review-r1.md。新增实际smoke子进程环境回归：原代码留下true/false同键别名，RED exit1；最小修复归一清除隔离键全部别名后写固定值，显式隔离Pvf:ArchivePath及直接PVF入口。GREEN含旧守卫共11通过。另用Database:Path/Networking:Enabled/LauncherAuth:Enabled/GmWeb:ListenUrl/Pvf__ArchivePath与bootstrap毒化环境运行真实Windows候选验证。产品边界仍7项，当前manifest重冻，旧manifest保留r1。
