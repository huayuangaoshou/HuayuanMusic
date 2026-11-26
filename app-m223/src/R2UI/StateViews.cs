using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HuayuanPreview.R2;

public sealed class StateViews : ContentControl
{
    internal void Ready()
    {
        Classes.Remove("state-loading");
        Content = null;
    }

    internal void Loading()
    {
        var lines = new StackPanel { Spacing = DesignTokens.Space2 };
        for (var i = 0; i < DesignTokens.StateSkeletonRows; i++)
            lines.Children.Add(new Border
            {
                Classes = { "state-skeleton" }, CornerRadius = new(DesignTokens.RadiusS),
                Height = DesignTokens.SongRowCover
            });

        Classes.Set("state-loading", !DesignTokens.ShaderDegrade);
        MaxWidth = double.PositiveInfinity;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
        Content = lines;
    }

    internal void Empty(string text, string label, Action action) =>
        Message(text, label, action, error: false);

    internal void Error(Func<Task> retry) =>
        Message("此功能暂时不可用", "重试", () => _ = retry(), error: true);

    private void Message(string text, string label, Action action, bool error)
    {
        Classes.Remove("state-loading");
        MaxWidth = DesignTokens.StateMessageWidth;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        var panel = new StackPanel { Spacing = DesignTokens.Space3 };
        if (!error)
        {
            var note = new PathIcon
            {
                Data = ArtworkPlaceholder.Note, Width = DesignTokens.Space5, Height = DesignTokens.Space5,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            note.Classes.Add("state-note");
            panel.Children.Add(note);
        }
        panel.Children.Add(new TextBlock
        {
            Classes = { "muted", error ? "state-error-copy" : "state-empty-copy" },
            Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
        });
        var button = new Button { Content = label, Classes = { "glass-pill" }, HorizontalAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
        Content = new Border { Classes = { "porcelain" }, Padding = new(DesignTokens.Space4), Child = panel };
    }
}
