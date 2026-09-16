using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MiniGameDoctor.Editor
{
    [Serializable]
    internal sealed class BuildAssetSnapshot
    {
        public string AssetPath;
        public long PackedBytes;
    }

    [Serializable]
    internal sealed class BuildSnapshot
    {
        public string CapturedAtUtc;
        public string Platform;
        public string Result;
        public string OutputPath;
        public long TotalBytes;
        public double DurationSeconds;
        public List<BuildAssetSnapshot> Assets = new List<BuildAssetSnapshot>();
    }

    internal static class BuildReportSnapshotStore
    {
        private const string RelativePath = "Library/MiniGameDoctor/last-build.json";

        public static string GetPath(string projectRoot)
        {
            return Path.Combine(projectRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        public static void Save(BuildReport report)
        {
            if (report == null)
                return;

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var path = GetPath(projectRoot);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var aggregate = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var packed in report.packedAssets ?? Array.Empty<PackedAssets>())
            {
                foreach (var info in packed.contents ?? Array.Empty<PackedAssetInfo>())
                {
                    if (string.IsNullOrEmpty(info.sourceAssetPath))
                        continue;

                    var bytes = ToLong(info.packedSize);
                    aggregate.TryGetValue(info.sourceAssetPath, out var current);
                    aggregate[info.sourceAssetPath] = current > long.MaxValue - bytes ? long.MaxValue : current + bytes;
                }
            }

            var snapshot = new BuildSnapshot
            {
                CapturedAtUtc = DateTime.UtcNow.ToString("O"),
                Platform = report.summary.platform.ToString(),
                Result = report.summary.result.ToString(),
                OutputPath = report.summary.outputPath,
                TotalBytes = ToLong(report.summary.totalSize),
                DurationSeconds = report.summary.totalTime.TotalSeconds,
                Assets = aggregate
                    .OrderByDescending(pair => pair.Value)
                    .Take(100)
                    .Select(pair => new BuildAssetSnapshot { AssetPath = pair.Key, PackedBytes = pair.Value })
                    .ToList()
            };

            File.WriteAllText(path, JsonUtility.ToJson(snapshot, true));
        }

        public static bool TryLoad(string projectRoot, out BuildSnapshot snapshot)
        {
            snapshot = null;
            var path = GetPath(projectRoot);
            if (!File.Exists(path))
                return false;

            try
            {
                snapshot = JsonUtility.FromJson<BuildSnapshot>(File.ReadAllText(path));
                return snapshot != null;
            }
            catch
            {
                return false;
            }
        }

        private static long ToLong(ulong value)
        {
            return value > long.MaxValue ? long.MaxValue : (long)value;
        }
    }

    internal sealed class BuildReportRecorder : IPostprocessBuildWithReport
    {
        public int callbackOrder => 10000;

        public void OnPostprocessBuild(BuildReport report)
        {
            BuildReportSnapshotStore.Save(report);
        }
    }
}
