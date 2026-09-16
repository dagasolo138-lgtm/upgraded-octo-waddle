using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace MiniGameDoctor.Editor
{
    internal interface IDiagnosticRule
    {
        string Id { get; }
        string DisplayName { get; }
        IEnumerable<DiagnosticIssue> Scan(DiagnosticContext context);
    }

    internal sealed class DiagnosticContext
    {
        public readonly string ProjectRoot;
        public readonly string[] AssetPaths;

        public DiagnosticContext()
        {
            ProjectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            AssetPaths = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
                .ToArray();
        }

        public long GetFileSize(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return 0;

            var absolutePath = Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(absolutePath) ? new FileInfo(absolutePath).Length : 0;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024L * 1024L) return $"{bytes / 1024f:0.0} KB";
            return $"{bytes / (1024f * 1024f):0.0} MB";
        }
    }

    internal static class DiagnosticRunner
    {
        public static List<DiagnosticIssue> ScanAll()
        {
            var context = new DiagnosticContext();
            var issues = new List<DiagnosticIssue>();

            foreach (var rule in CreateRules())
            {
                try
                {
                    issues.AddRange(rule.Scan(context));
                }
                catch (Exception exception)
                {
                    issues.Add(new DiagnosticIssue(
                        rule.Id,
                        $"{rule.DisplayName} 扫描失败",
                        exception.Message,
                        "检查 Console 中的异常信息；该规则失败不会阻止其他规则继续扫描。",
                        DiagnosticSeverity.Error));
                }
            }

            return issues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.RuleId)
                .ThenBy(issue => issue.AssetPath)
                .ToList();
        }

        private static IEnumerable<IDiagnosticRule> CreateRules()
        {
            yield return new ResourcesFolderRule();
            yield return new LargeAssetRule();
            yield return new TextureImportRule();
            yield return new AudioImportRule();
            yield return new BuildSceneRule();
            yield return new BuildReportRule();
            yield return new ShaderVariantRule();
            yield return new FontAssetRule();
            yield return new SpriteAtlasRule();
            yield return new WebGlSettingsRule();
            yield return new WeChatPreparationRule();
        }
    }
}
