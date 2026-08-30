# Theme Forge（主题工坊）

一个 Playnite 扩展，把 **ThemeOptions** 与 **ThemeModifier** 的能力合并成一套统一的主题定制系统，并补上两者都缺的东西：**实时预览**、**完整中文界面**、**免重启即时生效**。

> 适用于 Playnite 10.x（Desktop 模式）。当前版本 1.1.0。

---

## 为什么要再写一个

Playnite 生态里有两个主题定制插件，各自解决一半问题：

| | ThemeOptions | ThemeModifier | **Theme Forge** |
|---|---|---|---|
| 预设（Presets）切换 | 支持 | 无 | 支持（含嵌套分组） |
| 主题声明式选项 | 支持（`options.yaml`） | 无 | 支持（`themeforge.yaml` / 兼容 `options.yaml`） |
| 直接改颜色 / 画刷 | 无 | 支持 | 支持（ARGB + Hex 取色器） |
| 改任意资源常量 | 无 | 仅 bool / 数值 | 支持 20+ 种 WPF 类型 |
| 实时预览 | 无 | 无 | **有**（独立预览面板） |
| 免重启生效 | 部分 | 部分 | **全量 DynamicResource 覆写** |
| 中文界面 | 无 | 无 | **有**（en_US / zh_CN） |
| 配置方案（Profile） | 无 | 无 | 有 |
| 导入 / 导出配置 | 无 | 无 | 有（JSON） |
| 缺失依赖扩展提示 | 无 | 无 | 有（`extensions.yaml`） |

两个插件同时装还会互相打架：它们都往 `Application.Current.Resources` 里塞覆写字典，谁最后写谁赢，且都不清理旧值。Theme Forge 用**单一长生命周期覆写字典**（原地改 key，不重复 merge）取代这套做法。

---

## 功能

### 五个页签

- **预设** — 主题预定义的整体风格切换，支持多级分组；每组自动补一个本地化的「默认」选项。
- **主题选项** — 主题在 `themeforge.yaml` 里声明的选项，按分组渲染成开关 / 滑块 / 下拉 / 取色器。
- **资源** — 兜底页：直接编辑主题里任意 `x:Key`。默认只列颜色和画刷；勾选「显示全部资源」可以看到主题定义的全部条目（Helium 这类主题有 1800+ 个）。
- **扩展** — 主题声明的必需 / 推荐扩展，未安装的高亮提示。
- **关于** — 版本、宿主版本、旧插件数据检测与一键导入。

### 实时预览

预览面板用**第二套独立资源字典**渲染一个主窗口的仿真版（顶栏、侧边栏、封面网格、详情面板、按钮、进度条、列表行、菜单悬停、弹出层与工具提示），改任何值立刻反映。圆角、渐变笔刷、窗口边框同样会跟着变。这样既可以在不污染当前界面的前提下试色（关掉「编辑时应用」），也可以在应用到真实窗口的同时对照看效果。

### 免重启生效

所有覆写都写进一个常驻的 `ResourceDictionary`，并且是**原地修改 key**而不是重新 merge 字典——只要主题用 `DynamicResource` 引用该 key，界面就会立刻刷新。少数只能用 `StaticResource` 消费的值会显式标注「需要重启」。

### 其他

- **配置方案**：同一主题存多套配色，随时切换。
- **导入 / 导出**：JSON 单文件，跨机器搬配置；导入时校验主题 Id 并提示不匹配。
- **搜索 / 只看已修改 / 全部展开折叠**：Helium 这种上百个选项的主题里非常必要。
- **未生效选项检测**：主题声明了某个选项但运行中的界面里没有对应资源时会警告，方便主题作者排错。
- **旧插件迁移**：一键读取 ThemeOptions / ThemeModifier 的存档并转换。

---

## 安装

