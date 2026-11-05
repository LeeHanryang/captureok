using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptureOk.Core.Editing;
using CaptureOk.Core.History;
using MenuItem = System.Windows.Controls.MenuItem;

namespace CaptureOk.Editor;

/// <summary>목록 항목 하나: 썸네일·시각·크기와 저장 안 함·고정·지금 편집 중 표시. 문서가 바뀌면 썸네일을 잠시 뒤 다시 만든다.</summary>
internal sealed class HistoryItem : INotifyPropertyChanged
{
    private static readonly TimeSpan ThumbnailDelay = TimeSpan.FromMilliseconds(300);

    private readonly Func<CaptureDocument, BitmapSource?> _render;
    private readonly DispatcherTimer _thumbnailTimer;
    private BitmapSource? _thumbnail;
    private int _renderedVersion;
    private bool _isCurrent;

    public HistoryItem(CaptureDocument doc, Func<CaptureDocument, BitmapSource?> render)
    {
        Doc = doc;
        _render = render;
        _thumbnailTimer = new DispatcherTimer { Interval = ThumbnailDelay };
        _thumbnailTimer.Tick += (_, _) => { _thumbnailTimer.Stop(); RenderNow(); };
        Doc.Changed += OnDocumentChanged;
        RenderNow();
    }

    private void RenderNow()
    {
        _renderedVersion = Doc.ContentVersion;
        Thumbnail = _render(Doc);
    }

    public CaptureDocument Doc { get; }
    public BitmapSource? Thumbnail { get => _thumbnail; private set { _thumbnail = value; Notify(); } }
    public string Caption => $"{Doc.CreatedAt:HH:mm:ss} · {Doc.ViewWidth} × {Doc.ViewHeight}";
    public string ToolTip => Doc.IsDirty ? $"{Caption}\n저장·복사하지 않음" : Caption;
    public bool IsDirty => Doc.IsDirty;
    public bool IsPinned => Doc.IsPinned;
    public bool IsCurrent { get => _isCurrent; set { if (_isCurrent == value) return; _isCurrent = value; Notify(); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>고정 여부처럼 문서 밖에서 바뀐 값을 다시 읽게 한다.</summary>
    public void Refresh() => Notify(nameof(IsPinned));

    /// <summary>목록에서 빠질 때 문서 구독을 끊는다.</summary>
    public void Detach()
    {
        _thumbnailTimer.Stop();
        Doc.Changed -= OnDocumentChanged;
    }

    private void OnDocumentChanged()
    {
        Notify(nameof(IsDirty));
        Notify(nameof(Caption));
        Notify(nameof(ToolTip));
        if (Doc.ContentVersion == _renderedVersion) return; // 저장·복사 표시만 바뀜 — 그림은 그대로
        _thumbnailTimer.Stop();
        _thumbnailTimer.Start();
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 편집 창 오른쪽 캡처 목록. 최신이 위, 클릭 = 그 캡처로 전환, 우클릭 = 저장·복사·고정·삭제.
/// 목록을 바꾸는 일(삭제·고정·저장)은 편집 창이 맡고, 패널은 요청만 보낸다.
/// </summary>
internal sealed partial class HistoryPanel : UserControl
{
    private readonly List<HistoryItem> _items = [];
    private CaptureHistory? _history;
    private Func<CaptureDocument, BitmapSource?> _render = _ => null;
    private bool _syncing;

    public HistoryPanel()
    {
        InitializeComponent();
        List.SelectionChanged += OnSelectionChanged;
        List.MouseRightButtonUp += OnRightClick;
        SaveAllButton.Click += (_, _) => SaveAllRequested?.Invoke();
        ClearButton.Click += (_, _) => ClearRequested?.Invoke();
    }

    public event Action<CaptureDocument>? SaveRequested;
    public event Action<CaptureDocument>? CopyRequested;
    public event Action<CaptureDocument>? PinToggleRequested;
    public event Action<CaptureDocument>? DeleteRequested;
    public event Action? SaveAllRequested;
    public event Action? ClearRequested;

    /// <param name="render">문서의 썸네일 (원본이 없으면 null)</param>
    public void Attach(CaptureHistory history, Func<CaptureDocument, BitmapSource?> render)
    {
        _history = history;
        _render = render;
        history.Changed += Sync;
        Sync();
    }

    /// <summary>목록 항목에 초점이 있을 때 Delete: 고른 캡처를 지운다.</summary>
    public bool DeleteFocused()
    {
        if (!List.IsKeyboardFocusWithin || List.SelectedItem is not HistoryItem item) return false;
        DeleteRequested?.Invoke(item.Doc);
        return true;
    }

    private void Sync()
    {
        if (_history is null) return;
        _syncing = true;
        try
        {
            var docs = _history.Items;
            foreach (var gone in _items.Where(i => !docs.Contains(i.Doc)).ToList())
            {
                gone.Detach();
                _items.Remove(gone);
            }
            var ordered = docs.Select(d => _items.FirstOrDefault(i => i.Doc == d) ?? new HistoryItem(d, _render)).ToList();
            _items.Clear();
            _items.AddRange(ordered);
            if (!List.Items.Cast<HistoryItem>().SequenceEqual(ordered)) List.ItemsSource = ordered.ToList();

            HistoryItem? current = null;
            foreach (var item in _items)
            {
                item.IsCurrent = item.Doc == _history.Current;
                item.Refresh();
                if (item.IsCurrent) current = item;
            }
            List.SelectedItem = current;
            if (current is not null) List.ScrollIntoView(current);

            Header.Text = $"최근 캡처 {_history.Items.Count} / {_history.MaxCount}" + (_history.PinnedCount > 0 ? $" · 고정 {_history.PinnedCount}" : "");
            EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SaveAllButton.IsEnabled = ClearButton.IsEnabled = _items.Count > 0;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _history is null || List.SelectedItem is not HistoryItem item) return;
        _history.Select(item.Doc.Id);
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(List, (DependencyObject)e.OriginalSource) is not ListBoxItem { DataContext: HistoryItem item } container) return;
        var doc = item.Doc;
        var menu = new ContextMenu { PlacementTarget = container };
        menu.Items.Add(MenuEntry("저장", () => SaveRequested?.Invoke(doc)));
        menu.Items.Add(MenuEntry("복사", () => CopyRequested?.Invoke(doc)));
        menu.Items.Add(MenuEntry(doc.IsPinned ? "고정 해제" : "고정", () => PinToggleRequested?.Invoke(doc)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry("삭제", () => DeleteRequested?.Invoke(doc)));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static MenuItem MenuEntry(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
