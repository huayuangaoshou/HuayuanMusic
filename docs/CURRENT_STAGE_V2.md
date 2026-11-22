# Huayuan Music · 当前执行阶段 V2（现行指针）

**2026-11-22 当前主线 · M2-23 桌面版。** `feat/m223-real-desktop` 从快照 `356c78ab` 派生。构建入口为 `app-m223/src/Preview.csproj`，默认挂载真实酷狗 Provider 与 R2UI 双工作空间。启动和普通播放永久移除项目审批票据、单次试播、人工确认和试播租约。保留账号登录、服务端权益、HTTPS 和请求来源约束。曲目入队保存会话内引用，实际打开时重新取得音源权限。平台音源地址不持久化。数据目录为各系统 AppData 下的 `HuayuanMusic-M223`。

验收结果：Windows x64 自包含 Release 为 0 错误、0 警告。Windows 实际主窗口可切换两个 R2 工作空间，重复启动没有创建第二实例。保存会话恢复并同步真实歌单 7 个、成员 2,902 条。两次曲目操作分别取得平台音源许可；该批返回的 HTTP 媒体地址升级 HTTPS 均在传输层失败，未回退明文，BASS 在线发声与连续播放仍待验证。

封存目录 `.p1-work/m2-23-normal-playback-20261118` 继续用于历史合成验收。可执行产物位于 `.p1-work/m223-desktop-live/app/HuayuanMusic.P17.Nightflight.exe`。旧外壳的上一轮未提交源码修改已暂存。

**2026-11-07 最新状态：** `P2BPF2_PROXY_HTTPS_OFFLINE=PASS`，在 `.p1-work/m2-08b-p2bpf2-proxy-https-20261107` 最小化增加媒体 HTTPS 使用 Windows 当前默认代理的显式开关，并用严格 NoBypassAudioProxy 阻止代理失败后直接联网。离线代理15/15、旧音频13/13、权限2允许11拒绝、R2本地与自包含各75×3、UIA PASS，0真实新在线请求。之前F1真实VIP和歌曲Grant成功，但官方音频HTTP源升级HTTPS传输失败，**R2真实音频仍未通过**。下一唯一：[F2-LIVE待批准HTTPS单曲](project-control/M2_08B_P2BPF2_LIVE_HTTPS_NEXT_20261107.md)；绝不自动启用明文HTTP/领取VIP/写回歌单。

**2026-11-07 最新阶段：** `P2BP-F1_CDN_DIAGNOSIS=PASS`，真实VIP ACTIVE/单曲许可PASS/Grant有效，但平台提供HTTP音源，SDK的HTTPS升级发生传输错误且HTTP回退安全拒绝，BASS RMS=0，实际在线播放**未通过**。本机Windows用户代理已启用，SDK音频UseProxy=false，可能是关键网络环境原因，尚待证明。离线媒体分支13/13、旧权限2过11拒、本地与自包含R2各75×3、UIA均PASS。候选 `.p1-work/m2-08b-p2bpf1-cdn-diagnostic-20261107`，脱敏报告与源码/runtime ZIP保存。唯一NEXT=[P2BP-F2安全HTTPS系统代理验证](project-control/M2_08B_P2BPF2_PROXY_HTTPS_NEXT_20261107.md)；真实请求要新授权，禁止自动开启明文HTTP。

**2026-11-07 新最高验收结果：** 最新 `P2BP-F REAL_VIP=ACTIVE, SONG_PERMISSION=PASS, AUDIO_OPEN=BLOCKED_UNAUTHORIZEDACCESS, BASS_RMS=0`。在唯一 Core/BASS/SQLite 下真实请求同一首酷狗概念版歌曲并获得音源许可，但尚未能成功下载/缓冲音频；原队列保留，无自动领取/官方写回。详细报告：`.p1-work/m2-08b-p2bpf-single-audio-20261107/M2_08B_P2BPF_MEDIA_OPEN_ACCEPTANCE_20261107.md`，脱敏真实证据和ZIP/SHA封存在隔离目录。网络白名单合成2放行11拒绝PASS、原R2本地与自包含75×3及Windows UIA全部PASS；测试截图最小化问题由已核验自身HWND恢复后解决。**NEXT=[P2BP-F1离线精确定位CDN拒绝](project-control/M2_08B_P2BPF1_AUDIO_OPEN_NEXT_20261107.md)，实网必须另行批准**。

