using System;

namespace MiniGameDoctor.Editor
{
    internal enum DiagnosticSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    [Serializable]
    internal sealed class DiagnosticIssue
    {
        public string RuleId;
        public string Title;
        public string Message;
        public string Recommendation;
        public string AssetPath;
        public DiagnosticSeverity Severity;

        public DiagnosticIssue(
            string ruleId,
            string title,
            string message,
            string recommendation,
            DiagnosticSeverity severity,
            string assetPath = null)
        {
            RuleId = ruleId;
            Title = title;
            Message = message;
            Recommendation = recommendation;
            Severity = severity;
            AssetPath = assetPath;
        }
    }
}
