using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CaptureOk.Capture;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Files;
using CaptureOk.Core.Geometry;
using CaptureOk.Core.History;
using CaptureOk.Core.Settings;
using CaptureOk.History;
using CaptureOk.Native;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using Path = System.IO.Path;
using TextBox = System.Windows.Controls.TextBox;

namespace CaptureOk.Editor;

/// <summary>
/// 캡처 결과·편집 창. 앱에 하나만 있고, 캡처 목록의 현재 문서를 보여 준다.
/// 저장·복사 후에도 열려 있고, 닫기(X·Esc)는 숨기기만 한다.
/// </summary>
public partial class EditorWindow : FluentWindow
{
    private const string PngExtension = ".png";
    private const double MinZoom = 0.1, MaxZoom = 8.0, ZoomStep = 0.1;
    private const double DefaultWidth = 1240, DefaultHeight = 820; // DIP, XAML의 Width·Height와 같게
    private const double FitRoom = 24 + 4; // DIP, 캔버스 둘레 여백(Stage Margin 12 ×2) + 스크롤 막대가 생기지 않을 여유

    private static readonly Dictionary<EditorTool, SymbolRegular> ToolIcons = new()
    {
        [EditorTool.Select] = SymbolRegular.Cursor24,
        [EditorTool.Pen] = SymbolRegular.Pen24,
        [EditorTool.Highlighter] = SymbolRegular.Highlight24,
        [EditorTool.Rectangle] = SymbolRegular.RectangleLandscape24,
        [EditorTool.Ellipse] = SymbolRegular.Circle24,
        [EditorTool.Line] = SymbolRegular.Line24,
        [EditorTool.Arrow] = SymbolRegular.ArrowUpRight24,
        [EditorTool.Text] = SymbolRegular.TextT24,
        [EditorTool.Number] = SymbolRegular.NumberCircle124,
        [EditorTool.Mosaic] = SymbolRegular.GridDots24,
        [EditorTool.Blur] = SymbolRegular.Blur24,
        [EditorTool.Crop] = SymbolRegular.Crop24,
        [EditorTool.Eraser] = SymbolRegular.Eraser24,
    };

    /// <summary>도구 막대 그룹 사이 구분선 위치 (이 도구 앞에 구분선).</summary>
    private static readonly HashSet<EditorTool> GroupStarts = [EditorTool.Pen, EditorTool.Rectangle, EditorTool.Text, EditorTool.Mosaic];

    private readonly CaptureHistory _history;
    private readonly Func<CaptureSettings> _settings;
    private readonly Func<Guid, BitmapSource?> _imageOf;
    private readonly Action<string> _rememberFolder;
    private readonly Action<string> _notifySaved; // 저장 알림 (열기·폴더 열기)
    private readonly Action<bool> _rememberPanel;
    private readonly Action<int> _rememberPanelWidth;
    private readonly Action<uint, int> _rememberPen;
    private readonly PinnedCaptures _pinned;
    private readonly Dictionary<EditorTool, ToggleButton> _toolButtons = [];
    private readonly TextEditorOverlay _text;
    private readonly Dictionary<Guid, EffectCache> _effects = [];
    private CaptureDocument? _doc;
    private BitmapSource? _image;
    private double _zoom = 1.0;
    private bool _fitToWindow = true; // 확대율을 직접 바꾸기 전까지는 캡처 전체가 보이게 맞춘다
    private bool _placed;
    private bool _allowClose;

    // 옵션 막대
    private uint _color;
    private int _width;
    private readonly Ellipse _colorSwatch = new() { Width = 16, Height = 16, Stroke = Brushes.Gray, StrokeThickness = 1 };
    private readonly ComboBox _widthBox = OptionBox(64, "굵기");
    private readonly ToggleButton _dashToggle = new() { Content = ToggleIcon(SymbolRegular.LineDashes24), ToolTip = "점선", Padding = new Thickness(6), Focusable = false };
    private readonly ToggleButton _fillToggle = new() { Content = ToggleIcon(SymbolRegular.ColorFill24), ToolTip = "채우기", Padding = new Thickness(6), Focusable = false };
    private readonly ComboBox _fontBox = OptionBox(78, "글자 크기");
    private readonly ComboBox _numberBox = OptionBox(78, "번호 크기");
    private readonly ComboBox _mosaicBox = OptionBox(86, "모자이크 크기");
    private readonly ComboBox _blurBox = OptionBox(78, "블러 세기");
    private Button _colorButton = null!;