**2026-11-07 当前最新阶段：** `P2BP-E REAL_ENTITLEMENT_ACTIVE_PASS`，旧 P2BP 试播拦截是VIP GET未授权，不是无会员证据。此次使用原 SDK 和保存登录完成一次真实HTTPS VIP状态只读核实，当前账号 **ACTIVE**，VIP领取0、媒体请求0、歌单写回0；脱敏报告位于 `.p1-work/m2-08b-p2bp-e-entitlement-20261107/M2_08B_P2BPE_ENTITLEMENT_ACCEPTANCE_20261107.md`。最新 [NEXT P2BP-F 单源短试听受控整合](project-control/M2_08B_P2BPF_SINGLE_AUDIO_NEXT_20261107.md)；先源码/合成负向测试，之后真实音源/CDN必须另获许可，SQLite迁移与发行尚NO_GO。

**2026-11-07 当前最高状态：** [P2BP 单曲权限真实试验 PARTIAL](project-control/M2_08B_P2BP_E_VIP_READONLY_NEXT_20261107.md)，SDK 已真实返回歌曲音源权限，但首次和诊断复测均在 `SDK_VIP_ENTITLEMENT_UNVERIFIED` 被安全门禁拒绝，**尚未验证账号 VIP 当前状态，也未下载/实际播放音乐**。真实官方歌单7/2875、歌曲封面元数据2774/2875的 P2B-R 只读结果不变。两次各一次源请求且队列原样保留，零 VIP 领取/远端写回；源码、脱敏报告、自包含本地功能75×3 PASS在 `.p1-work/m2-08b-p2b-playback-single-20261107`。NEXT=只读单次查询 VIP 权益接口，须另行明确批准；平台音频、真实混合队列和发布仍 NO_GO。

**2026-11-07 最新状态：** [P2B-R 真实酷狗概念版账号歌单只读接线 PASS](project-control/M2_08B_P2BR_REAL_READONLY_ACCEPTANCE_20261107.md)。实际7个官方歌单、2875歌单成员、1/7歌单封面显示，2774/2875歌曲具备封面URL元数据（非实际逐张验证）。只读快照已分账号保存、脱网显示，零真实音源请求；程序正常关闭，唯一 Core+BASS/SQLite 不变。P2B-R新版 UI 文案补丁通过原75×3和自包含UIA，真实账号请求没有重复执行。NEXT=另行授权的在线单曲/来源权限受控验收；VIP/官方写回/发行未授权。

**2026-11-07 当前最新状态与下一任务：** P1 本地真实 Core+BASS+SQLite 及成熟 R2 UI 已经由 ChatGPT 独立运行、真实控件/系统鼠标/音频会话与重启持久化核对，P1-PF 进一步修复测试快照写入共享竞争，独立 ZIP/自包含 EXE 已验证；P1-PF 报告位于 `.p1-work/m2-08b-p1-pf-probe-20261107/M2_08B_P1_PF_ACCEPTANCE_20261107.md`。**NEXT=[M2-08B-P2A 概念版 Provider 离线只读接线]**(project-control/M2_08B_P2A_KUGOU_CONCEPT_OFFLINE_BRIDGE_BRIEF_20261107.md)；只接经过验证的现成 SDK/Provider 到 R2 酷狗独立工作空间，不造第二播放器；真实账号、在线/权益、正式发布均未授权。

**2026-11-06 当前最高指针：** [M2-08B-P0 首次真实本地 Core 接线 PASS / M2-08B-P1 下一任务](project-control/M2_08B_P0_ACCEPTED_AND_P1_REAL_R2_UI_BRIEF_20261106.md)。用户此前反馈 I1-P1 交互“还行”，随后“继续”已限于**隔离本地真实 Core 接线**。P0 新隔离代码通过实际 UI 按钮→既有 BASS/SQLite Core→Snapshot 回读、首次/重启 10/10+10/10、原 Core 76/71 及真实 UIA 2→3→3→2；但 P0 是诊断面板，绝非 R2 成熟 UI 已经合并。NEXT=P1 把 R2-I1-P1 双工作空间与成熟歌曲列表接到原有唯一 Core，概念版真实账号/权益及正式发行仍 NO_GO。

