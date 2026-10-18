"""固定 A21 工件只读取证；不运行客户端、不写生产文件。"""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'marriage-housing-20261018'/'a21'))
import a21lib as L
out=L.header_lines()
functions=[(0x230ec90,'门移动 / 101和-101分支'),(0x2306c80,'CMD0024入口出口构包'),(0x23f80e0,'缓存入房前town/area'),(0x16182f0,'读取入房前town'),(0x2643c80,'读取入房前area'),(0xd35760,'CMD0015购买应答'),(0xae6d10,'模板类型到容器种类'),(0x1248ac0,'容器种类到容器指针'),(0x121aea0,'婚姻个人点余额getter'),(0x892b80,'NOTI03D3个人点更新'),(0xa29520,'货币材料10002606渲染特判'),(0xa2ab30,'货币材料10002606购买资格'),(0x1fe8500,'确认购买10002606特判'),(0x87e5e0,'03FE房间收回到保管箱'),(0x880e20,'03FE保管箱摆放到房间'),(0x87c490,'03FE保管箱内交换'),(0x8824b0,'03DA结果端点消费'),(0x881ca0,'保管箱绝对值更新'),(0x1044710,'壁纸地板摆放前确认'),(0x10446a0,'特殊摆放确认回调'),(0x10454d0,'普通收回确认回调'),(0x273d360,'布局是否含slot0/1'),(0x1048e50,'树种类初始化的+2C和+30'),(0x1049c90,'树快照尾i8写+2C'),(0x1047f90,'树NPC菜单资格分支'),(0x1048350,'树信息刷新'),(0x10993f0,'树信息窗口初始化')]
for e,title in functions:out+=L.dump_function(e,title)
Path(__file__).with_name('client-static.txt').write_text('\n'.join(out)+'\n',encoding='utf-8')
print('client-static.txt written')
