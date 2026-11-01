# FolderSizes 功能对比记录

核验日期：2026-11-01（北京时间）。FolderSizes 官方下载页显示 **10.3.90.0**，更新日志标注发布于 **2026-10-15**，未发现此后更新。10.3.90 重点是扫描取消、旭日图和矩形树图绘制；下列功能多数已在 10.0 提供，不是最近十天新发布的功能。

| 能力 | FolderScope 1.1 状态 |
| --- | --- |
| 大型 Excel 自动跨表 | 已补齐，每表 1,048,575 条数据加表头；重复表头、冻结、筛选 |
| 云文件/占位内容控制 | 已增加跳过选项；内容检测识别并跳过不可安全读取的候选。未做真实云提供商端到端验收 |
| 容量预测 | 已增加同卷、同容量的近 30 天线性趋势；至少 3 时点且跨度 24 小时，显示依据与限制 |
| 重复文件比较 | 按大小分组、头尾抽样排除，再完整 SHA-256；文件变更/失败不再静默遗漏 |
| 文件级快照比较 | 1.0 已有，本次没有重复实现 |
| 作者、标题、标签等属性搜索 | 尚未实现 Windows 属性系统搜索 |
| 创建/修改/访问年份切换 | 仍按修改年份分类 |
| 可配置非业务文件分类 | 仍是固定图片、音频、视频分类 |
| 硬链接唯一文件统计 | 仍按路径计数，不等于物理卷占用 |
| 完整停靠面板、加密 ZIP、NTFS 就地压缩 | 尚未实现 |

本次还修复了 `*` 排除无效、线程设置失效、分类下钻越界、MFT 记录边界/更新序列/引用序列校验、并发名字与父路径混配、失败时预读缓冲区释放，以及大小写路径冲突未标记不完整等问题。

FolderSizes 是闭源产品。官方性能说明不能证明其具体算法，也不能证明本项目“整盘几秒”或“MFT 必然更快”。本项目未使用它的私有代码、DLL 或文件格式。

资料：[官方下载页](https://www.foldersizes.com/download)、[官方更新日志](https://www.foldersizes.com/content/static/releasenotes.htm)、[10 版功能](https://www.foldersizes.com/features/whatsnew)、[文件属性搜索](https://www.foldersizes.com/support/file-search-faq)、[容量报告参数](https://www.foldersizes.com/support/help/disk-reporter-parameters)、[扫描与硬链接选项](https://www.foldersizes.com/support/help/scanning)。

云属性判断依据[微软文件属性文档](https://learn.microsoft.com/en-us/windows/win32/fileio/file-attribute-constants)：`0x40000` 仅在目录枚举上下文表示 `RECALL_ON_OPEN`，不能把 MFT 中的扩展属性标志误作云状态。