**2026-11-06 最新现行任务（覆盖 R2-V2 视觉修补指针）：** 用户检查 R2-V2 后明确重点是**歌曲列表添加、删除、排序、单击/双击、右键与多选**要基本和成熟软件一致，见 [M2-08A-R2-I1 歌曲列表交互差异及复用任务书](project-control/M2_08A_R2_I1_SONG_LIST_INTERACTION_PARITY_20261106.md)。已核对成熟 donor SongListItemControl 及本体 CoreQueueAdapter / QueueMotion / C31Menus / PlaylistEntryControls 已有大部分能力，当前 R2-V2 UI 未接齐；不能继续堆外观。NEXT=R2-I1 隔离可运行交互预览+逐项行为回读，正式 Core/概念版 Provider 未获接线许可，M2-08B BLOCKED。

**2026-11-06 最新 NEXT（优先于下方旧指针）：** [M2-08A-R2-V2 结构性视觉修复任务书](project-control/M2_08A_R2_V2_FINAL_STRUCTURE_POLISH_BRIEF_20261106.md)。R2-V 已交付离线合成 Windows 预览，99 native 截图及三模式 GUI 通过，WorkspaceState/能力矩阵保持原字节；真实音频、概念版账号和权益仍未接。审查发现 VisualPolish.cs 在 LayoutUpdated 全树扫描并修改控件结构，存在长期性能/维护隐患；酷狗 UI 视觉仅部分符合成熟官方体验。下一仅做最后一轮稳定模板重构与酷狗工作空间可用功能的桌面化，交付隔离 EXE/证据后停 USER_UI_APPROVAL_PENDING，M2-08B BLOCKED。

**2026-11-06 已就绪的下一开发任务：** [M2-08A-R2 实施任务书](project-control/M2_08A_R2_WORKSPACE_SWITCH_IMPLEMENTATION_BRIEF_20261106.md)。检查确认还无 R2 交付目录，R1 的 PreviewWindow.axaml/Program.cs 当前仍是混合本地与酷狗的同一侧栏、一个 Pages 集合及部分不具真实后端的演示入口。NEXT=R2 独立隔离可运行预览，优先完成本地/酷狗概念版整套工作空间切换、页面/搜索/滚动状态独立恢复、播放展示单实例、跨来源队列/来源标签和功能矩阵。**本次只完成任务交接，R2 EXE 尚未生成；M2-08B 仍未批准。**

**2026-11-06 最新现行下一跳（覆盖本文所有旧 NEXT）：** 用户批准 [多平台独立工作空间架构](project-control/M2_08A_MULTI_PLATFORM_WORKSPACE_DECISION_20261106.md)，并非固定一个侧栏只替换 DLC 列表。本地音乐与酷狗概念版进入时各呈现完整平台导航、歌单、搜索与播放/歌词体验；保留各自浏览现场，主壳只负责轻量平台入口和统一窗口，背后始终 ONE_CORE / ONE_QUEUE / ONE_PLAYER。酷狗真实能力=概念版 Provider，普通酷狗 PC 只作桌面交互参考，无法接线的官方按钮删除重排。**NEXT=M2-08A-R2 工作空间切换的隔离模拟预览+能力矩阵与状态恢复验收**；M2-08B 仍待真人 UI 批准，VIP/歌曲封面技术债/正式发行状态不变。

**2026-11-06 最新 UI 准入优先：** [M2-08A-R2 功能真实性与删减重排门禁](project-control/M2_08A_R2_FEATURE_REALITY_GATE_20261106.md)。只呈现已有真正业务/有确定最小接线路径的功能，任何未获后端/授权/回读证明的官方 PC 按钮、菜单、页面都不加入，空位按官方使用习惯重排；预览必须标明纯合成数据，不能将动画/Mock 当作功能通过。下一仍 M2-08A-R2 能力映射+统一主壳隔离预览；未批准 M2-08B。

