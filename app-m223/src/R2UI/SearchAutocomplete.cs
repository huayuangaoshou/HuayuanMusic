using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview.R2;

public partial class R2ShellView
{
    private CancellationTokenSource? suggestionRequest;
    private int chosenSuggestion = -1;
    private DesktopSuggestion[] suggestions = [];
    private bool showingRecent;
    private string? lastSubmittedSearch;
    private string? suggestionAccount;
    private readonly LinkedList<(string Prefix, DesktopSuggestion[] Items)> suggestionCache = new();

    private void InitializeOnlineSearch()
    {
        CurrentFilter.GotFocus += (_, _) =>
        {
            if (model?.Workspace.IsKugou == true && string.IsNullOrWhiteSpace(CurrentFilter.Text))
                _ = RequestSuggestionsAsync("");
        };
        SuggestionsPopup.PlacementTarget = CurrentFilter;
        SuggestionList.ItemTemplate = new FuncDataTemplate<DesktopSuggestion>((item, _) => item == null ? null :
            BuildSuggestion(item));
        CurrentFilter.KeyDown += SearchKeyDown;
        SuggestionList.SelectionChanged += (_, _) => chosenSuggestion = SuggestionList.SelectedIndex;
        SuggestionList.PointerReleased += async (_, args) =>
        {
            if (args.InitialPressMouseButton == MouseButton.Left && SuggestionList.SelectedItem is DesktopSuggestion item)
                await SubmitSearchAsync(item.Query);
        };
        DetachedFromVisualTree += (_, _) => CancelSuggestions();
    }

    private void UpdateSearchContext()
    {
        CancelSuggestions();
        lastSubmittedSearch = null;
        CurrentFilter.PlaceholderText = model?.Workspace.Local == true ? "筛选本地音乐" : "搜索酷狗全网音乐";
    }

    private void CancelSuggestions(bool close = true)
    {
        var previous = suggestionRequest;
        suggestionRequest = null;
        previous?.Cancel();
        if (close) SuggestionsPopup.IsOpen = false;
    }