1. 到 [Releases](https://github.com/Whereis-Alice/PlayniteThemeForge/releases) 下载 `.pext`，双击安装；或把编译产物放到
   `%APPDATA%\Playnite\Extensions\ThemeForge_f0c1a7d2-3b64-4f18-9d5a-2c8e6b41a903\`。
2. 重启 Playnite。入口在 **设置 → 扩展 → Theme Forge**，也可以开启顶栏按钮。

旧插件（ThemeOptions / ThemeModifier）可以先留着不卸，Theme Forge 不依赖它们；但两者都在运行时会争抢资源覆写，**确认迁移无误后建议卸载**。

---

## 从旧插件迁移

**关于 → 从 ThemeOptions / ThemeModifier 导入**。会转换：

- 主题选项取值
- 预设选择
- 颜色与纯色画刷
- 主题常量（bool / 数值）

> ⚠️ **渐变画刷（LinearGradientBrush / RadialGradientBrush）不会被迁移，需要手工重建。**
> 原因：WPF 的渐变画刷没有字符串 TypeConverter，旧插件用私有格式序列化渐变停靠点，无法可靠还原成通用表示。Theme Forge 的做法是让主题用**预设 xaml 文件**来切换渐变（见下文 `Files:`），比在插件里编辑渐变更可控。

---

## 给主题作者：`themeforge.yaml`

把 `themeforge.yaml` 放在主题根目录（和 `theme.yaml` 同级）即可。Theme Forge 按**优先级递减**合并三种 schema，**已存在的条目优先**：

1. `themeforge.yaml`（原生）
2. `options.yaml`（ThemeOptions 兼容）
3. `thememodifier.yaml`（ThemeModifier 兼容）

也就是说，已经支持旧插件的主题**不改任何文件**就能被 Theme Forge 读出来。

### 骨架

```yaml
Groups:
  - Id: palette
    LocKey: LOCMyThemeGroupPalette          # 本地化键，缺省时用 Title
    DescriptionLocKey: LOCMyThemeGroupDescPalette
    Icon: "\uec1d"
    Order: 10                                # 升序；同序按 Title

Variables:
  SidebarItemSize:                           # key 必须是主题里某个 x:Key
    Type: Double
    Default: "40"
    LocKey: LOCMyThemeOptSidebarItemSize
    DescriptionLocKey: LOCMyThemeDescSidebarItemSize
    Group: sidebar                           # 对应 Groups 里的 Id（或 Title）
    Slider: { Min: 28, Max: 72, Step: 1 }

  GridViewCoverSubtitleVisibility:
    Type: Visibility
    Default: Visible
    Group: gridCovers
    Choices:
      - { Value: Visible,   LocKey: LOCMyThemeValVisible }
      - { Value: Collapsed, LocKey: LOCMyThemeValCollapsed }

Presets:
  - Id: Accent
    LocKey: LOCMyThemePresetAccent
    Presets:
      - Id: Azure                            # 不要自己写 Default 子项，插件会自动补
        LocKey: LOCMyThemePresetAccentAzure
        Constants:
          GlyphColor: { Type: Color, Value: "#4FA3E3" }
      - Id: Crimson
        Files: [ Presets/Accent/Crimson.xaml ]   # 相对主题根目录
```

### 要点

- **`Type` 必须是这些之一**：`String, Boolean, Bool, Int32, Int, Integer, Double, Single, Color, SolidColorBrush, Brush, LinearGradientBrush, RadialGradientBrush, Thickness, CornerRadius, Duration, TimeSpan, Visibility, FontFamily, FontWeight, FontStyle, GridLength, HorizontalAlignment, VerticalAlignment, TextAlignment, TextWrapping, Stretch, Orientation, Dock, ScrollBarVisibility`。
- **不要把渐变画刷声明成 `Variables`** —— 它们没有字符串转换器，改用预设的 `Files:`。
- 预设 `Files:` 指向的 xaml 会被 `XamlReader.Load` 单独解析，因此**只能写纯 WPF**，不能出现 `{ThemeFile ...}` 之类 Playnite 专有标记扩展。
- 每个预设分组会自动插入一个本地化的「默认」子项（`LOCThemeForgeDefault`）放在首位，**不要自己再写一个 Default**。
- 值的叠加顺序：预设 `Constants` → `Variables` 声明值 → 用户在「资源」页的自由覆写。
- 主题的本地化文件放在 `<主题根>/Localization/<lang>.xaml`；找不到当前语言时回落到主题的 `en_US.xaml`，再回落到 Playnite 自身的 `ResourceProvider`。

### 声明依赖扩展：`extensions.yaml`

```yaml
Required:
  - ExtraMetadataLoader_705fdbca-e1fc-4004-b839-1d040b8b4429
Recommended:
  - playnite-successstory-plugin
Names:
  playnite-successstory-plugin: SuccessStory
```

未安装的会在「扩展」页高亮，并按开关设置弹一次提示。

---

## 相对上游修掉的问题

| 问题 | 上游行为 | 这里的处理 |
|---|---|---|
| ThemeModifier 声明语法里的备注被吞进 key | `Key (备注): 标签` 会把 `Key (备注)` 整体当成资源名，于是这一项永远匹配不到真实资源、永远不生效 | 解析时把括号备注拆出来当描述，key 只取前半段 |
| 分组合并重复 | 多 schema 合并时分组是无脑追加，同名分组会在界面里出现两次 | 按 `Id` / `Title` 大小写不敏感去重，保持「已存在的优先」 |
| 分组描述无法本地化 | `OptionGroup` 只有 `Description` 字面量 | 增加 `DescriptionLocKey`，与选项一致地走本地化解析 |
| 覆写字典越积越多 | 每次应用都新建字典 merge 进 `Application.Current.Resources`，旧字典不移除；且部分值改完不刷新 | 单一常驻覆写字典，**原地改 key**，`DynamicResource` 立即刷新，无泄漏 |
| 渐变画刷静默失败 | 声明成变量后不报错，只是没效果 | 明确禁止，并由校验工具在打包前报错 |
| 主题写错 key 无人提醒 | 无 | 「未生效选项」检测 + 离线校验工具 |

---

## 开发

```powershell
dotnet build source/ThemeForge.csproj -c Release
```

- 目标框架 `net462`，C# 7.3，`PlayniteSDK 6.12.0`。
- 产物：`source/bin/Release/ThemeForge.dll`。

仓库里还有两个**不随扩展分发**的开发工具：

- `tools/harness` — 离线 WPF 渲染台。不启动 Playnite 就能把设置界面的五个页签渲染成 PNG，用来检查布局和本地化串长。
- `tools/validator` — 离线校验工具。对一个主题目录做全量静态检查：`themeforge.yaml` 能否解析、每个变量 key 是否真的存在于主题 xaml、`Type` 是否可解析且非渐变、默认值是否落在滑块范围 / 选项集合内、分组是否声明、`LocKey` 是否在**两种语言**里都存在且数量一致、预设文件是否存在且能被 `XamlReader` 解析、全部 xaml 能否通过 `XmlDocument.Load`、`extensions.yaml` 与 `thememodifier.yaml` 是否合法。

```powershell
dotnet build tools/validator/Validator.csproj -c Release
./tools/validator/bin/Release/Validator.exe "C:\path\to\theme\source"
```

> 校验工具需要引用 Playnite 安装目录里的 `YamlDotNet.dll`。默认路径可以用 MSBuild 属性覆盖：
> `dotnet build tools/validator/Validator.csproj -c Release -p:PlayniteDir="E:\Software\Playnite"`

另外记一笔踩坑：`Playnite.SDK.Data.Serialization` 内部委托给一个由宿主通过 internal 方法注入的 `IDataSerializer`，在 Playnite 进程外调用会直接 `NullReferenceException`（表现为 `FromDirectory` 静默返回 null）。校验工具用一个基于 YamlDotNet 的 shim 通过反射把它塞进去。

---

## 许可与致谢

MIT。

思路、schema 约定与文件格式兼容性来自两个同为 MIT 的项目，谢谢作者：

- [ashpynov/ThemeOptions](https://github.com/ashpynov/ThemeOptions) — Artem Shptynov
- [Lacro59/playnite-thememodifier-plugin](https://github.com/Lacro59/playnite-thememodifier-plugin) — Lacro59

配套主题：[Helium Nova](https://github.com/Whereis-Alice/Helium-Nova)。