**2026-11-06 最新批准架构与下一唯一任务（优先于本文全部旧指针）：** [M2-08A 统一主壳 / 本地保底 / 多 DLC 内容视图](project-control/M2_08A_UNIFIED_HOST_MULTI_DLC_ARCH_DECISION_20261106.md)。**ONE_HOST / ONE_CORE / ONE_QUEUE**；本地播放器独立完整，酷狗与未来 QQ/网易云仅切换各自官方体验的内容页，不能双壳套娃或复制播放栏/SDK。现有 M2-08A KA 预览已离线独立 PASS 但视觉 PARTIAL；M2-08A-R1 官方 PC 公开证据版据交付报告为离线42截图 PASS，但部分官方 PC 页面实证未闭环、**没有用户正式 UI 批准**。NEXT=M2-08A-R2，仅做 Host/DLC 组合的可运行隔离预览及真实页面核对；M2-08B=BLOCKED。歌单封面真实仅1/7有图、VIP及发行等旧门禁保持不变。

**2026-11-05 最新用户 UI 指示：** M2-08A 不是“官方式 AI 原创设计”，而是**对应官方客户端界面/交互/可合法使用资源原版优先复用**，仅对不能直接复用的部分作必要的高保真适配。禁止预先强制 AI 设计的统一导航或风格拼接；逐页面原版对照并交付 Windows 可运行预览，等用户审阅。详见 [官方原版优先决策](project-control/M2_08A_OFFICIAL_DIRECT_REUSE_PRIORITY_20261105.md)。M2-07D 真实歌单封面仍1/7，M2-08B 未授权。

**2026-11-05 最新用户继续命令 / 覆盖以下所有旧 NEXT：** 正式启动 M2-08A **官方式 UI 设计预览阶段（设计任务书已落盘，真实预览尚未交付）**。用户实机反馈 M2-07D 7个歌单仅1个封面有图，真人封面 PARTIAL；脱敏分类 PLAYLIST_LIST_URL_REJECTED=6、DETAIL_NO_COVER=6，保留技术债、不以假的歌单封面遮挡。现行任务书：[M2-08A UI 蓝图和隔离运行预览](project-control/M2_08A_OFFICIAL_UI_PREVIEW_BRIEF_20261105.md)，M2-08B=WAIT_USER_UI_APPROVAL。不得重造播放器引擎、复制 KA 风格/品牌资产、改旧候选、访问账号或擅自发行。

**2026-11-05 最新真人覆盖指针：** [M2-07C 真实歌单/歌曲封面 PARTIAL 回执与 M2-07D 下一唯一任务](project-control/M2_07C_REAL_ARTWORK_PARTIAL_20261105.md)。M2-07C 独立离线 52/52、风险57/57、体验41/41、旧14/14全 PASS；用户实机确认**歌曲封面恢复 HUMAN_PASS**，但**歌单封面仍未显示 HUMAN_REPORTED_FAIL**。只读脱敏快照：7歌单中仅1条有规范化封面字段、6条缺失；歌曲2875条中2774有字段。另发现旧临时 Expander 页的标题封面只有展开才加载，SDK已有只读歌单详情 GetInfoAsync 但当前受限网关未放行。NEXT=M2-07D 歌单封面单点修复；成功后 M2-08A 官方式整体蓝图/预览，用户认可后才 M2-08B UI 替换。下方旧 M2-07C NEXT 及“完全无封面”已被本段覆盖。

**2026-11-05 最新覆盖指针 / 下一唯一任务：** [M2-07C → M2-08A → M2-08B UI 实施顺序](project-control/M2_07C_M2_08_UI_SEQUENCE_DECISION_20261105.md)。M2-07B 从 ZIP 独立复测：封面48/48、风险57/57、歌单41/41、旧14/14，退出0，仅为 OFFLINE PASS；用户打开真实酷狗后反馈「全部都没有封面」，**REAL_ARTWORK=HUMAN_REPORTED_FAIL**。已发现 `.p1-work/m2-07c-artwork-repair-20261105` 工作区，但交付状态未核实，优先继续/核对现有隔离工作区，不能宣称已完成。M2-07C 只修真实封面兼容/诊断，不再装修旧页面；之后 M2-08A 设计完整官方式 UI 并经用户看预览批准，才进入 M2-08B 替换正式 UI 层。旧 M2-05/M2-06 下一跳和“先继续打磨旧页”皆过期。REAL_121S/VIP/发行仍待，PUBLIC_RELEASE=NO_GO。

