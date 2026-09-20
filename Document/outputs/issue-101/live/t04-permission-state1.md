# T04 权限状态1：仍无难度选项

用户确认重新登录后蜘蛛洞穴仍无难度选项。夹具日志证明选角后和打开选图前均已发送 NOTI0005 body `01009600000001`（dungeon150,state1）；未据此宣称客户端有效getter或普通难度UI已生效。该结果不能证明非空权限协议错误，也不能把任务模式当作普通难度差分入口。

日服官方2016-01-27（Season4 Act14）更新说明明确：接取史诗任务后进入对应场景地下城，初始固定Normal，难度随剧情推进改变；完成场景后才涉及一般地下城。来源：https://arad.nexon.co.jp/news/update_view.aspx?no=6975 。

官方2016年4月后续改版又增加场景Hard模式，需满足区域通关时间和被击次数。来源：https://arad.nexon.co.jp/campaign/2016april_other.aspx 。这是独立机制，不应混同NOTI0005普通权限。两个官方页面为历史版本参考；当前A21二进制是否含4月Hard机制尚未确认。

结合本地150包含story mode及用户任务进度，当前入口仍受剧情模式限制是最强解释，尚未用对应UI分支逐条证明。后续应先找到一般地下城入口或完成剧情的测试状态，再做权限差分；不继续盲目提高state，也不让用户反复刷图碰运气。

样本：[t04-permission-state1.jsonl](t04-permission-state1.jsonl)，SHA-256 `985a292f85b9bad19538136f59c73e0676c8efdc685f7b92f49101a907a2bf24`。
