using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MyMelody.Core;

namespace MyMelody.App;

internal sealed class CollectionCard : Border
{
    private readonly int _unlockedStage;
    private readonly bool _isDisplayed;
    private readonly int? _displayStage;
    private readonly TextBlock _previewLabel;
    public string CharacterId { get; }
    public int PreviewStage { get; private set; }
    public SpriteView Sprite { get; }
    public IReadOnlyList<Button> StageButtons { get; }
    public Button ApplyButton { get; }
    public Button FollowGrowthButton { get; }
    public event Action<int>? PreviewChanged;
    public event Action<int?>? DisplayRequested;

    public CollectionCard(CharacterDefinition definition, CharacterProgress? owned, int previewStage,
        string? displayCharacterId, int? displayStage)
    {
        CharacterId = definition.Id;
        _unlockedStage = owned?.Stage ?? 0;
        _isDisplayed = displayCharacterId == CharacterId;
        _displayStage = displayStage;
        PreviewStage = Math.Clamp(previewStage, 1, Math.Max(1, _unlockedStage));
        Style = (Style)FindResource("Card");
        Margin = new Thickness(0, 0, 10, 12);
        Padding = new Thickness(12);
        AutomationProperties.SetAutomationId(this, "collection-" + CharacterId);

        var panel = new StackPanel();
        Child = panel;
        Sprite = new SpriteView { Height = 125, Width = 140, Opacity = owned == null ? 0.22 : 1 };
        panel.Children.Add(Sprite);
        panel.Children.Add(new TextBlock
        {
            Text = definition.Name, FontSize = 15, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 6)
        });
        panel.Children.Add(Caption(owned == null ? "아직 만나지 못했어요" : owned.IsComplete
            ? "성장 완료 ♡" : $"{owned.Stage}단계까지 성장했어요"));

        var stages = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0) };
        var buttons = new List<Button>();
        for (int stage = 1; stage <= 3; stage++)
        {
            int chosenStage = stage;
            var button = new Button
            {
                Content = $"{stage}단계", IsEnabled = stage <= _unlockedStage,
                FontSize = 11, Padding = new Thickness(3, 7, 3, 7), Margin = new Thickness(2, 0, 2, 0),
                ToolTip = owned == null ? "이 친구를 만나면 열려요." : stage <= _unlockedStage
                    ? $"{stage}단계 모습 보기" : $"누적 {(stage - 1) * 12}시간을 연습하면 열려요."
            };
            AutomationProperties.SetName(button, $"{definition.Name} {stage}단계" + (button.IsEnabled ? " 모습 보기" : " 잠김"));
            ToolTipService.SetShowOnDisabled(button, true);
            button.Click += (_, _) =>
            {
                if (!button.IsEnabled) return;
                PreviewStage = chosenStage;
                RefreshPreview();
                PreviewChanged?.Invoke(chosenStage);
            };
            buttons.Add(button);
            stages.Children.Add(button);
        }
        StageButtons = buttons.AsReadOnly();
        panel.Children.Add(stages);
        _previewLabel = Caption("");
        _previewLabel.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(_previewLabel);

        ApplyButton = new Button
        {
            FontSize = 11, Padding = new Thickness(6, 8, 6, 8), Margin = new Thickness(0, 10, 0, 0),
            Visibility = owned == null ? Visibility.Collapsed : Visibility.Visible,
            ToolTip = "이 모습으로 고정해요. 연습과 성장은 계속 쌓여요."
        };
        ApplyButton.Click += (_, _) => { if (ApplyButton.IsEnabled) DisplayRequested?.Invoke(PreviewStage); };
        panel.Children.Add(ApplyButton);
        bool followsGrowth = _isDisplayed && _displayStage == null;
        FollowGrowthButton = new Button
        {
            Content = followsGrowth ? "성장에 따라 표시 중" : "성장에 맞춰 표시",
            IsEnabled = owned != null && !followsGrowth,
            Visibility = owned == null || owned.IsComplete ? Visibility.Collapsed : Visibility.Visible,
            FontSize = 10, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 7, 4, 4), Margin = new Thickness(0, 3, 0, 0),
            ToolTip = "이 친구의 현재 모습을 표시하고, 성장하면 새 모습으로 바꿔요."
        };
        FollowGrowthButton.Click += (_, _) => { if (FollowGrowthButton.IsEnabled) DisplayRequested?.Invoke(null); };
        panel.Children.Add(FollowGrowthButton);
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        Sprite.ShowCharacter(CharacterId, PreviewStage);
        for (int i = 0; i < StageButtons.Count; i++)
        {
            bool selected = _unlockedStage > 0 && i + 1 == PreviewStage;
            StageButtons[i].Background = selected ? new SolidColorBrush(Color.FromRgb(248, 220, 231)) : Brushes.White;
            StageButtons[i].FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
        bool isCurrentLook = _isDisplayed && (_displayStage ?? _unlockedStage) == PreviewStage;
        _previewLabel.Text = _unlockedStage == 0 ? "만난 뒤 단계를 열어 보세요" : isCurrentLook
            ? $"{PreviewStage}단계 · 바탕화면에 표시 중" : $"{PreviewStage}단계 모습";
        ApplyButton.IsEnabled = _unlockedStage > 0 && !(_isDisplayed && _displayStage == PreviewStage);
        ApplyButton.Content = !ApplyButton.IsEnabled && _unlockedStage > 0 ? "이 모습으로 표시 중" : $"{PreviewStage}단계로 표시";
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(150, 123, 137)),
        TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
    };
}
