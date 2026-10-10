# 部署与回滚

- Runtime：`E:\Project\DNF\DNF-JP-86-Server\artifacts\epic-book-crafting-20261009/runtime`；PID：57480。
- 程序集：`0.2.3+epic-book-crafting.20261009.161ba2ca`。源码manifest指纹：`161BA2CA7D17F79F8FEFA6B57691FFC2FFF5D1DACC051BCD9AEB75222A513764`，运行程序集哈希在artifacts/epic-book-crafting-20261009/prepared.json。Host DLL SHA256：6878be62c05ad952004aa02064a5047213d4b89e40b4ca66feddc1cfcdebc399。
- 7001/10010/10011/10100/18080/18081监听全部属于新进程；bootstrap与GM health均ready，部署后再核对路径/程序集/GM健康。
- DB：`E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`，继续使用原实时库。
- 启动前备份：`E:\Project\DNF\DNF-JP-86-Server\artifacts\epic-book-crafting-20261009/before-server-start.db`，integrity_check=ok；SHA256=f3134b97d78a545b8ffcddccc1eb45da96f21e1dd033a3cefc1748b8cb6c986f。
- 审计changed=[]，包括角色、任务、成就、称号、物品、属性、邮件和account_epic_pieces；1123账号4的10300000余额1000，准备时尚无450004装备。
- appsettings/experience/Resources/wwwroot沿用旧运行配置；客户端EXE、0200插件、PVF哈希核验未变。
- 旧程序：`E:\Project\DNF\DNF-JP-86-Server\artifacts\achievement-state-events-20261009\runtime\Dfo.Server.Host.exe`，保持可回滚。回滚应核验新服务身份后停止本次进程并启动旧程序，继续使用实时数据库；不得用备份覆盖测试后数据。
- 本次未推送、未PR/合并。原分支和工作区继续保留。

## 用户测试

1123重新登录，在史诗图鉴搜索“黄金石”，制作“普拉塔尼的黄金石”。检查获得1件装备、1000碎片及配方材料扣除、图鉴与材料归零更新；再尝试缺料制作应有失败回执而不继续扣除。重登确认持久化。实机结果仍为pending，自动化不代表ClientVerified。
