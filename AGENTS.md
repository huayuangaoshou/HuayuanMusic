# Huayuan Music 仓库开发协作规则

**2026-11-22 现行主线 · M2-23 桌面版。** 在 `feat/m223-real-desktop` 的 `app-m223/src/Preview.csproj` 上继续开发，入口为 R2UI 和本地、酷狗概念版独立工作空间。复用 M2-23 随附 Core、BASS 和唯一全局队列。普通启动、入队、播放、切歌和重播长期取消项目审批、试播票据、租约和次数门槛。平台登录、服务端权益、媒体来源和 HTTPS 校验继续生效。旧外壳与封存候选用作历史基线。进度与验收范围见 [CURRENT_STAGE_V2.md](docs/CURRENT_STAGE_V2.md)。

**2026-11-07 唯一最新阶段（以下所有旧 NEXT 仅为历史）：** P2BP-F2 **OFFLINE_PASS / REAL_HTTPS_NOT_TESTED**。新隔离候选 `.p1-work/m2-08b-p2bpf2-proxy-https-20261107` 仅在 combinedVipAudio 且显式开启 `M208B_P2BPF2_MEDIA_SYSTEM_PROXY=1` 时，让媒体 HTTPS GET 使用现有 .NET 默认系统代理。代理绕过/不可达失败即拒绝、禁直连/HTTP/Cookie/凭据/重定向、TLS校验不变。模拟代理CONNECT专项15/15、旧媒体13/13、SDK安全2过11拒、本地/自包含R2各75×3与自包含UIA全部PASS；**没有新的真实账号或CDN访问，在线BASS仍未PASS**。唯一下阶段：[F2-LIVE 一次HTTPS系统代理短试听（需新明确授权）](docs/project-control/M2_08B_P2BPF2_LIVE_HTTPS_NEXT_20261107.md)。旧P2B-R真实歌单只读保留，正式仓库76项tracked差异不清理、不发布。

**2026-11-07 最新状态（覆盖以下旧F1待诊断任务）：** P2BP-F1 实际真机证明官方歌曲SDK已授予音源且Grant有效，但服务端返回HTTP源，当前程序HTTPS升级传输失败后拒绝明文回退；`BASS_PLAYBACK=NOT_PASSED`，没有发出原HTTP请求。独立内存媒体打开13/13、网络白名单2合法11拒绝、本地及自包含R2 75×3、UIA均PASS。隔离包在 `.p1-work/m2-08b-p2bpf1-cdn-diagnostic-20261107`；正式仓库76项改动保留。现场发现Windows用户代理启用，而媒体HttpClient使用UseProxy=false，可能是HTTPS直连失败原因，**未实网验证**。唯一NEXT：[P2BP-F2优先试安全HTTPS系统代理](docs/project-control/M2_08B_P2BPF2_PROXY_HTTPS_NEXT_20261107.md)，须新授权才发真实媒体请求；不自动放开HTTP、领VIP或写回歌单。

**2026-11-07 最新唯一 NEXT（覆盖以下旧P2BP-F“待试播”）：** 已经受控真实执行P2BP-F同账号一次VIP GET=ACTIVE，单曲 `/v5/url` 许可 `SDK_PERMISSION_GRANTED`，但 `DirectSdkGateway.OpenAsync` 触发 `UnauthorizedAccessException`，**未取得完整CDN音频、BASS解码RMS为0**。真实单曲技术状态 `PARTIAL`，不能声称播放器已能播放官方在线歌曲。QueueEntryId增量已撤销，原队列一致，BASS设备释放。最新任务：[P2BP-F1 离线定位音源打开失败与HTTPS安全策略](docs/project-control/M2_08B_P2BPF1_AUDIO_OPEN_NEXT_20261107.md)，任何再次真实VIP/音源/音频GET必须新增用户明确批准；不要绕开权限或擅自启明文HTTP。原 P2B-R 已验收歌单浏览可继续使用，原正式仓库禁止清理/覆盖。

