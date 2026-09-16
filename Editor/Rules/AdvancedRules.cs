using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace MiniGameDoctor.Editor
{
    internal sealed class BuildReportRule : IDiagnosticRule
    {
        public string Id => "build.report";
        public string DisplayName => "最近构建报告";

        public IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context)
        {
            if (!BuildReportSnapshotStore.TryLoad(context.ProjectRoot, out var snapshot))
            {
                yield return new DiagnosticIssue(
                    Id,
                    "还没有可分析的 BuildReport",
                    "MiniGame Doctor 会在下一次 Player Build 完成后自动记录构建总量和最大的打包资源。",
                    "完成一次 WebGL/目标平台构建后重新体检，即可看到真实打包贡献，而不只是源文件大小。",
                    DiagnosticSeverity.Info);
                yield break;
            }

            if (snapshot.TotalBytes >= 40L * 1024L * 1024L)
            {
                yield return new DiagnosticIssue(
                    Id,
                    $"最近构建体积约 {DiagnosticContext.FormatBytes(snapshot.TotalBytes)}",
                    $"平台：{snapshot.Platform}；结果：{snapshot.Result}；记录时间：{snapshot.CapturedAtUtc}。",
                    "结合下面的打包资源贡献优先处理大项；微信小游戏还需要继续关注首包与分包后的实际大小。",
                    snapshot.TotalBytes >= 100L * 1024L * 1024L ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning);
            }

            foreach (var asset in (snapshot.Assets ?? new List<BuildAssetSnapshot>()).Take(12))
            {
                if (asset.PackedBytes < 2L * 1024L * 1024L)
                    continue;

                yield return new DiagnosticIssue(
                    Id,
                    $"构建大项：{Path.GetFileName(asset.AssetPath)}",
                    $"该资源在最近一次构建的 PackedAssets 中累计贡献约 {DiagnosticContext.FormatBytes(asset.PackedBytes)}。",
                    "检查它是否必须进入首包，并结合资源类型调整压缩、分辨率、加载方式或分包策略。",
                    asset.PackedBytes >= 10L * 1024L * 1024L ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    asset.AssetPath.StartsWith("Assets/", StringComparison.Ordinal) ? asset.AssetPath : null);
            }
        }
    }

    internal sealed class ShaderVariantRule : IDiagnosticRule
    {
        public string Id => "shader.variants";
        public string DisplayName => "Shader 与变体";

        public IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context)
        {
            var graphicsSettings = GraphicsSettings.GetGraphicsSettings();
            if (graphicsSettings != null)
            {
                var serialized = new SerializedObject(graphicsSettings);
                var alwaysIncluded = serialized.FindProperty("m_AlwaysIncludedShaders");
                if (alwaysIncluded != null && alwaysIncluded.isArray)
                {
                    var nullCount = 0;
                    for (var i = 0; i < alwaysIncluded.arraySize; i++)
                    {
                        var shader = alwaysIncluded.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                        if (shader == null)
                        {
                            nullCount++;
                            continue;
                        }

                        var keywordCount = shader.keywordSpace.keywordCount;
                        if (keywordCount >= 32)
                        {
                            yield return new DiagnosticIssue(
                                Id,
                                $"Always Included Shader 关键词较多：{shader.name}",
                                $"该 Shader 有 {keywordCount} 个本地关键词，并被列入 Always Included Shaders。Always Included 会强制把所有变体组合带入构建，容易造成变体膨胀。",
                                "确认它是否真的需要全量变体；能用 ShaderVariantCollection 或正常材质引用覆盖时，尽量不要放在 Always Included。",
                                keywordCount >= 64 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                                AssetDatabase.GetAssetPath(shader));
                        }
                    }

                    if (nullCount > 0)
                    {
                        yield return new DiagnosticIssue(
                            Id,
                            $"Always Included Shaders 中有 {nullCount} 个空条目",
                            "Graphics Settings 的 Always Included Shaders 包含无效引用。",
                            "可以安全清理这些空条目，不会删除任何有效 Shader。",
                            DiagnosticSeverity.Info,
                            safeFixId: SafeFixRegistry.RemoveNullAlwaysIncludedShaders);
                    }
                }
            }

            foreach (var guid in AssetDatabase.FindAssets("t:ShaderVariantCollection"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;

                var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(path);
                if (collection == null || collection.variantCount < 2000)
                    continue;

                yield return new DiagnosticIssue(
                    Id,
                    $"ShaderVariantCollection 包含 {collection.variantCount:N0} 个变体",
                    "大型变体集合会增加构建时间、包体和运行时 Shader 预热成本。",
                    "确认集合里只保留目标平台真正需要的变体，并避免把开发期采集到的所有变体无筛选地带入正式包。",
                    collection.variantCount >= 10000 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    path);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Shader"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                    continue;

                var keywordCount = shader.keywordSpace.keywordCount;
                if (keywordCount < 96)
                    continue;

                yield return new DiagnosticIssue(
                    Id,
                    $"Shader 本地关键词很多：{shader.name}",
                    $"检测到 {keywordCount} 个本地关键词。关键词越多，潜在变体组合越容易指数增长。",
                    "检查 multi_compile / shader_feature 的拆分方式，优先剔除目标平台永远不会使用的功能组合。",
                    keywordCount >= 128 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                    path);
            }
        }
    }

    internal sealed class FontAssetRule : IDiagnosticRule
    {
        public string Id => "assets.font";
        public string DisplayName => "TMP / 字体";

        public IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;

                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null || asset.GetType().FullName != "TMPro.TMP_FontAsset")
                    continue;

                var type = asset.GetType();
                var mode = ReadProperty(type, asset, "atlasPopulationMode")?.ToString() ?? "Unknown";
                var atlasCount = ReadIntProperty(type, asset, "atlasTextureCount");
                var atlasWidth = ReadIntProperty(type, asset, "atlasWidth");
                var atlasHeight = ReadIntProperty(type, asset, "atlasHeight");
                var characterCount = ReadCollectionCount(type, asset, "characterTable");
                var assetBytes = context.GetFileSize(path);

                if (assetBytes >= 8L * 1024L * 1024L)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"字体资源较大：{Path.GetFileName(path)}",
                        $"字体 Asset 源文件约 {DiagnosticContext.FormatBytes(assetBytes)}，字符约 {characterCount:N0}，Atlas {atlasWidth}×{atlasHeight}，Atlas 数 {Math.Max(1, atlasCount)}。",
                        "中文字体优先按游戏真实文案生成静态字符集；不要为了省事把整个 Unicode/CJK 字符集全部塞进首包。",
                        assetBytes >= 20L * 1024L * 1024L ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                        path);
                }

                if (string.Equals(mode, "Dynamic", StringComparison.OrdinalIgnoreCase))
                {
                    var sourceFont = ReadProperty(type, asset, "sourceFontFile") as Font;
                    var sourcePath = sourceFont == null ? null : AssetDatabase.GetAssetPath(sourceFont);
                    var sourceBytes = string.IsNullOrEmpty(sourcePath) ? 0 : context.GetFileSize(sourcePath);
                    if (sourceBytes >= 2L * 1024L * 1024L)
                    {
                        yield return new DiagnosticIssue(
                            Id,
                            $"动态 TMP 字体会携带源字体：{Path.GetFileName(path)}",
                            $"Population Mode 为 Dynamic，源字体约 {DiagnosticContext.FormatBytes(sourceBytes)}。动态字体需要在运行时从源字体补字，因此源字体会进入构建。",
                            "若游戏文案基本固定，评估改为 Static 并只预生成实际使用字符；需要动态用户名/聊天时再保留 Dynamic。",
                            sourceBytes >= 10L * 1024L * 1024L ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                            path);
                    }
                }
                else if (string.Equals(mode, "DynamicOS", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"字体依赖目标系统字体：{Path.GetFileName(path)}",
                        "Population Mode 为 Dynamic OS，最终效果依赖目标平台是否存在对应系统字体。",
                        "WebGL/小游戏发布前在真实目标环境验证字体可用性；跨平台稳定性要求高时不要只依赖系统字体。",
                        DiagnosticSeverity.Info,
                        path);
                }

                if (atlasWidth >= 4096 || atlasHeight >= 4096 || atlasCount > 1)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"字体 Atlas 规模较大：{Path.GetFileName(path)}",
                        $"Atlas 尺寸 {atlasWidth}×{atlasHeight}，Atlas 数 {Math.Max(1, atlasCount)}。",
                        "检查采样尺寸、字符集和 Multi Atlas；中文 UI 经常可以通过真实文案字符裁剪显著降低纹理占用。",
                        atlasCount >= 3 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                        path);
                }
            }
        }

        private static object ReadProperty(Type type, object target, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return property?.GetValue(target, null);
        }

        private static int ReadIntProperty(Type type, object target, string name)
        {
            var value = ReadProperty(type, target, name);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static int ReadCollectionCount(Type type, object target, string name)
        {
            return ReadProperty(type, target, name) is ICollection collection ? collection.Count : 0;
        }
    }

    internal sealed class SpriteAtlasRule : IDiagnosticRule
    {
        public string Id => "assets.sprite-atlas";
        public string DisplayName => "Sprite Atlas";

        public IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context)
        {
            var atlasPaths = context.AssetPaths
                .Where(path => path.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToArray();

            if (atlasPaths.Length == 0)
            {
                var spriteCount = AssetDatabase.FindAssets("t:Sprite")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Count(path => path.StartsWith("Assets/", StringComparison.Ordinal));

                if (spriteCount >= 100)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"项目有大量 Sprite，但没有检测到 Sprite Atlas",
                        $"当前至少检测到 {spriteCount} 个 Sprite 资源。大量零散纹理可能增加 draw call、资源元数据和加载开销。",
                        "按 UI 模块/关卡/生命周期规划 Sprite Atlas；不要简单把所有 Sprite 塞进一个超大图集。",
                        DiagnosticSeverity.Warning);
                }
                yield break;
            }

            if (EditorSettings.spritePackerMode == SpritePackerMode.Disabled)
            {
                yield return new DiagnosticIssue(
                    Id,
                    "Sprite Atlas packing 被禁用",
                    $"项目存在 {atlasPaths.Length} 个 Sprite Atlas，但 EditorSettings.spritePackerMode 当前为 Disabled，图集不会在构建中正常打包。",
                    "在 Project Settings > Editor > Sprite Packer 中选择适合项目的 V2 模式。V1→V2 会迁移资源，因此这里不自动修改。",
                    DiagnosticSeverity.Error);
            }

            foreach (var path in atlasPaths)
            {
                var importer = AssetImporter.GetAtPath(path) as SpriteAtlasImporter;
                if (importer == null)
                    continue;

                var texture = importer.textureSettings;
                if (texture.readable)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"Sprite Atlas 开启 Read/Write：{Path.GetFileName(path)}",
                        "图集纹理在 CPU 侧保留可读副本，会明显增加纹理内存。",
                        "如果运行时不读取图集像素，关闭 Read/Write。",
                        DiagnosticSeverity.Warning,
                        path);
                }

                if (texture.generateMipMaps)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"Sprite Atlas 开启 Mip Maps：{Path.GetFileName(path)}",
                        "纯 UI / 常规 2D 图集通常不需要 mipmap，会增加纹理数据量。",
                        "若图集中的 Sprite 不会在 3D 世界中大幅缩小，关闭 Generate Mip Maps。",
                        DiagnosticSeverity.Info,
                        path);
                }

                var webSettings = importer.GetPlatformSettings("WebGL");
                if (webSettings.overridden && webSettings.maxTextureSize > 2048)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"WebGL 图集 Max Size 为 {webSettings.maxTextureSize}：{Path.GetFileName(path)}",
                        "大型图集会增加单次上传显存、解码峰值和低端设备压力。",
                        "确认图集拆分和目标设备能力；小游戏常见 UI 图集优先控制在 2048 级别。",
                        webSettings.maxTextureSize >= 4096 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                        path);
                }

                if (!importer.includeInBuild)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"Sprite Atlas 未 Include in Build：{Path.GetFileName(path)}",
                        "该图集不会自动包含并加载到 Player Build。若没有 Late Binding / Addressables 加载逻辑，相关 Sprite 可能不可见。",
                        "确认这是有意的远程/延迟加载设计，并验证运行时 atlasRequested / Addressables 加载链路。",
                        DiagnosticSeverity.Info,
                        path);
                }

                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (atlas != null)
                {
                    var packables = SpriteAtlasExtensions.GetPackables(atlas);
                    if (packables == null || packables.Length == 0)
                    {
                        yield return new DiagnosticIssue(
                            Id,
                            $"空 Sprite Atlas：{Path.GetFileName(path)}",
                            "Objects For Packing 为空，这个图集不会产生有效的 Sprite 打包收益。",
                            "添加正确的 Sprite/文件夹，或者删除不再使用的空图集。",
                            DiagnosticSeverity.Info,
                            path);
                    }
                }
            }
        }
    }
}
