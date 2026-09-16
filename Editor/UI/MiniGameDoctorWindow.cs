using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MiniGameDoctor.Editor
{
    internal sealed class MiniGameDoctorWindow : EditorWindow
    {
        private const string UxmlPath = "Packages/com.ash.minigame-doctor/Editor/UI/MiniGameDoctorWindow.uxml";

        private Button _scanButton;
        private Label _summaryLabel;
        private Label _statusLabel;
        private VisualElement _resultsContainer;

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
            _summaryLabel = rootVisualElement.Q<Label>("summaryLabel");
            _statusLabel = rootVisualElement.Q<Label>("statusLabel");
            _resultsContainer = rootVisualElement.Q<VisualElement>("resultsContainer");

            _scanButton.clicked += RunScan;
        }

        private void RunScan()
        {
            _scanButton.SetEnabled(false);
            _statusLabel.text = "正在扫描项目…";
            _resultsContainer.Clear();

            try
            {
                var issues = DiagnosticRunner.ScanAll();
                RenderResults(issues);
            }
            finally
            {
                _scanButton.SetEnabled(true);
            }
        }

        private void RenderResults(IReadOnlyList<DiagnosticIssue> issues)
        {
            var errors = 0;
            var warnings = 0;
            var infos = 0;

            foreach (var issue in issues)
            {
                switch (issue.Severity)
                {
                    case DiagnosticSeverity.Error: errors++; break;
                    case DiagnosticSeverity.Warning: warnings++; break;
                    default: infos++; break;
                }
            }

            _summaryLabel.text = $"严重 {errors}  ·  警告 {warnings}  ·  建议 {infos}";
            _statusLabel.text = issues.Count == 0
                ? "扫描完成：当前规则没有发现问题。"
                : $"扫描完成：共发现 {issues.Count} 项。";

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

        private static VisualElement CreateIssueCard(DiagnosticIssue issue)
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

            if (!string.IsNullOrEmpty(issue.AssetPath))
            {
                var footer = new VisualElement();
                footer.AddToClassList("issue-footer");

                var path = new Label(issue.AssetPath);
                path.AddToClassList("issue-path");
                footer.Add(path);

                if (issue.AssetPath.StartsWith("Assets/"))
                {
                    var locate = new Button(() => LocateAsset(issue.AssetPath)) { text = "定位" };
                    locate.AddToClassList("locate-button");
                    footer.Add(locate);
                }

                card.Add(footer);
            }

            return card;
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
