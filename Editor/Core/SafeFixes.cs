using System;
using UnityEditor;
using UnityEngine.Rendering;

namespace MiniGameDoctor.Editor
{
    internal static class SafeFixRegistry
    {
        public const string RemoveNullAlwaysIncludedShaders = "graphics.remove-null-always-included";
        public const string DisableWebGlDebugSymbols = "webgl.disable-debug-symbols";

        public static bool CanApply(DiagnosticIssue issue)
        {
            return issue != null && !string.IsNullOrEmpty(issue.SafeFixId) &&
                   (issue.SafeFixId == RemoveNullAlwaysIncludedShaders ||
                    issue.SafeFixId == DisableWebGlDebugSymbols);
        }

        public static string GetLabel(DiagnosticIssue issue)
        {
            if (issue == null)
                return "修复";

            switch (issue.SafeFixId)
            {
                case RemoveNullAlwaysIncludedShaders:
                    return "清理空 Shader";
                case DisableWebGlDebugSymbols:
                    return "关闭调试符号";
                default:
                    return "修复";
            }
        }

        public static bool Apply(DiagnosticIssue issue, out string message)
        {
            message = null;
            if (!CanApply(issue))
            {
                message = "该问题没有可用的低风险自动修复。";
                return false;
            }

            try
            {
                switch (issue.SafeFixId)
                {
                    case RemoveNullAlwaysIncludedShaders:
                        return RemoveNullShaders(out message);
                    case DisableWebGlDebugSymbols:
                        PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
                        AssetDatabase.SaveAssets();
                        message = "已将 WebGL Debug Symbols 设为 Off。";
                        return true;
                    default:
                        message = "未知修复项。";
                        return false;
                }
            }
            catch (Exception exception)
            {
                message = exception.Message;
                return false;
            }
        }

        private static bool RemoveNullShaders(out string message)
        {
            var graphicsSettings = GraphicsSettings.GetGraphicsSettings();
            if (graphicsSettings == null)
            {
                message = "无法读取 Graphics Settings。";
                return false;
            }

            var serialized = new SerializedObject(graphicsSettings);
            var shaders = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (shaders == null || !shaders.isArray)
            {
                message = "当前 Unity 版本中未找到 Always Included Shaders 序列化字段。";
                return false;
            }

            var removed = 0;
            for (var i = shaders.arraySize - 1; i >= 0; i--)
            {
                var element = shaders.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue != null)
                    continue;

                shaders.DeleteArrayElementAtIndex(i);
                removed++;
            }

            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            message = removed > 0 ? $"已清理 {removed} 个空 Shader 条目。" : "没有需要清理的空 Shader 条目。";
            return true;
        }
    }
}
