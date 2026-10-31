# DupFinder — 重名文件/文件夹清理工具

[English](README_EN.md) | 中文

<img src="assets/icon_preview.png" width="96" height="96" align="right" alt="DupFinder icon">

扫描指定目录，找出**同名**的文件与文件夹，勾选后一键移入回收站（可恢复）。

## 一、获取程序

- **方式 A（推荐）**：到本仓库右侧 **Releases** 下载 `DupFinder.exe`（约 200 KB，含 16~256 共 9 种尺寸的图标），双击即用
- **方式 B**：自己编译 —— 安装 .NET SDK 6.0+ 后运行 `build.bat`，产物在 `dist\DupFinder.exe`

运行依赖：Windows 自带的 .NET Framework 4.0+（Win10 均已内置），**不需要安装 .NET Runtime，不需要任何第三方库**，单个文件即可运行。也可以把文件夹直接拖到窗口上作为扫描目录。

## 二、什么是「重名」

按**去掉扩展名后的名称**匹配，不区分大小写。因此：

```
D:\data\aaa\          <- 文件夹
D:\data\aaa.txt
D:\data\sub\aaa.zip
D:\data\sub\deep\aaa.exe
```

这 4 项会被归为同一组 `aaa`。目录树下任意位置（含子目录）的同名项都会聚到一组。
唯一文件名不会被列出（例如只有 `bbb.txt` 一个，就不算重名）。

可选关闭「忽略扩展名」：勾掉该选项后按完整文件名匹配，`aaa.txt` 与 `aaa.zip` 就不再算重名。

## 三、界面用法

1. **扫描目录**：输入或「浏览...」选择，也可直接把文件夹拖进窗口
2. **选项**
   - 递归子目录（默认开）
   - 忽略扩展名匹配（默认开，即上面的规则）
   - 包含文件 / 包含文件夹（可只查其中一类）
   - 跳过系统/版本控制目录（默认开：回收站、System Volume Information、.git、.svn 等）
   - 计算文件夹体积（默认关，开启后扫描会变慢）
   - 最少同名数（默认 2，可调成 3、4 只看更严重的情况）
3. **选择要清理的项**
   - 勾选单项；勾选组名 = 整组全选/取消
   - 「智能选择」：按「每组保留最新修改 / 最早修改 / 体积最大 / 路径最短 / 不保留」自动勾选其余项
   - 全选 / 全不选 / 反选
4. **删除**：点「删除选中项到回收站」，会先弹出确认框列出将删除的清单与总体积（含文件夹时会红色警示"文件夹连同内部内容一起删除"），确认后移入回收站，**可随时从回收站还原**
5. 双击某项可在资源管理器中定位；右键可复制完整路径、取消勾选、从列表移除；「导出清单」可导出 CSV

## 四、安全设计

- 只走 Windows Shell 的回收站接口，不做永久删除
- 删除前逐项做安全校验，以下情况会被拦截并提示：磁盘根目录、扫描根目录本身、Windows/System/Program Files/用户目录/桌面/文档等受保护路径、不在扫描目录之内的路径、路径超过 240 字符
- 跳过符号链接/快捷方式等重解析点，避免删到目录树之外的内容
- 无访问权限的目录只记录警告，不会中断扫描

## 五、命令行模式（可选，便于批量/自动化）

```bat
DupFinder.exe --cli --dir "D:\data"                    :: 列出重名组
DupFinder.exe --cli --dir "D:\data" --no-recursive     :: 只看顶层
DupFinder.exe --cli --dir "D:\data" --with-extension   :: 按完整文件名匹配
DupFinder.exe --cli --dir "D:\data" --files-only       :: 只看文件
DupFinder.exe --cli --dir "D:\data" --min 3            :: 只看 3 项以上的重名组
DupFinder.exe --cli --dir "D:\data" --folder-size      :: 顺便统计文件夹体积
DupFinder.exe --cli --dir "D:\data" --json             :: JSON 输出
DupFinder.exe --cli --dir "D:\data" --delete --yes     :: 每组保留最新的一项，其余移入回收站
```

> `--delete` 必须配合 `--yes` 才会真正执行。

## 六、从源码重新构建

需要 .NET SDK 6.0+（仅构建期需要，不影响产物运行）：

```bat
build.bat
```

或手动：`dotnet build -c Release`，产物在 `bin\Release\net48\DupFinder.exe`。

> 说明：`Microsoft.NETFramework.ReferenceAssemblies` 这个 NuGet 包只在**编译期**提供 .NET Framework 4.8 的引用程序集，不会被打包进 exe。

## 七、高分屏 / 多显示器适配

程序在这几台机器上都验证过（4K 屏 + 150% 缩放、主屏 100% + 副屏 150% 混用）：

- 启动时启用 Per-Monitor DPI 感知，避免被系统位图拉伸（否则文字发虚、窗口被放大到超出屏幕）
- 窗口尺寸按「窗口实际所在显示器」的可用区域与该屏缩放系数换算，不会超出屏幕边缘
- 选项区与按钮区是自动换行的流式布局，窗口拉窄时控件自动换行增高，不会被截断

排查用（默认关闭，不影响正常使用）：

```bat
set DUPFINDER_DEBUG=1
DupFinder.exe
```
会在 `%TEMP%\dupfinder_layout.txt` 输出各区域的实际坐标；`DUPFINDER_SHOT=1` 会额外输出窗口截图 `%TEMP%\dupfinder_shot.png`。

## 八、源码结构

```
src/
  Models.cs     数据模型（重名项/重名组/扫描选项/结果）
  Scanner.cs    目录遍历与重名分组（支持取消、异常收集）
  RecycleBin.cs 通过 Shell API 移入回收站
  Safety.cs     删除前路径安全校验 + 格式化辅助
  Cli.cs        命令行模式
  NativeDpi.cs  进程 DPI 感知与缩放系数
  MainForm.cs   WinForms 界面
  Program.cs    入口
```

```
tools/
  gen_icon.py   生成 assets/DupFinder.ico（纯标准库，见第九节）
```

## 九、程序图标

- 图标文件：`assets/DupFinder.ico`，含 16 / 20 / 24 / 32 / 40 / 48 / 64 / 128 / 256 共 9 种尺寸，32bpp 带透明通道
- 生成方式：`python tools/gen_icon.py` —— 纯 Python 标准库（zlib + struct）实现，用 SDF + 超采样绘制，**不依赖 Pillow 等任何第三方包**。改完脚本重新生成并编译即可生效
- 图标经 csproj 的 `ApplicationIcon` 嵌进 exe 的 Win32 资源，所以资源管理器、任务栏、Alt-Tab 里都会显示；同时嵌了一份托管资源供运行时设置窗体图标，会按 DPI 自动挑选尺寸（150% 缩放下取 48×48，不会模糊）

## 十、许可证

MIT License，详见 [LICENSE](LICENSE)。