**2026-11-07 最新最高验收结论（下方 P2BP / P2BPE旧NEXT只作历史）：** [P2BP-E 真实会员权益只读查询 PASS · ACTIVE](docs/project-control/M2_08B_P2BPF_SINGLE_AUDIO_NEXT_20261107.md)。通过 HuayuanMusic 已保存酷狗概念版登录和原签名 SDK，只查询固定 `kugouvip.kugou.com/v1/get_union_vip?busi_type=concept` 一次，真实有效；无自动领VIP、购买、真实音源/CDN、远端歌单写回。原R2 75×3、自包含75×3、Windows UIA 和纯离线白名单1通过7拒绝PASS。下一唯一 NEXT=[P2BP-F 单曲播放先模拟后真人授权](docs/project-control/M2_08B_P2BPF_SINGLE_AUDIO_NEXT_20261107.md)；本次未授权再次请求真实歌曲，不动正式仓库/账号。

**2026-11-07 最新最高状态（下方旧 P2B-R NEXT 仅为历史）：** P2B-Playback 单曲受控真实网关试验 `BLOCKED_VIP_ENTITLEMENT_UNVERIFIED`，已从真实 SDK 响应定位到 `priv_status==1`，不是可证明的账号无 VIP；P2B-R 官方歌单7个/2875成员仍真实只读 PASS。本轮每次单曲音源请求仅1次、无CDN下载/BASS播放、无会员领取/写回，已清空测试新条目且原队列保留。最新唯一下一任务：[P2BP-E 单次只读核实权益（新用户批准后）](docs/project-control/M2_08B_P2BP_E_VIP_READONLY_NEXT_20261107.md)，不可绕过权限，不重试换歌；正式仓库和旧候选不覆盖。

**2026-11-07 最新唯一阶段指针（覆盖下方旧 P2A NEXT）：** [M2-08B-P2B-R 真实账号 HTTPS 只读验收 PASS](docs/project-control/M2_08B_P2BR_REAL_READONLY_ACCEPTANCE_20261107.md)：从 HuayuanMusic 已保存登录安全恢复并真实读取 7 个官方歌单、2875 个歌单成员；只有1/7歌单封面有实际可见图，真实媒体源请求0。独立 P2B-R 候选的本地 Core+BASS/SQLite 和 P2A 离线回归、自包含 UIA 均 PASS，私人快照/截图不打入 ZIP。**NEXT=单首在线音源/混合队列受控测试，必须另行获得用户明确授权**，VIP 自动领取、官方歌单写回、公开发布仍 NO_GO。

**2026-11-07 最新唯一 NEXT（覆盖下方 P0/P1 和旧 M2-08A NEXT）：** P1-PF 已完成本地真实 Core / BASS / SQLite 工作空间技术闭环，Windows 原生鼠标右键、双击、拖动、进程专属音频输出、持久化回读已独立 PASS；测试探针锁冲突的最小补丁已通过新 UIA 3/3、自包含 UIA、59×3、新旧 Core 76/71。原 P1/正式主仓不覆盖。接下来执行 [M2-08B-P2A 酷狗概念版离线 Provider→R2 工作空间接线](docs/project-control/M2_08B_P2A_KUGOU_CONCEPT_OFFLINE_BRIDGE_BRIEF_20261107.md)，只读/模拟/单核心，**不连接真实账号、不联网、不领取 VIP、不写回官方歌单或发布**。

**2026-11-06 最新现行阶段（覆盖本文下方所有旧 M2-08A NEXT 和 M2-08B BLOCKED 状态）：** 用户已授权“仅隔离测试数据接本地真实 Core”。[M2-08B-P0 真实接线 PASS 与 P1 下一任务](docs/project-control/M2_08B_P0_ACCEPTED_AND_P1_REAL_R2_UI_BRIEF_20261106.md)：P0 复用唯一 CorePlaybackAdapter+BASS+SQLite，从 UI 控件触发追加/删除/上移/播放，并真实回读，首启10/10重启10/10、旧 Core 76/71、真实 Windows UIA 队列 2→3→3→2 全 PASS，正式仓库不合并。**NEXT=P1，将原 R2-I1-P1 成熟双工作空间 UI 真正绑定单一 Core**，禁止把 P0 临时诊断面板当成最终 UI；真实酷狗概念账号/VIP/发布仍待独立授权。

