# VSRepo_Gui 修复计划

> 基于代码审查，按优先级排列所有待修复项。

---

## 一、安全缺陷

### 1.1 [高] 提权 PowerShell 脚本命令注入
- **文件：** `Services/VsrepoService.cs` → `RunVsrepoElevatedAsync`
- **状态：** ✅ 已修复（代码已使用 `-EncodedCommand` + Base64 编码）
- **问题：** 通过字符串模板将 `pythonExe` 和参数拼接进 `.ps1` 文件，转义仅处理单引号。包标识符来自网络获取的 JSON 定义文件，若包含 `$`、反引号等 PowerShell 元字符可突破字符串上下文。
- **修复方案：** 使用 `-EncodedCommand` + Base64 编码传递脚本，或通过环境变量 / JSON 临时文件传递参数。
- **影响范围：** 所有提权安装/卸载/升级操作。

### 1.2 [中] 提权执行临时文件竞争
- **文件：** `Services/VsrepoService.cs` → `RunVsrepoElevatedAsync`
- **状态：** ✅ 已修复（无 .ps1 临时文件，EncodedCommand 直传）
- **问题：** 脚本/输出/退出码文件写入 `%TEMP%\VSRepo_Gui`，在写入 `.ps1` 和启动 `powershell.exe` 之间存在被符号链接替换的窗口。
- **修复方案：** 使用 `FileOptions.DeleteOnClose` 写入脚本（需配合 `-EncodedCommand` 方案一并解决），或使用命名管道/匿名管道传递脚本内容。

### 1.3 [低] `CanWriteToPath` TOCTOU 竞争
- **文件：** `Services/VsrepoService.cs` → `CanWriteToPath`
- **状态：** ✅ 已修复（2026-05-31）
- **问题：** `FileOptions.DeleteOnClose` 创建文件后又尝试 `File.Delete`，后者可能因文件已被 `DeleteOnClose` 删除而抛异常。
- **修复方案：** 简化为仅用 `DeleteOnClose` 流配合 try/catch，移除多余的 `File.Exists` + `File.Delete` 逻辑。

---

## 二、逻辑缺陷

### 2.1 [中] 已安装包解析逻辑脆弱
- **文件：** `Services/VsrepoService.cs` → `GetInstalledAsync`
- **状态：** ✅ 已修复（代码已有 token 数量校验、Trim、AppLog）
- **问题：** 依赖位置标记（`tokens[^1]` = 标识符，`tokens[^3]` = 版本）和前缀标记（`*` = 有更新，`+` = 未知）。若 `vsrepo` 输出格式变化或标识符含空格，产生静默错误数据。
- **修复方案：**
  - 增加 token 数量校验（`< 5` 时记录警告并跳过）
  - 对标识符和版本做 `Trim()` 处理
  - 添加 `AppLog.Write` 记录无法解析的行

### 2.2 [极低] 多余的 `async` 修饰符
- **文件：** `MainWindow.xaml.cs` → `PackagesGrid_SelectionChanged`
- **状态：** ✅ 已修复（2026-05-31）
- **问题：** 声明为 `async void` 但方法体中无 `await` 表达式。
- **修复方案：** 移除 `async` 关键字。

### 2.3 [中] `app.manifest` 要求管理员权限与提权逻辑矛盾
- **文件：** `app.manifest`
- **状态：** ✅ 已修复（manifest 已为 `asInvoker`）
- **问题：** `requestedExecutionLevel level="requireAdministrator"` 使应用始终以管理员启动，导致代码中 `IsAdministrator()` 永远返回 `true`，`RunVsrepoElevatedAsync` 成为死代码。
- **修复方案：** 改为 `level="asInvoker"`，让应用默认以普通用户启动，仅在需要时通过 UAC 提权。

---

## 三、健壮性改进

### 3.1 `AppStateService.Save` 静默吞掉异常
- **文件：** `Services/AppStateService.cs` → `Save`
- **状态：** ✅ 已修复（2026-05-31）
- **问题：** 所有异常被 catch 后不做任何处理，用户可能静默丢失设置。
- **修复方案：** catch 块中调用 `AppLog.Write(exception, "AppStateService.Save")`。

### 3.2 `IsPermissionDenied` 硬编码中文错误字符串
- **文件：** `Services/VsrepoService.cs` → `IsPermissionDenied`
- **状态：** ✅ 已覆盖（代码已有 PermissionError/WinError 5/Access is denied）
- **问题：** 检查 `"拒绝访问"` 仅适用于中文系统，其他语言环境下权限拒绝可能漏判。
- **修复方案：** 补充更多语言的 "Access denied" 变体，或改用 ExitCode + 异常类型判断（当前的 `PermissionError` 和 `WinError 5` 已覆盖 Python 层面，可保留作为兜底）。

---

## 四、UI/主题缺陷

