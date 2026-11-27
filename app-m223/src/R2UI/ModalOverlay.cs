using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

internal sealed record ModalOptions(
    string Title, double MaxWidth, double MaxHeight = 600, Control? InitialFocus = null,
    bool ShowTitle = true);

internal sealed class ModalOverlay : Grid
{
    private ModalOverlay() { }
    protected override Type StyleKeyOverride => typeof(Grid);
    private Task? completion;

    internal static Task? CurrentCompletion(Control host) =>
        OverlayLayer.GetOverlayLayer(host)?.Children.OfType<ModalOverlay>().FirstOrDefault()?.completion;

    internal static async Task<T> ShowAsync<T>(Control owner, Control content, ModalOptions options,
        Button confirm, Button cancel, Func<T> result, Func<bool>? canConfirm = null,
        bool defaultConfirm = true)
    {
        var session = TryShow<T>(owner, content, options);
        if (session == null) return default!;
        confirm.IsDefault = defaultConfirm;
        cancel.IsCancel = true;
        confirm.Click += (_, _) =>
        {
            if (canConfirm?.Invoke() != false) session.Complete(result());
        };
        cancel.Click += (_, _) => session.Dismiss();
        return await session.Completion;
    }

    internal static Session<T>? TryShow<T>(Control owner, Control content, ModalOptions options,
        Func<bool>? canDismiss = null)
    {
        if (TopLevel.GetTopLevel(owner) is not Window window || !window.IsVisible || !window.IsEnabled)
            return null;
        var host = owner is Window ? window.Content as Control : owner;
        if (host == null || OverlayLayer.GetOverlayLayer(host) is not { } layer ||
            layer.Children.OfType<ModalOverlay>().Any())
            return null;

        var body = new Grid
        {
            Margin = new Thickness(DesignTokens.Space5),
            RowDefinitions = new RowDefinitions(options.ShowTitle ? "Auto,*" : "*"),
            RowSpacing = DesignTokens.Space3
        };
        if (options.ShowTitle)
        {
            body.Children.Add(new TextBlock
            {
                Classes = { "section-title" },
                Text = options.Title,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
            SetRow(content, 1);
        }
        body.Children.Add(content);

        var card = new Border
        {
            Classes = { "glass", "glass-float" },
            BorderThickness = new Thickness(0),
            Margin = new Thickness(DesignTokens.Space5),
            MaxWidth = options.MaxWidth,
            MaxHeight = options.MaxHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = true,
            Child = new Grid
            {
                Children =
                {
                    new GlassOptics
                    {
                        Radius = DesignTokens.RadiusM, Frost = DesignTokens.FrostRegular,
                        Saturation = DesignTokens.SaturationChrome, IsHitTestVisible = false
                    },
                    body
                }
            }
        };
        AutomationProperties.SetName(card, options.Title);
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        if (!DesignTokens.ShaderDegrade) card.Classes.Add("glass-enter");

        var overlay = new ModalOverlay { Classes = { "modal-scrim" }, Children = { card } };
        overlay.Bind(WidthProperty, new Binding("ClientSize.Width") { Source = window });
        overlay.Bind(HeightProperty, new Binding("ClientSize.Height") { Source = window });
        var focus = window.FocusManager?.GetFocusedElement();
        var session = new Session<T>(canDismiss);
        overlay.completion = session.Completion;
        session.Closed += () =>
        {
            host.DetachedFromVisualTree -= Detached;
            window.Closed -= WindowClosed;
            layer.Children.Remove(overlay);
            if (focus is Visual visual && visual.IsAttachedToVisualTree()) focus.Focus();
        };
        void Detached(object? sender, VisualTreeAttachmentEventArgs args) => session.Complete(default!);
        void WindowClosed(object? sender, EventArgs args) => session.Complete(default!);
        host.DetachedFromVisualTree += Detached;
        window.Closed += WindowClosed;
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape || args.Handled) return;
            args.Handled = true;
            session.Dismiss();
        };
        layer.Children.Add(overlay);
        if (options.InitialFocus?.Focus() != true) card.Focus();
        return session;
    }

    internal sealed class Session<T>(Func<bool>? canDismiss)
    {
        private readonly TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<T> Completion => completion.Task;
        internal bool IsOpen => !completion.Task.IsCompleted;
        internal event Action? Closed;

        internal void Dismiss()
        {
            if (IsOpen && canDismiss?.Invoke() != false) Complete(default!);
        }

        internal void Complete(T result)
        {
            if (!completion.TrySetResult(result)) return;
            Closed?.Invoke();
            Closed = null;
        }
    }
}
