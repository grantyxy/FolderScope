# FolderScope

**FolderSizes 的开源替代品，基于 C# / .NET Framework 完全重写，支持简体中文和英文界面。**

[English](README.en.md) · [下载预览版](https://github.com/grantyxy/FolderScope/releases) · [构建检查](https://github.com/grantyxy/FolderScope/actions/workflows/build.yml) · [MIT 许可证](LICENSE)

FolderScope 是一款 Windows 磁盘空间分析工具：查看文件夹占用、定位大文件与重复文件、生成报告，并通过图表了解存储分布。功能与布局参考 [FolderSizes](https://www.foldersizes.com/)，以独立代码实现；当前版本包含经过 Claude 协助重写与优化的界面和扫描引擎。

项目不依赖 FolderSizes 的安装、DLL 或许可证，与其无隶属关系。“完全重写”指独立实现代码；当前预览版不承诺所有功能、界面细节或文件格式与 FolderSizes 一致。

## 下载与运行

1. 打开 [Releases](https://github.com/grantyxy/FolderScope/releases)，下载 `FolderScope-v1.0.0-preview.1-win-x64.zip`。
2. 解压到可写目录，双击 `FolderScope.exe`。请保留同目录的 `FolderScope.exe.config` 和中英文帮助文件。
3. 在右上角 **中文 / EN** 菜单切换语言；语言选择会保存，已有扫描结果会保留。

面向 **Windows 10 / 11 x64**，需要 **.NET Framework 4.8**。运行不需要 Python、Node.js 或独立服务。本次本地验证环境为 Windows 11 x64；其他 Windows 版本未做完整兼容性验收。

## 主要功能

| 功能 | 当前实现 |
| --- | --- |
| 双语界面 | 简体中文 / English，包含菜单、报告、导出表头及主要提示；浅色 / 深色主题 |
| 双扫描引擎 | 本地 NTFS MFT 读取，以及并行目录枚举；不满足条件时回退到目录枚举 |
| 扫描控制 | 多路径合并、暂停 / 继续 / 取消、过滤、深度限制、事件日志、可选所有者采集 |
| 可视化 | 柱状图、饼图、旭日图、矩形树图，支持文件夹下钻和 PNG 导出 |
| 文件报告 | 17 类，包括最大、最旧、最新、临时、重复、类型、分类、属性、所有者、大小、名称、时间、深度、年份、非业务文件、空文件夹、超长路径 |
| 重复检测 | 按名称、名称与大小、名称 / 大小 / 修改时间，或 SHA-256 内容比较 |
| 搜索与过滤 | 通配符、正则表达式、大小、占用空间、路径长度、文件数等条件；排除优先 |
| 快照与比较 | 自有 `.fsscan` 元数据快照、离线查看、快照差异、多时间点比较、磁盘历史 |
| 报告导出 | XLSX、CSV、HTML、PDF、XML、TXT；打印预览 |
| 文件操作 | 打开、定位、复制、移动、重命名、移入回收站、ZIP 归档 |
| 自动化 | 命令行扫描、Windows 计划任务、网络共享枚举、SMTP 邮件报告 |

### 关于 MFT 扫描

默认启用“使用 NTFS MFT 直接读取”。读取本地 NTFS 卷的 MFT 通常需要管理员权限；可在界面的“以管理员身份运行”入口启动。普通权限、网络路径、不支持的文件系统或 MFT 读取失败时，使用目录枚举。扫描结束后的状态栏显示实际采用的引擎。

即使只选择卷内的一个文件夹，MFT 模式仍可能读取整个卷的 MFT，再筛选目标目录。它不保证在所有目录和存储设备上都更快。本次本地自检使用普通用户权限，验证的是目录枚举路径，尚未对 MFT 引擎进行完整验收或发布性能比较。

## 从源码构建

使用 Windows 自带的 .NET Framework C# 编译器，不需要 NuGet 包或 Visual Studio 项目文件：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

产物为仓库根目录的 `FolderScope.exe`。源代码位于 `src/`；程序配置和清单分别是 `FolderScope.exe.config`、`app.manifest`。

运行内置自检：

```powershell
$testDir = Join-Path $env:TEMP ('FolderScope-tests-' + [guid]::NewGuid().ToString('N'))
$process = Start-Process -FilePath .\FolderScope.exe -ArgumentList @('--self-test', ('"' + $testDir + '"')) -Wait -PassThru
Get-Content (Join-Path $testDir 'test-results.txt')
if ($process.ExitCode -ne 0) { throw 'FolderScope self-test failed' }
```

自检在指定目录中生成测试文件、快照和报告。2026-10-23 发布准备时，本地重新编译后 **29 / 29 项通过**；摘要见 [验证结果.txt](验证结果.txt)。这些检查覆盖扫描统计、过滤、重复检测、快照、导出、复制、ZIP 和双语资源，不代表所有界面与环境相关功能均已验收。GitHub Actions 会重新构建并执行同一套自检。

## 命令行示例

```text
FolderScope.exe --scan "D:\Data" --snapshot "D:\Reports\scan.fsscan" --export "D:\Reports\details.xlsx"
FolderScope.exe --scan "D:\Data|E:\Data" --report "Largest Files" --export "D:\Reports\large.csv"
FolderScope.exe --scan "D:\Data" --report "Duplicate Files" --duplicate-mode "Content (SHA-256)" --export "D:\Reports\duplicates.html"
```

报告名称使用固定英文标识符。退出码为 `0`（运行结束）、`1`（错误）、`3`（取消）；即使退出码为 `0`，也应查看相邻 `.events.txt` 文件中的跳过、权限或不完整扫描信息。PowerShell 中需要等待程序结束时，请使用 `Start-Process -Wait -PassThru`。

## 当前边界

- 这是首个开源预览版。文件报告窗口首次显示时的短暂空白现象曾被报告，本次发布未对该现象完成复现或修复验收。
- 快照使用 FolderScope 自有格式，不兼容 FolderSizes 快照。未实现完整的停靠 / 浮动面板、所有高级规则、容量预测或加密 ZIP。
- 文件大小合计不等于卷实际物理占用；文件系统元数据、硬链接、压缩 / 稀疏文件和权限都会影响解释。MFT 模式的复杂 NTFS 场景仍需要更多验证。
- 元数据相同不代表文件内容相同；需要内容比较时使用 SHA-256。目录重解析点不跟随，避免循环遍历。
- PDF 使用位图页面，文本不可搜索或复制；完整路径宜导出为 XLSX、CSV 或 XML。
- 网络共享、真实 SMTP 投递、计划任务定时触发和大规模磁盘性能尚未在本次发布中验收。删除、移动等操作也未针对用户现有文件做破坏性测试。
- 设置、历史、作业和日志保存在 `%LOCALAPPDATA%\FolderScope`。提交问题时请去除个人路径、邮箱、共享地址和凭据。

中英文离线帮助包含更具体的使用方式；源代码、许可证和构建脚本均在本仓库内。

## 许可证

[MIT](LICENSE)。FolderSizes 名称用于说明项目参考对象；其商标和相关权利归各自权利人所有。