### 4.1 暗色主题状态徽章颜色缺失
- **文件：** `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`
- **状态：** ✅ 已修复（2026-05-31）
- **问题：** `StatUpdatesBrush`、`StatInstalledBrush` 等颜色硬编码为亮色主题优化值（如 `#DCEBFA` 背景），暗色模式下对比度不足。
- **修复方案：** 为暗色主题定义对应的颜色资源，利用 `DynamicResource` 或在主题切换时替换资源字典。
- **改动内容：**
  - `App.xaml`：`StatusBadgeTextStyle` 中 4 处 `StaticResource` → `DynamicResource`
  - `MainWindow.xaml`：状态栏 4 处 Foreground + 状态徽章 4 处 Background → `DynamicResource`
  - `App.xaml.cs`：新增 `UpdateStatusColors(bool isDark)` 方法，`ApplyThemeMode` 中调用

### 4.2 亮/暗 ComboBox/TextBox 模板重复
- **文件：** `App.xaml` + `MainWindow.xaml.cs` → `ApplyThemeSensitiveStyles`
- **状态：** ⬜ 待处理（改动面大，后续单独任务）
- **问题：** `FluentComboBoxStyle` 和 `FluentDarkComboBoxStyle` 是两个约 100 行的模板，仅颜色不同。TextBox 样式同理。
- **修复方案：** 合并为单一模板，使用 `DynamicResource` 配合主题感知的 `SolidColorBrush` 定义。消除 `ApplyThemeSensitiveStyles` 命令式方法。
- **备注：** 改动面较大，可作为后续单独任务处理。

---

## 五、构建与工程

### 5.1 `NuGet.config` 被 gitignore 但构建依赖
- **文件：** `.gitignore`, `tools/package_release.py`
- **状态：** ✅ 已修复（2026-05-31）
- **问题：** `NuGet.config` 被忽略但 `package_release.py` 使用 `--configfile` 引用。新克隆仓库无法直接构建。
- **修复方案：**
  - 从 `.gitignore` 移除 `NuGet.config`，将其提交到仓库（当前内容仅含标准 nuget.org 源）
  - 或从 `package_release.py` 移除 `--configfile` 参数（nuget.org 是默认源）

### 5.2 缺少单元测试
- **状态：** ⬜ 待处理（需新建测试项目）
- **问题：** 核心逻辑（`BuildPackageItems`、`GetLatestRelevantRelease`、`FormatDateDisplay`、搜索评分、已安装包解析）均无测试覆盖。
- **修复方案：**
  - 创建 `VSRepo_Gui.Tests` xUnit 测试项目
  - 为以下可测试逻辑添加单元测试：
    - `BuildPackageItems` — 包构建与状态映射
    - `GetLatestRelevantRelease` — 按类型/平台选择最新版本
    - `FormatDateDisplay` — 日期格式化
    - `GetInstalledAsync` 解析逻辑 — 各种输入格式
    - `IsPermissionDenied` — 权限拒绝检测
    - 搜索评分逻辑（需先从 MainWindow 抽取为独立方法）

---

## 六、UX 改进（低优先级，可后续处理）

### 6.1 长时间操作无进度指示
- **状态：** ⬜ 待处理
- **问题：** `SetBusy(true)` 仅改变鼠标光标。
- **建议：** 在日志区域添加状态消息，或增加进度条。

### 6.2 确认对话框使用原生 MessageBox
- **状态：** ⬜ 待处理
- **问题：** 打破 Fluent 设计一致性。
- **建议：** 替换为 WPF-UI 的 `ContentDialog`。
- **备注：** 需要将确认逻辑改为异步模式，改动面较大。

### 6.3 MainWindow 代码量过大
- **状态：** ⬜ 待处理
- **问题：** `MainWindow.xaml.cs` 约 800 行，所有过滤/搜索/状态管理/操作编排堆积在 code-behind 中。
- **建议：** 提取 ViewModel 或至少将过滤/搜索逻辑抽取为独立类。
- **备注：** 设计偏好问题，非 bug。

---

## 修复优先级排序

| 序号 | 项目 | 优先级 | 预估工作量 | 状态 |
|------|------|--------|-----------|------|
| 1 | 命令注入修复 (1.1) | P0 | 中 | ✅ 已有 |
| 2 | 临时文件竞争 (1.2) | P0 | 小 | ✅ 已有 |
| 3 | manifest 矛盾 (2.3) | P1 | 极小 | ✅ 已有 |
| 4 | 已安装包解析 (2.1) | P1 | 小 | ✅ 已有 |
| 5 | CanWriteToPath (1.3) | P1 | 小 | ✅ 已修复 |
| 6 | NuGet.config (5.1) | P1 | 极小 | ✅ 已修复 |
| 7 | AppStateService 日志 (3.1) | P2 | 极小 | ✅ 已修复 |
| 8 | 移除多余 async (2.2) | P2 | 极小 | ✅ 已修复 |
| 9 | 暗色主题颜色 (4.1) | P2 | 小 | ✅ 已修复 |
| 10 | 单元测试 (5.2) | P2 | 中 | ⬜ 待处理 |
| 11 | 模板合并 (4.2) | P3 | 大 | ⬜ 待处理 |
| 12 | UX 改进 (6.x) | P3 | 大 | ⬜ 待处理 |