**2026-11-05 最新真人验收 / 覆盖下方所有旧 NEXT：** M2-06B 已获用户明确反馈「都正常」，覆盖**首次集中风险说明正常、重启同范围不再弹窗、在线歌曲真人能听到声音**三项，`M206B_RISK_UX=HUMAN_PASS`；M2-06A 原歌单不消失、发声、无重复普通弹窗亦已有真人 PASS。M2-06B 独立 Windows 合成复测风险 GUI **57/57**、M2-06A GUI **41/41**、完整旧回归 **14/14** PASS、外层退出 0。详见 `docs/project-control/M2_06B_HUMAN_EXPERIENCE_FEEDBACK_20261105.md`。**本轮授权/风险 UX 阶段收口，不再反复开发。下一推荐：成熟官方式 DLC 界面与真实歌单/歌曲封面，先查现成可合法复用实现；酷狗概念免费 VIP 自动领取继续保持核心需求并核实真实适用性。** 121 秒后重播、真人撤权、VIP 领取和公开发行仍未通过；`PUBLIC_RELEASE=NO_GO`。


**2026-11-05 最新真人进度与下一优先级（覆盖下面 2026-11-04 历史指针）：** M2-06A **点击播放歌单不消失、人耳听到声音、普通操作无重复确认，三项用户实机反馈 PASS**；完整离线 41/41 + 14/14 已独立 PASS。人体验证见 `docs/project-control/M2_06A_HUMAN_EXPERIENCE_FEEDBACK_20261105.md`。用户进一步要求**首次集中展示全部主要风险/权限，一次明确确认后保持正常音乐客户端体验**，同范围告知不应每次重启重弹；可选 HTTP 风险在同一弹窗一键选择并遵守会话范围。细则见 `docs/project-control/PRODUCT_EXPERIENCE_DECISION_20261104.md` 的 4A 节。121 秒后重播、真实撤权和 VIP 自动领取仍 PENDING；公开发行 NO_GO。**下一建议任务：M2-06B，仅集中风险告知/权限设置 UX，不重造播放或 SDK**。


**最新用户决定 / 覆盖下方旧 NEXT_TASK：** [2026-11-04 官方式 DLC UI、真实封面、低打扰与免费 VIP 产品原则](project-control/PRODUCT_EXPERIENCE_DECISION_20261104.md)。M2-06 独立离线复测 14/14、PS5.1 实际退出码0；用户真人**登录成功，官方歌单读取、展开正常**，但**点击歌曲后歌单整个消失，授权弹窗过多**。当前先执行 M2-06A 小范围修复页面重建和会话许可，再逐步按对应官方成熟客户端完善封面、布局、交互及动画。免费 VIP 打开 DLC 自动核查/领取是正式需求，尚未真人验收；实际播放/121 秒后切歌仍待测。PUBLIC_RELEASE=NO_GO。