**2026-11-06 用户最新体验优先级，覆盖下方所有继续视觉修饰的 NEXT：** [M2-08A-R2-I1 歌曲列表交互对齐](docs/project-control/M2_08A_R2_I1_SONG_LIST_INTERACTION_PARITY_20261106.md)。不再自己研究按钮的摆法，优先逐项复用现成成熟音乐软件和仓库中 SongListItemControl、CoreQueueAdapter、QueueMotion、C31Menus、PlaylistEntryControls、R2EntryTest 的完整交互。单击选中、双击/Enter 播单首、不清队列；区分队列追加/下一首、本地歌单增加/移除、重复条目、右键、拖动/列排序和多选。每个动作必须接得到唯一 Core 与能回读，不能实接的禁用/不显示；概念版官方歌单写回保持只读。NEXT=R2-I1 隔离操作预览，非再一轮配色；M2-08B 未授权。

**2026-11-06 最新唯一可执行下一任务：** [M2-08A-R2-V2 最后一轮结构性视觉修复](docs/project-control/M2_08A_R2_V2_FINAL_STRUCTURE_POLISH_BRIEF_20261106.md)。从 R2-V 隔离派生，保留两套完整工作空间、浏览现场恢复和唯一播放队列。必须删除 VisualPolish.cs 在 Window.LayoutUpdated 遍历可视树、动态拆字符串与插入 Grid 子元素的临时设计，改为 Avalonia 稳定模板/绑定。酷狗概念版 Provider 是真实能力来源、普通酷狗 PC 只作有依据的桌面 UI 参考。只展示可接线功能，完成后让用户亲看，M2-08B 仍禁止自动启动。

**2026-11-06 现行可执行任务：** [M2-08A-R2 独立工作空间切换闭环实施任务书](docs/project-control/M2_08A_R2_WORKSPACE_SWITCH_IMPLEMENTATION_BRIEF_20261106.md)。已核查 R1 的实际 PreviewWindow/Program：目前仍把本地与酷狗放在同一侧栏，存在未接线假入口；当前未发现 R2 候选。下一阶段 Codex 从已验收 R1 隔离派生，只做两个完整工作空间+独立浏览状态恢复+单播放队列的 Windows 可运行预览，不能直接做 M2-08B。

**2026-11-06 最新用户批准的最终 UI 交互架构（本文件最高优先级，覆盖以下旧“只切内容区”方案）：** [多平台独立工作空间决策](docs/project-control/M2_08A_MULTI_PLATFORM_WORKSPACE_DECISION_20261106.md)。一个 HuayuanMusic 宿主 + 唯一 BASS/队列，但本地音乐、酷狗概念版及未来 DLC 各有**完整且可恢复的独立使用体验工作空间**；切换整套导航/歌单/搜索/播放页视觉，不是只换中间面板。紧凑平台切换器、单个全局播放栏可随平台换外观但不复制核心。当前酷狗在线后端=概念版 Provider；普通酷狗 Windows PC=桌面 UI 参照。已接线功能才显示，不可用的官方功能删除重排。NEXT=M2-08A-R2 隔离最小工作空间切换闭环，M2-08B 仍需用户视觉批准。

**2026-11-06 最新功能真实性门禁，最高 UI 优先级：** 用户要求“功能做了必须真能接上，做不到就不加、调整布局”。见 [M2-08A-R2 功能真实性门禁](docs/project-control/M2_08A_R2_FEATURE_REALITY_GATE_20261106.md)。任何可见按钮、菜单、快捷键先映射已有 Core/Provider 后端、授权与结果回读；没有真实可接线能力就**删除该入口并重新布局**，不保留假按钮/灰色占位，不能拿合成 UI 事件冒充功能实现。官方 PC 的界面仅对可用功能高保真适配；预览明确注明模拟，M2-08B 仍须真人确认。

