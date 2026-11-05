using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Editor;

/// <summary>
/// 캡처 원본과 편집 객체를 그리고 그리기·선택·이동·지우개·자르기 입력을 받는다.
/// 보이는 영역(View)은 평소 문서의 자른 영역, 자르기 도구일 때는 원본 전체다.
/// 화면 좌표(DIP) ↔ 원본 픽셀: 원본 = DIP ÷ Scale + View 원점 (Scale = 확대율 ÷ DPI 배율).
/// 이미 그린 객체는 한 번 그려 둔 Drawing을 다시 쓰고, 마우스를 움직일 때는 진행 중인 것만 새로 그린다.
/// </summary>
internal sealed class AnnotationCanvas : FrameworkElement
{
    private const double HitToleranceDip = 4;
    private const double GripToleranceDip = 7;
    private const double GripSizeDip = 8;
    private const double MinPointSpacing = 0.5;
    private static readonly Brush HitBackground = Freeze(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
    private static readonly Brush SelectionBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x6F, 0xFF)));
    private static readonly Brush CropDimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)));
    private static readonly Brush LabelBackground = Freeze(new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20)));
    private static readonly Pen GripPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), 1));
    private static readonly Typeface LabelFace = new("Segoe UI");

    private CaptureDocument? _document;
    private Drawing? _committed; // 이미 그린 객체 (원본 픽셀 좌표), 바뀔 때만 다시 만든다
    private bool _drawing;
    private PointD _start;
    private StrokeBuilder? _stroke; // 그리는 중인 펜·형광펜 획
    private Annotation? _preview;
    private bool _moving;
    private PointD _moveFrom;
    private PointD _moveOffset;
    private Guid? _hiddenId;
    private EditorTool _tool = EditorTools.Default;
    private RectD _lastView;

    // 지우개 드래그: 지나가며 지운 객체는 바로 숨기고, 놓을 때 한 번에 지운다 (실행 취소 한 번)
    private bool _erasing;
    private HashSet<Guid> _erased = [];
    private PointD _lastErase;

    // 자르기: 고른 영역, 끄는 손잡이, 끌기 시작할 때의 영역
    private RectD? _pendingCrop;
    private bool _croppingDrag;
    private CropGrip _cropGrip;
    private RectD _cropStart;

    // 모자이크·블러 드래그 미리보기: 계산은 뒤에서 하고 가장 최근 영역만 계산한다
    private (BitmapSource Image, Rect Area)? _effectPreview;
    private bool _previewRunning;
    private EffectJob? _queuedJob;
    private EffectJob? _latestJob;
    private int _previewGeneration;

    public CaptureDocument? Document
    {
        get => _document;
        set
        {
            CancelInProgress();
            if (_document is not null) _document.Changed -= OnDocumentChanged;
            _document = value;
            _pendingCrop = _tool == EditorTool.Crop ? CurrentCropOrWhole() : null;
            if (_document is not null) _document.Changed += OnDocumentChanged;
            SelectedId = null;
            Rebuild();
            RaiseViewChangedIfNeeded(force: true);
        }
    }

    /// <summary>현재 문서의 원본 이미지.</summary>
    public BitmapSource? BaseImage { get; set; }

    /// <summary>현재 문서 원본의 모자이크·블러 캐시.</summary>
    public EffectCache? Effects { get; set; }

    public EditorTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            // 지금 자른 영역(없으면 사진 전체)에서 시작해 테두리를 끌어 줄이거나 넓힐 수 있게
            _pendingCrop = _tool == EditorTool.Crop ? CurrentCropOrWhole() : null;
            RaiseViewChangedIfNeeded(force: false);
            InvalidateVisual();
        }
    }

    public StrokeStyle PenStyle { get; set; } = new(0xFFFF0000, 3, false);
    public bool Filled { get; set; }
    public double TextSize { get; set; } = DrawingOptions.DefaultFontSize;
    public double NumberDiameter { get; set; } = DrawingOptions.DefaultNumberDiameter;
    public int MosaicBlock { get; set; } = DrawingOptions.DefaultMosaicBlock;
    public int BlurRadius { get; set; } = DrawingOptions.DefaultBlurRadius;

    /// <summary>원본 픽셀 1개가 화면에서 차지하는 DIP (확대율 ÷ DPI 배율).</summary>
    public double Scale { get; set; } = 1;

    /// <summary>지금 보여 주는 원본 영역.</summary>
    public RectD View => _document is null
        ? default
        : _tool == EditorTool.Crop ? WholeImage : _document.ViewRect;

    /// <summary>자르기 도구에서 고른 (아직 적용하지 않은) 영역.</summary>
    public RectD? PendingCrop => _pendingCrop;

    public Guid? SelectedId { get; private set; }

    /// <summary>다시 편집 중인 텍스트처럼, 캔버스에 그리지 않을 객체.</summary>
    public Guid? HiddenId
    {
        get => _hiddenId;
        set { _hiddenId = value; Rebuild(); }
    }

    /// <summary>텍스트 도구로 클릭했거나 텍스트 객체를 더블클릭함 (위치, 다시 편집할 기존 객체).</summary>
    public event Action<PointD, TextAnnotation?>? TextEditRequested;

    /// <summary>캔버스를 누르기 전에 진행 중인 텍스트 입력을 끝낸다. 끝낸 입력이 있었으면 true (이번 클릭은 그걸로 끝).</summary>
    public Func<bool>? FinishTextEdit { get; set; }

    public event Action<string>? StatusRequested;

    /// <summary>보이는 영역(크기·원점)이 바뀌었다 — 창이 캔버스 크기를 다시 맞춘다.</summary>
    public event Action? ViewChanged;

    public PointD ToImage(Point dip) => new(dip.X / Scale + View.X, dip.Y / Scale + View.Y);
    public Point ToDip(PointD px) => new((px.X - View.X) * Scale, (px.Y - View.Y) * Scale);

    private RectD WholeImage => _document is null ? default : new RectD(0, 0, _document.ImageWidth, _document.ImageHeight);

    private RectD? CurrentCropOrWhole() => _document is null ? null : _document.CropRect ?? WholeImage;

    /// <summary>진행 중인 드래그·이동·지우기를 버린다 (도구·문서 전환, 실행 취소 전).</summary>
    public void CancelInProgress()
    {
        var needsRebuild = _moving || _erased.Count > 0;
        _drawing = false;
        _moving = false;
        _erasing = false;
        _erased = [];
        if (_croppingDrag)
        {
            _croppingDrag = false;
            _pendingCrop = _cropStart;
        }
        _preview = null;
        _stroke = null;
        ResetEffectPreview();
        if (IsMouseCaptured) ReleaseMouseCapture(); // 상태를 먼저 지워서 OnLostMouseCapture가 확정하지 않게 한다
        if (needsRebuild) Rebuild();
        else InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (_document is null || SelectedId is not { } id || _document.Find(id) is null) return;
        _document.Execute(new RemoveAnnotation(id));
        SelectedId = null;
    }

    public void ClearSelection()
    {
        SelectedId = null;
        InvalidateVisual();
    }

    /// <summary>자르기 도구에서 Enter: 고른 영역으로 자른다. 고른 영역이 없으면 false. 바뀐 게 없으면 편집으로 남기지 않는다.</summary>
    public bool ApplyPendingCrop()
    {
        if (_document is null || _pendingCrop is not { } crop) return false;
        CancelInProgress();
        var target = CropEditing.Normalize(crop, _document.ImageWidth, _document.ImageHeight);
        RectD? next = target is null || target == WholeImage ? null : target;
        _pendingCrop = null;
        if (next != _document.CropRect) _document.Execute(new SetCrop(next));
        _pendingCrop = null;
        return true;
    }

    public void CancelPendingCrop()
    {
        CancelInProgress();
        _pendingCrop = null;
        InvalidateVisual();
    }

    /// <summary>도구에 맞는 커서: 펜·형광펜·지우개는 그 도구 모양(끝이 그리는 점), 도형·영역 도구는 십자.</summary>
    public void UpdateCursor() => Cursor = ToolCursors.For(EditorTools.CursorOf(Tool));

    // ---- 입력 ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_document is null) return;
        if (FinishTextEdit?.Invoke() == true) { e.Handled = true; return; }

        var p = ToImage(e.GetPosition(this));
        var tolerance = HitToleranceDip / Scale;
        e.Handled = true;

        switch (Tool)
        {
            case EditorTool.Select:
                var hit = HitTester.Hit(_document.Annotations, p, tolerance);
                SelectedId = hit?.Id;
                if (hit is TextAnnotation text && e.ClickCount >= 2)
                {
                    TextEditRequested?.Invoke(text.Position, text);
                }
                else if (hit is not null)
                {
                    _moving = true;
                    _moveFrom = p;
                    _moveOffset = default;
                    CaptureMouse();
                    Rebuild(); // 옮기는 객체는 미리 그린 그림에서 빼고 따로 그린다
                    return;
                }
                InvalidateVisual();
                return;

            case EditorTool.Eraser:
                _erasing = true;
                _erased = [];
                _lastErase = p;
                EraseAlong(p, p, includeEffects: true);
                CaptureMouse();
                return;

            case EditorTool.Number:
                _document.Execute(new AddAnnotation(new NumberAnnotation(Guid.NewGuid(), p, ShapeSnapping.NextNumber(_document.Annotations),
                    PenStyle.ColorArgb, NumberDiameter)));
                return;

            case EditorTool.Text:
                TextEditRequested?.Invoke(p, null);
                return;

            case EditorTool.Crop:
                // 테두리·모서리를 누르면 그 변을, 안쪽은 영역 이동, 바깥은 새로 고르기
                _cropStart = _pendingCrop ?? WholeImage;
                _cropGrip = CropEditing.HitGrip(_cropStart, p, GripToleranceDip / Scale, _document.ImageWidth, _document.ImageHeight);
                _start = _cropGrip == CropGrip.None ? ClampToImage(p) : p;
                _croppingDrag = true;
                CaptureMouse();
                return;
        }

        _drawing = true;
        _start = p;
        _stroke = new StrokeBuilder(p, MinPointSpacing);
        _previewGeneration++;
        UpdatePreview(p);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = ToImage(e.GetPosition(this));
        if (!IsMouseCaptured)
        {
            if (Tool == EditorTool.Crop && _document is not null)
                Cursor = CursorOf(CropEditing.HitGrip(_pendingCrop ?? WholeImage, p, GripToleranceDip / Scale, _document.ImageWidth, _document.ImageHeight));
            return;
        }
        if (_moving)
        {
            _moveOffset = new PointD(p.X - _moveFrom.X, p.Y - _moveFrom.Y);
            InvalidateVisual();
            return;
        }
        if (_erasing)
        {
            EraseTo(p);
            return;
        }
        if (_croppingDrag)
        {
            UpdateCrop(p);
            return;
        }
        if (_drawing) UpdatePreview(p);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        var p = ToImage(e.GetPosition(this));
        if (_drawing) UpdatePreview(p);
        if (_croppingDrag) UpdateCrop(p);
        if (_erasing) EraseTo(p);
        FinishInProgress();
        ReleaseMouseCapture();
    }

    /// <summary>
    /// Alt+Tab·Win 키·다른 창 때문에 버튼을 누른 채 마우스 제어를 잃으면, 화면에만 남고 저장에는 빠지는 일이 없도록
    /// 그 자리에서 확정한다.
    /// </summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        FinishInProgress();
    }

    private void FinishInProgress()
    {
        if (_document is null) return;
        if (_croppingDrag)
        {
            _croppingDrag = false;
            _pendingCrop ??= _cropStart; // 클릭만 했으면 고른 영역을 그대로 둔다
            StatusRequested?.Invoke(CropStatus());
            InvalidateVisual();
            return;
        }
        if (_erasing)
        {
            _erasing = false;
            var erased = _erased;
            _erased = [];
            if (erased.Count > 0) _document.Execute(new RemoveAnnotations(erased));
            return;
        }
        if (_moving)
        {
            _moving = false;
            if (SelectedId is { } id && (_moveOffset.X != 0 || _moveOffset.Y != 0))
                _document.Execute(EditCommands.Move(_document, id, _moveOffset.X, _moveOffset.Y));
            _moveOffset = default;
            Rebuild();
            return;
        }
        if (!_drawing) return;
        var finished = _preview;
        _drawing = false;
        _preview = null;
        ResetEffectPreview();
        if (finished is StrokeAnnotation stroke) finished = stroke with { Points = _stroke!.Points.ToArray() }; // 그리는 동안은 목록을 그대로 썼다
        _stroke = null;
        if (finished is not null && !IsTooSmall(finished)) _document.Execute(new AddAnnotation(finished));
        else InvalidateVisual();
    }

    /// <summary>지우개를 이전 위치에서 p까지 끈다. 같은 점은 다시 보지 않는다 (겹친 객체가 클릭 한 번에 줄줄이 지워지지 않게).</summary>
    private void EraseTo(PointD p)
    {
        if (p == _lastErase) return;
        EraseAlong(_lastErase, p, includeEffects: false);
        _lastErase = p;
    }

    /// <summary>
    /// 닿은 객체를 숨긴다. 모자이크·블러는 누른 자리에서만 지운다 — 펜 자국 따위를 문지르다
    /// 지나가는 바람에 가려 둔 정보가 드러나지 않게.
    /// </summary>
    private void EraseAlong(PointD from, PointD to, bool includeEffects)
    {
        if (_document is null) return;
        var hits = HitTester.HitAlong(_document.Annotations, from, to, HitToleranceDip / Scale, _erased,
            includeEffects ? null : a => a is not (MosaicAnnotation or BlurAnnotation));
        if (hits.Count == 0) return;
        foreach (var a in hits) _erased.Add(a.Id);
        Rebuild();
    }

    private void UpdateCrop(PointD p)
    {
        if (_document is null) return;
        int w = _document.ImageWidth, h = _document.ImageHeight;
        var raw = _cropGrip == CropGrip.None
            ? RectD.FromPoints(_start, ClampToImage(p))
            : CropEditing.Drag(_cropStart, _cropGrip, p.X - _start.X, p.Y - _start.Y, w, h);
        // 적용될 영역 그대로(픽셀 맞춤) 보여 준다. 새로 고르는 중 1px 미만이면 아직 없음.
        _pendingCrop = CropEditing.Normalize(raw, w, h);
        StatusRequested?.Invoke(CropStatus());
        InvalidateVisual();
    }

    private string CropStatus() => _pendingCrop is { } r ? $"자를 영역 {r.Width:0} × {r.Height:0} — Enter로 적용, Esc로 취소" : "";

    private static Cursor CursorOf(CropGrip grip) => grip switch
    {
        CropGrip.Move => Cursors.SizeAll,
        CropGrip.Left or CropGrip.Right => Cursors.SizeWE,
        CropGrip.Top or CropGrip.Bottom => Cursors.SizeNS,
        CropGrip.Left | CropGrip.Top or CropGrip.Right | CropGrip.Bottom => Cursors.SizeNWSE,
        CropGrip.Right | CropGrip.Top or CropGrip.Left | CropGrip.Bottom => Cursors.SizeNESW,
        _ => Cursors.Cross,
    };

    private void UpdatePreview(PointD p)
    {
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var id = _preview?.Id ?? Guid.NewGuid();
        switch (Tool)
        {
            case EditorTool.Pen or EditorTool.Highlighter:
                if (!_stroke!.Add(p, shift, ctrl)) return;
                // 펜·형광펜에는 점선을 쓰지 않는다 (옵션은 색·굵기만). 그리는 동안은 복사하지 않고 같은 목록을 넘긴다.
                _preview = new StrokeAnnotation(id, _stroke.Points, PenStyle with { Dashed = false }, Highlighter: Tool == EditorTool.Highlighter);
                break;
            case EditorTool.Rectangle or EditorTool.Ellipse:
                var end = shift ? ShapeSnapping.SnapSquare(_start, p) : p;
                _preview = new ShapeAnnotation(id, Tool == EditorTool.Rectangle ? ShapeKind.Rectangle : ShapeKind.Ellipse,
                    RectD.FromPoints(_start, end), PenStyle, Filled);
                break;
            case EditorTool.Line or EditorTool.Arrow:
                _preview = new LineAnnotation(id, _start, shift ? ShapeSnapping.Snap45(_start, p) : p, PenStyle, Arrow: Tool == EditorTool.Arrow);
                break;
            case EditorTool.Mosaic:
                _preview = new MosaicAnnotation(id, ClampRect(RectD.FromPoints(_start, p)), MosaicBlock);
                RequestEffectPreview(_preview);
                break;
            case EditorTool.Blur:
                _preview = new BlurAnnotation(id, ClampRect(RectD.FromPoints(_start, p)), BlurRadius);
                RequestEffectPreview(_preview);
                break;
        }
        InvalidateVisual();
    }

    // ---- 모자이크·블러 미리보기 ----

    /// <summary>
    /// 드래그 중인 영역의 효과를 뒤에서 계산한다. 계산 중이면 가장 최근 영역 하나만 기다리게 해서
    /// 큰 블러도 마우스가 끊기지 않고, 효과는 조금 늦게 따라온다.
    /// </summary>
    private void RequestEffectPreview(Annotation annotation)
    {
        if (Effects is not { } cache || cache.Prepare(annotation) is not { } job) return;
        if (job == _latestJob) return;
        _latestJob = job;
        if (_previewRunning) { _queuedJob = job; return; }
        StartPreview(cache, job);
    }

    private void StartPreview(EffectCache cache, EffectJob job)
    {
        _previewRunning = true;
        var generation = _previewGeneration;
        Task.Run(() => cache.Run(job)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            _previewRunning = false;
            if (t.IsCompletedSuccessfully && _drawing && generation == _previewGeneration && ReferenceEquals(cache, Effects))
            {
                // 마지막으로 요청한 영역이면 캐시에 넣어, 놓을 때 다시 계산하지 않게 한다.
                _effectPreview = cache.Finish(job, t.Result, remember: job == _latestJob);
                InvalidateVisual();
            }
            if (_queuedJob is { } next && Effects is { } current)
            {
                _queuedJob = null;
                StartPreview(current, next);
            }
        }), TaskScheduler.Default);
    }

    private void ResetEffectPreview()
    {
        _previewGeneration++;
        _effectPreview = null;
        _queuedJob = null;
        _latestJob = null;
    }

    /// <summary>실수로 클릭만 한 도형·선·효과는 만들지 않는다 (펜 점 하나는 점으로 남긴다).</summary>
    private static bool IsTooSmall(Annotation a) => a switch
    {
        ShapeAnnotation s => s.Rect.Width < 1 && s.Rect.Height < 1,
        LineAnnotation l => Math.Abs(l.End.X - l.Start.X) < 1 && Math.Abs(l.End.Y - l.Start.Y) < 1,
        MosaicAnnotation or BlurAnnotation => a.Bounds.Width < 1 || a.Bounds.Height < 1,
        _ => false,
    };

    private PointD ClampToImage(PointD p) => _document is null
        ? p
        : new PointD(Math.Clamp(p.X, 0, _document.ImageWidth), Math.Clamp(p.Y, 0, _document.ImageHeight));

    private RectD ClampRect(RectD r) => _document is null ? r : r.ClampTo(_document.ImageWidth, _document.ImageHeight);

    // ---- 그리기 ----

    private void OnDocumentChanged()
    {
        // 자르기 도구에서 실행 취소·다시 실행하면 표시 중인 영역도 문서의 자른 영역을 따라간다.
        if (_tool == EditorTool.Crop && !_croppingDrag) _pendingCrop = CurrentCropOrWhole();
        Rebuild();
        RaiseViewChangedIfNeeded(force: false);
    }

    private void RaiseViewChangedIfNeeded(bool force)
    {
        var view = View;
        if (!force && view == _lastView) return;
        _lastView = view;
        ViewChanged?.Invoke();
    }

    /// <summary>이미 그린 객체 그림을 다시 만든다 (문서 변경, 숨김·이동·지우기 시작·끝).</summary>
    private void Rebuild()
    {
        if (_document is null)
        {
            _committed = null;
        }
        else
        {
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                foreach (var a in _document.Annotations)
                {
                    if (a.Id == _hiddenId || _erased.Contains(a.Id) || (_moving && a.Id == SelectedId)) continue;
                    AnnotationRenderer.Draw(dc, a, Effects);
                }
            }
            group.Freeze();
            _committed = group;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(HitBackground, null, new Rect(RenderSize)); // 투명 영역도 마우스를 받게
        if (_document is null) return;

        var view = View;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.PushTransform(new ScaleTransform(Scale, Scale));
        dc.PushTransform(new TranslateTransform(-view.X, -view.Y));

        if (BaseImage is not null) dc.DrawImage(BaseImage, new Rect(0, 0, BaseImage.PixelWidth, BaseImage.PixelHeight));
        if (_committed is not null) dc.DrawDrawing(_committed);
        if (_moving && SelectedId is { } movingId && _document.Find(movingId) is { } moving)
            AnnotationRenderer.DrawMoving(dc, moving, _moveOffset.X, _moveOffset.Y, Effects);

        var thin = 1 / Scale;
        if (_preview is MosaicAnnotation or BlurAnnotation)
        {
            var r = _preview.Bounds;
            var rect = new Rect(r.X, r.Y, r.Width, r.Height);
            if (_effectPreview is { } fx)
            {
                // 계산이 조금 늦어 이전 영역의 결과일 수 있으니 지금 영역 안만 보여 준다.
                dc.PushClip(new RectangleGeometry(rect));
                dc.DrawImage(fx.Image, fx.Area);
                dc.Pop();
            }
            dc.DrawRectangle(null, new Pen(SelectionBrush, thin) { DashStyle = DashStyles.Dash }, rect);
        }
        else if (_preview is not null)
        {
            AnnotationRenderer.Draw(dc, _preview, Effects);
        }

        if (SelectedId is { } id && _document.Find(id) is { } selected)
        {
            var b = (_moving ? selected.Offset(_moveOffset.X, _moveOffset.Y) : selected).Bounds.Inflate(3 / Scale);
            dc.DrawRectangle(null, new Pen(SelectionBrush, thin) { DashStyle = DashStyles.Dash }, new Rect(b.X, b.Y, b.Width, b.Height));
        }

        var crop = Tool == EditorTool.Crop ? _pendingCrop : null;
        if (crop is { } c)
        {
            var full = new RectangleGeometry(new Rect(0, 0, _document.ImageWidth, _document.ImageHeight));
            var keep = new RectangleGeometry(new Rect(c.X, c.Y, c.Width, c.Height));
            dc.DrawGeometry(CropDimBrush, null, new CombinedGeometry(GeometryCombineMode.Exclude, full, keep));
            dc.DrawRectangle(null, new Pen(Brushes.White, thin) { DashStyle = DashStyles.Dash }, new Rect(c.X, c.Y, c.Width, c.Height));
        }

        dc.Pop();
        dc.Pop();
        dc.Pop();

        // 손잡이·크기 표시는 화면 크기(DIP)로, 캔버스 가장자리에 걸려도 잘리지 않게 그린다.
        if (crop is { } g) DrawCropGrips(dc, g);
    }

    private void DrawCropGrips(DrawingContext dc, RectD crop)
    {
        var topLeft = ToDip(new PointD(crop.X, crop.Y));
        var bottomRight = ToDip(new PointD(crop.Right, crop.Bottom));
        var (l, t, r, b) = (topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        var (cx, cy) = ((l + r) / 2, (t + b) / 2);
        var half = GripSizeDip / 2;
        foreach (var (x, y) in new[] { (l, t), (cx, t), (r, t), (r, cy), (r, b), (cx, b), (l, b), (l, cy) })
            dc.DrawRectangle(Brushes.White, GripPen, new Rect(x - half, y - half, GripSizeDip, GripSizeDip));

        var label = new FormattedText($"{crop.Width:0} × {crop.Height:0}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            LabelFace, 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var labelY = t - label.Height - 8 >= 0 ? t - label.Height - 8 : t + 8; // 위에 자리가 없으면 안쪽에
        var box = new Rect(l + 6, labelY, label.Width + 10, label.Height + 2);
        dc.DrawRoundedRectangle(LabelBackground, null, box, 3, 3);
        dc.DrawText(label, new Point(box.X + 5, box.Y + 1));
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