    private async Task RequestSuggestionsAsync(string query)
    {
        if (lastSubmittedSearch == query)
        {
            CancelSuggestions();
            return;
        }
        CancelSuggestions(close: false);
        if (model?.Workspace.IsKugou != true || !CurrentFilter.IsFocused)
        {
            SuggestionsPopup.IsOpen = false;
            return;
        }
        if (query.Length == 0)
        {
            ShowSearchSuggestions(DesktopPreferences.Recent.Select(RecentSuggestion).ToArray(), recent: true);
            return;
        }

        if (suggestionAccount != model.Concept.AccountKey)
        {
            suggestionCache.Clear();
            suggestionAccount = model.Concept.AccountKey;
        }
        var local = InstantSuggestions(query);
        ShowSearchSuggestions(local.Length == 0 ? [new(query, "搜索此关键词", query)] : local, false);
        using var request = new CancellationTokenSource();
        suggestionRequest = request;
        try
        {
            await Task.Delay(90, request.Token);
            var remote = model.Concept.Connected
                ? await model.Concept.DesktopCall((g, ct) => g.SuggestDesktopAsync(query, ct), request.Token)
                : [];
            if (!request.IsCancellationRequested && model.Workspace.IsKugou && CurrentFilter.IsFocused &&
                suggestionAccount == model.Concept.AccountKey &&
                string.Equals(CurrentFilter.Text?.Trim(), query, StringComparison.Ordinal))
            {
                if (remote.Length > 0)
                {
                    CacheSuggestions(query, remote);
                    var enriched = remote.Select(item => item.Artist.Length > 0 ? item :
                        local.FirstOrDefault(candidate => candidate.Artist.Length > 0 &&
                            candidate.Title.Equals(item.Title, StringComparison.OrdinalIgnoreCase)) ?? item);
                    var items = enriched.Concat(local).DistinctBy(item => item.Query, StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
                    ShowSearchSuggestions(items, false);
                }
                Program.Log("R2_SEARCH_SUGGEST", new { immediate = local.Length, remote = remote.Length, queryLength = query.Length });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Program.Log("R2_SEARCH_SUGGEST_FAILED", new { type = error.GetType().Name });
        }
        finally
        {
            if (ReferenceEquals(suggestionRequest, request)) suggestionRequest = null;
        }
    }

    private static DesktopSuggestion RecentSuggestion(string term) => new(term, "最近搜索", term);

    private static IEnumerable<DesktopSuggestion> MatchingHistory(string query) =>
        DesktopPreferences.Recent.Where(term => term.Contains(query, StringComparison.OrdinalIgnoreCase)).Select(RecentSuggestion);

    private DesktopSuggestion[] InstantSuggestions(string query)
    {
        var node = suggestionCache.First;
        var cached = new List<DesktopSuggestion>();
        while (node != null)
        {
            if (string.Equals(query, node.Value.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                cached.InsertRange(0, node.Value.Items);
                suggestionCache.Remove(node);
                suggestionCache.AddFirst(node);
                break;
            }
            if (query.StartsWith(node.Value.Prefix, StringComparison.OrdinalIgnoreCase))
                cached.AddRange(node.Value.Items.Where(item => Matches(item.Title, item.Artist)));
            node = node.Next;
        }
        var snapshot = model?.Concept.Snapshot;
        var loaded = (snapshot?.AccountKey == suggestionAccount ? snapshot : null)?.Playlists.SelectMany(list => list.Tracks)
            .Where(track => Matches(track.Title, track.Artist))
            .DistinctBy(track => track.ProviderTrackId)
            .Take(8)
            .Select(track => new DesktopSuggestion(track.Title, track.Artist,
                string.IsNullOrWhiteSpace(track.Artist) ? track.Title : track.Title + " " + track.Artist)) ?? [];
        return cached.Concat(MatchingHistory(query)).Concat(loaded)
            .DistinctBy(item => item.Query, StringComparer.OrdinalIgnoreCase).Take(8).ToArray();

        bool Matches(string title, string artist) => title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            artist.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void CacheSuggestions(string query, DesktopSuggestion[] items)
    {
        for (var node = suggestionCache.First; node != null; node = node.Next)
            if (node.Value.Prefix.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                suggestionCache.Remove(node);
                break;
            }
        suggestionCache.AddFirst((query, items));
        if (suggestionCache.Count > 48) suggestionCache.RemoveLast();
    }

    private Control BuildSuggestion(DesktopSuggestion entry)
    {
        var query = CurrentFilter.Text?.Trim() ?? "";
        TextBlock Highlight(string text, bool title)
        {
            var label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = title ? 13 : 11 };
            if (title) label.FontWeight = FontWeight.SemiBold; else label.Classes.Add("muted");
            int index = query.Length > 0 ? text.IndexOf(query, StringComparison.OrdinalIgnoreCase) : -1;
            if (index < 0) label.Text = text;
            else
            {
                label.Inlines!.Add(new Run(text[..index]));
                label.Inlines.Add(new Run(text.Substring(index, query.Length)) { Foreground = new SolidColorBrush(Color.Parse(title ? "#4A9BFF" : "#FF6FA5")) });
                label.Inlines.Add(new Run(text[(index + query.Length)..]));
            }
            return label;
        }
        var panel = new StackPanel { Spacing = 1, Margin = new Thickness(2, 1) };
        panel.Children.Add(Highlight(entry.Title, true));
        if (entry.Artist.Length > 0) panel.Children.Add(Highlight(entry.Artist, false));
        return panel;
    }

    private void ShowSearchSuggestions(DesktopSuggestion[] items, bool recent)
    {
        if (SuggestionsPopup.IsOpen && showingRecent == recent && suggestions.SequenceEqual(items)) return;
        suggestions = items;
        showingRecent = recent;
        chosenSuggestion = -1;
        var selection = SuggestionList.SelectedItem as DesktopSuggestion;
        SuggestionList.ItemsSource = recent ? Array.Empty<DesktopSuggestion>() : items;
        SuggestionList.SelectedIndex = -1;
        SuggestionList.IsVisible = !recent;
        HistoryChips.IsVisible = recent;
        HistoryChips.Children.Clear();
        if (recent)
        {
            foreach (var term in items)
            {
                var chip = new Button { Content = term.Title, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 5, 4) };
                chip.Click += async (_, _) => await SubmitSearchAsync(term.Query);
                HistoryChips.Children.Add(chip);
            }
        }
        SuggestionTitle.Text = recent ? "最近搜索" : "搜索建议";
        ClearHistory.IsVisible = recent;
        if (!recent && selection != null)
            SuggestionList.SelectedIndex = Array.FindIndex(items, item => item.Query == selection.Query);
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
            SuggestionList.ScrollIntoView(suggestions[chosenSuggestion]);
            args.Handled = true;
        }
        else if (args.Key == Key.Enter)
        {
            args.Handled = true;
            await SubmitSearchAsync(chosenSuggestion >= 0 && SuggestionsPopup.IsOpen ? suggestions[chosenSuggestion].Query : CurrentFilter.Text ?? "");
        }
    }

    private async Task SubmitSearchAsync(string query)
    {
        if (model?.Workspace.IsKugou != true || string.IsNullOrWhiteSpace(query)) return;
        query = query.Trim();
        lastSubmittedSearch = query;
        CancelSuggestions();
        SetFilterText(query);
        try { await DesktopPreferences.RememberAsync(query); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Program.Log("R2_HISTORY_WRITE_FAILED", new { type = error.GetType().Name }); }
        if (model.Workspace.IsKugou) await model.Workspace.OpenOnlineSearchAsync(query);
        if (lastSubmittedSearch == query && CurrentFilter.Text?.Trim() == query) CancelSuggestions();
    }

    private async void ClearRecentSearches(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try { await DesktopPreferences.ClearRecentAsync(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Program.Log("R2_HISTORY_WRITE_FAILED", new { type = error.GetType().Name }); }
        CancelSuggestions();
    }
}
