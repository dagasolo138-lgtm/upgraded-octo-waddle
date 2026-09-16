using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiniGameDoctor.Editor
{
    internal sealed class ResourcesFolderRule : IDiagnosticRule
    {
        public string Id => "assets.resources";
        public string DisplayName => "Resources 目录";

        public IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context)
        {
            var resourceAssets = context.AssetPaths
                .Where(path => path.StartsWith("Assets/Resources/", StringComparison.Ordinal) ||
                               path.IndexOf("/Resources/", StringComparison.Ordinal) >= 0)
                .Select(path => new { Path = path, Size = context.GetFileSize(path) })
                .Where(item => item.Size > 0)
                .OrderByDescending(item => item.Size)
                .ToList();

            if (resourceAssets.Count == 0)
                yield break;

            var total = resourceAssets.Sum(item => item.Size);
            var largest = string.Join("\n", resourceAssets.Take(5)
                .Select(item => $"• {item.Path} ({DiagnosticContext.FormatBytes(item.Size)})"));

            yield return new DiagnosticIssue(
                Id,
                $"Resources 中包含 {resourceAssets.Count} 个文件",
                $"Resources 文件总大小约 {DiagnosticContext.FormatBytes(total)}。其中较大的文件：\n{largest}",
                "只保留真正需要 Resources.Load 的内容；其余资源优先改为直接引用、Addressables 或平台分包。",
                total >= 10L * 1024L * 1024L ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning);
        }
    }

    internal sealed class LargeAssetRule : IDiagnosticRule
    {
        private const long WarningThreshold = 5L * 1024L * 1024L;
        public string Id => "assets.large";
        public string DisplayName => "大型资源";

        public IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context)
        {
            foreach (var path in context.AssetPaths)
            {
                var size = context.GetFileSize(path);
                if (size < WarningThreshold)
                    continue;

                yield return new DiagnosticIssue(
                    Id,
                    $"大型资源：{Path.GetFileName(path)}",
                    $"源文件大小约 {DiagnosticContext.FormatBytes(size)}。大型单文件通常会显著影响 WebGL/小游戏下载、解压和内存峰值。",
                    "确认该资源是否必须进入首包，并检查压缩、分辨率、音频编码或远程资源拆分策略。",
                    size >= 20L * 1024L * 1024L ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    path);
            }
        }
    }

    internal sealed class TextureImportRule : IDiagnosticRule
    {
        public string Id => "assets.texture";
        public string DisplayName => "纹理导入";

        public IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                if (importer.isReadable)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"纹理开启了 Read/Write：{Path.GetFileName(path)}",
                        "Read/Write Enabled 会让纹理在 CPU 侧额外保留一份可读副本，移动端和 WebGL 通常没有必要。",
                        "如果运行时不需要 GetPixels/SetPixels 等 CPU 读写操作，关闭 Read/Write Enabled。",
                        DiagnosticSeverity.Warning,
                        path);
                }

                if (importer.maxTextureSize > 2048)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"纹理 Max Size 为 {importer.maxTextureSize}：{Path.GetFileName(path)}",
                        "4096/8192 级纹理很容易抬高小游戏包体、显存和首屏加载压力。",
                        "确认实际显示尺寸；UI、图标和大部分 2D 资源优先限制在 2048 或更低。",
                        importer.maxTextureSize >= 4096 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                        path);
                }

                if (importer.textureCompression == TextureImporterCompression.Uncompressed)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"纹理未压缩：{Path.GetFileName(path)}",
                        "该纹理导入设置为 Uncompressed。",
                        "除非有明确的像素精度需求，否则为目标平台设置合适的纹理压缩。",
                        DiagnosticSeverity.Warning,
                        path);
                }

                if (importer.textureType == TextureImporterType.Sprite && importer.mipmapEnabled)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"Sprite 开启了 Mip Maps：{Path.GetFileName(path)}",
                        "普通 2D UI Sprite 通常不需要 Mip Maps，会增加构建体积和纹理内存。",
                        "若该 Sprite 不会在 3D 世界中明显缩小显示，关闭 Generate Mip Maps。",
                        DiagnosticSeverity.Info,
                        path);
                }
            }
        }
    }

    internal sealed class AudioImportRule : IDiagnosticRule
    {
        public string Id => "assets.audio";
        public string DisplayName => "音频导入";

        public IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;

                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (importer == null || clip == null)
                    continue;

                var sourceBytes = context.GetFileSize(path);
                var settings = importer.defaultSampleSettings;

                if (settings.loadType == AudioClipLoadType.DecompressOnLoad && sourceBytes >= 1024L * 1024L)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"大型音频使用 Decompress On Load：{Path.GetFileName(path)}",
                        $"源文件约 {DiagnosticContext.FormatBytes(sourceBytes)}，时长 {clip.length:0.0}s。加载后 PCM 内存可能远高于源文件大小。",
                        "长音乐/环境音优先考虑 Streaming；低频播放的较长音频可考虑 Compressed In Memory。",
                        clip.length >= 20f ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                        path);
                }

                if (clip.channels > 1 && clip.length >= 15f && !importer.forceToMono)
                {
                    yield return new DiagnosticIssue(
                        Id,
                        $"长音频为立体声：{Path.GetFileName(path)}",
                        $"该音频有 {clip.channels} 个声道、时长 {clip.length:0.0}s。对于不需要立体声空间感的语音/环境音，声道数会直接增加解码和内存成本。",
                        "确认是否真的需要立体声；不需要时可启用 Force To Mono。",
                        DiagnosticSeverity.Info,
                        path);
                }
            }
        }
    }
}
