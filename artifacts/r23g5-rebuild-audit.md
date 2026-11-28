# R23g5 队列全量操作调用链

静态基线为 R23g4 `33bb2a5`。范围为 Windows 桌面入口可达的生产链路。下表中 `src/` 相对 `app-m223/`，`core/` 相对 `app-m223/core/src/Apps/KugouAvaloniaPlayer/`。N 为队列大小，M 为曲库映射大小。UI 归属依据调用上下文，后台项单列。

## 快照重建的入口与触发条件

唯一计时位置在 `src/CorePlaybackAdapter.cs:53`。该处清除 `collectionDirty`，重建歌词并遍历播放队列两次。两轮均用 `Id`，而 `Id` 在曲库映射中按路径线性查找（同文件 44 行），所以队列部分最坏是 O(N×M)。当前曲目的单次 `Id` 也随每次发布执行。

| 文件与行 | 入口、触发事件及频率 | 线程 |
|---|---|---|
| `src/CorePlaybackAdapter.cs:32,34–35,51–53` | 初始脏位为真。队列集合和歌词集合共用 `OnCollection`，Add、Remove、Reset 或歌词装载均可置脏，并合并到一个待执行的 Publish。切歌可通过换词间接触发。单句歌词进度、位置属性与本地红心自身只请求发布，是否全量重建取决于当时的脏位。 | Publish 明确 Post 到 UI |
| `src/CorePlaybackAdapter.cs:35,47,54–61,82–84` | 初始化、导入完成与命令 `Run` 的 finally 直接发布。覆盖播放、切歌、红心、跳转、音量、队列与歌单编辑等。每次命令结束一次，可能先有排队的 Publish 消费脏位。 | 桌面启动或 UI 命令上下文 |
| `src/DesktopQueueMemory.cs:33–90,119–137` | 启动恢复时枚举旧队列、生成两种恢复顺序，调用 RestoreDesktopQueue 并置脏发布。后台目录恢复完成后另有一次直接 Publish，只有脏位为真时重建。 | 恢复应用与发布在 UI，包含异步读取 |
| `src/DesktopQueue.cs:8–54` | 从列表播放时全量构造请求键和原顺序键，再逐项比较。复用队列时按出现次序扫描以定位条目，替换时批量建立条目、置脏，最终由 Run 发布。每次点播列表一次。 | UI 命令上下文 |
| `src/KugouCoreBridge.cs:18–39,43–146,164` | 平台撤销时移除该来源队列项、清映射并置脏。平台准入默认重建曲库快照并置脏发布，批量路径可延后统一发布。禁用平台也显式置脏发布。修剪临时条目时扫描队列以保留使用中的来源。 | 桌面 UI 生命周期与准入续体 |
| `src/CoreQueueAdapter.cs:11–16` | 受控诊断的停播选中直接 Publish，普通桌面传输走正常入口。 | 调用方，诊断路径 |

## 原队列、列表与持久化全量操作

| 文件与行 | 触发事件及频率 | 线程 |
|---|---|---|
| `core/Services/PlaybackQueueManager.cs:27–28,39–71,94–104,150–154,174–207` | Move 修改两套顺序。RestoreExact 按条目查原顺序（最坏 O(N²)），替换或恢复时 Clear/AddRange。歌单上下文变化、清空以及随机模式切换可重排或重建整个集合。各动作一次，可能有多个集合通知。 | 桌面命令或恢复的 UI 阶段 |
| `core/Services/PlaybackQueueManager.cs:107–146`，`core/ViewModels/PlayerViewModel.cs:574–591`，`core/ViewModels/PlayerViewModel.Queue.cs:43–74,269–283` | 插入下一首查重、找当前位置并插入两套顺序。追加时复制合并序列做容量检查。删除会扫描两个集合，删除当前条目还会找后继及改变播放状态。每次插入、追加或删除执行。 | UI 命令上下文 |
| `core/ViewModels/PlayerViewModel.Queue.cs:19–40,343`，`core/ViewModels/PlayerViewModel.cs:214–215` | 每个队列集合通知立即重建 Core 的 `DisplayPlaybackQueue`，并安排保存。创建、恢复及私人电台状态切换也直接同步显示队列。这里与 R2 自己的显示快照是不同路径，即使抽屉没有展开也会执行。 | 检查访问权并明确切到 UI |
| `core/Services/PlaybackQueueManager.cs:74–91`，`core/ViewModels/PlayerViewModel.Queue.cs:155–167,432–446`，`core/ViewModels/PlayerViewModel.QueueEditing.cs:36–49,72`，`core/ViewModels/PlayerViewModel.Transition.cs:101` | 手动前后切歌、自然结束、不可播顺延与转场预备使用线性 IndexOf/Contains。一次查询不改结构，顺延可连续找多项。随机上一首额外检查历史条目是否仍在队列。 | 桌面命令或派发到 UI 的播放回调 |
| `src/CoreQueueAdapter.cs:10–42,52`，`src/R2UI/CorePresentation.cs:206,239–242,272–273` | 队列操作按条目查找，拖动前插、旧式按曲目重排或歌单管理会复制原队列。批量追加比较入队前后条目，搜索结果播放可能扫描两遍找匹配项。每次对应操作执行。 | UI 命令上下文 |
| `src/R2UI/CorePresentation.cs:90,163–185` | 快照变更时可比较完整队列。队列内容或曲库引用变化时重建显示数组、全部重排号码并 Refresh；切歌或播放状态改变会再 Refresh 所有队列行。取当前显示曲目也做线性查找。 | UI 发布订阅链 |
| `src/R2UI/R2ShellView.axaml.cs:109,193–196`，`src/R2UI/InteractionController.cs:118–139,168–193` | 平台访问状态改变时刷新全部队列行。显示数组替换时比较、替换 ItemsSource 并恢复选区。跟随播放在定位时枚举列表寻找当前行，之后的动画 Tick 只写滚动偏移。 | UI 或 Loaded dispatcher 回调 |
| `src/R2UI/Workspaces.cs:187–190,327–357,406–415,532–541` | 工作空间更新会按变化重填曲库、搜索、红心、历史或歌单。切歌、播放状态变更及不可播通知会遍历曲目行并更新当前状态；不可播通知还刷新整个队列视图。每次相应状态变更执行。 | UI 发布订阅链 |
| `src/R2UI/LocalFolders.cs:48–52`，`core/ViewModels/PlayerViewModel.QueueEditing.cs:12–22` | 曲库移除按命中曲目过滤队列并逐个移除；平台撤销另检查原顺序中的平台项。批量操作可能多次触发集合通知。 | UI 生命周期或曲库命令 |
| `src/DesktopQueueMemory.cs:172–192,218–251`，`src/Program.cs:224`，`src/R2UI/PluginManager.cs:46` | 每次发布比较与上次保存的完整队列。曲目、模式、播放状态变更可立即触发保存，播放位置单独变动按 30 秒节流。全量筛选和序列化前快照在 UI；写文件由 Task.Run 串联。退出与插件管理另有显式调用。 | UI 快照，后台写盘 |
| `core/ViewModels/PlayerViewModel.Queue.cs:19–25,354,373–401`，`core/ViewModels/PlayerViewModel.QueueEditing.cs:70–73`，`src/CorePlaybackAdapter.cs:85` | Core 缓存另有约 250 ms 的取消合并机制，惯常从 UI 续体复制可见与原始队列，再在后台存盘。显式 Flush 则同步复制并保存，由队列编辑、退出等路径调用。 | UI 快照或显式同步 Flush，延迟保存的写入在后台 |

静态清点只说明哪些路径具备全量成本。是否对应某次停摆以实机日志为准。