- 日期：2026-11-04（Asia/Shanghai）
- **唯一产品主线：M1 Windows 独立本地音乐播放器**。V2 路线源：`docs/PRODUCT_ROADMAP_V2.md`，用户批准 B/V2-1 至 V2-6。
- V1 历史原件：`docs/PRODUCT_ROADMAP_LOCK.md`、`docs/CURRENT_STAGE.md` 保留原字节，`docs/DESIGN_BOUNDARIES.md` 继续约束视觉和成熟功能复用。
- 已完成：`GOV-03=PASS_SCOPE`、`CORE-01=PASS_SCOPE_READ_ONLY_AUDIT`、`CORE-02=ISOLATED_CODE_AND_REGRESSION_PASS`、`CORE-03=HUMAN_AUDIO_PASS_PARTIAL_INTERACTION`、**`CORE-04=M1_DAILY_ISOLATED_BUILD_AND_SYNTHETIC_IMPORT_RESTART_PASS`**。
- **`M1_LOCAL_INDEPENDENCE=PARTIAL`，`PUBLIC_RELEASE=NO_GO`**，正式主仓库未合并，个人真实音乐的 M1 新候选真人验收和日常独立安装尚未完成。
- **`CORE-05_AUTOMATED_SELFTEST=PASS`**：已由助手自行在 Windows 隔离环境运行 MP3/FLAC/WAV 解码、播放暂停/Seek/静音、真实窗口截图、重启恢复、队列/数据库/安全拒绝回归，**无需用户再提供音频文件或逐项操作**。完整报告：`docs/project-control/CORE_05_M1_SELFTEST_ACCEPTANCE_20261103.md`。保留新候选真人听觉/完整视觉 `PENDING`，不伪称通过。
- **`CORE-06=ISOLATED_NATIVE_SELFCONTAINED_ENTRY_AND_RESTART_PASS`**：新 Windows x64 自包含 EXE 直接启动，首次空库与进程重启 PASS，旧 R3 完整回归 exit=0，受控 synthetic WAV 导入 10/重启 7、安全负向 PASS。完整报告：`docs/project-control/CORE_06_NATIVE_WINDOWS_HOST_ACCEPTANCE_20261103.md`。
- **`M2-02-READONLY=PASS`（2026-11-04）**：在 R5 单一 SDK 登录会话上接入概念版领取记录只读查询，GET 白名单与资格/服务器日期预检 35/35 PASS，原成熟 SDK 原生 GUI 75/75 PASS，完整 Windows Release 0 错误/0 警告；未进行真实领取或网络请求。报告：`docs/project-control/M2_02_KUGOU_CONCEPT_READONLY_CLAIM_HISTORY_ACCEPTANCE_20261104.md`。
- **`M2-02B=PASS_SCOPE_OFFLINE_DURABLE_GUARD`（2026-11-04）**：新增单 SDK 会话只读领取预检及落盘防重复账本；35 项原安全测试+22 项账本测试+75 项原 SDK GUI 回归全部 PASS，Release 0 错误/0 警告。具体见 `docs/project-control/M2_02B_KUGOU_CONCEPT_DURABLE_CLAIM_GUARD_ACCEPTANCE_20261104.md`。实网领取、权益到账、每日自动领取均**未测试/未启用**。
- **`M2-02C=PASS_SCOPE_OFFLINE_ONLY`（2026-11-04）**：原 `UserClient` 的查询记录→模拟领取→查权益三段链在同一合成 SDK 会话下成功联调，新增35项断言；原35项只读/白名单 +22项持久防重合计 **92/92 PASS**，独立进程完整重复6轮 PASS；成熟 SDK GUI 75/75、Windows Release 0/0 PASS。补了预取消占用账本与并发共享冲突修复。报告：`docs/project-control/M2_02C_KUGOU_CONCEPT_SAME_SESSION_OFFLINE_CLAIM_ACCEPTANCE_20261104.md`。**真实账号/免费 VIP 领取/到账/自动领取仍为0次或未实现**。
- **`M2-03=PASS_SCOPE_PUBLIC_AUDIT_AND_SDK_WIRE_FIX`（2026-11-04）**：核实官方概念版每日签到福利宣传存在，第三方领取接口为测试/非官方适配，未找到 HuayuanMusic 获得第三方领取/发行授权的证明；原 SDK 固定 `source_id=90139` 和 `DateTime.Today`，已在隔离候选新增显式传入活动来源/服务器日期重载及请求一致性检查，**新增21/21、旧92/92，合计113/113 离线 PASS**；原成熟 SDK GUI 75/75、Release 0/0 PASS。实网领取入口仍关闭，平台活动/第三方权限待证。报告：`docs/project-control/M2_03_KUGOU_CONCEPT_ACTIVITY_LICENSE_AND_SDK_WIRE_ACCEPTANCE_20261104.md`。
- **`M2-04=PASS_SCOPE_OFFLINE_NATIVE_COMPOSITE`（2026-11-04）**：以 CORE-06 唯一 BASS 本体为基线，隔离集成 M2-03/R5 的酷狗概念版受信任 Provider、官方独立歌单页面/快照与延迟音源桥接，**真实账号连接工厂仍默认未配置**；本体完整 R3 回归 exit 0，混合模拟 BASS 队列/撤权 54/54、酷狗成熟 SDK GUI 75/75、VIP SDK 安全离线 113/113、CORE-06 原生产导入 10+7、真正 Windows 自包含 EXE 首启及重启 2/2 PASS，最终 GUI Release 0 warning / 0 error。完整报告：`docs/project-control/M2_04_CORE06_KUGOU_DLC_ISOLATED_INTEGRATION_20261104.md`。真实账号/在线歌曲/免费 VIP 领取均 NOT_TESTED；M1 仍 PARTIAL、发行 NO_GO。
- **下一唯一阶段：`M2-05=READY_FOR_APPROVAL_ONLY_R5_LIVE_READONLY_AND_LONG_WAIT_GATE`**，仅在独立批准真实账号只读和在线播放/HTTP 风险条件后，验证合并版真实官方歌单与 R5 最新版超过120秒的播放/切歌/撤权；未获授权前继续离线保持不联网。`CORE-07` 后置。

