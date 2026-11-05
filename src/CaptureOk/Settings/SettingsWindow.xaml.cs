using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptureOk.Core.Files;
using CaptureOk.Core.Geometry;
using CaptureOk.Core.Hotkeys;
using CaptureOk.Core.Settings;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using CaptureMode = CaptureOk.Core.Settings.CaptureMode;
using ThemeMode = CaptureOk.Core.Settings.ThemeMode;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace CaptureOk.Settings;

/// <summary>
/// 환경설정 창 (하나만 재사용). 왼쪽 메뉴로 페이지를 고르고, 바꾸면 바로 저장·적용한다.
/// 페이지는 열 때마다 지금 설정으로 새로 만든다.
/// </summary>
internal sealed partial class SettingsWindow : FluentWindow
{
    private const string GitHubUrl = "https://github.com/LeeHanryang";

    private static readonly (string Title, SettingsPage? Page)[] Pages =
    [
        ("일반", SettingsPage.General),
        ("캡처", SettingsPage.Capture),
        ("단축키", SettingsPage.Hotkeys),
        ("저장", SettingsPage.Save),
        ("고정 크기", SettingsPage.FixedSizes),
        ("캡처 목록", SettingsPage.History),
        ("정보", null),
    ];

    private static readonly (CaptureMode Mode, string Name)[] HotkeyModes =
    [
        (CaptureMode.Region, "영역 지정 캡처"),
        (CaptureMode.Window, "창 캡처"),
        (CaptureMode.Element, "요소 캡처"),
        (CaptureMode.FullScreen, "전체 화면 캡처"),
        (CaptureMode.FixedSize, "고정 크기 캡처"),
        (CaptureMode.LastRegion, "직전 영역 다시 캡처"),
        (CaptureMode.Scroll, "스크롤 캡처"),
    ];

