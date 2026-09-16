using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MiniGameDoctor.Editor
{
    internal sealed class MiniGameDoctorWindow : EditorWindow
    {
        private const string UxmlPath = "Packages/com.ash.minigame-doctor/Editor/UI/MiniGameDoctorWindow.uxml";

        private Button _scanButton;
        private Button _fixAllButton;
        private Label _summaryLabel;
        private Label _statusLabel;
        private VisualElement _resultsContainer;
        private List<DiagnosticIssue> _lastIssues = new List<DiagnosticIssue>();

        [MenuItem("Tools/MiniGame Doctor")]
        public static void Open()
        {
            var window = GetWindow<MiniGameDoctorWindow>();
            window.titleContent = new GUIContent("MiniGame Doctor");
            window.minSize = new Vector2(720f, 520f);
        }

        public void CreateGUI()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (tree == null)
            {
                rootVisualElement.Add(new Label($"MiniGame Doctor UI 加载失败：{UxmlPath}"));
                return;
            }

            tree.CloneTree(rootVisualElement);
            _scanButton = rootVisualElement.Q<Button>("scanButton");
            _fixAllButton = rootVisualElement.Q<Button>("fixAllButton");
            _summaryLabel = rootVisualElement.Q<Label>("summaryLabel");
            _statusLabel = rootVisualElement.Q<Label>("statusLabel");
            _resultsContainer = rootVisualElement.Q<VisualElement>("resultsContainer");

            _scanButton.clicked += RunScan;
            _fixAllButton.clicked += ApplyAllSafeFixes;
            _fixAllButton.SetEnabled(false);
        }

        private void RunScan()
        {
            _scanButton.SetEnabled(false);
            _fixAllButton.SetEnabled(false);
            _statusLabel.text = "正在扫描项目…";
            _resultsContainer.Clear();

            try
            {
                _lastIssues = DiagnosticRunner.ScanAll();
                RenderResults(_lastIssues);
            }
            finally
            {
                _scanButton.SetEnabled(true);
                _fixAllButton.SetEnabled(_lastIssues.Any(SafeFixRegistry.CanApply));
            }
        }

        private void RenderResults(IReadOnlyList<DiagnosticIssue> issues)
        {
            var errors = 0;
            var warnings = 0;
            var infos = 0;
            var fixable = 0;

            foreach (var issue in issues)
            {
                switch (issue.Severity)
                {
                    case DiagnosticSeverity.Error: errors++; break;
                    case DiagnosticSeverity.Warning: warnings++; break;
                    default: infos++; break;
                }

                if (SafeFixRegistry.CanApply(issue))
                    fixable++;
            }

            _summaryLabel.text = $"严重 {errors}  ·  警告 {warnings}  ·  建议 {infos}  ·  可低风险修复 {fixable}";
            _statusLabel.text = issues.Count == 0
                ? "扫描完成：当前规则没有发现问题。"
                : $"扫描完成：共发现 {issues.Count} 项。自动修复只覆盖明确的低风险项。";

            if (issues.Count == 0)
            {
                var empty = new Label("✓ 当前检查项全部通过");
                empty.AddToClassList("empty-state");
                _resultsContainer.Add(empty);
                return;
            }

            foreach (var issue in issues)
                _resultsContainer.Add(CreateIssueCard(issue));
        }

        private VisualElement CreateIssueCard(DiagnosticIssue issue)
        {
            var card = new VisualElement();
            card.AddToClassList("issue-card");
            card.AddToClassList(issue.Severity switch
            {
                DiagnosticSeverity.Error => "issue-error",
                DiagnosticSeverity.Warning => "issue-warning",
                _ => "issue-info"
            });

            var header = new VisualElement();
            header.AddToClassList("issue-header");

            var badge = new Label(issue.Severity switch
            {
                DiagnosticSeverity.Error => "严重",
                DiagnosticSeverity.Warning => "警告",
                _ => "建议"
            });
            badge.AddToClassList("issue-badge");

            var title = new Label(issue.Title);
            title.AddToClassList("issue-title");
            header.Add(badge);
            header.Add(title);
            card.Add(header);

            var message = new Label(issue.Message);
            message.AddToClassList("issue-message");
            card.Add(message);

            var recommendation = new Label($"处理建议：{issue.Recommendation}");
            recommendation.AddToClassList("issue-recommendation");
            card.Add(recommendation);

            if (!string.IsNullOrEmpty(issue.AssetPath) || SafeFixRegistry.CanApply(issue))
            {
                var footer = new VisualElement();
                footer.AddToClassList("issue-footer");

                if (!string.IsNullOrEmpty(issue.AssetPath))
                {
                    var path = new Label(issue.AssetPath);
                    path.AddToClassList("issue-path");
                    footer.Add(path);

                    if (issue.AssetPath.StartsWith("Assets/"))
                    {
                        var locate = new Button(() => LocateAsset(issue.AssetPath)) { text = "定位" };
                        locate.AddToClassList("locate-button");
                        footer.Add(locate);
                    }
                }
                else
                {
                    var spacer = new VisualElement();
                    spacer.AddToClassList("footer-spacer");
                    footer.Add(spacer);
                }

                if (SafeFixRegistry.CanApply(issue))
                {
                    var fix = new Button(() => ApplySafeFix(issue)) { text = SafeFixRegistry.GetLabel(issue) };
                    fix.AddToClassList("fix-button");
                    footer.Add(fix);
                }

                card.Add(footer);
            }

            return card;
        }

        private void ApplySafeFix(DiagnosticIssue issue)
        {
            if (!EditorUtility.DisplayDialog(
                    "应用低风险修复",
                    $"{issue.Title}\n\n{issue.Recommendation}\n\n应用后 MiniGame Doctor 会重新扫描。",
                    "应用修复",
                    "取消"))
                return;

            if (!SafeFixRegistry.Apply(issue, out var message))
            {
                EditorUtility.DisplayDialog("修复失败", message ?? "未知错误", "确定");
                return;
            }

            Debug.Log($"[MiniGame Doctor] {message}");
            RunScan();
        }

        private void ApplyAllSafeFixes()
        {
            var fixable = _lastIssues
                .Where(SafeFixRegistry.CanApply)
                .GroupBy(issue => $"{issue.SafeFixId}|{issue.AssetPath}")
                .Select(group => group.First())
                .ToList();

            if (fixable.Count == 0)
                return;

            if (!EditorUtility.DisplayDialog(
                    "修复所有低风险项",
                    $"将应用 {fixable.Count} 项低风险修复。不会自动修改字体字符集、音频压缩、Shader 变体组合或 Sprite Atlas 迁移。",
                    "全部应用",
                    "取消"))
                return;

            foreach (var issue in fixable)
            {
                if (SafeFixRegistry.Apply(issue, out var message))
                    Debug.Log($"[MiniGame Doctor] {message}");
                else
                    Debug.LogWarning($"[MiniGame Doctor] 修复失败：{message}");
            }

            RunScan();
        }

        private static void LocateAsset(string assetPath)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
                return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