**2026-11-06 最新用户批准架构（覆盖以下旧 UI NEXT）：** [一个 HuayuanMusic 主壳 + 完整独立本地播放器 + 多个官方体验 DLC 内容页面 + 唯一 BASS/队列] (docs/project-control/M2_08A_UNIFIED_HOST_MULTI_DLC_ARCH_DECISION_20261106.md)。禁止把多个官方客户端窗口嵌入宿主形成双标题栏/双导航/双播放栏，禁止把本地播放器藏在酷狗里。下一仅执行 M2-08A-R2 隔离架构与交互预览，对照酷狗官方 PC 真实可核实页面；KA 只作可复用控件源。原 KA 与 R1 候选都非用户 UI 批准，M2-08B 仍被阻断。

**2026-11-05 M2-08A 最新 UI 要求（优先于下方所有自定布局建议）：** 用户要求平台 DLC **能直接拿对应官方客户端现成体验的就直接合法复用**，从页面、图标语义、动效到菜单及操作流程，尽量原版一致；AI 自创 UI 最后考虑。逐页面对照真实官方版本，合法/技术上能复用的优先；受许可限制不能直接复制的使用高保真适配并注明差异。具体见 [M2-08A 原版客户端直接复用优先决策](docs/project-control/M2_08A_OFFICIAL_DIRECT_REUSE_PRIORITY_20261105.md)。不得假定闭源客户端、品牌资源可自由提取，不能为了官方外观破坏本体唯一核心。

**2026-11-05 晚间最新用户授权阶段：M2-08A 官方式 UI 整体蓝图与可运行隔离预览，仅设计不改主程序。** 用户确认 M2-07D 真实歌单封面仍仅1/7有图，脱敏诊断 PLAYLIST_LIST_URL_REJECTED=6、DETAIL_NO_COVER=6，未关闭此数据兼容问题但不再盲修。新阶段唯一任务书：[M2-08A UI 预览](docs/project-control/M2_08A_OFFICIAL_UI_PREVIEW_BRIEF_20261105.md)。M2-08B 必须等待用户实际观看并认可预览；下文旧 M2-07D NEXT 已过时。

**2026-11-05 最新真人封面反馈与任务补充：** M2-07C 已经离线独立复测 PASS；用户明确反馈歌曲封面有了、歌单封面仍没有。现行 NEXT=M2-07D **歌单封面小范围修复**，之后仍按用户批准路线进入 M2-08A 官方式 UI 蓝图预览 → 用户认可 → M2-08B UI 替换。现场脱敏计数：7歌单有图1、缺图6；2,875首曲目有图2,774。详情与安全约束见 [真实封面 PARTIAL 回执](docs/project-control/M2_07C_REAL_ARTWORK_PARTIAL_20261105.md)。此前 M2-07B 全无封面和 M2-07C 未完成的旧指针都已过时，禁止重开歌曲封面或提前装修临时歌单页面。

**2026-11-05 最新强制任务排序：** [M2-07C 真实封面修复 → M2-08A 官方式 UI 整体蓝图/用户预览批准 → M2-08B UI 层替换与回归](docs/project-control/M2_07C_M2_08_UI_SEQUENCE_DECISION_20261105.md)。M2-07B 合成回归 PASS，但用户实际反馈歌单、歌曲**全部没有封面**，真实封面 HUMAN_REPORTED_FAIL；不得把原合成 PASS 或旧 M2-05/M2-06 NEXT 当现行任务。M2-07C 现有隔离目录优先复用，禁止继续打磨即将替换的临时歌单界面。路线批准不授权真实账号访问、VIP 操作、发布或覆盖正式代码。

**用户 2026-11-04 最新产品体验决策优先：** [官方式 UI、成熟功能复用、真实封面、低打扰授权与免费 VIP 目标](docs/project-control/PRODUCT_EXPERIENCE_DECISION_20261104.md)。此决策**覆盖**本文及旧 V2/DESIGN_BOUNDARIES 中“必须两套 AI 原创 UI、不可参考成熟官方页面布局/交互”等冲突旧要求；旧字节级设计文档仅作历史。后续所有开发任务**必须先读新决策**，不再由 AI 自创陌生布局或反复弹窗代替正常播放器体验；必要的账号、权益和许可证校验仍保留。


