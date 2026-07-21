# AZW3 Reader — Windows 桌面版电子书阅读器

基于 C# WPF + WebView2 的 AZW3/MOBI/KF8/EPUB 格式电子书阅读器。

## 环境要求

- Windows 10/11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Microsoft Edge WebView2（Windows 10/11 内置）
- 支持的文件: `.epub` / `.azw3` / `.mobi` / `.azw` / `.kf8`（**不支持 DRM 加密文件**）

## 快速开始

```bash
# 构建
dotnet build Azw3Reader.sln

# 运行
dotnet run --project src/Azw3Reader.App
```

## 项目结构

```
src/
├── Azw3Reader.Core/              # 核心解析库（无 UI 依赖）
│   ├── Models/                   # 数据模型
│   │   ├── BookInfo.cs           # 书籍元数据 + ExtractionResult
│   │   ├── Chapter.cs            # 章节模型
│   │   ├── ExthRecord.cs         # EXTH 元数据记录
│   │   ├── MobiHeader.cs         # MOBI 头结构
│   │   └── PdbHeader.cs          # PDB (Palm 数据库) 头结构
│   ├── Parsers/                  # 文件格式解析器
│   │   ├── PdbParser.cs          # PDB 容器格式解析
│   │   ├── MobiParser.cs         # MOBI 头 + EXTH 记录解析
│   │   ├── PalmDocDecompressor.cs   # PalmDoc LZ77 解压缩
│   │   └── HuffCdicDecompressor.cs  # HUFF/CDIC Huffman 解压缩 (KF8)
│   ├── Services/                 # 解析编排服务
│   │   ├── Azw3Extractor.cs      # AZW3/MOBI 主提取器
│   │   ├── EpubExtractor.cs      # EPUB 提取器（ZIP + OPF + spine/nav）
│   │   ├── BookLoader.cs         # 按扩展名分发加载
│   │   └── ChapterSplitter.cs    # 章节拆分（pagebreak / nav / 标题检测三级降级）
│   └── BigEndian.cs              # 大端字节序辅助方法
└── Azw3Reader.App/               # WPF UI 应用
    ├── Controls/                 # UI 控件
    │   ├── BookReaderControl.xaml / .cs   # 阅读器控件（工具栏 + WebView2）
    ├── Services/                 # 应用服务
    │   ├── ThemeManager.cs       # CSS 主题引擎（10 个主题）
    │   ├── ReadingProgressService.cs   # 阅读进度持久化
    │   └── WebViewBridge.cs      # JS-C# 双向桥接
    ├── ViewModels/               # MVVM ViewModel
    │   └── ReaderViewModel.cs
    ├── Converters/               # 值转换器
    ├── Styles/                   # 样式资源
    ├── App.xaml / App.xaml.cs    # 应用入口
    ├── MainWindow.xaml / .cs     # 主窗口
    └── AssemblyInfo.cs           # 程序集信息
```

## 功能

- [x] **AZW3/MOBI/KF8 格式解析** — PDB 容器 + MOBI 头 + EXTH 元数据
- [x] **EPUB 格式解析** — ZIP + OPF + spine，目录支持 EPUB3 nav / EPUB2 NCX
- [x] **三种压缩支持** — 无压缩 / PalmDoc (LZ77) / HUFF/CDIC (Huffman)
- [x] **章节导航** — `<mbp:pagebreak>` 标记 → `<nav>` TOC → `<h1>/<h2>/<h3>` 标题检测，三级降级策略
- [x] **图片提取与显示** — JPEG / PNG / GIF（EPUB 内联为 data URI）
- [x] **10 种阅读主题** — 默认、灰色、羊皮纸、草绿、樱桃、天空、Solarized、Gruvbox、Nord、夜间
- [x] **字体大小调节** — 12–36px，实时切换
- [x] **左右区域点击翻页** — 左 33% 上一页，右 33% 下一页
- [x] **键盘翻页** — 方向键 ← → / PageUp / PageDown
- [x] **阅读进度保存与恢复** — 自动保存到 `%LOCALAPPDATA%/Azw3Reader/progress.json`
- [x] **拖放打开文件** — 拖入 .epub/.azw3/.mobi/.azw/.kf8 文件即可打开
- [x] **标记与笔记** — 选中文字可高亮或划线，点击标记可添加/编辑评论
- [x] **标注导出 Markdown** — 工具栏「导出标注」生成本书全部摘录与笔记
- [ ] 全文搜索

## 限制

- **不支持 DRM 加密文件** — 需先解除 DRM 保护
- 部分早期 MOBI 格式（PalmDoc Lite）的布局可能不准确