    internal EditorWindow(CaptureHistory history, PinnedCaptures pinned, Func<CaptureSettings> settings, Func<Guid, BitmapSource?> imageOf,
        Action<string> rememberFolder, Action<bool> rememberPanel, Action<int> rememberPanelWidth, Action<uint, int> rememberPen, Action<string> notifySaved)
    {
        _notifySaved = notifySaved;
        _history = history;
        _pinned = pinned;
        _settings = settings;
        _imageOf = imageOf;
        _rememberFolder = rememberFolder;
        _rememberPanel = rememberPanel;
        _rememberPanelWidth = rememberPanelWidth;
        _rememberPen = rememberPen;
        InitializeComponent();

        _text = new TextEditorOverlay(TextLayer, Annotations);

        BuildToolbar();
        BuildOptions();
        Annotations.TextEditRequested += (pos, existing) => _text.Begin(pos, existing, _color, SelectedFontSize());
        Annotations.FinishTextEdit = _text.Finish;
        Annotations.StatusRequested += message => StatusRight.Text = message;
        Annotations.ViewChanged += () => { UpdateImageSize(); OnDocumentChanged(); };

        ClearButton.Click += (_, _) => ClearAll();
        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => Redo();
        CopyButton.Click += (_, _) => Copy();
        SaveButton.Click += (_, _) => Save();
        SettingsButton.Click += (_, _) => ((App)Application.Current).OpenSettings();
        HistoryToggle.IsChecked = settings().ShowHistoryPanel;
        HistoryToggle.Checked += (_, _) => SetPanelVisible(true);
        HistoryToggle.Unchecked += (_, _) => SetPanelVisible(false);
        ApplyPanelVisibility(settings().ShowHistoryPanel);
        HistorySplitter.DragCompleted += (_, _) => _rememberPanelWidth((int)Math.Round(HistoryColumn.ActualWidth));
        HistoryPanel.Attach(history, RenderThumbnail);
        HistoryPanel.SaveRequested += doc => SaveDoc(doc);
        HistoryPanel.CopyRequested += doc => CopyDoc(doc);
        HistoryPanel.PinToggleRequested += TogglePin;
        HistoryPanel.DeleteRequested += DeleteCapture;
        HistoryPanel.SaveAllRequested += SaveAll;
        HistoryPanel.ClearRequested += ClearHistory;
        // 캔버스를 누르면 키보드 초점을 목록에서 가져온다 (목록에 초점이 남으면 Delete가 객체 대신 캡처를 지움)
        Annotations.PreviewMouseLeftButtonDown += (_, _) => { if (HistoryPanel.IsKeyboardFocusWithin) Keyboard.Focus(this); };

        Scroll.PreviewMouseWheel += OnWheel;
        Scroll.SizeChanged += (_, _) => { if (_fitToWindow) UpdateImageSize(); };
        ZoomText.MouseLeftButtonUp += (_, _) => { _fitToWindow = true; UpdateImageSize(); };
        PreviewKeyDown += OnKey;
        PreviewMouseLeftButtonDown += FinishTextOnOutsideClick;
        DpiChanged += (_, _) => UpdateImageSize();
        _history.Changed += () => { if (_history.Current != _doc) Display(_history.Current); };
    }

    public EditorTool CurrentTool { get; private set; }

    /// <summary>새 캡처: 보여 주고 창을 맨 앞으로 가져온다. 처음 한 번만 캡처 영역 근처에 두고, 100%로 보이게 창을 키운다.</summary>
    public void ShowDocument(CaptureDocument doc, PixelRect captureRect)
    {
        if (doc != _doc) Display(doc);
        ShowWindowFor(captureRect);
    }

    /// <summary>트레이에서 편집 창 열기: 목록의 현재 캡처(없으면 빈 화면)를 보여 준다.</summary>
    public void ShowCurrent()
    {
        if (_history.Current != _doc || _doc is null) Display(_history.Current);
        if (!_placed)
        {
            _placed = true;
            Show();
        }
        BringToFront();
    }

    /// <summary>앱 종료 전에: 입력 중인 텍스트를 객체로 확정한다 (고정 캡처면 보관에 들어가도록).</summary>
    public void CommitPendingEdits() => _text.Finish();

    /// <summary>상태 표시줄 오른쪽에 알림을 띄운다.</summary>
    public void ShowStatus(string message) => StatusRight.Text = message;

    /// <summary>
    /// 목록에서 고른 캡처(또는 없음)를 보여 준다. 창 크기·위치는 바꾸지 않는다.
    /// 진행 중인 텍스트 입력·드래그·자르기 영역은 이전 캡처에서 끝내거나 버린다.
    /// </summary>
    private void Display(CaptureDocument? doc)
    {
        _text.Finish();
        Annotations.CancelInProgress();
        if (_doc is not null) _doc.Changed -= OnDocumentChanged;
        _doc = doc;
        if (_doc is not null) _doc.Changed += OnDocumentChanged;
        _image = doc is null ? null : _imageOf(doc.Id);
        Annotations.BaseImage = _image;
        Annotations.Effects = doc is not null && _image is not null ? EffectsOf(doc, _image) : null;
        if (doc is null) _effects.Clear(); // 마지막 캡처를 지웠으면 그 원본 사본·효과 결과도 바로 놓는다
        Annotations.Document = doc;
        var empty = doc is null || _image is null;
        Stage.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _fitToWindow = true;
        UpdateImageSize();
        Scroll.ScrollToHome(); // 다른 캡처로 바꾸면 (긴 캡처도) 맨 위부터
        OnDocumentChanged();
        StatusRight.Text = "";
    }