## 有证据的本体进度

| 能力 | 当前验收事实 | 尚未通过 |
| --- | --- | --- |
| 本地播放 | CORE-02 复用 BASS/真实队列及 LRC、用户明确反馈“有”，真人听觉 PASS；R3 旧隔离回归亦 PASS | 新 M1 日常候选的人耳音乐试听、所有格式/用户习惯和正式 root 集成 |
| 空库与自主导入 | CORE-04 新候选真实 GUI 首次**0 曲目、0 歌单、无自动播放** PASS；对工程固定 synthetic WAV 先预览后确认复制并创建歌单、构建生产队列/SQLite PASS；进程重启曲库、歌单、GUID 持久化 PASS（首次 10 / 重启 7） | 用户自主选择一首个人音乐的真人鼠标/导入/播放回执；无复制的超大曲库索引 |
| 启动与安全 | CORE-06 可双击 `HuayuanMusic.M1.Local.exe`，真正 **self-contained .NET10 win-x64**，不调用 Python 或 SDK，首次/重启真实窗口 PASS；旧门禁 12+10+5+1 回归 PASS，伪造原生 session 退出 78 | 正式安装程序、用户数据固定目录与文件移动迁移、个人真人综合验收仍待；一项 Win 1314 软链接负向未执行 |
| 队列、歌单与缓存 | R3/CORE-02 的唯一 QueueEntryId、PlaylistEntryId、SQLite v4 与 Schema 1/2/3 校验复用；新 M1 首次导入/保存与重启自动回归 PASS | 大型真实用户资料安全迁移、真实高容量长时间人机测试 |
| UI | 新候选沿用夜航原创临时壳，提供本地曲库、导入文件、文件夹、播放/暂停/歌词/队列 | 150% DPI 真人全功能、拖拽视觉动态、最终原创设计与性能 GO |
| 平台 DLC | 酷狗 R3 真人曾听到在线音乐；R4 只读 7 官方歌单 / 2878 元数据；R5 离线 75/75 | R5 真人续播、真实 DLC 安装/卸载、授权/同步/写回均不得推断已通过 |
| 合规与发布 | 保留上游声明、受控开发候选 | BASS 分发、平台条款、品牌素材、安装包和公开发行统一 NO_GO |

## 当前可直接体验的候选

**CORE-06 最新 Windows 原生自包含 M1 测试版**：

`D:\AI\Projects\HuayuanMusic\.p1-work\core06-native-local-20261103\standalone\preview\HuayuanMusic.M1.Local.exe`

这是一个可双击 EXE，不需要 Python、PowerShell 或开发机 .NET SDK；必须保留完整 `standalone` 目录，软件资料就在该目录下。旧 CORE-04 的 `Open-HuayuanMusic-Local.cmd` 仅为历史回退入口。

使用方法：**先正常关闭仍在运行的旧 CORE-02**，再在 Windows 资源管理器中双击上面 CMD。进入「我的曲库」，单击「导入文件」或「文件夹」，由用户自行选择有权使用的 MP3/FLAC/WAV，预览确认后导入；本阶段只读复制到该候选独立 profile、原文件不修改。单文件 256 MiB、单次至多 500 首/2 GiB，暂不适合庞大的长期曲库。关闭窗口后再次双击，检查歌曲、队列/歌单与收藏/历史。

当前报告：`docs/project-control/CORE_06_NATIVE_WINDOWS_HOST_ACCEPTANCE_20261103.md`；内部自包含 ZIP：CORE-06 候选 `artifacts/CORE06-HuayuanMusic-M1-Native-DEV-win-x64.zip`，SHA `20cdf56678c3f3b51060b8d39951a3ed27c2134d65efcee3451267e66331b077`；源码 ZIP：`recovery/CORE06-source.zip`，SHA `952fd5655692d63184e754d6ea5b2a97d2090d9a956ebc48e163f81fb7e71dab`。备份：`docs/project-control/rollback/CORE_06_NATIVE_20261103/`。

**禁止**自动触碰私人音乐库、正式源码、原 CORE-02 数据或在线账号；不擅自发布、安装、清理、Git reset/commit/push；单纯自动化 PASS 不等于用户真人体验通过。