    private readonly App _app;
    private readonly Dictionary<CaptureMode, TextBlock> _hotkeyErrors = [];
    private readonly DispatcherTimer _templateTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private string? _pendingTemplate;
    private int _lastPage;

    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        foreach (var (title, _) in Pages) Nav.Items.Add(new ListBoxItem { Content = title, Padding = new Thickness(12, 8, 12, 8) });
        Nav.SelectionChanged += (_, _) =>
        {
            // Ctrl+클릭으로 선택이 풀리면 보던 페이지를 다시 고른다
            if (Nav.SelectedIndex < 0) { Nav.SelectedIndex = _lastPage; return; }
            _lastPage = Nav.SelectedIndex;
            ShowPage();
        };
        // 테마가 바뀌면 보이는 페이지를 새 색으로 다시 그린다 (카드 색은 만들 때 정해지므로)
        Wpf.Ui.Appearance.ApplicationThemeManager.Changed += (_, _) => Dispatcher.BeginInvoke(() => { if (IsVisible) ShowPage(); });
        ResetButton.Click += (_, _) => ResetPage();
        _templateTimer.Tick += (_, _) => CommitTemplate();
        Closing += (s, e) => { CommitTemplate(); e.Cancel = true; Hide(); }; // 닫기 = 숨기기 (앱이 끝날 때 함께 정리됨)
        Nav.SelectedIndex = 0;
    }

    public void ShowAndActivate()
    {
        ShowPage();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    /// <summary>설정 파일이 바깥에서 바뀌었을 때 보이는 페이지를 새 값으로.</summary>
    public void Reload()
    {
        if (IsVisible) ShowPage();
    }

    private CaptureSettings S => _app.Settings;

    private SettingsPage? CurrentPage => Nav.SelectedIndex is >= 0 and var i && i < Pages.Length ? Pages[i].Page : null;

    private void ShowPage()
    {
        CommitTemplate();
        Page.Children.Clear();
        _hotkeyErrors.Clear();
        Status.Text = "";
        var index = Math.Max(0, Nav.SelectedIndex);
        Page.Children.Add(new TextBlock { Text = Pages[index].Title, FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        ResetButton.Visibility = Pages[index].Page is null ? Visibility.Collapsed : Visibility.Visible;
        switch (Pages[index].Page)
        {
            case SettingsPage.General: BuildGeneral(); break;
            case SettingsPage.Capture: BuildCapture(); break;
            case SettingsPage.Hotkeys: BuildHotkeys(); break;
            case SettingsPage.Save: BuildSave(); break;
            case SettingsPage.FixedSizes: BuildFixedSizes(); break;
            case SettingsPage.History: BuildHistory(); break;
            default: BuildAbout(); break;
        }
    }

    private void ResetPage()
    {
        if (CurrentPage is not { } page) return;
        _pendingTemplate = null;
        _app.UpdateSettings(s => SettingsPages.Reset(s, page, UserProfile));
        ShowPage();
        Status.Text = "이 페이지를 기본값으로 되돌렸습니다";
    }

    private void Update(Action<CaptureSettings> change, string? status = null)
    {
        var problem = _app.UpdateSettings(change);
        Status.Text = problem ?? status ?? "저장됨";
    }

    // ---- 페이지 ----

    private void BuildGeneral()
    {
        // 자동 실행은 레지스트리가 실제 상태 (바깥에서 바뀌었을 수 있음)
        Add(Toggle("Windows 시작 시 자동 실행", "로그인하면 트레이에서 바로 시작합니다", CaptureOk.Tray.StartupRegistration.IsEnabled(), v => Update(s => s.RunAtStartup = v)));
        var theme = new ComboBox { MinWidth = 180 };
        foreach (var name in new[] { "Windows 설정 따라가기", "밝게", "어둡게" }) theme.Items.Add(name);
        theme.SelectedIndex = (int)S.Theme;
        theme.SelectionChanged += (_, _) => Update(s => s.Theme = (ThemeMode)theme.SelectedIndex);
        Add(Row("테마", "화면 색 모드", theme));

        Add(Section("고급"));
        Add(Row("설정 파일", "설정은 이 창에서 바꾸면 되며, 직접 고치면 앱이 다시 불러옵니다",
            ActionButton("열기", () => OpenInNotepad(_app.SettingsPath))));
        Add(Row("로그", "문제가 생겼을 때 확인용", ActionButton("폴더 열기", () => OpenFolder(Path.GetDirectoryName(AppLog.FilePath)!))));
    }

    private void BuildCapture()
    {
        Add(Toggle("드래그를 놓으면 바로 캡처", "끄면 놓은 뒤 방향키로 조정하고 Enter·더블클릭으로 캡처합니다",
            S.ConfirmOnRelease, v => Update(s => s.ConfirmOnRelease = v)));
        Add(Toggle("창·화면 요소 자동 감지", "영역 지정에서 마우스 아래 창·버튼·패널을 강조하고, 클릭하면 그 부분을 캡처합니다 (휠로 바깥·안쪽 요소)",
            S.AutoDetectElements, v => Update(s => s.AutoDetectElements = v)));
        Add(Toggle("마우스 커서 포함", "캡처한 이미지에 그 순간의 마우스 커서를 넣습니다", S.IncludeCursor, v => Update(s => s.IncludeCursor = v)));
        Add(Toggle("십자 안내선 표시", "영역을 고를 때 커서 위치에 가로·세로 안내선", S.ShowGuides, v => Update(s => s.ShowGuides = v)));
        Add(Toggle("돋보기 표시", "영역을 고를 때 커서 주변을 크게 보여 줍니다", S.ShowMagnifier, v => Update(s => s.ShowMagnifier = v)));
        Add(Toggle("조작 안내 표시", "영역을 고를 때 화면에 클릭·드래그·휠 클릭·우클릭 안내를 보여 줍니다", S.ShowCaptureHint, v => Update(s => s.ShowCaptureHint = v)));
    }

    private void BuildHotkeys()
    {
        Add(Hint("칸을 누르고 원하는 키를 누르세요. Backspace = 지우기. 방식마다 2개까지 쓸 수 있습니다."));
        if (S.Hotkeys.Values.SelectMany(v => v).Any(k => string.Equals(HotkeyGesture.Display(k), "PrtSc", StringComparison.Ordinal))
            && App.WindowsPrintScreenOpensSnipping())
        {
            Add(Row("PrintScreen을 Windows가 가로채는 중", "설정 > 접근성 > 키보드 > 'Print Screen 키를 사용하여 화면 캡처 열기'를 끄면 CaptureOK가 받습니다",
                ActionButton("Windows 설정 열기", () => OpenUri("ms-settings:easeofaccess-keyboard"))));
        }
        foreach (var (mode, name) in HotkeyModes)
        {
            var keys = S.Hotkeys.TryGetValue(mode, out var list) ? list : [];
            var first = MakeHotkeyBox(keys.ElementAtOrDefault(0) ?? "");
            var second = MakeHotkeyBox(keys.ElementAtOrDefault(1) ?? "");
            void Save(string _)
            {
                if (second.Value.Length > 0 && second.Value == first.Value) second.Value = ""; // 같은 키를 두 칸에 넣으면 하나만
                Update(s => s.Hotkeys[mode] = new[] { first.Value, second.Value }.Where(k => k.Length > 0).ToList());
            }
            first.Changed += Save;
            second.Changed += Save;
            var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
            _hotkeyErrors[mode] = error;
            var boxes = new StackPanel { Orientation = Orientation.Horizontal, Children = { first, Spacer(8), second } };
            var description = mode switch
            {
                CaptureMode.Scroll => "긴 페이지를 스크롤하며 이어 찍기. 영역 지정 중 가운데 버튼(휠) 클릭으로도 시작",
                CaptureMode.Region or CaptureMode.Element => "강조된 부분을 우클릭해 메뉴에서 GIF 녹화 (Esc 또는 멈춤 버튼으로 끝냄, 최대 30초)",
                _ => null,
            };
            Add(Row(name, description, new StackPanel { Children = { boxes, error } }));
        }
        RefreshHotkeyErrors();
    }

    private HotkeyBox MakeHotkeyBox(string value)
    {
        var box = new HotkeyBox { Value = value };
        box.FocusChanged += focused => _app.SetHotkeysSuspended(focused);
        box.Hint += text => Status.Text = text;
        box.Changed += _ => Dispatcher.BeginInvoke(RefreshHotkeyErrors, DispatcherPriority.Background);
        return box;
    }

    /// <summary>방식끼리 겹치는 키와, 다른 프로그램이 이미 써서 등록하지 못한 키를 칸 아래에 보여 준다.</summary>
    private void RefreshHotkeyErrors()
    {
        var duplicates = HotkeyConflicts.Duplicates(S.Hotkeys);
        var failed = _app.FailedHotkeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (mode, label) in _hotkeyErrors)
        {
            var lines = new List<string>();
            if (duplicates.TryGetValue(mode, out var dup)) lines.Add($"다른 캡처 방식과 겹칩니다: {string.Join(", ", dup.Select(HotkeyGesture.Display))}");
            var mine = (S.Hotkeys.TryGetValue(mode, out var keys) ? keys : []).Select(k => HotkeyGesture.TryParse(k, out var g) ? g.ToString() : k)
                .Where(k => failed.Contains(k) && !(dup?.Contains(k) ?? false)).ToList();
            if (mine.Count > 0 && mode != CaptureMode.Scroll)
                lines.Add($"다른 프로그램이 쓰고 있어 등록하지 못했습니다: {string.Join(", ", mine.Select(HotkeyGesture.Display))}");
            label.Text = string.Join("\n", lines);
            label.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void BuildSave()
    {
        var folder = new TextBox { Text = S.SaveFolder, IsReadOnly = true, MinWidth = 280 };
        var browse = ActionButton("찾아보기", () =>
        {
            var dialog = new OpenFolderDialog { Title = "기본 저장 폴더", InitialDirectory = Directory.Exists(S.SaveFolder) ? S.SaveFolder : UserProfile };
            if (dialog.ShowDialog(this) != true) return;
            Update(s => s.SaveFolder = dialog.FolderName);
            folder.Text = dialog.FolderName;
        });
        Add(Row("기본 저장 폴더", "저장할 때 처음 열리는 폴더, 자동 저장 위치", new StackPanel { Orientation = Orientation.Horizontal, Children = { folder, Spacer(8), browse } }));

        var template = new TextBox { Text = S.FileNameTemplate, MinWidth = 220 };
        var preview = new TextBlock { Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"), Margin = new Thickness(0, 6, 0, 0) };
        void UpdatePreview() => preview.Text = "예: " + Path.GetFileName(FileNamer.Next("", template.Text, DateTime.Now, ".png", _ => false));
        template.TextChanged += (_, _) =>
        {
            UpdatePreview();
            _pendingTemplate = template.Text;
            _templateTimer.Stop();
            _templateTimer.Start();
        };
        UpdatePreview();
        Add(Row("파일 이름 규칙", "%Y 연 · %m 월 · %d 일 · %H 시 · %M 분 · %S 초 (같은 이름이 있으면 _1, _2…)",
            new StackPanel { Children = { template, preview } }));
        Add(Toggle("자동 저장", "저장(Ctrl+S)할 때 위치를 묻지 않고 기본 저장 폴더에 자동 이름으로 저장합니다",
            S.AutoSave, v => Update(s => s.AutoSave = v)));
    }

    private void CommitTemplate()
    {
        _templateTimer.Stop();
        if (_pendingTemplate is not { } text) return;
        _pendingTemplate = null;
        if (text != S.FileNameTemplate) Update(s => s.FileNameTemplate = text);
    }

    private void BuildFixedSizes()
    {
        Add(Hint($"고정 크기 캡처(오버레이에서 숫자 1~{CaptureSettings.MaxFixedSizes}로 전환)에 쓸 크기. 최대 {CaptureSettings.MaxFixedSizes}개."));
        var list = new ListBox { MinHeight = 150, MaxHeight = 240, MinWidth = 220 };
        void Fill(int select)
        {
            list.Items.Clear();
            foreach (var (size, i) in S.FixedSizes.Select((s, i) => (s, i))) list.Items.Add($"{i + 1}.  {size.Width} × {size.Height}");
            list.SelectedIndex = Math.Min(select, list.Items.Count - 1);
        }
        Fill(0);

        var width = new TextBox { Width = 80, Text = "1920" };
        var height = new TextBox { Width = 80, Text = "1080" };
        var add = ActionButton("추가", () =>
        {
            if (!int.TryParse(width.Text, out var w) || !int.TryParse(height.Text, out var h)) { Status.Text = "숫자로 입력해 주세요"; return; }
            var size = new PixelSize(w, h);
            var check = S.FixedSizes.ToList();
            if (!FixedSizeRules.TryAdd(check, size))
            {
                Status.Text = check.Count >= CaptureSettings.MaxFixedSizes ? $"최대 {CaptureSettings.MaxFixedSizes}개까지입니다"
                    : check.Contains(size) ? "이미 있는 크기입니다" : $"가로·세로는 1~{FixedSizeRules.MaxSide}px";
                return;
            }
            Update(s => FixedSizeRules.TryAdd(s.FixedSizes, size), "추가했습니다"); // 최신 설정에 바꾼다
            Fill(S.FixedSizes.Count - 1);
        });
        var remove = ActionButton("삭제", () =>
        {
            var i = list.SelectedIndex;
            if (i < 0) return;
            if (S.FixedSizes.Count <= 1) { Status.Text = "하나는 남아 있어야 합니다"; return; }
            var target = S.FixedSizes[i];
            Update(s => s.FixedSizes.Remove(target), "삭제했습니다");
            Fill(i);
        });
        Button Mover(string text, int delta) => ActionButton(text, () =>
        {
            var i = list.SelectedIndex;
            if (i < 0) return;
            var target = S.FixedSizes[i];
            Update(s => FixedSizeRules.Move(s.FixedSizes, s.FixedSizes.IndexOf(target), delta));
            Fill(Math.Max(0, S.FixedSizes.IndexOf(target)));
        });
        var buttons = new StackPanel { Margin = new Thickness(8, 0, 0, 0), Children = { Mover("위로", -1), Spacer(0, 4), Mover("아래로", +1), Spacer(0, 4), remove } };
        Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { list, buttons } });
        Add(Row("새 크기", "가로 × 세로 (px)", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { width, new TextBlock { Text = " × ", VerticalAlignment = VerticalAlignment.Center }, height, Spacer(8), add },
        }));
    }

    private void BuildHistory()
    {
        Add(Toggle("캡처 목록 패널 표시", "편집 창 오른쪽의 최근 캡처 목록", S.ShowHistoryPanel, v => Update(s => s.ShowHistoryPanel = v)));
        var max = new TextBox { Width = 70, Text = S.HistoryMaxCount.ToString() };
        void CommitMax()
        {
            if (!int.TryParse(max.Text, out var n)) { max.Text = S.HistoryMaxCount.ToString(); return; }
            n = Math.Clamp(n, CaptureSettings.MinHistoryCount, CaptureSettings.MaxHistoryCount);
            max.Text = n.ToString();
            if (n != S.HistoryMaxCount) Update(s => s.HistoryMaxCount = n);
        }
        max.LostKeyboardFocus += (_, _) => CommitMax();
        max.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitMax(); };
        Add(Row("최대 개수", $"{CaptureSettings.MinHistoryCount}~{CaptureSettings.MaxHistoryCount}. 넘으면 고정하지 않은 가장 오래된 캡처부터 빠집니다", max));
        Add(Toggle("목록 비우기 전에 확인", null, S.ConfirmClearHistory, v => Update(s => s.ConfirmClearHistory = v)));
        Add(Toggle("종료할 때 저장 안 한 캡처 확인", "고정한 캡처는 다음 실행에 돌아오므로 세지 않습니다",
            S.ConfirmExitWithUnsaved, v => Update(s => s.ConfirmExitWithUnsaved = v)));
    }

    private void BuildAbout()
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        version = version.Split('+')[0];
        var logo = new System.Windows.Controls.Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")), Width = 48, Height = 48, Margin = new Thickness(0, 0, 14, 0) };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = "CaptureOK", FontSize = 20, FontWeight = FontWeights.SemiBold });
        name.Children.Add(new TextBlock { Text = $"버전 {version}", Margin = new Thickness(0, 2, 0, 0), Foreground = (Brush)FindResource("TextFillColorSecondaryBrush") });
        Add(new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12), Children = { logo, name } });
        Add(new TextBlock { Text = "© 2026 LeeHanryang. 만든 사람이 저작권을 가집니다." });
        var link = new Hyperlink(new Run(GitHubUrl)) { NavigateUri = new Uri(GitHubUrl) };
        link.RequestNavigate += (_, e) => OpenUri(e.Uri.AbsoluteUri);
        Add(new TextBlock { Inlines = { link }, Margin = new Thickness(0, 4, 0, 16) });
        Add(Row("라이선스", "CaptureOK는 GPL-3.0으로 배포됩니다", ActionButton("보기", () => OpenInNotepad(Path.Combine(AppContext.BaseDirectory, "LICENSE.txt")))));
        Add(Row("오픈소스 고지", "WPF UI, Fluent UI System Icons, .NET 등 사용한 오픈소스의 저작권 표시와 라이선스",
            ActionButton("보기", () => OpenInNotepad(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt")))));
    }

    // ---- 꾸밈 도우미 ----

    private void Add(UIElement element) => Page.Children.Add(element);

    private Border Row(string title, string? description, UIElement control)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (description is not null)
            text.Children.Add(new TextBlock { Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush") });
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        grid.Children.Add(text);
        Grid.SetColumn((UIElement)(control is FrameworkElement fe ? WithAlignment(fe) : control), 1);
        grid.Children.Add(control);
        return new Border
        {
            Child = grid,
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource("ControlFillColorDefaultBrush"),
            BorderBrush = (Brush)FindResource("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
        };
    }

    private static FrameworkElement WithAlignment(FrameworkElement e)
    {
        e.VerticalAlignment = VerticalAlignment.Center;
        return e;
    }

    private Border Toggle(string title, string? description, bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = "켬", OffContent = "끔" };
        toggle.Checked += (_, _) => changed(true);
        toggle.Unchecked += (_, _) => changed(false);
        return Row(title, description, toggle);
    }

    private TextBlock Section(string title) =>
        new() { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6) };

    private TextBlock Hint(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Foreground = (Brush)FindResource("TextFillColorSecondaryBrush") };

    private static Button ActionButton(string text, Action click)
    {
        var button = new Button { Content = text, MinWidth = 80 };
        button.Click += (_, _) => click();
        return button;
    }

    private static FrameworkElement Spacer(double width, double height = 0) => new Border { Width = width, Height = height };

    // ---- 바깥 열기 ----

    private void OpenInNotepad(string path) => Run($"열기: {Path.GetFileName(path)}", () =>
    {
        if (!File.Exists(path)) throw new FileNotFoundException("파일이 없습니다", path);
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
        Process.Start(new ProcessStartInfo(notepad, $"\"{path}\"") { UseShellExecute = true });
    });

    private void OpenFolder(string folder) => Run("폴더 열기", () =>
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    });

    private void OpenUri(string uri) => Run("열기", () => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }));

    private void Run(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Write($"환경설정 {name} 실패: {ex.Message}");
            Status.Text = $"{name}에 실패했습니다: {ex.Message}";
        }
    }
}
