using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace HuayuanPreview.R2;

public partial class R2ShellView
{
    private CancellationTokenSource? suggestionRequest;
    private int chosenSuggestion = -1;
    private string[] suggestions = [];
    private bool showingRecent;

    private void InitializeOnlineSearch()
    {
        CurrentFilter.GotFocus += (_, _) =>
        {
            if (model?.Workspace.IsKugou == true && string.IsNullOrWhiteSpace(CurrentFilter.Text))
                ShowSearchSuggestions(DesktopPreferences.Recent, recent: true);
        };
        CurrentFilter.KeyDown += SearchKeyDown;
        SuggestionList.SelectionChanged += (_, _) => chosenSuggestion = SuggestionList.SelectedIndex;
        SuggestionList.PointerReleased += async (_, args) =>
        {
            if (args.InitialPressMouseButton == MouseButton.Left && SuggestionList.SelectedItem is string text)
                await SubmitSearchAsync(text);
        };
        DetachedFromVisualTree += (_, _) => CancelSuggestions();
    }

    private void UpdateSearchContext()
    {
        CancelSuggestions();
        CurrentFilter.PlaceholderText = model?.Workspace.Local == true ? "筛选本地音乐" : "搜索酷狗全网音乐";
    }

    private void CancelSuggestions()
    {
        var previous = suggestionRequest;
        suggestionRequest = null;
        previous?.Cancel();
        SuggestionsPopup.IsOpen = false;
    }

    private async Task RequestSuggestionsAsync(string query)
    {
        CancelSuggestions();
        if (model?.Workspace.IsKugou != true || !CurrentFilter.IsFocused)
            return;
        if (query.Length == 0)
        {
            ShowSearchSuggestions(DesktopPreferences.Recent, recent: true);
            return;
        }

        using var request = new CancellationTokenSource();
        suggestionRequest = request;
        try
        {
            await Task.Delay(275, request.Token);
            var matches = DesktopPreferences.Recent.Where(term => term.Contains(query, StringComparison.OrdinalIgnoreCase));
            var remote = model.Concept.Connected
                ? await model.Concept.DesktopCall((g, ct) => g.SuggestDesktopAsync(query, ct), request.Token)
                : [];
            if (!request.IsCancellationRequested && model.Workspace.IsKugou &&
                string.Equals(CurrentFilter.Text?.Trim(), query, StringComparison.Ordinal))
            {
                ShowSearchSuggestions(remote.Concat(matches).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(), false);
                Program.Log("R2_SEARCH_SUGGEST", new { count = suggestions.Length });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!request.IsCancellationRequested)
                ShowSearchSuggestions(DesktopPreferences.Recent.Where(term => term.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray(), false);
            Program.Log("R2_SEARCH_SUGGEST_FAILED", new { type = error.GetType().Name });
        }
        finally
        {
            if (ReferenceEquals(suggestionRequest, request)) suggestionRequest = null;
        }
    }

    private void ShowSearchSuggestions(string[] items, bool recent)
    {
        suggestions = items;
        showingRecent = recent;
        chosenSuggestion = -1;
        SuggestionList.ItemsSource = recent ? Array.Empty<string>() : items;
        SuggestionList.SelectedIndex = -1;
        SuggestionList.IsVisible = !recent;
        HistoryChips.IsVisible = recent;
        HistoryChips.Children.Clear();
        if (recent)
        {
            foreach (var term in items)
            {
                var chip = new Button { Content = term, Margin = new Thickness(0, 0, 5, 4) };
                chip.Click += async (_, _) => await SubmitSearchAsync(term);
                HistoryChips.Children.Add(chip);
            }
        }
        SuggestionTitle.Text = recent ? "最近搜索" : "搜索建议";
        ClearHistory.IsVisible = recent;
        SuggestionsPopup.IsOpen = items.Length > 0 && model?.Workspace.IsKugou == true;
    }

    private async void SearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (model?.Workspace.IsKugou != true)
            return;
        if (args.Key == Key.Escape)
        {
            CancelSuggestions();
            args.Handled = true;
        }
        else if (SuggestionsPopup.IsOpen && !showingRecent && args.Key is Key.Up or Key.Down)
        {
            int delta = args.Key == Key.Down ? 1 : -1;
            chosenSuggestion = chosenSuggestion == -1 && delta < 0
                ? suggestions.Length - 1 : Math.Clamp(chosenSuggestion + delta, 0, suggestions.Length - 1);
            SuggestionList.SelectedIndex = chosenSuggestion;
            args.Handled = true;
        }
        else if (args.Key == Key.Enter)
        {
            args.Handled = true;
            await SubmitSearchAsync(chosenSuggestion >= 0 && SuggestionsPopup.IsOpen ? suggestions[chosenSuggestion] : CurrentFilter.Text ?? "");
        }
    }

    private async Task SubmitSearchAsync(string query)
    {
        if (model?.Workspace.IsKugou != true || string.IsNullOrWhiteSpace(query)) return;
        query = query.Trim();
        CancelSuggestions();
        updatingFilter = true;
        CurrentFilter.Text = query;
        updatingFilter = false;
        try { await DesktopPreferences.RememberAsync(query); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Program.Log("R2_HISTORY_WRITE_FAILED", new { type = error.GetType().Name }); }
        if (model.Workspace.IsKugou) await model.Workspace.OpenOnlineSearchAsync(query);
    }

    private async void ClearRecentSearches(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try { await DesktopPreferences.ClearRecentAsync(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Program.Log("R2_HISTORY_WRITE_FAILED", new { type = error.GetType().Name }); }
        CancelSuggestions();
    }
}
