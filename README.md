# MiniGame Doctor

MiniGame Doctor 是一个 Unity Editor 发布前体检工具，第一阶段针对 **WebGL / 微信小游戏** 项目。

## V0.1 已实现

- Resources 目录体积与大文件检查
- 单文件大型资源检查
- 纹理 Read/Write、Max Size、未压缩、Sprite Mip Maps 检查
- 音频 Decompress On Load、长立体声音频检查
- 首场景依赖体积快速检查
- WebGL Brotli、Decompression Fallback、Engine Stripping、Managed Stripping、Data Caching 检查
- Unity 6 WebAssembly 2023 检查
- 微信小游戏转换 SDK 基础识别
- UI Toolkit 结果面板、严重度分级、Asset 一键定位

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

## 设计原则

MiniGame Doctor 不直接“乱改项目”。V0.1 默认只读扫描并给出定位和处理建议，避免批量优化误伤项目。后续版本会把可证明安全的修改加入“安全自动修复”，并在修改前展示具体差异。

## 下一步

- BuildReport 解析：按最终构建贡献而不是源文件大小排序
- SpriteAtlas / Shader Variant / TMP 字体体积检查
- 微信小游戏首包与分包专项规则
- 一键生成 Markdown / JSON 体检报告
- 安全自动修复与修改前后对比
- CI 模式：在 GitHub Actions 中设置包体和错误阈值

## Package

- Package ID: `com.ash.minigame-doctor`
- Version: `0.1.0`
- Menu: `Tools > MiniGame Doctor`