    private void ShowWindowFor(PixelRect captureRect)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        if (!_placed)
        {
            _placed = true;
            PlaceNear(captureRect);
        }
        GrowToShowFullSize();
        BringToFront();
    }

    /// <summary>
    /// 캡처가 100%로 다 보이도록 창을 키운다 (모니터 작업 영역 안에서만, 줄이지는 않음).
    /// 화면보다 큰 캡처는 작업 영역 크기까지 키운 뒤 창에 맞춰 줄여 보여 준다.
    /// 좌표·크기는 모두 물리 픽셀: 100%에서 캡처 1px = 화면 1px.
    /// </summary>
    private void GrowToShowFullSize()
    {
        if (_image is null || WindowState != WindowState.Normal) return;
        UpdateLayout();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (Scroll.ActualWidth <= 0 || !NativeMethods.GetWindowRect(hwnd, out var r)) return;
        var s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        // 창에서 캡처가 아닌 부분(제목·도구 막대·상태 막대·목록 패널·여백)의 크기
        var chromeWidth = width - Scroll.ActualWidth * s;
        var chromeHeight = height - Scroll.ActualHeight * s;
        var view = Annotations.View;
        var neededWidth = Math.Ceiling(Math.Round(view.Width) + FitRoom * s + chromeWidth);
        var neededHeight = Math.Ceiling(Math.Round(view.Height) + FitRoom * s + chromeHeight);
        var work = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        var (w, h) = ZoomMath.GrowToFit(width, height, neededWidth, neededHeight, work.Width, work.Height);
        if ((int)w == width && (int)h == height) return;
        var x = ZoomMath.ClampInto(r.Left, (int)w, work.Left, work.Right);
        var y = ZoomMath.ClampInto(r.Top, (int)h, work.Top, work.Bottom);
        NativeMethods.SetWindowPos(hwnd, 0, x, y, (int)w, (int)h, NativeMethods.SWP_NOZORDER);
    }

    /// <summary>앱 종료 때만 실제로 닫히게 한다.</summary>
    public void AllowClose() => _allowClose = true;

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Topmost = true;
        Topmost = false;
        NativeMethods.BringToForeground(new WindowInteropHelper(this).Handle);
        Activate();
        Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _text.Finish();
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    // ---- 도구 막대 ----

    private void BuildToolbar()
    {
        foreach (var tool in EditorTools.Order)
        {
            if (GroupStarts.Contains(tool))
                ToolPanel.Children.Add(new Border { Width = 1, Height = 20, Margin = new Thickness(6, 0, 6, 0), Background = (Brush)FindResource("ControlStrokeColorDefaultBrush") });

            var button = new ToggleButton
            {
                Content = ToggleIcon(ToolIcons[tool]),
                ToolTip = $"{EditorTools.NameOf(tool)} ({EditorTools.ShortcutOf(tool)})",
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 2, 0),
                Focusable = false,
            };
            var t = tool;
            button.Click += (_, _) => SelectTool(t);
            _toolButtons[tool] = button;
            ToolPanel.Children.Add(button);
        }
    }

    /// <summary>옵션 막대의 드롭다운. 기본 콤보 상자는 도구 버튼보다 커서 막대에서 혼자 튀므로 버튼 높이에 맞춘다.</summary>
    private static ComboBox OptionBox(double minWidth, string toolTip) => new()
    {
        MinWidth = minWidth,
        MinHeight = 0,
        Height = 30,
        Padding = new Thickness(10, 0, 6, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 13,
        ToolTip = toolTip,
        Margin = new Thickness(0, 0, 4, 0),
    };

    /// <summary>
    /// 토글 버튼 안의 아이콘. 아이콘이 버튼 템플릿이 정한 글자색(보통·선택·비활성)을 물려받지 못하고
    /// 시스템 기본색(검정)으로 그려져 다크 모드에서 안 보이므로, 내용 표시기의 글자색에 직접 묶는다.
    /// </summary>
    internal static SymbolIcon ToggleIcon(SymbolRegular symbol)
    {
        var icon = new SymbolIcon { Symbol = symbol };
        icon.SetBinding(SymbolIcon.ForegroundProperty, new System.Windows.Data.Binding
        {
            Path = new PropertyPath(System.Windows.Documents.TextElement.ForegroundProperty),
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ContentPresenter), 1),
        });
        return icon;
    }

    private void SelectTool(EditorTool tool)
    {
        _text.Finish();
        Annotations.CancelInProgress();
        if (tool != EditorTool.Select) Annotations.ClearSelection();
        CurrentTool = tool;
        Annotations.Tool = tool;
        Annotations.UpdateCursor();
        foreach (var (t, b) in _toolButtons) b.IsChecked = t == tool;
        StatusLeft.Text = EditorTools.HelpOf(tool);
        StatusRight.Text = "";
        UpdateOptionsVisibility();
    }

    // ---- 옵션 막대 ----

    private void BuildOptions()
    {
        var settings = _settings();
        _color = settings.PenColorArgb;
        _width = DrawingOptions.Widths.OrderBy(w => Math.Abs(w - settings.PenWidth)).First();

        var colorButton = _colorButton = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _colorSwatch, new SymbolIcon { Symbol = SymbolRegular.ChevronDown12, Margin = new Thickness(4, 0, 0, 0) } } },
            ToolTip = "색",
            Padding = new Thickness(6, 5, 6, 5),
            Margin = new Thickness(0, 0, 4, 0),
            Focusable = false,
        };
        colorButton.Click += (_, _) => OpenColorMenu(colorButton);

        foreach (var w in DrawingOptions.Widths) _widthBox.Items.Add($"{w}px");
        _widthBox.SelectedIndex = DrawingOptions.Widths.ToList().IndexOf(_width);
        _widthBox.SelectionChanged += (_, _) =>
        {
            if (_widthBox.SelectedIndex < 0) return;
            _width = DrawingOptions.Widths[_widthBox.SelectedIndex];
            ApplyStyle(remember: true);
        };

        foreach (var size in DrawingOptions.FontSizes) _fontBox.Items.Add($"글자 {size}");
        _fontBox.SelectedIndex = DrawingOptions.FontSizes.ToList().IndexOf(DrawingOptions.DefaultFontSize);
        _fontBox.SelectionChanged += (_, _) => { Annotations.TextSize = SelectedFontSize(); _text.SetFontSize(SelectedFontSize()); };

        foreach (var d in DrawingOptions.NumberDiameters) _numberBox.Items.Add($"번호 {d}");
        _numberBox.SelectedIndex = DrawingOptions.NumberDiameters.ToList().IndexOf(DrawingOptions.DefaultNumberDiameter);
        _numberBox.SelectionChanged += (_, _) => Annotations.NumberDiameter = DrawingOptions.NumberDiameters[Math.Max(0, _numberBox.SelectedIndex)];

        foreach (var b in DrawingOptions.MosaicBlockSizes) _mosaicBox.Items.Add($"칸 {b}px");
        _mosaicBox.SelectedIndex = DrawingOptions.MosaicBlockSizes.ToList().IndexOf(DrawingOptions.DefaultMosaicBlock);
        _mosaicBox.SelectionChanged += (_, _) => Annotations.MosaicBlock = DrawingOptions.MosaicBlockSizes[Math.Max(0, _mosaicBox.SelectedIndex)];

        foreach (var (name, _) in DrawingOptions.BlurStrengths) _blurBox.Items.Add($"세기 {name}");
        _blurBox.SelectedIndex = DrawingOptions.BlurStrengths.ToList().FindIndex(s => s.Radius == DrawingOptions.DefaultBlurRadius);
        _blurBox.SelectionChanged += (_, _) => Annotations.BlurRadius = DrawingOptions.BlurStrengths[Math.Max(0, _blurBox.SelectedIndex)].Radius;

        _dashToggle.Margin = new Thickness(4, 0, 2, 0);
        _dashToggle.Click += (_, _) => ApplyStyle(remember: false);
        _fillToggle.Click += (_, _) => Annotations.Filled = _fillToggle.IsChecked == true;

        foreach (UIElement e in new UIElement[] { colorButton, _widthBox, _dashToggle, _fillToggle, _fontBox, _numberBox, _mosaicBox, _blurBox }) OptionsPanel.Children.Add(e);
        ApplyStyle(remember: false);
        SelectTool(EditorTools.Default);
    }

    private void UpdateOptionsVisibility()
    {
        var t = CurrentTool;
        _fillToggle.Visibility = t is EditorTool.Rectangle or EditorTool.Ellipse ? Visibility.Visible : Visibility.Collapsed;
        // 점선은 도형·직선·화살표만 (spec 3.2: 펜·형광펜 옵션은 색·굵기)
        _dashToggle.Visibility = t is EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Line or EditorTool.Arrow
            ? Visibility.Visible : Visibility.Collapsed;
        var effectOrCrop = t is EditorTool.Mosaic or EditorTool.Blur or EditorTool.Crop;
        _colorButton.Visibility = effectOrCrop ? Visibility.Collapsed : Visibility.Visible;
        _widthBox.Visibility = t is EditorTool.Text or EditorTool.Number || effectOrCrop ? Visibility.Collapsed : Visibility.Visible;
        _fontBox.Visibility = t == EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
        _numberBox.Visibility = t == EditorTool.Number ? Visibility.Visible : Visibility.Collapsed;
        _mosaicBox.Visibility = t == EditorTool.Mosaic ? Visibility.Visible : Visibility.Collapsed;
        _blurBox.Visibility = t == EditorTool.Blur ? Visibility.Visible : Visibility.Collapsed;
    }

    private double SelectedFontSize() => DrawingOptions.FontSizes[Math.Max(0, _fontBox.SelectedIndex)];

    private void OpenColorMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var (name, argb) in DrawingOptions.Palette)
        {
            var item = new MenuItem
            {
                Header = name,
                Icon = new Ellipse { Width = 14, Height = 14, Fill = AnnotationRenderer.BrushOf(argb), Stroke = Brushes.Gray, StrokeThickness = 1 },
                IsChecked = argb == _color,
            };
            var value = argb;
            item.Click += (_, _) => SetColor(value);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var custom = new MenuItem { Header = "사용자 지정…" };
        custom.Click += (_, _) => PickCustomColor();
        menu.Items.Add(custom);
        menu.IsOpen = true;
    }

    private void PickCustomColor()
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(unchecked((int)_color)) };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SetColor(0xFF000000 | (uint)(dialog.Color.ToArgb() & 0x00FFFFFF));
    }

    private void SetColor(uint argb)
    {
        _color = argb;
        _text.SetColor(argb);
        ApplyStyle(remember: true);
    }

    private void ApplyStyle(bool remember)
    {
        _colorSwatch.Fill = AnnotationRenderer.BrushOf(_color);
        Annotations.PenStyle = new StrokeStyle(_color, _width, _dashToggle.IsChecked == true);
        if (remember) _rememberPen(_color, _width);
    }

    // ---- 목록 패널 ----

    private bool _applyingPanelSetting;

    private void SetPanelVisible(bool visible)
    {
        ApplyPanelVisibility(visible);
        if (!_applyingPanelSetting) _rememberPanel(visible);
    }

    /// <summary>환경설정에서 목록 패널 표시를 바꿨을 때 (다시 설정에 쓰지 않는다).</summary>
    public void ApplyPanelSetting(bool visible)
    {
        _applyingPanelSetting = true;
        try
        {
            HistoryToggle.IsChecked = visible;
            ApplyPanelVisibility(visible);
        }
        finally
        {
            _applyingPanelSetting = false;
        }
    }

    /// <summary>목록 패널과 구분선을 보이거나 숨긴다. 보일 때는 기억한 폭(150~480)으로.</summary>
    private void ApplyPanelVisibility(bool visible)
    {
        HistoryPanel.Visibility = HistorySplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        HistoryColumn.MinWidth = visible ? CaptureSettings.MinHistoryPanelWidth : 0;
        HistoryColumn.MaxWidth = visible ? CaptureSettings.MaxHistoryPanelWidth : double.PositiveInfinity;
        HistoryColumn.Width = new GridLength(visible ? _settings().HistoryPanelWidth : 0);
    }

    /// <summary>목록 썸네일. 패널을 넓혀도 또렷하도록 넉넉한 크기(화면 배율 반영)로 만든다.</summary>
    private BitmapSource? RenderThumbnail(CaptureDocument doc) =>
        _imageOf(doc.Id) is { } image
            ? Thumbnails.Render(doc, image, EffectsOf(doc, image), (int)(CaptureSettings.MaxHistoryPanelWidth * 0.66 * VisualTreeHelper.GetDpi(this).DpiScaleX))
            : null;

    private async void TogglePin(CaptureDocument doc)
    {
        if (doc.IsPinned)
        {
            _pinned.Unpin(_history, doc);
            StatusRight.Text = "고정을 풀었습니다";
            return;
        }
        StatusRight.Text = await _pinned.PinAsync(_history, doc) switch
        {
            PinResult.Pinned => "고정했습니다 — 앱을 껐다 켜도 남습니다",
            PinResult.LimitReached => $"고정은 {CaptureHistory.MaxPinned}개까지입니다",
            PinResult.Failed => "고정하지 못했습니다 (보관 폴더에 쓸 수 없음)",
            _ => "",
        };
    }

    private void DeleteCapture(CaptureDocument doc)
    {
        // 되돌릴 수 없으니 저장 안 한 캡처와 고정 캡처(보관한 것도 지워짐)는 먼저 묻는다.
        var question = doc.IsPinned ? "고정한 캡처입니다. 삭제하면 보관한 것도 지워집니다. 삭제할까요?"
            : doc.IsDirty ? "저장·복사하지 않은 캡처입니다. 삭제할까요?" : null;
        if (question is not null && MessageBox.Show(this, question, "CaptureOK",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        if (doc == _doc)
        {
            _text.Finish();
            Annotations.CancelInProgress();
        }
        if (doc.IsPinned) _pinned.Forget(doc);
        _history.Remove(doc.Id);
    }

    /// <summary>목록 비우기: 고정한 캡처는 남긴다. 설정에 따라 먼저 확인한다.</summary>
    private void ClearHistory()
    {
        var unpinned = _history.Items.Where(d => !d.IsPinned).ToList();
        if (unpinned.Count == 0)
        {
            StatusRight.Text = "지울 캡처가 없습니다 (고정한 캡처는 남습니다)";
            return;
        }
        if (_settings().ConfirmClearHistory)
        {
            var unsaved = unpinned.Count(d => d.IsDirty);
            var message = $"고정하지 않은 캡처 {unpinned.Count}개를 목록에서 지울까요?" + (unsaved > 0 ? $"\n그중 {unsaved}개는 저장·복사하지 않았습니다." : "");
            if (MessageBox.Show(this, message, "CaptureOK", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        }
        _text.Finish();
        Annotations.CancelInProgress();
        // 확인 창이 떠 있는 동안 새 캡처가 들어왔을 수 있으니, 물어본 캡처만 지운다.
        var removed = _history.ClearUnpinned(unpinned.Select(d => d.Id).ToList());
        StatusRight.Text = $"{removed.Count}개를 목록에서 지웠습니다";
    }

    /// <summary>모두 저장: 폴더를 고르면 목록의 캡처를 찍은 시각 이름으로 한꺼번에 저장한다.</summary>
    private void SaveAll()
    {
        _text.Finish();
        CommitPendingCrop();
        var docs = _history.Items.ToList();
        if (docs.Count == 0) return;
        var settings = _settings();
        var dialog = new OpenFolderDialog
        {
            Title = "모두 저장할 폴더",
            InitialDirectory = Directory.Exists(settings.SaveFolder) ? settings.SaveFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) != true) return;

        int saved = 0, failed = 0;
        foreach (var (doc, path) in BatchNames.Plan(docs, dialog.FolderName, settings.FileNameTemplate, PngExtension, File.Exists))
        {
            if (RenderOf(doc) is not { } image) { failed++; continue; }
            try
            {
                ImageExporter.SavePng(image, path);
                doc.MarkSaved();
                saved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Write($"모두 저장 실패 {path}: {ex.Message}");
                failed++;
            }
        }
        _rememberFolder(dialog.FolderName);
        StatusRight.Text = failed == 0 ? $"{saved}개 저장함: {dialog.FolderName}" : $"{saved}개 저장, {failed}개는 저장하지 못했습니다";
    }

    private void OnDocumentChanged()
    {
        var hasDoc = _doc is not null;
        CopyButton.IsEnabled = SaveButton.IsEnabled = ClearButton.IsEnabled = hasDoc;
        UndoButton.IsEnabled = _doc?.CanUndo == true;
        RedoButton.IsEnabled = _doc?.CanRedo == true;
        var title = _doc is null ? "CaptureOK" : $"CaptureOK — {_doc.ViewWidth} × {_doc.ViewHeight}";
        Title = title;
        TitleBarControl.Title = title;
    }

    // ---- 편집 명령 ----

    private void Undo()
    {
        _text.Finish();
        Annotations.CancelInProgress();
        _doc?.Undo();
    }

    private void Redo()
    {
        _text.Finish();
        Annotations.CancelInProgress();
        _doc?.Redo();
    }

    private void ClearAll()
    {
        _text.Finish();
        Annotations.CancelInProgress();
        if (_doc is { Annotations.Count: > 0 } doc)
        {
            doc.Execute(new ClearAnnotations());
            Annotations.ClearSelection();
        }
    }

    // ---- 확대 ----

    private void UpdateImageSize()
    {
        if (_image is null) return;
        // 캔버스는 보이는 영역(자른 영역, 자르기 도구일 때는 원본 전체)만큼만 차지한다.
        var s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var view = Annotations.View;
        var viewWidth = Math.Max(1, Math.Round(view.Width)) / s;
        var viewHeight = Math.Max(1, Math.Round(view.Height)) / s;
        if (_fitToWindow)
        {
            // 여백과 스크롤 막대가 생기지 않을 정도의 여유를 뺀 공간에 맞춘다. 작은 캡처는 100% 그대로.
            // 아주 긴 캡처(스크롤 캡처)는 폭에 맞추고 세로로 스크롤한다
            var fit = ZoomMath.Fit(viewWidth, viewHeight, Scroll.ActualWidth - FitRoom, Scroll.ActualHeight - FitRoom, SystemParameters.VerticalScrollBarWidth);
            _zoom = Math.Max(MinZoom, Math.Floor(fit * 100) / 100);
        }
        var width = viewWidth * _zoom;
        var height = viewHeight * _zoom;
        Annotations.Width = TextLayer.Width = width;
        Annotations.Height = TextLayer.Height = height;
        Annotations.Scale = _zoom / s;
        // 확대할 때는 픽셀이 또렷하게(최근접), 줄여서 볼 때는 글자가 깨지지 않게 부드럽게 줄인다.
        RenderOptions.SetBitmapScalingMode(Annotations, _zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        Annotations.InvalidateVisual();
        _text.ApplyLook();
        ZoomText.Text = $"{Math.Round(_zoom * 100)}%";
    }

    private void SetZoom(double zoom)
    {
        _fitToWindow = false;
        _zoom = Math.Clamp(Math.Round(zoom, 1), MinZoom, MaxZoom);
        UpdateImageSize();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        SetZoom(_zoom + (e.Delta > 0 ? ZoomStep : -ZoomStep));
        e.Handled = true;
    }

    /// <summary>
    /// 텍스트 입력 중 빈 영역·다른 버튼을 누르면 입력을 끝낸다.
    /// 입력 상자 자신과 옵션 막대(색·글자 크기)는 제외하고, 캔버스 클릭은 캔버스가 직접 처리한다.
    /// </summary>
    private void FinishTextOnOutsideClick(object sender, MouseButtonEventArgs e)
    {
        if (!_text.IsActive || e.OriginalSource is not DependencyObject source) return;
        if (_text.Contains(source) || IsWithin(source, OptionsPanel) || IsWithin(source, Annotations)) return;
        _text.Finish();
    }

    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? e = element; e is not null; e = e is Visual ? VisualTreeHelper.GetParent(e) : LogicalTreeHelper.GetParent(e))
            if (ReferenceEquals(e, ancestor)) return true;
        return false;
    }

    // ---- 키 ----

    private void OnKey(object sender, KeyEventArgs e)
    {
        // 한글 입력기가 켜져 있으면 실제 키가 ImeProcessedKey로 들어온다.
        var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

        // 텍스트 입력 중에는 Esc·Ctrl+Enter(완료)만 가로채고 나머지는 입력 상자에 맡긴다.
        if (Keyboard.FocusedElement is TextBox)
        {
            if (_text.HandleKey(key)) e.Handled = true;
            return;
        }

        // 자르기 도구: Enter는 적용하고 선택 도구로 돌아가고, Esc는 고른 영역만 버린다 (창은 숨기지 않음).
        if (CurrentTool == EditorTool.Crop && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (key == Key.Enter)
            {
                if (Annotations.ApplyPendingCrop()) SelectTool(EditorTool.Select);
                e.Handled = true;
                return;
            }
            if (key == Key.Escape)
            {
                Annotations.CancelPendingCrop();
                SelectTool(EditorTool.Select);
                e.Handled = true;
                return;
            }
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl)
        {
            switch (key)
            {
                case Key.Z: Undo(); break;
                case Key.Y: Redo(); break;
                case Key.C: Copy(); break;
                case Key.S: Save(); break;
                case Key.Delete: ClearAll(); break; // Delete = 고른 것만, Ctrl+Delete = 모두
                case Key.D0 or Key.NumPad0: SetZoom(1.0); break;
                default: return;
            }
            e.Handled = true;
            return;
        }
        switch (key)
        {
            case Key.Escape:
                Annotations.CancelInProgress();
                Hide();
                e.Handled = true;
                return;
            case Key.Delete:
                if (!HistoryPanel.DeleteFocused()) Annotations.DeleteSelected(); // 목록에 초점이 있으면 캡처 삭제
                e.Handled = true;
                return;
        }
        if (Keyboard.Modifiers == ModifierKeys.None && key is >= Key.A and <= Key.Z
            && EditorTools.TryFromShortcut((char)('A' + (key - Key.A)), out var tool))
        {
            SelectTool(tool);
            e.Handled = true;
        }
    }

    // ---- 저장·복사 ----

    private BitmapSource? RenderOf(CaptureDocument doc) =>
        _imageOf(doc.Id) is { } image ? Flattener.Render(doc, image, EffectsOf(doc, image)) : null;

    /// <summary>
    /// 지금 보여 주는 문서의 원본 효과 캐시 하나만 들고 있는다 (큰 캡처의 원본 사본·결과가 문서마다 쌓이지 않게).
    /// 다른 문서는 그때만 쓰는 캐시를 새로 만든다.
    /// </summary>
    private EffectCache EffectsOf(CaptureDocument doc, BitmapSource image)
    {
        if (doc.Id != _doc?.Id) return new EffectCache(image);
        foreach (var other in _effects.Keys.Where(id => id != doc.Id).ToList()) _effects.Remove(other);
        if (!_effects.TryGetValue(doc.Id, out var cache)) _effects[doc.Id] = cache = new EffectCache(image);
        return cache;
    }

    /// <summary>자르기 도구에서 영역만 고르고 Enter 없이 저장·복사하면, 고른 영역으로 잘라서 내보낸다.</summary>
    private void CommitPendingCrop()
    {
        if (CurrentTool == EditorTool.Crop && Annotations.ApplyPendingCrop()) SelectTool(EditorTool.Select);
    }

    // 저장·복사는 시작한 시점의 문서를 끝까지 대상으로 한다.
    // 저장 창이 열려 있는 동안 새 캡처가 들어와 _doc이 바뀌어도, 다른 문서를 저장됨으로 표시하지 않기 위함.

    private void Save()
    {
        if (_doc is { } doc) SaveDoc(doc);
    }

    /// <summary>지금 캡처 또는 목록에서 고른 캡처를 저장한다.</summary>
    private void SaveDoc(CaptureDocument doc)
    {
        _text.Finish();
        if (doc == _doc) CommitPendingCrop();
        if (_settings().AutoSave) AutoSaveToFolder(doc);
        else SaveAs(doc);
    }

    private void AutoSaveToFolder(CaptureDocument doc)
    {
        if (RenderOf(doc) is not { } image) return;
        var settings = _settings();
        var path = FileNamer.Next(settings.SaveFolder, settings.FileNameTemplate, DateTime.Now, PngExtension, File.Exists);
        try
        {
            ImageExporter.SavePng(image, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"저장 실패 {path}: {ex.Message}");
            MessageBox.Show(this, $"저장 폴더에 저장하지 못했습니다.\n{ex.Message}\n\n다른 위치를 골라 주세요.", "CaptureOK", MessageBoxButton.OK, MessageBoxImage.Warning);
            SaveAs(doc);
            return;
        }
        Saved(doc, path);
    }

    private void SaveAs(CaptureDocument doc)
    {
        if (RenderOf(doc) is not { } image) return;
        var settings = _settings();
        var suggested = FileNamer.Next(settings.SaveFolder, settings.FileNameTemplate, DateTime.Now, PngExtension, File.Exists);
        var dialog = new SaveFileDialog
        {
            Filter = "PNG 이미지 (*.png)|*.png",
            FileName = Path.GetFileName(suggested),
            InitialDirectory = Directory.Exists(settings.SaveFolder) ? settings.SaveFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ImageExporter.SavePng(image, dialog.FileName, overwrite: true); // 덮어쓰기는 대화상자에서 이미 확인함
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"다른 이름으로 저장 실패 {dialog.FileName}: {ex.Message}");
            MessageBox.Show(this, $"저장하지 못했습니다.\n{ex.Message}", "CaptureOK", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _rememberFolder(Path.GetDirectoryName(dialog.FileName)!);
        Saved(doc, dialog.FileName);
    }

    private void Saved(CaptureDocument doc, string path)
    {
        doc.MarkSaved();
        StatusRight.Text = $"저장됨: {Path.GetFileName(path)}";
        _notifySaved(path);
    }

    private void Copy()
    {
        if (_doc is { } doc) CopyDoc(doc);
    }

    private void CopyDoc(CaptureDocument doc)
    {
        _text.Finish();
        if (doc == _doc) CommitPendingCrop();
        if (RenderOf(doc) is not { } image) return;
        if (!ImageExporter.TryCopyToClipboard(image))
        {
            StatusRight.Text = "클립보드를 다른 프로그램이 쓰고 있어 복사하지 못했습니다";
            return;
        }
        doc.MarkSaved();
        StatusRight.Text = "클립보드에 복사됨";
    }

    // ---- 배치 ----

    /// <summary>
    /// 창의 왼쪽 위를 캡처 영역 근처에 두되, 그 모니터 안으로 들어오게 한다.
    /// 크기는 그 모니터 배율로 기본 크기(DIP)를 바꾼 값이고, 화면보다 크면 화면의 90%로 줄인다.
    /// 배율이 다른 모니터로 옮기면 WPF가 창 크기를 배율 비율로 바꾸므로, 먼저 옮기고 나서 크기를 정한다.
    /// </summary>
    private void PlaceNear(PixelRect rect)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = ScreenGrabber.CurrentLayout().MonitorAt(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var bounds = monitor.Bounds;
        var width = (int)Math.Min(DefaultWidth * monitor.Scale, bounds.Width * 0.9);
        var height = (int)Math.Min(DefaultHeight * monitor.Scale, bounds.Height * 0.9);
        var x = Math.Clamp(rect.X - 40, bounds.X, Math.Max(bounds.X, bounds.Right - width));
        var y = Math.Clamp(rect.Y - 100, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - height));
        NativeMethods.SetWindowPos(hwnd, 0, x, y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER);
        NativeMethods.SetWindowPos(hwnd, 0, x, y, width, height, NativeMethods.SWP_NOZORDER);
    }
}
