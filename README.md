# MiniGame Doctor

MiniGame Doctor 是一个 Unity Editor 发布前体检工具，当前重点针对 **WebGL / 微信小游戏** 项目。

## V0.2 已实现

### 资源与场景
- Resources 目录体积与大文件检查
- 单文件大型资源检查
- 纹理 Read/Write、Max Size、未压缩、Sprite Mip Maps 检查
- 音频 Decompress On Load、长立体声音频检查
- 首场景依赖体积快速检查

### BuildReport
- 每次 Player Build 后自动记录最近一次 BuildReport
- 保存总构建大小、目标平台、构建耗时与 PackedAssets 贡献
- 体检时按真实 packed size 标记最大的构建资源
- 数据只写入 `Library/MiniGameDoctor/`，不会污染 Git 仓库

### Shader
- Always Included Shaders 空引用检查
- Always Included Shader 本地关键词数量检查
- ShaderVariantCollection 变体数量检查
- 自定义 Shader 高关键词数量检查

### TMP / 字体
- TMP Font Asset 源文件体积检查
- Dynamic 字体源字体随包风险提示
- Dynamic OS 跨平台字体可用性提示
- 4096 级 Atlas / Multi Atlas 检查
- 中文大字符集裁剪建议

### Sprite Atlas
- 大量 Sprite 但没有图集的项目提示
- Sprite Packer Disabled 检查
- Atlas Read/Write、Mip Maps、WebGL Max Size 检查
- Include in Build / Late Binding 风险提示
- 空 Atlas 检查

### WebGL / 微信小游戏
- Brotli、Decompression Fallback、Engine Stripping、Managed Stripping、Data Caching 检查
- Debug Symbols 检查
- Unity 6 WebAssembly 2023 检查
- 微信小游戏转换 SDK 基础识别

### 低风险自动修复
- 清理 Always Included Shaders 中的空引用
- 将 WebGL Debug Symbols 设为 Off
- 支持单项修复与“修复所有低风险项”

MiniGame Doctor **不会**自动修改字体字符集、音频压缩、Shader 变体组合、Sprite Atlas V1→V2 迁移等可能改变项目语义的配置。

## 安装

Unity 2022.3 LTS 或更高版本：

1. 打开 `Window > Package Manager`
2. 点击左上角 `+`
3. 选择 `Add package from git URL...`
4. 输入：

```text
https://github.com/dagasolo138-lgtm/upgraded-octo-waddle.git
```

安装后打开：

```text
Tools > MiniGame Doctor
```

点击 **开始体检** 即可扫描当前项目。

## BuildReport 使用方式

安装 V0.2 后正常执行一次 Player Build。MiniGame Doctor 的构建回调会把最近一次构建摘要保存到：

```text
Library/MiniGameDoctor/last-build.json
```

之后重新点击 **开始体检**，即可看到真实打包资源贡献。

## 设计原则

1. 默认先诊断，再修改。
2. 自动修复只覆盖低风险、容易回滚的发布配置。
3. 所有可能改变运行时逻辑或资源内容的优化只给出定位与建议。
4. 优先使用 Unity 公共 Editor API，避免直接改 `.meta`、场景 YAML 或内部缓存。

## 下一步

- 微信小游戏首包 / 分包专项规则
- Addressables 分组与重复依赖分析
- Markdown / JSON 体检报告导出
- 修复前后差异记录与 Undo 支持
- CI 模式与 GitHub Actions 阈值检查
- 构建历史趋势：包体突然膨胀检测

## Package

- Package ID: `com.ash.minigame-doctor`
- Version: `0.2.0`
- Menu: `Tools > MiniGame Doctor`
