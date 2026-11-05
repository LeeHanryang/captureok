using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Editor;

/// <summary>
/// 텍스트 도구의 입력 상자. 캔버스 위 레이어에 TextBox를 띄우고, 끝내면 텍스트 객체를 추가·교체·삭제한다.
/// Enter = 줄바꿈, Esc·Ctrl+Enter·바깥 클릭 = 입력 완료.
/// </summary>
internal sealed class TextEditorOverlay(Canvas layer, AnnotationCanvas canvas)
{
    private TextBox? _box;
    private PointD _position;
    private TextAnnotation? _existing;
    private uint _color;
    private double _fontSize;

    public bool IsActive => _box is not null;

    public void Begin(PointD position, TextAnnotation? existing, uint color, double fontSize)
    {
        Finish();
        _position = existing?.Position ?? position;
        _existing = existing;
        _color = existing?.ColorArgb ?? color;
        _fontSize = existing?.FontSize ?? fontSize;
        canvas.HiddenId = existing?.Id;

        _box = new TextBox
        {
            Style = null, // 앱 전역 컨트롤 스타일(여백·지우기 버튼)을 쓰지 않는다
            Text = existing?.Text ?? "",
            AcceptsReturn = true,
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0x1E, 0x6F, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            MinWidth = 24,
            FontFamily = new FontFamily("Malgun Gothic"),
        };
        ApplyLook();
        // 포커스가 빠지는 것만으로는 끝내지 않는다: 색·글자 크기를 바꾸거나 Alt+Tab 해도 입력이 이어지게.
        // 끝내는 경우는 캔버스·빈 영역 클릭, Esc, Ctrl+Enter, 도구·문서 전환, 저장·복사.
        layer.Children.Add(_box);
        _box.Focus();
        _box.CaretIndex = _box.Text.Length;
    }

    /// <summary>색·크기 옵션을 바꾸면 입력 중인 글자에도 적용한다.</summary>
    public void SetColor(uint argb)
    {
        _color = argb;
        ApplyLook();
        Refocus();
    }

    public void SetFontSize(double size)
    {
        _fontSize = size;
        ApplyLook();
        Refocus();
    }

    /// <summary>요소가 입력 상자(또는 그 안쪽)인지.</summary>
    public bool Contains(DependencyObject? element)
    {
        for (var e = element; e is not null; e = e is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(e) : LogicalTreeHelper.GetParent(e))
            if (ReferenceEquals(e, _box)) return true;
        return false;
    }

    /// <summary>옵션 메뉴·드롭다운이 닫힌 뒤 입력 상자로 포커스를 돌려놓는다.</summary>
    private void Refocus()
    {
        if (_box is { } box) layer.Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_box, box)) box.Focus(); });
    }

    /// <summary>확대율이 바뀌면 위치·글자 크기를 다시 맞춘다.</summary>
    public void ApplyLook()
    {
        if (_box is null) return;
        var dip = canvas.ToDip(_position);
        // TextBox 내부 여백(약 2px)만큼 당겨, 확정 후 그려지는 글자 위치와 맞춘다.
        Canvas.SetLeft(_box, dip.X - 3);
        Canvas.SetTop(_box, dip.Y - 1);
        _box.FontSize = _fontSize * canvas.Scale;
        _box.Foreground = AnnotationRenderer.BrushOf(_color);
        _box.CaretBrush = AnnotationRenderer.BrushOf(_color | 0xFF000000);
    }

    public bool HandleKey(Key key)
    {
        if (_box is null) return false;
        if (key == Key.Escape || (key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            Finish();
            return true;
        }
        return false;
    }

    /// <returns>입력 중이었으면 true</returns>
    public bool Finish()
    {
        if (_box is not { } box) return false;
        _box = null; // 아래에서 포커스가 바뀌며 다시 불려도 한 번만 처리
        var text = box.Text.TrimEnd();
        layer.Children.Remove(box);
        canvas.HiddenId = null;

        var doc = canvas.Document;
        if (doc is null) return true;
        if (_existing is not null)
        {
            if (text.Length == 0)
                doc.Execute(new RemoveAnnotation(_existing.Id));
            else if (text != _existing.Text || _color != _existing.ColorArgb || _fontSize != _existing.FontSize)
                doc.Execute(new ReplaceAnnotation(Make(_existing.Id, text)));
        }
        else if (text.Length > 0)
        {
            doc.Execute(new AddAnnotation(Make(Guid.NewGuid(), text)));
        }
        _existing = null;
        return true;
    }

    private TextAnnotation Make(Guid id, string text)
    {
        var (width, height) = AnnotationRenderer.MeasureText(text, _fontSize);
        return new TextAnnotation(id, _position, text, _color, _fontSize, width, height);
    }
}
