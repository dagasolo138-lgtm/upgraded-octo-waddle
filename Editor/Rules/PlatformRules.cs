using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace MiniGameDoctor.Editor
{
    internal sealed class BuildSceneRule : IDiagnosticRule
    {
        public string Id => "build.scenes";
        public string DisplayName => "构建场景";

        public IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context)
        {
            var enabledScenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            if (enabledScenes.Length == 0)
            {
                yield return new DiagnosticIssue(
                    Id,
                    "Build Settings 中没有启用场景",
                    "当前项目没有可进入构建的场景。",
                    "在 File > Build Settings / Build Profiles 中至少启用一个启动场景。",
                    DiagnosticSeverity.Error);
                yield break;
            }

            var firstScene = enabledScenes[0].path;
            if (string.IsNullOrEmpty(firstScene))
                yield break;

            var dependencies = AssetDatabase.GetDependencies(firstScene, true)
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
                .Distinct()
                .ToArray();
            var bytes = dependencies.Sum(context.GetFileSize);

            if (bytes >= 20L * 1024L * 1024L)
            {
                yield return new DiagnosticIssue(
                    Id,
                    "首场景直接依赖资源偏大",
                    $"首个启用场景 {firstScene} 的直接/间接 Assets 依赖源文件总量约 {DiagnosticContext.FormatBytes(bytes)}。这不是最终包体大小，但能快速暴露首屏资源过重问题。",
                    "把非首屏必需内容从首场景引用链中拆出去，按关卡/模块延迟加载或分包。",
                    bytes >= 50L * 1024L * 1024L ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    firstScene);
            }
        }
    }

    internal sealed class WebGlSettingsRule : IDiagnosticRule
    {
        public string Id => "platform.webgl";
        public string DisplayName => "WebGL 发布设置";

        public IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context)
        {
            if (PlayerSettings.WebGL.compressionFormat != WebGLCompressionFormat.Brotli)
            {
                yield return new DiagnosticIssue(
                    Id,
                    "WebGL 未使用 Brotli 压缩",
                    $"当前 Compression Format：{PlayerSettings.WebGL.compressionFormat}。",
                    "正式 HTTPS/CDN 发布通常优先使用 Brotli；如果目标服务器不能正确发送 Content-Encoding，再按部署环境选择 Gzip/Decompression Fallback。",
                    DiagnosticSeverity.Warning);
            }

            if (PlayerSettings.WebGL.decompressionFallback)
            {
                yield return new DiagnosticIssue(
                    Id,
                    "WebGL 开启 Decompression Fallback",
                    "浏览器会额外执行 JavaScript 解压逻辑，增加启动成本和额外脚本体积。",
                    "如果 CDN/服务器能正确提供 Brotli/Gzip Content-Encoding，关闭 Decompression Fallback。",
                    DiagnosticSeverity.Warning);
            }

            if (!PlayerSettings.stripEngineCode)
            {
                yield return new DiagnosticIssue(
                    Id,
                    "Strip Engine Code 未开启",
                    "未使用的 Unity 引擎代码可能被带入 WebGL 构建。",
                    "正式构建开启 Strip Engine Code，并在真机/浏览器回归测试反射与动态加载功能。",
                    DiagnosticSeverity.Warning);
            }

            var strippingLevel = PlayerSettings.GetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL);
            if (strippingLevel == ManagedStrippingLevel.Minimal || strippingLevel == ManagedStrippingLevel.Low)
            {
                yield return new DiagnosticIssue(
                    Id,
                    "Managed Stripping Level 偏低",
                    $"当前 WebGL Managed Stripping Level：{strippingLevel}。",
                    "正式小游戏/WebGL 构建建议评估 Medium/High；使用反射或动态类型时用 link.xml 保留必要代码。",
                    DiagnosticSeverity.Warning);
            }

            if (!PlayerSettings.WebGL.dataCaching)
            {
                yield return new DiagnosticIssue(
                    Id,
                    "WebGL Data Caching 未开启",
                    "重复访问时无法利用浏览器 IndexedDB 缓存构建数据。",
                    "对普通 WebGL 发布可考虑开启 Data Caching；微信小游戏最终行为仍应以转换 SDK 与真机测试为准。",
                    DiagnosticSeverity.Info);
            }

            if (PlayerSettings.WebGL.debugSymbolMode != WebGLDebugSymbolMode.Off)
            {
                yield return new DiagnosticIssue(
                    Id,
                    "WebGL 构建仍生成 Debug Symbols",
                    $"当前 Debug Symbols：{PlayerSettings.WebGL.debugSymbolMode}。发布包如果不需要线上符号文件，可以关闭以减少构建输出。",
                    "正式发布前可设为 Off；若你依赖线上堆栈符号化，可继续保留 External。",
                    DiagnosticSeverity.Info,
                    safeFixId: SafeFixRegistry.DisableWebGlDebugSymbols);
            }

#if UNITY_6000_0_OR_NEWER
            if (!PlayerSettings.WebGL.wasm2023)
            {
                yield return new DiagnosticIssue(
                    Id,
                    "WebAssembly 2023 未开启",
                    "Unity 6 项目可以使用较新的 WebAssembly 语言特性获得更好的体积/性能基础。",
                    "确认目标浏览器基线支持后开启 WebAssembly 2023。",
                    DiagnosticSeverity.Info);
            }
#endif
        }
    }

    internal sealed class WeChatPreparationRule : IDiagnosticRule
    {
        public string Id => "platform.wechat";
        public string DisplayName => "微信小游戏准备";

        public IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context)
        {
            var manifestPath = Path.Combine(context.ProjectRoot, "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
                yield break;

            var manifest = File.ReadAllText(manifestPath);
            var looksInstalled = manifest.IndexOf("wechat-miniprogram", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 manifest.IndexOf("minigame-tuanjie-transform-sdk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 manifest.IndexOf("minigame-unity-webgl-transform", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!looksInstalled)
            {
                yield return new DiagnosticIssue(
                    Id,
                    "未在 manifest.json 中识别到微信小游戏转换 SDK",
                    "当前只检测了官方仓库常见标识。若项目尚未接入转换 SDK，Unity WebGL 构建不能直接作为微信小游戏工程使用。",
                    "发布微信小游戏前接入官方 Unity/团结引擎转换 SDK；如果你使用本地包或不同包名，可忽略此项。",
                    DiagnosticSeverity.Info,
                    "Packages/manifest.json");
            }
        }
    }
}
