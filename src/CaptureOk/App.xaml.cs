using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptureOk.Capture;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Files;
using CaptureOk.Core.Geometry;
using CaptureOk.Core.History;
using CaptureOk.Core.Settings;
using CaptureOk.Editor;
using CaptureOk.History;
using CaptureOk.Hotkeys;
using CaptureOk.Native;
using CaptureOk.Settings;
using CaptureOk.Tray;

namespace CaptureOk;

public partial class App : Application
{
    /// <summary>아직 동작하지 않는 모드 (단축키를 등록하지 않는다).</summary>
    private static readonly CaptureMode[] NotYetSupported = [];

    private static readonly TimeSpan OwnWriteGrace = TimeSpan.FromSeconds(1);

    private SingleInstance? _instance;
    private TrayHost? _tray;
    private SavedNotifier? _saved;
    private HotkeyRegistrar? _hotkeys;
    private SettingsStore _store = null!;
    private CaptureSettings _settings = null!;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _reloadTimer;
    private DateTime _lastOwnWrite;
    private bool _capturing;
    private CaptureHistory _history = null!;
    private readonly Dictionary<Guid, BitmapSource> _images = [];
    private EditorWindow? _editor;
    private PinnedCaptures _pinned = null!;
    private bool _exiting;

    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // 설치·제거 프로그램이 부른다: 실행 중인 앱에 종료를 요청하고 이 프로세스는 바로 끝낸다
        if (e.Args.Any(a => string.Equals(a, "--exit", StringComparison.OrdinalIgnoreCase)))
        {
            SingleInstance.SignalExit();
            Shutdown();
            return;
        }

        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            AppLog.Write("이미 실행 중이라 종료.");
            Shutdown();
            return;
        }

        SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CaptureOk", "settings.json");
        _store = new SettingsStore(SettingsPath);
        _settings = _store.Load(UserProfile);
        GifCapture.DeleteLeftovers();
        if (!File.Exists(SettingsPath)) SaveOwn(() => _store.Save(_settings));
        // 자동 실행은 레지스트리(Run 값)가 실제 상태다. 설정 파일과 다르면 레지스트리를 따른다.
        var startup = StartupRegistration.IsEnabled();
        if (_settings.RunAtStartup != startup) TrySave(s => s.RunAtStartup = startup);
        AppTheme.Apply(_settings.Theme);

        _history = new CaptureHistory(_settings.HistoryMaxCount);
        RestorePinned();
        _history.Changed += ReleaseImagesNotInHistory; // 목록에서 지운·빠진 캡처의 원본을 놓는다
        _tray = new TrayHost(OnCapture, () => _settings.SaveFolder, OpenEditor, OpenSettings, RequestExit);
        _saved = new SavedNotifier(_tray);
        SavedNotifier.RemoveOldRegistration();
        _tray.UpdateShortcuts(_settings.Hotkeys);
        SessionEnding += (_, _) => ExitNow(); // 로그오프·종료 때는 묻지 않는다
        _instance.ExitRequested += () => Dispatcher.BeginInvoke(ExitNow); // 설치·제거 중에도 묻지 않는다 (설치 프로그램이 먼저 물음)
        _hotkeys = new HotkeyRegistrar();
        _hotkeys.Pressed += OnCapture;
        RegisterHotkeys("시작", notify: true);
        WatchSettingsFile(SettingsPath);
    }

    internal string SettingsPath { get; private set; } = "";

    /// <summary>지금 설정 (읽기 전용으로 쓴다 — 바꿀 때는 UpdateSettings).</summary>
    internal CaptureSettings Settings => _settings;

    /// <summary>마지막으로 단축키를 등록할 때 실패한 키 (다른 프로그램이 이미 쓰는 키 등).</summary>
    internal IReadOnlyList<string> FailedHotkeys { get; private set; } = [];

    /// <summary>
    /// 환경설정 창에서: 최신 설정에 change만 적용해 저장하고(다른 곳의 변경을 덮어쓰지 않게), 바뀐 부분만 앱에 적용한다.
    /// </summary>
    /// <returns>문제가 있었으면 사용자에게 보여 줄 문구 (저장 실패, 자동 실행 등록 실패), 없으면 null</returns>
    internal string? UpdateSettings(Action<CaptureSettings> change)
    {
        var before = _settings;
        var problem = TrySave(change);
        return ApplySettings(before, _settings, "환경설정", notify: false) ?? problem;
    }

    /// <summary>편집 창 등에서 기억하는 값(펜 색, 목록 폭·표시)도 같은 길로 저장·적용하고, 열린 환경설정 창을 새로 그린다.</summary>
    private void Remember(Action<CaptureSettings> change)
    {
        UpdateSettings(change);
        _settingsWindow?.Reload();
    }

    private string? TrySave(Action<CaptureSettings> change)
    {
        try
        {
            SaveOwn(() => _settings = _store.Update(UserProfile, change, fallback: _settings));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 이전 값과 비교해 적용할 수 있도록 복사본을 바꾼다 (같은 객체를 바꾸면 바뀐 게 없어 보임)
            var copy = _settings.Clone();
            change(copy);
            _settings = copy;
            AppLog.Write($"설정 저장 실패: {ex.Message}");
            return "설정 파일에 쓰지 못해 이번 실행에만 적용됩니다";
        }
    }

    /// <summary>이전 설정과 비교해 바뀐 것만 적용한다 (단축키 재등록, 테마, 목록 최대 개수·패널, 자동 실행, 트레이 표시).</summary>
    private string? ApplySettings(CaptureSettings before, CaptureSettings after, string reason, bool notify)
    {
        string? problem = null;
        if (!SameHotkeys(before.Hotkeys, after.Hotkeys)) RegisterHotkeys(reason, notify);
        if (before.Theme != after.Theme) AppTheme.Apply(after.Theme);
        if (before.HistoryMaxCount != after.HistoryMaxCount)
        {
            var lost = _history.SetMaxCount(after.HistoryMaxCount).Count(d => d.IsDirty);
            ReleaseImagesNotInHistory();
            if (lost > 0) _tray?.ShowBalloon("CaptureOK", $"최대 개수가 줄어 저장하지 않은 오래된 캡처 {lost}개가 목록에서 빠졌습니다.");
        }
        if (before.ShowHistoryPanel != after.ShowHistoryPanel) _editor?.ApplyPanelSetting(after.ShowHistoryPanel);
        // 레지스트리(실제 상태)와 다르면 맞춘다 — 바깥에서 바뀌었어도 토글한 대로 되게
        if (after.RunAtStartup != StartupRegistration.IsEnabled())
        {
            try { StartupRegistration.Set(after.RunAtStartup); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                AppLog.Write($"자동 실행 설정 실패: {ex.Message}");
                problem = "Windows 시작 시 자동 실행을 등록하지 못했습니다";
            }
        }
        _tray?.UpdateShortcuts(after.Hotkeys);
        return problem;
    }

    private static bool SameHotkeys(Dictionary<CaptureMode, List<string>> a, Dictionary<CaptureMode, List<string>> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var other) && kv.Value.SequenceEqual(other));

    private bool _hotkeysSuspended;

    /// <summary>
    /// 단축키 칸에 키를 입력하는 동안은 전역 단축키를 잠시 풀어, 누른 키가 캡처로 가로채이지 않게 한다.
    /// 풀려 있는 동안 설정이 바뀌어도 다시 등록하지 않고(실패 여부만 확인), 칸을 벗어나면 등록한다.
    /// </summary>
    internal void SetHotkeysSuspended(bool suspended)
    {
        _hotkeysSuspended = suspended;
        if (suspended) _hotkeys?.RegisterAll(new Dictionary<CaptureMode, List<string>>());
        else RegisterHotkeys("단축키 입력 끝", notify: false);
    }

    private SettingsWindow? _settingsWindow;

    internal void OpenSettings()
    {
        AppLog.Write("환경설정 열기");
        _settingsWindow ??= new SettingsWindow(this);
        _settingsWindow.ShowAndActivate();
    }

    /// <summary>지난번에 고정해 둔 캡처를 편집 상태째 목록에 되살린다 (깨진 것은 로그만 남기고 건너뜀).</summary>
    private void RestorePinned()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaptureOk", "pinned");
        _pinned = new PinnedCaptures(new PinnedStore(root), id => _images.GetValueOrDefault(id), TimeSpan.FromSeconds(1));
        var restored = _pinned.RestoreAll(AppLog.Write);
        foreach (var (doc, image) in restored) _images[doc.Id] = image;
        _history.Restore(restored.Select(r => r.Doc));
        if (restored.Count > 0) AppLog.Write($"고정 캡처 {restored.Count}개 복원");
    }

    private void RegisterHotkeys(string reason, bool notify)
    {
        var map = _settings.Hotkeys.Where(kv => !NotYetSupported.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var failed = _hotkeys!.RegisterAll(map);
        FailedHotkeys = failed;
        if (_hotkeysSuspended) _hotkeys.RegisterAll(new Dictionary<CaptureMode, List<string>>()); // 입력 중: 실패 여부만 확인하고 다시 푼다
        AppLog.Write($"{reason}. 단축키 등록 실패: [{string.Join(", ", failed)}]");
        if (!notify) return; // 환경설정 창은 실패를 칸 아래에 직접 보여 준다
        if (failed.Count > 0)
        {
            _tray?.ShowBalloon("단축키를 등록하지 못했습니다",
                $"{string.Join(", ", failed)}\n다른 캡처 프로그램이나 Windows 캡처 도구가 쓰고 있을 수 있습니다. " +
                "그 프로그램에서 끄거나, 트레이 메뉴 > 환경설정 > 단축키에서 다른 키로 바꾸세요.");
            return;
        }

        var usesPrintScreen = map.Values.SelectMany(v => v).Any(k => string.Equals(k.Trim(), "PrintScreen", StringComparison.OrdinalIgnoreCase));
        if (usesPrintScreen && WindowsPrintScreenOpensSnipping())
        {
            AppLog.Write("Windows 설정이 PrintScreen을 캡처 도구에 연결하고 있음");
            _tray?.ShowBalloon("PrintScreen 키를 Windows가 가로채고 있습니다",
                "설정 > 접근성 > 키보드 > 'Print Screen 키를 사용하여 화면 캡처 열기'를 끄면 CaptureOK가 PrintScreen을 받습니다.");
        }
    }

    /// <summary>Windows 11 기본값은 켜짐이라, 값이 없으면 켜진 것으로 본다.</summary>
    internal static bool WindowsPrintScreenOpensSnipping()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
        return key?.GetValue("PrintScreenKeyForSnippingEnabled") is not int value || value != 0;
    }

    /// <summary>앱 실행 중 사용자가 settings.json을 고치면 다시 불러오고 단축키를 다시 등록한다.</summary>
    private void WatchSettingsFile(string settingsPath)
    {
        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            if (DateTime.UtcNow - _lastOwnWrite < OwnWriteGrace) return; // 앱이 직접 쓴 변경
            if (!_store.TryLoad(UserProfile, out var loaded))
            {
                // 고치다 만 파일: 기본값으로 바꾸지 않고 지금 설정을 유지한다 (깨진 파일은 .bak으로 보존됨)
                _tray?.ShowBalloon("CaptureOK", "설정 파일을 읽지 못해 이전 설정을 그대로 씁니다. 파일 내용을 확인해 주세요.");
                return;
            }
            var before = _settings;
            _settings = loaded;
            ApplySettings(before, _settings, "설정 파일 변경", notify: true);
            _settingsWindow?.Reload();
            _tray?.ShowBalloon("CaptureOK", "설정을 다시 불러왔습니다.");
        };

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(settingsPath)!, Path.GetFileName(settingsPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        FileSystemEventHandler restart = (_, _) => Dispatcher.BeginInvoke(() => { _reloadTimer.Stop(); _reloadTimer.Start(); });
        _watcher.Changed += restart;
        _watcher.Created += restart;
        _watcher.Renamed += (s, ev) => restart(s, ev);
        _watcher.EnableRaisingEvents = true;
    }

    private void SaveOwn(Action write)
    {
        _lastOwnWrite = DateTime.UtcNow;
        write();
    }

    private async void OnCapture(CaptureMode mode)
    {
        if (_capturing) return;
        _capturing = true;
        try
        {
            var result = await CaptureSession.RunAsync(mode, _settings);
            AppLog.Write(result is null ? $"{mode} 캡처 취소" : $"{mode} 캡처 {result.Rect}");
            if (result is null) return;

            if (result.RecordGif) // 영역 지정 중 우클릭
            {
                await RecordGif(result.Rect);
                return;
            }

            if (result.Mode == CaptureMode.Scroll) // 영역 지정 중 가운데 클릭도 여기로 온다
            {
                // 고른 영역을 실제 화면에서 스크롤하며 이어 찍는다
                var scrolled = await ScrollCapture.RunAsync(result.Rect);
                AppLog.Write($"스크롤 캡처 {scrolled.Image.PixelWidth}×{scrolled.Image.PixelHeight} {scrolled.Notice}");
                ShowResult(result with { Image = scrolled.Image });
                if (scrolled.Notice is { } notice) _editor?.ShowStatus($"스크롤 캡처: {notice}");
                return;
            }

            ShowResult(result);
            RememberLastRegion(mode, result);
        }
        catch (Exception ex)
        {
            AppLog.Write($"{mode} 캡처 실패: {ex}");
            _tray?.ShowBalloon("캡처 실패", ex.Message, System.Windows.Forms.ToolTipIcon.Error);
        }
        finally
        {
            _capturing = false;
        }
    }

    /// <summary>영역을 GIF로 녹화해 저장한다. 편집 창은 거치지 않는다 (편집 창은 정지 이미지 전용).</summary>
    private async Task RecordGif(PixelRect rect)
    {
        var recorded = await GifCapture.RunAsync(rect);
        string? saved = null;
        try
        {
            saved = await SaveGif(recorded, rect);
        }
        finally
        {
            GifCapture.TryDelete(recorded.Path);
        }
        if (saved is null)
        {
            AppLog.Write("GIF 저장 취소");
            return;
        }
        var size = recorded.Bytes >= 1 << 20 ? $"{recorded.Bytes / (double)(1 << 20):F1}MB" : $"{Math.Max(1, recorded.Bytes >> 10)}KB";
        var message = $"{Path.GetFileName(saved)} ({recorded.Duration.TotalSeconds:F0}초, {size})";
        _saved?.Show("GIF를 저장했습니다", recorded.Notice is { } notice ? $"{message}\n{notice}" : message, saved);
    }

    /// <summary>임시 파일을 저장할 곳으로 복사한다. 저장 위치를 고르다 취소해도 녹화를 잃지 않게 한 번 더 묻는다. 저장한 경로를 돌려준다 (버렸으면 null).</summary>
    private async Task<string?> SaveGif(GifCapture.Result recorded, PixelRect rect)
    {
        // 트레이 앱이라 앞에 나설 창이 없으므로, 대화상자가 다른 창 뒤에 숨지 않게 맨 위 창에 붙인다
        Window? owner = null;
        Window Owner() => owner ??= NewDialogOwner(rect);
        try
        {
            var askFirst = !_settings.AutoSave;
            while (true)
            {
                var path = askFirst
                    ? AskGifPath(Owner())
                    : FileNamer.Next(_settings.SaveFolder, _settings.FileNameTemplate, DateTime.Now, ".gif", File.Exists);
                if (path is null)
                {
                    var answer = MessageBox.Show(Owner(), "녹화한 GIF를 저장하지 않고 버릴까요?", "CaptureOK", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                    if (answer == MessageBoxResult.Yes) return null;
                    continue;
                }

                var existed = File.Exists(path);
                try
                {
                    // 옮기지 않고 복사한다: 옮기면 임시 폴더의 권한이 따라가고, 다른 드라이브면 어차피 복사다. 큰 파일에도 앱이 멈추지 않게 따로 돌린다
                    await Task.Run(() =>
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.Copy(recorded.Path, path, overwrite: true); // 덮어쓰기는 대화상자에서 이미 확인함 (자동 이름은 겹치지 않음)
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    AppLog.Write($"GIF 저장 실패 {path}: {ex.Message}");
                    if (!existed) GifCapture.TryDelete(path); // 쓰다 만 파일
                    MessageBox.Show(Owner(), $"GIF를 저장하지 못했습니다.\n{ex.Message}\n\n다른 위치를 골라 주세요.", "CaptureOK", MessageBoxButton.OK, MessageBoxImage.Warning);
                    askFirst = true;
                    continue;
                }
                if (askFirst) RememberSaveFolder(Path.GetDirectoryName(path)!);
                return path;
            }
        }
        finally
        {
            owner?.Close();
        }
    }

    private string? AskGifPath(Window owner)
    {
        var folder = Directory.Exists(_settings.SaveFolder) ? _settings.SaveFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "GIF 이미지 (*.gif)|*.gif",
            FileName = Path.GetFileName(FileNamer.Next(folder, _settings.FileNameTemplate, DateTime.Now, ".gif", File.Exists)),
            InitialDirectory = folder,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    /// <summary>녹화한 모니터 위에 보이지 않는 맨 위 창을 띄워 앞으로 가져온다. 다 쓰면 Close.</summary>
    private static Window NewDialogOwner(PixelRect rect)
    {
        var monitor = ScreenGrabber.CurrentLayout().MonitorAt(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var owner = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Opacity = 0,
            ShowInTaskbar = false,
            Topmost = true,
            Width = 1,
            Height = 1,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        var handle = new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
        NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, monitor.Bounds.X + monitor.Bounds.Width / 2, monitor.Bounds.Y + monitor.Bounds.Height / 3, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        owner.Show();
        NativeMethods.BringToForeground(handle);
        return owner;
    }

    /// <summary>"직전 영역"은 사용자가 직접 고른 영역만 기억한다. 저장 실패는 캡처 결과에 영향을 주지 않는다.</summary>
    private void RememberLastRegion(CaptureMode mode, CaptureResult result)
    {
        if (mode is CaptureMode.FullScreen or CaptureMode.LastRegion) return;
        TrySave(s => s.LastRegion = result.Rect);
    }

    /// <summary>캡처 결과를 목록 맨 위에 넣고 하나뿐인 편집 창에 보여 준다.</summary>
    private void ShowResult(CaptureResult result)
    {
        var doc = new CaptureDocument(Guid.NewGuid(), result.Image.PixelWidth, result.Image.PixelHeight, DateTime.Now);
        _images[doc.Id] = result.Image;
        var evicted = _history.Add(doc);
        foreach (var old in evicted) _images.Remove(old.Id);

        var editor = EnsureEditor();
        editor.ShowDocument(doc, result.Rect);
        if (evicted.Any(d => d.IsDirty))
            editor.ShowStatus($"저장하지 않은 오래된 캡처 {evicted.Count(d => d.IsDirty)}개가 목록에서 빠졌습니다");
    }

    private void ReleaseImagesNotInHistory()
    {
        var keep = _history.Items.Select(d => d.Id).ToHashSet();
        foreach (var id in _images.Keys.Where(id => !keep.Contains(id)).ToList()) _images.Remove(id);
    }

    private EditorWindow EnsureEditor() => _editor ??= new EditorWindow(_history, _pinned, () => _settings, id => _images.GetValueOrDefault(id),
        RememberSaveFolder, RememberPanelVisible, RememberPanelWidth, RememberPen,
        path => _saved?.Show("저장했습니다", Path.GetFileName(path), path));

    /// <summary>트레이에서 편집 창 열기. 캡처가 하나도 없으면 빈 목록과 안내 문구를 보여 준다.</summary>
    private void OpenEditor() => EnsureEditor().ShowCurrent();

    // 편집 창이 기억하는 값도 환경설정과 같은 길로 저장·적용한다 (열린 환경설정 창도 새 값으로).
    private void RememberPanelWidth(int width) => Remember(s => s.HistoryPanelWidth = width);
    private void RememberPen(uint color, int width) => Remember(s => { s.PenColorArgb = color; s.PenWidth = width; });
    private void RememberPanelVisible(bool visible) => Remember(s => s.ShowHistoryPanel = visible);
    private void RememberSaveFolder(string folder) => Remember(s => s.SaveFolder = folder);

    /// <summary>
    /// 트레이 "종료". 끄면 사라지는 저장·복사 안 한 캡처가 있으면 편집 창을 띄워 확인한다
    /// (고정 캡처는 다음 실행에 돌아오므로 세지 않는다).
    /// </summary>
    private void RequestExit()
    {
        _editor?.CommitPendingEdits(); // 입력 중인 텍스트도 편집으로 쳐서 센다
        var unsaved = _history.LostOnExitCount;
        if (_settings.ConfirmExitWithUnsaved && unsaved > 0 && _editor is not null)
        {
            _editor.BringToFront();
            var answer = MessageBox.Show(_editor, $"저장하지 않은 캡처 {unsaved}개가 있습니다. 종료할까요?", "CaptureOK",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }
        ExitNow();
    }

    private void ExitNow()
    {
        if (_exiting) return;
        _exiting = true;
        _editor?.CommitPendingEdits(); // 입력 중인 텍스트를 먼저 확정해야 아래 저장에 들어간다
        _pinned.FlushAll(); // 고정 캡처의 마지막 편집을 남긴다
        _editor?.AllowClose();
        _editor?.Close();
        Shutdown();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // 트레이 상주 앱이 한 번의 오류로 꺼지면 단축키가 모두 사라지므로, 기록하고 계속 실행한다.
        AppLog.Write($"처리되지 않은 예외: {e.Exception}");
        _tray?.ShowBalloon("CaptureOK 오류", e.Exception.Message, System.Windows.Forms.ToolTipIcon.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