本仓库的**现行产品路线为用户于 2026-11-03 明确批准的 V2**：[docs/PRODUCT_ROADMAP_V2.md](docs/PRODUCT_ROADMAP_V2.md)，决策凭据 [docs/project-control/V2_ROUTE_DECISION_20261103.md](docs/project-control/V2_ROUTE_DECISION_20261103.md)。2026-10-24 的 [V1 原始锁定文档](docs/PRODUCT_ROADMAP_LOCK.md)仅作历史版本和当时验收依据，**原件不改**。原创 UI/复用边界继续遵守 [docs/DESIGN_BOUNDARIES.md](docs/DESIGN_BOUNDARIES.md)。

任何 Codex / AI 开发任务在计划、修改或测试前，先读取 V2 路线、[docs/CURRENT_STAGE_V2.md](docs/CURRENT_STAGE_V2.md)、[docs/project-control/PROJECT_STATE.md](docs/project-control/PROJECT_STATE.md)、[ROADMAP.md](docs/project-control/ROADMAP.md)、[HANDOFF.md](docs/project-control/HANDOFF.md)、设计边界及当前阶段验收回执；核对 Git 工作树与可恢复检查点，**不能覆盖未提交工作**。旧 [docs/CURRENT_STAGE.md](docs/CURRENT_STAGE.md)是 2026-10-24 的历史指针、受 P1.5 SHA 固定，必须保留原字节；研究历史验收时才作为旧阶段证据读取。

**V2 执行顺序（已批准）**：G0 治理与复用审计 → M1 Windows 本地播放器独立保底（无网络、无账号、无 DLC）和数据可靠性 → M2 隔离 DLC 研究/真人门禁 → M3 可信 DLC 生命周期 → M4 原创 UI 与本体完整验收 → M5 本体及各 DLC 分别通过合规/发行门禁 → M6 其他平台逐个可行性审计。**只允许在 M1 保底通过后、全面发行验收前开展受控隔离 DLC 研发**。旧正常 Program/App 非隔离启动仍不能据历史 Mock/受控测试称完整 PASS。新任务以 `CURRENT_STAGE_V2.md` 及最新验收为准，禁止以旧 P1.5 指针覆盖当前状态。

**用户已明确批准产品路线 V2-1 至 V2-6，但只授权 V2 路线治理落地。** 这不授权源码合并、真实账号登录/网络获取私人数据、领取 VIP、替点 HTTP 风险确认、官方歌单写回、Git commit/push、安装或发布；这些须在未来独立任务中分别明确批准。任何阶段的源码构建/受控测试 PASS 不等于正常主程序或发行 PASS，`PUBLIC_RELEASE=NO_GO`。

每条交付给用户的 Codex 开发指令必须包含推荐模型、思考档位与一句话理由，依据当时可用模型及任务难度合理选择。不得因为开发新功能而跳过回归、人体验收、许可审查、文件安全和回退检查。

必须**保留唯一 .NET/Avalonia+BASS 播放核心与队列**，沿用成熟 SDK 与已证实 R3/R4/R5 研究成果；平台歌单分开呈现、核心元数据独立于 DLC，卸载仍留存歌单及身份但不保证可播放。不能另造播放器/引擎、未经许可复制 MusicFree AGPL 代码或执行未知插件。平台官方读取与写回权须逐平台验证；原创 UI 不继承 KA 视觉。

旧 P1.5 归档 manifest 固定了旧 `AGENTS.md` 的字节，V2 更新本文件后，在当前工作树直接运行旧全源哈希验证可能不匹配；已保留旧 AGENTS 字节级备份在 `docs/project-control/rollback/V2_ACTIVATION_20261103/AGENTS.pre_v2.md`，应在冻结历史快照验旧基线，并为 V2 新任务建立独立回归。不擅自放宽旧验证器。

无用户对具体动作的**明确批准**，禁止 Git 强制重置/清理、覆盖或删除用户数据、自动账号网络操作、提交、推送、部署和发布。
