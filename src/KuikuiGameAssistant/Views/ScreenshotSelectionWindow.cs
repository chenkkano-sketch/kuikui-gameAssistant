using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Forms = System.Windows.Forms;

namespace KuikuiGameAssistant.Views;

public sealed class ScreenshotSelectionWindow : Forms.Form
{
    private static readonly Color AccentColor = Color.FromArgb(255, 0, 120, 212);
    private static readonly Color ToolbarBackColor = Color.FromArgb(242, 17, 24, 39);
    private static readonly Color ToolbarItemColor = Color.FromArgb(255, 55, 65, 81);
    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 34, 197, 94),
        Color.FromArgb(255, 239, 68, 68),
        Color.FromArgb(255, 245, 158, 11),
        Color.FromArgb(255, 59, 130, 246),
        Color.FromArgb(255, 168, 85, 247),
        Color.White,
        Color.Black
    };
    private static readonly float[] StrokeWidths = { 2f, 3.5f, 5.5f, 8f };

    private readonly Rectangle _virtualBounds;
    private readonly Bitmap _screenBitmap;
    private readonly List<Annotation> _annotations = new();
    private readonly List<ToolbarButton> _toolbarButtons = new();
    private readonly List<StyleSwatch> _colorSwatches = new();
    private readonly List<ThicknessSwatch> _thicknessSwatches = new();
    private readonly Forms.TextBox _textEditor = new();

    private int _selectedAnnotationIndex = -1;
    private Point _startPoint;
    private Point _currentPoint;
    private Point _annotationStart;
    private Point _annotationCurrent;
    private Point _cursorPoint;
    private Point _resizeStart;
    private Point _annotationEditStart;
    private Rectangle _selectionBeforeResize;
    private Annotation? _annotationBeforeEdit;
    private List<Point>? _activeStroke;
    private Rectangle _selection;
    private RectangleF _styleMenuBounds;
    private bool _isDraggingSelection;
    private bool _isResizingSelection;
    private bool _isAnnotating;
    private bool _isEditingAnnotation;
    private bool _hasSelection;
    private bool _styleMenuVisible;
    private ResizeHandle _activeResizeHandle = ResizeHandle.None;
    private AnnotationEditHandle _activeAnnotationHandle = AnnotationEditHandle.None;
    private AnnotationTool _activeTool = AnnotationTool.Pen;
    private Color _strokeColor = Palette[0];
    private float _strokeWidth = StrokeWidths[1];

    public ScreenshotSelectionWindow()
    {
        _virtualBounds = Forms.SystemInformation.VirtualScreen;
        _screenBitmap = new Bitmap(_virtualBounds.Width, _virtualBounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(_screenBitmap))
        {
            graphics.CopyFromScreen(_virtualBounds.Location, Point.Empty, _virtualBounds.Size);
        }

        _cursorPoint = ClampToBitmap(new Point(
            Forms.Cursor.Position.X - _virtualBounds.Left,
            Forms.Cursor.Position.Y - _virtualBounds.Top));

        AutoScaleMode = Forms.AutoScaleMode.None;
        Bounds = _virtualBounds;
        Cursor = Forms.Cursors.Cross;
        DoubleBuffered = true;
        FormBorderStyle = Forms.FormBorderStyle.None;
        KeyPreview = true;
        ShowInTaskbar = false;
        StartPosition = Forms.FormStartPosition.Manual;
        TopMost = true;

        _textEditor.Visible = false;
        _textEditor.BorderStyle = Forms.BorderStyle.FixedSingle;
        _textEditor.Font = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Point);
        _textEditor.KeyDown += TextEditor_KeyDown;
        _textEditor.LostFocus += (_, _) => CommitTextEditor();
        Controls.Add(_textEditor);

        SetStyle(Forms.ControlStyles.AllPaintingInWmPaint
                 | Forms.ControlStyles.OptimizedDoubleBuffer
                 | Forms.ControlStyles.UserPaint, true);
    }

    public Rectangle? SelectedBounds { get; private set; }
    public ScreenshotCompletionAction CompletionAction { get; private set; } = ScreenshotCompletionAction.CopyToClipboard;
    private Bitmap? SelectedBitmap { get; set; }

    public Bitmap? TakeSelectedBitmap()
    {
        var bitmap = SelectedBitmap;
        SelectedBitmap = null;
        return bitmap;
    }

    protected override void OnPaint(Forms.PaintEventArgs e)
    {
        e.Graphics.CompositingQuality = CompositingQuality.HighSpeed;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawImageUnscaled(_screenBitmap, Point.Empty);

        var selection = _isDraggingSelection ? GetClientSelection() : _selection;
        using var dimBrush = new SolidBrush(Color.FromArgb(138, 0, 0, 0));
        if (selection.Width > 0 && selection.Height > 0)
        {
            using var region = new Region(ClientRectangle);
            region.Exclude(selection);
            e.Graphics.FillRegion(dimBrush, region);

            var state = e.Graphics.Save();
            e.Graphics.SetClip(selection);
            DrawAnnotations(e.Graphics);
            DrawActiveAnnotation(e.Graphics);
            DrawSelectedAnnotationAdorners(e.Graphics);
            e.Graphics.Restore(state);

            DrawSelectionFrame(e.Graphics, selection);
            DrawSizeLabel(e.Graphics, selection);

            if (_hasSelection && !_isDraggingSelection)
            {
                DrawToolbar(e.Graphics, selection);
                if (_styleMenuVisible)
                {
                    DrawStyleMenu(e.Graphics, selection);
                }
            }
        }
        else
        {
            e.Graphics.FillRectangle(dimBrush, ClientRectangle);
            DrawSelectionGuide(e.Graphics, ClientRectangle);
        }

        if (!_textEditor.Visible)
        {
            DrawColorSampler(e.Graphics);
        }
    }

    protected override void OnMouseDown(Forms.MouseEventArgs e)
    {
        _cursorPoint = ClampToBitmap(e.Location);

        if (e.Button == Forms.MouseButtons.Right)
        {
            CancelSelection();
            return;
        }

        if (e.Button != Forms.MouseButtons.Left)
        {
            return;
        }

        if (_textEditor.Visible)
        {
            CommitTextEditor();
        }

        if (_hasSelection && HandleStyleMenuClick(e.Location))
        {
            return;
        }

        if (_hasSelection && BeginResizeIfHit(e.Location))
        {
            return;
        }

        if (_hasSelection && HandleToolbarClick(e.Location))
        {
            return;
        }

        if (_hasSelection && TryBeginAnnotationEdit(e.Location))
        {
            return;
        }

        if (_styleMenuVisible)
        {
            _styleMenuVisible = false;
            Invalidate();
        }

        if (_hasSelection && _selection.Contains(e.Location))
        {
            _selectedAnnotationIndex = -1;
            BeginAnnotation(e.Location);
            return;
        }

        _startPoint = e.Location;
        _currentPoint = e.Location;
        _selection = Rectangle.Empty;
        _hasSelection = false;
        _annotations.Clear();
        _selectedAnnotationIndex = -1;
        _styleMenuVisible = false;
        _isDraggingSelection = true;
        Invalidate();
    }

    protected override void OnMouseMove(Forms.MouseEventArgs e)
    {
        var previousCursorPoint = _cursorPoint;
        _cursorPoint = ClampToBitmap(e.Location);

        if (_isDraggingSelection)
        {
            _currentPoint = e.Location;
            Invalidate();
            return;
        }

        if (_isResizingSelection)
        {
            ResizeSelection(e.Location);
            Invalidate();
            return;
        }

        if (_isEditingAnnotation)
        {
            EditSelectedAnnotation(e.Location);
            Invalidate();
            return;
        }

        if (_isAnnotating)
        {
            _annotationCurrent = ClampToSelection(e.Location);
            _activeStroke?.Add(_annotationCurrent);
            Invalidate();
            return;
        }

        UpdateCursor(e.Location);
        if (previousCursorPoint != _cursorPoint)
        {
            Invalidate();
        }
    }

    protected override void OnMouseUp(Forms.MouseEventArgs e)
    {
        _cursorPoint = ClampToBitmap(e.Location);

        if (e.Button != Forms.MouseButtons.Left)
        {
            return;
        }

        if (_isDraggingSelection)
        {
            _isDraggingSelection = false;
            _currentPoint = e.Location;
            var selection = GetClientSelection();
            if (selection.Width < 12 || selection.Height < 12)
            {
                SelectFullScreen();
                Invalidate();
                return;
            }

            _selection = selection;
            _hasSelection = true;
            Invalidate();
            return;
        }

        if (_isResizingSelection)
        {
            _isResizingSelection = false;
            _activeResizeHandle = ResizeHandle.None;
            Invalidate();
            return;
        }

        if (_isEditingAnnotation)
        {
            _isEditingAnnotation = false;
            _activeAnnotationHandle = AnnotationEditHandle.None;
            _annotationBeforeEdit = null;
            Invalidate();
            return;
        }

        if (_isAnnotating)
        {
            FinishAnnotation();
        }
    }

    protected override void OnMouseDoubleClick(Forms.MouseEventArgs e)
    {
        _cursorPoint = ClampToBitmap(e.Location);
        if (e.Button == Forms.MouseButtons.Left && _hasSelection && _selection.Contains(e.Location))
        {
            _isAnnotating = false;
            _activeStroke = null;
            ConfirmSelection(ScreenshotCompletionAction.CopyToClipboard);
        }
    }

    protected override void OnKeyDown(Forms.KeyEventArgs e)
    {
        if (e.KeyCode == Forms.Keys.Escape)
        {
            DialogResult = Forms.DialogResult.Cancel;
            Close();
        }
        else if (e.KeyCode == Forms.Keys.Enter && _hasSelection)
        {
            ConfirmSelection(ScreenshotCompletionAction.CopyToClipboard);
        }
        else if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Forms.Keys.C && !_textEditor.Visible)
        {
            CopyCurrentColor();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Forms.Keys.Z && _annotations.Count > 0)
        {
            _annotations.RemoveAt(_annotations.Count - 1);
            _selectedAnnotationIndex = Math.Min(_selectedAnnotationIndex, _annotations.Count - 1);
            Invalidate();
        }
        else if ((e.KeyCode == Forms.Keys.Delete || e.KeyCode == Forms.Keys.Back) && _selectedAnnotationIndex >= 0)
        {
            _annotations.RemoveAt(_selectedAnnotationIndex);
            _selectedAnnotationIndex = -1;
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screenBitmap.Dispose();
            _textEditor.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BeginAnnotation(Point point)
    {
        point = ClampToSelection(point);
        if (_activeTool == AnnotationTool.Text)
        {
            ShowTextEditor(point);
            return;
        }

        _annotationStart = point;
        _annotationCurrent = point;
        _activeStroke = _activeTool == AnnotationTool.Pen ? new List<Point> { point } : null;
        _isAnnotating = true;
    }

    private void FinishAnnotation()
    {
        _isAnnotating = false;
        if (_activeTool == AnnotationTool.Pen && _activeStroke is { Count: > 1 } stroke)
        {
            _annotations.Add(new StrokeAnnotation(stroke.ToArray(), _strokeColor, _strokeWidth));
            _selectedAnnotationIndex = _annotations.Count - 1;
        }
        else if (Distance(_annotationStart, _annotationCurrent) > 6)
        {
            if (_activeTool == AnnotationTool.Rectangle)
            {
                _annotations.Add(new RectangleAnnotation(NormalizeRectangle(_annotationStart, _annotationCurrent), _strokeColor, _strokeWidth));
                _selectedAnnotationIndex = _annotations.Count - 1;
            }
            else if (_activeTool == AnnotationTool.Arrow)
            {
                _annotations.Add(new ArrowAnnotation(_annotationStart, _annotationCurrent, _strokeColor, _strokeWidth));
                _selectedAnnotationIndex = _annotations.Count - 1;
            }
        }

        _activeStroke = null;
        Invalidate();
    }

    private bool HandleToolbarClick(Point point)
    {
        var button = _toolbarButtons.FirstOrDefault(x => x.Bounds.Contains(point));
        if (button is null)
        {
            return false;
        }

        switch (button.Action)
        {
            case ToolbarAction.Pen:
            case ToolbarAction.Rectangle:
            case ToolbarAction.Arrow:
            case ToolbarAction.Text:
                var tool = ToAnnotationTool(button.Action);
                var sameTool = _activeTool == tool;
                _activeTool = tool;
                _styleMenuVisible = !sameTool || !_styleMenuVisible;
                break;
            case ToolbarAction.Undo:
                if (_annotations.Count > 0)
                {
                    _annotations.RemoveAt(_annotations.Count - 1);
                    _selectedAnnotationIndex = Math.Min(_selectedAnnotationIndex, _annotations.Count - 1);
                }
                break;
            case ToolbarAction.Cancel:
                CancelSelection();
                break;
            case ToolbarAction.SaveImage:
                ConfirmSelection(ScreenshotCompletionAction.SaveImage);
                break;
            case ToolbarAction.Confirm:
                ConfirmSelection(ScreenshotCompletionAction.CopyToClipboard);
                break;
        }

        Invalidate();
        return true;
    }

    private bool HandleStyleMenuClick(Point point)
    {
        if (!_styleMenuVisible)
        {
            return false;
        }

        foreach (var swatch in _colorSwatches)
        {
            if (!swatch.Bounds.Contains(point))
            {
                continue;
            }

            _strokeColor = swatch.Color;
            Invalidate();
            return true;
        }

        foreach (var swatch in _thicknessSwatches)
        {
            if (!swatch.Bounds.Contains(point))
            {
                continue;
            }

            _strokeWidth = swatch.Width;
            Invalidate();
            return true;
        }

        if (_styleMenuBounds.Contains(point))
        {
            return true;
        }

        return false;
    }

    private bool BeginResizeIfHit(Point point)
    {
        var handle = HitResizeHandle(point);
        if (handle == ResizeHandle.None)
        {
            return false;
        }

        _styleMenuVisible = false;
        _activeResizeHandle = handle;
        _resizeStart = point;
        _selectionBeforeResize = _selection;
        _isResizingSelection = true;
        return true;
    }

    private void ResizeSelection(Point point)
    {
        const int minSize = 24;
        var left = _selectionBeforeResize.Left;
        var top = _selectionBeforeResize.Top;
        var right = _selectionBeforeResize.Right;
        var bottom = _selectionBeforeResize.Bottom;

        if (_activeResizeHandle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
        {
            left = Math.Clamp(point.X, ClientRectangle.Left, right - minSize);
        }

        if (_activeResizeHandle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight)
        {
            right = Math.Clamp(point.X, left + minSize, ClientRectangle.Right);
        }

        if (_activeResizeHandle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
        {
            top = Math.Clamp(point.Y, ClientRectangle.Top, bottom - minSize);
        }

        if (_activeResizeHandle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight)
        {
            bottom = Math.Clamp(point.Y, top + minSize, ClientRectangle.Bottom);
        }

        _selection = Rectangle.FromLTRB(left, top, right, bottom);
    }

    private bool TryBeginAnnotationEdit(Point point)
    {
        var hit = HitAnnotation(point);
        if (hit is null)
        {
            return false;
        }

        _selectedAnnotationIndex = hit.Index;
        _activeAnnotationHandle = hit.Handle;
        _annotationBeforeEdit = _annotations[_selectedAnnotationIndex];
        _annotationEditStart = point;
        _isEditingAnnotation = true;
        _styleMenuVisible = false;
        ApplySelectedAnnotationStyle(_annotationBeforeEdit);
        Invalidate();
        return true;
    }

    private AnnotationHit? HitAnnotation(Point point)
    {
        for (var i = _annotations.Count - 1; i >= 0; i--)
        {
            var annotation = _annotations[i];
            if (annotation is ArrowAnnotation arrow)
            {
                if (HandleCircle(arrow.Start, 18).Contains(point))
                {
                    return new AnnotationHit(i, AnnotationEditHandle.ArrowStart);
                }

                if (HandleCircle(arrow.End, 18).Contains(point))
                {
                    return new AnnotationHit(i, AnnotationEditHandle.ArrowEnd);
                }

                if (DistanceToSegment(point, arrow.Start, arrow.End) <= Math.Max(7, arrow.Width + 4))
                {
                    return new AnnotationHit(i, AnnotationEditHandle.Move);
                }

                continue;
            }

            var bounds = AnnotationBounds(annotation);
            foreach (var handle in BuildAnnotationHandleRects(bounds, 16))
            {
                if (handle.Bounds.Contains(point))
                {
                    return new AnnotationHit(i, handle.Handle);
                }
            }

            var hitBounds = RectangleF.Inflate(bounds, 8, 8);
            if (hitBounds.Contains(point))
            {
                return new AnnotationHit(i, AnnotationEditHandle.Move);
            }
        }

        return null;
    }

    private void EditSelectedAnnotation(Point point)
    {
        if (_selectedAnnotationIndex < 0 || _annotationBeforeEdit is null)
        {
            return;
        }

        point = ClampToSelection(point);
        var offset = new Size(point.X - _annotationEditStart.X, point.Y - _annotationEditStart.Y);
        var updated = _annotationBeforeEdit switch
        {
            RectangleAnnotation rectangle => EditRectangleAnnotation(rectangle, point, offset),
            ArrowAnnotation arrow => EditArrowAnnotation(arrow, point, offset),
            StrokeAnnotation stroke => EditStrokeAnnotation(stroke, point, offset),
            TextAnnotation text => EditTextAnnotation(text, point, offset),
            _ => _annotationBeforeEdit
        };

        _annotations[_selectedAnnotationIndex] = updated;
    }

    private Annotation EditRectangleAnnotation(RectangleAnnotation rectangle, Point point, Size offset)
    {
        if (_activeAnnotationHandle == AnnotationEditHandle.Move)
        {
            return rectangle with { Bounds = OffsetRectangle(rectangle.Bounds, offset) };
        }

        return rectangle with { Bounds = ResizeRectangle(rectangle.Bounds, point, _activeAnnotationHandle, 10) };
    }

    private Annotation EditArrowAnnotation(ArrowAnnotation arrow, Point point, Size offset)
    {
        return _activeAnnotationHandle switch
        {
            AnnotationEditHandle.ArrowStart => arrow with { Start = point },
            AnnotationEditHandle.ArrowEnd => arrow with { End = point },
            _ => arrow with { Start = arrow.Start + offset, End = arrow.End + offset }
        };
    }

    private Annotation EditStrokeAnnotation(StrokeAnnotation stroke, Point point, Size offset)
    {
        if (_activeAnnotationHandle == AnnotationEditHandle.Move)
        {
            return stroke with { Points = stroke.Points.Select(x => x + offset).ToArray() };
        }

        var oldBounds = AnnotationBounds(stroke);
        var newBounds = ResizeRectangleF(oldBounds, point, _activeAnnotationHandle, 12);
        return stroke with { Points = ScalePoints(stroke.Points, oldBounds, newBounds) };
    }

    private Annotation EditTextAnnotation(TextAnnotation text, Point point, Size offset)
    {
        if (_activeAnnotationHandle == AnnotationEditHandle.Move)
        {
            return text with { Location = text.Location + offset };
        }

        var oldBounds = AnnotationBounds(text);
        var newBounds = ResizeRectangleF(oldBounds, point, _activeAnnotationHandle, 20);
        var scale = Math.Max(newBounds.Width / Math.Max(1, oldBounds.Width), newBounds.Height / Math.Max(1, oldBounds.Height));
        return text with
        {
            Location = new Point((int)newBounds.Left, (int)newBounds.Top),
            FontSize = Math.Clamp(text.FontSize * scale, 8f, 48f)
        };
    }

    private void ApplySelectedAnnotationStyle(Annotation annotation)
    {
        switch (annotation)
        {
            case StrokeAnnotation stroke:
                _strokeColor = stroke.Color;
                _strokeWidth = stroke.Width;
                break;
            case RectangleAnnotation rectangle:
                _strokeColor = rectangle.Color;
                _strokeWidth = rectangle.Width;
                break;
            case ArrowAnnotation arrow:
                _strokeColor = arrow.Color;
                _strokeWidth = arrow.Width;
                break;
            case TextAnnotation text:
                _strokeColor = text.Color;
                break;
        }
    }

    private void ConfirmSelection(ScreenshotCompletionAction action)
    {
        if (_textEditor.Visible)
        {
            CommitTextEditor();
        }

        if (!_hasSelection || _selection.Width <= 0 || _selection.Height <= 0)
        {
            return;
        }

        SelectedBounds = new Rectangle(
            _virtualBounds.Left + _selection.Left,
            _virtualBounds.Top + _selection.Top,
            _selection.Width,
            _selection.Height);
        SelectedBitmap = BuildSelectedBitmap();
        CompletionAction = action;
        DialogResult = Forms.DialogResult.OK;
        Close();
    }

    private Bitmap BuildSelectedBitmap()
    {
        var bitmap = new Bitmap(_selection.Width, _selection.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.DrawImage(_screenBitmap, new Rectangle(0, 0, _selection.Width, _selection.Height), _selection, GraphicsUnit.Pixel);
        graphics.TranslateTransform(-_selection.Left, -_selection.Top);
        DrawAnnotations(graphics);
        return bitmap;
    }

    private Rectangle GetClientSelection()
    {
        return Rectangle.Intersect(NormalizeRectangle(_startPoint, _currentPoint), ClientRectangle);
    }

    private Point ClampToSelection(Point point)
    {
        return new Point(
            Math.Clamp(point.X, _selection.Left, _selection.Right),
            Math.Clamp(point.Y, _selection.Top, _selection.Bottom));
    }

    private ResizeHandle HitResizeHandle(Point point)
    {
        var hit = BuildHandleRects(_selection, hitSize: 16)
            .FirstOrDefault(x => x.Bounds.Contains(point));
        return hit?.Handle ?? ResizeHandle.None;
    }

    private void UpdateCursor(Point point)
    {
        if (!_hasSelection)
        {
            Cursor = Forms.Cursors.Cross;
            return;
        }

        Cursor = HitResizeHandle(point) switch
        {
            ResizeHandle.TopLeft or ResizeHandle.BottomRight => Forms.Cursors.SizeNWSE,
            ResizeHandle.TopRight or ResizeHandle.BottomLeft => Forms.Cursors.SizeNESW,
            ResizeHandle.Left or ResizeHandle.Right => Forms.Cursors.SizeWE,
            ResizeHandle.Top or ResizeHandle.Bottom => Forms.Cursors.SizeNS,
            _ when _toolbarButtons.Any(x => x.Bounds.Contains(point))
                   || (_styleMenuVisible && _styleMenuBounds.Contains(point)) => Forms.Cursors.Hand,
            _ when _selection.Contains(point) && _activeTool == AnnotationTool.Text => Forms.Cursors.IBeam,
            _ when _selection.Contains(point) => Forms.Cursors.Cross,
            _ => Forms.Cursors.Cross
        };
    }

    private static Rectangle NormalizeRectangle(Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static double Distance(Point start, Point end)
    {
        var dx = start.X - end.X;
        var dy = start.Y - end.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void SelectFullScreen()
    {
        _selection = new Rectangle(0, 0, ClientRectangle.Width, ClientRectangle.Height);
        _hasSelection = true;
        _annotations.Clear();
        _selectedAnnotationIndex = -1;
        _styleMenuVisible = false;
    }

    private void CancelSelection()
    {
        DialogResult = Forms.DialogResult.Cancel;
        Close();
    }

    private void CopyCurrentColor()
    {
        try
        {
            Forms.Clipboard.SetText(ColorToHex(ReadScreenPixel(_cursorPoint)));
        }
        catch
        {
            // Clipboard access can fail when another process owns it; the picker keeps running.
        }
    }

    private Point ClampToBitmap(Point point)
    {
        return new Point(
            Math.Clamp(point.X, 0, Math.Max(0, _screenBitmap.Width - 1)),
            Math.Clamp(point.Y, 0, Math.Max(0, _screenBitmap.Height - 1)));
    }

    private Color ReadScreenPixel(Point point)
    {
        point = ClampToBitmap(point);
        return _screenBitmap.GetPixel(point.X, point.Y);
    }

    private static string ColorToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static float ClampFloat(float value, float min, float max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Clamp(value, min, max);
    }

    private static Rectangle OffsetRectangle(Rectangle rectangle, Size offset)
    {
        return new Rectangle(rectangle.Location + offset, rectangle.Size);
    }

    private static Rectangle ResizeRectangle(Rectangle rectangle, Point point, AnnotationEditHandle handle, int minSize)
    {
        var left = rectangle.Left;
        var top = rectangle.Top;
        var right = rectangle.Right;
        var bottom = rectangle.Bottom;

        if (handle is AnnotationEditHandle.Left or AnnotationEditHandle.TopLeft or AnnotationEditHandle.BottomLeft)
        {
            left = Math.Min(point.X, right - minSize);
        }

        if (handle is AnnotationEditHandle.Right or AnnotationEditHandle.TopRight or AnnotationEditHandle.BottomRight)
        {
            right = Math.Max(point.X, left + minSize);
        }

        if (handle is AnnotationEditHandle.Top or AnnotationEditHandle.TopLeft or AnnotationEditHandle.TopRight)
        {
            top = Math.Min(point.Y, bottom - minSize);
        }

        if (handle is AnnotationEditHandle.Bottom or AnnotationEditHandle.BottomLeft or AnnotationEditHandle.BottomRight)
        {
            bottom = Math.Max(point.Y, top + minSize);
        }

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static RectangleF ResizeRectangleF(RectangleF rectangle, Point point, AnnotationEditHandle handle, int minSize)
    {
        var left = rectangle.Left;
        var top = rectangle.Top;
        var right = rectangle.Right;
        var bottom = rectangle.Bottom;

        if (handle is AnnotationEditHandle.Left or AnnotationEditHandle.TopLeft or AnnotationEditHandle.BottomLeft)
        {
            left = Math.Min(point.X, right - minSize);
        }

        if (handle is AnnotationEditHandle.Right or AnnotationEditHandle.TopRight or AnnotationEditHandle.BottomRight)
        {
            right = Math.Max(point.X, left + minSize);
        }

        if (handle is AnnotationEditHandle.Top or AnnotationEditHandle.TopLeft or AnnotationEditHandle.TopRight)
        {
            top = Math.Min(point.Y, bottom - minSize);
        }

        if (handle is AnnotationEditHandle.Bottom or AnnotationEditHandle.BottomLeft or AnnotationEditHandle.BottomRight)
        {
            bottom = Math.Max(point.Y, top + minSize);
        }

        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static Point[] ScalePoints(Point[] points, RectangleF oldBounds, RectangleF newBounds)
    {
        if (oldBounds.Width <= 0.1f || oldBounds.Height <= 0.1f)
        {
            return points.ToArray();
        }

        return points
            .Select(point =>
            {
                var xRatio = (point.X - oldBounds.Left) / oldBounds.Width;
                var yRatio = (point.Y - oldBounds.Top) / oldBounds.Height;
                return new Point(
                    (int)Math.Round(newBounds.Left + xRatio * newBounds.Width),
                    (int)Math.Round(newBounds.Top + yRatio * newBounds.Height));
            })
            .ToArray();
    }

    private static RectangleF AnnotationBounds(Annotation annotation)
    {
        switch (annotation)
        {
            case StrokeAnnotation stroke when stroke.Points.Length > 0:
                var minX = stroke.Points.Min(point => point.X);
                var minY = stroke.Points.Min(point => point.Y);
                var maxX = stroke.Points.Max(point => point.X);
                var maxY = stroke.Points.Max(point => point.Y);
                return RectangleF.FromLTRB(minX, minY, maxX, maxY);
            case RectangleAnnotation rectangle:
                return rectangle.Bounds;
            case ArrowAnnotation arrow:
                return RectangleF.FromLTRB(
                    Math.Min(arrow.Start.X, arrow.End.X),
                    Math.Min(arrow.Start.Y, arrow.End.Y),
                    Math.Max(arrow.Start.X, arrow.End.X),
                    Math.Max(arrow.Start.Y, arrow.End.Y));
            case TextAnnotation text:
                using (var font = new Font("Segoe UI", text.FontSize, FontStyle.Bold, GraphicsUnit.Point))
                {
                    var size = Forms.TextRenderer.MeasureText(text.Text, font);
                    return new RectangleF(text.Location.X, text.Location.Y, size.Width + 12, size.Height + 8);
                }
            default:
                return RectangleF.Empty;
        }
    }

    private static IEnumerable<AnnotationHandleRect> BuildAnnotationHandleRects(RectangleF bounds, int hitSize)
    {
        if (bounds.IsEmpty)
        {
            yield break;
        }

        var half = hitSize / 2f;
        var cx = bounds.Left + bounds.Width / 2f;
        var cy = bounds.Top + bounds.Height / 2f;
        var points = new (AnnotationEditHandle Handle, float X, float Y)[]
        {
            (AnnotationEditHandle.TopLeft, bounds.Left, bounds.Top),
            (AnnotationEditHandle.Top, cx, bounds.Top),
            (AnnotationEditHandle.TopRight, bounds.Right, bounds.Top),
            (AnnotationEditHandle.Right, bounds.Right, cy),
            (AnnotationEditHandle.BottomRight, bounds.Right, bounds.Bottom),
            (AnnotationEditHandle.Bottom, cx, bounds.Bottom),
            (AnnotationEditHandle.BottomLeft, bounds.Left, bounds.Bottom),
            (AnnotationEditHandle.Left, bounds.Left, cy)
        };

        foreach (var (handle, x, y) in points)
        {
            yield return new AnnotationHandleRect(handle, new RectangleF(x - half, y - half, hitSize, hitSize));
        }
    }

    private static RectangleF HandleCircle(Point point, float diameter)
    {
        var radius = diameter / 2f;
        return new RectangleF(point.X - radius, point.Y - radius, diameter, diameter);
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx == 0 && dy == 0)
        {
            return Distance(point, start);
        }

        var t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / (double)(dx * dx + dy * dy);
        t = Math.Clamp(t, 0d, 1d);
        var closest = new Point(
            (int)Math.Round(start.X + t * dx),
            (int)Math.Round(start.Y + t * dy));
        return Distance(point, closest);
    }

    private void ShowTextEditor(Point point)
    {
        var width = Math.Min(260, Math.Max(130, _selection.Right - point.X - 8));
        var bounds = new Rectangle(point.X, point.Y, width, 34);
        _textEditor.Bounds = bounds;
        _textEditor.Text = string.Empty;
        _textEditor.ForeColor = _strokeColor;
        _textEditor.BackColor = Color.FromArgb(32, 32, 32);
        _textEditor.Visible = true;
        _textEditor.Focus();
    }

    private void TextEditor_KeyDown(object? sender, Forms.KeyEventArgs e)
    {
        if (e.KeyCode == Forms.Keys.Enter)
        {
            CommitTextEditor();
            e.Handled = true;
        }
        else if (e.KeyCode == Forms.Keys.Escape)
        {
            _textEditor.Visible = false;
            e.Handled = true;
        }
    }

    private void CommitTextEditor()
    {
        if (!_textEditor.Visible)
        {
            return;
        }

        var text = _textEditor.Text.Trim();
        var location = _textEditor.Location;
        _textEditor.Visible = false;
        if (!string.IsNullOrWhiteSpace(text))
        {
            _annotations.Add(new TextAnnotation(location, text, _strokeColor, 15f));
            _selectedAnnotationIndex = _annotations.Count - 1;
            Invalidate();
        }
    }

    private void DrawAnnotations(Graphics graphics)
    {
        foreach (var annotation in _annotations)
        {
            DrawAnnotation(graphics, annotation, preview: false);
        }
    }

    private void DrawActiveAnnotation(Graphics graphics)
    {
        if (!_isAnnotating)
        {
            return;
        }

        if (_activeTool == AnnotationTool.Pen && _activeStroke is { Count: > 1 } stroke)
        {
            DrawAnnotation(graphics, new StrokeAnnotation(stroke.ToArray(), _strokeColor, _strokeWidth), preview: true);
        }
        else if (_activeTool == AnnotationTool.Rectangle)
        {
            DrawAnnotation(graphics, new RectangleAnnotation(NormalizeRectangle(_annotationStart, _annotationCurrent), _strokeColor, _strokeWidth), preview: true);
        }
        else if (_activeTool == AnnotationTool.Arrow)
        {
            DrawAnnotation(graphics, new ArrowAnnotation(_annotationStart, _annotationCurrent, _strokeColor, _strokeWidth), preview: true);
        }
    }

    private static void DrawAnnotation(Graphics graphics, Annotation annotation, bool preview)
    {
        var alpha = preview ? 190 : 255;
        switch (annotation)
        {
            case StrokeAnnotation stroke when stroke.Points.Length > 1:
                using (var pen = CreateAnnotationPen(Color.FromArgb(alpha, stroke.Color), stroke.Width, false))
                {
                    graphics.DrawLines(pen, stroke.Points);
                }
                break;
            case RectangleAnnotation rectangle:
                using (var pen = CreateAnnotationPen(Color.FromArgb(alpha, rectangle.Color), rectangle.Width, false))
                {
                    graphics.DrawRectangle(pen, rectangle.Bounds);
                }
                break;
            case ArrowAnnotation arrow:
                using (var pen = CreateAnnotationPen(Color.FromArgb(alpha, arrow.Color), arrow.Width, true))
                {
                    graphics.DrawLine(pen, arrow.Start, arrow.End);
                }
                break;
            case TextAnnotation text:
                using (var font = new Font("Segoe UI", text.FontSize, FontStyle.Bold, GraphicsUnit.Point))
                using (var back = new SolidBrush(Color.FromArgb(210, 17, 24, 39)))
                using (var fore = new SolidBrush(text.Color))
                {
                    var size = graphics.MeasureString(text.Text, font);
                    var rect = new RectangleF(text.Location.X, text.Location.Y, size.Width + 12, size.Height + 8);
                    graphics.FillRoundedRectangle(back, rect, 6);
                    graphics.DrawString(text.Text, font, fore, rect.Left + 6, rect.Top + 4);
                }
                break;
        }
    }

    private void DrawSelectedAnnotationAdorners(Graphics graphics)
    {
        if (_selectedAnnotationIndex < 0 || _selectedAnnotationIndex >= _annotations.Count)
        {
            return;
        }

        var annotation = _annotations[_selectedAnnotationIndex];
        using var outline = new Pen(Color.FromArgb(220, AccentColor), 1.6f) { DashStyle = DashStyle.Dash };
        using var fill = new SolidBrush(Color.White);
        using var edge = new Pen(AccentColor, 1.4f);

        if (annotation is ArrowAnnotation arrow)
        {
            graphics.DrawLine(outline, arrow.Start, arrow.End);
            foreach (var handle in new[] { HandleCircle(arrow.Start, 12), HandleCircle(arrow.End, 12) })
            {
                graphics.FillEllipse(fill, handle);
                graphics.DrawEllipse(edge, handle);
            }

            return;
        }

        var bounds = AnnotationBounds(annotation);
        if (bounds.IsEmpty)
        {
            return;
        }

        graphics.DrawRoundedRectangle(outline, bounds, 4);
        foreach (var handle in BuildAnnotationHandleRects(bounds, 10))
        {
            graphics.FillEllipse(fill, handle.Bounds);
            graphics.DrawEllipse(edge, handle.Bounds);
        }
    }

    private static Pen CreateAnnotationPen(Color color, float width, bool arrow)
    {
        var pen = new Pen(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        if (arrow)
        {
            pen.CustomEndCap = new AdjustableArrowCap(Math.Max(4, width * 1.55f), Math.Max(5, width * 1.9f), true);
        }

        return pen;
    }

    private static void DrawSelectionFrame(Graphics graphics, Rectangle selection)
    {
        using var glowPen = new Pen(Color.FromArgb(110, AccentColor), 6f);
        using var borderPen = new Pen(AccentColor, 2.4f);
        var frame = Rectangle.FromLTRB(selection.Left, selection.Top, Math.Max(selection.Left, selection.Right - 1), Math.Max(selection.Top, selection.Bottom - 1));
        graphics.DrawRectangle(glowPen, frame);
        graphics.DrawRectangle(borderPen, frame);
        foreach (var (_, bounds) in BuildHandleRects(selection, hitSize: 12))
        {
            using var fill = new SolidBrush(Color.White);
            using var edge = new Pen(AccentColor, 1.5f);
            graphics.FillEllipse(fill, bounds);
            graphics.DrawEllipse(edge, bounds);
        }
    }

    private static IEnumerable<HandleRect> BuildHandleRects(Rectangle selection, int hitSize)
    {
        if (selection.IsEmpty)
        {
            yield break;
        }

        var half = hitSize / 2f;
        var cx = selection.Left + selection.Width / 2f;
        var cy = selection.Top + selection.Height / 2f;
        var points = new (ResizeHandle Handle, float X, float Y)[]
        {
            (ResizeHandle.TopLeft, selection.Left, selection.Top),
            (ResizeHandle.Top, cx, selection.Top),
            (ResizeHandle.TopRight, selection.Right, selection.Top),
            (ResizeHandle.Right, selection.Right, cy),
            (ResizeHandle.BottomRight, selection.Right, selection.Bottom),
            (ResizeHandle.Bottom, cx, selection.Bottom),
            (ResizeHandle.BottomLeft, selection.Left, selection.Bottom),
            (ResizeHandle.Left, selection.Left, cy)
        };

        foreach (var (handle, x, y) in points)
        {
            yield return new HandleRect(handle, new RectangleF(x - half, y - half, hitSize, hitSize));
        }
    }

    private static void DrawSizeLabel(Graphics graphics, Rectangle selection)
    {
        var label = $"{selection.Width} x {selection.Height}";
        using var labelFont = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Point);
        var labelSize = graphics.MeasureString(label, labelFont);
        var labelRect = new RectangleF(selection.Left + 8, selection.Top + 8, labelSize.Width + 16, labelSize.Height + 7);
        using var labelBack = new SolidBrush(Color.FromArgb(238, 7, 18, 34));
        using var labelText = new SolidBrush(Color.White);
        using var labelEdge = new Pen(Color.FromArgb(135, AccentColor), 1.2f);
        graphics.FillRoundedRectangle(labelBack, labelRect, 7);
        graphics.DrawRoundedRectangle(labelEdge, labelRect, 7);
        graphics.DrawString(label, labelFont, labelText, labelRect.Left + 8, labelRect.Top + 3);
    }

    private void DrawToolbar(Graphics graphics, Rectangle selection)
    {
        _toolbarButtons.Clear();
        var toolSpecs = new[]
        {
            new ToolbarSpec(ToolbarAction.Pen, _activeTool == AnnotationTool.Pen, false, true),
            new ToolbarSpec(ToolbarAction.Rectangle, _activeTool == AnnotationTool.Rectangle, false, true),
            new ToolbarSpec(ToolbarAction.Arrow, _activeTool == AnnotationTool.Arrow, false, true),
            new ToolbarSpec(ToolbarAction.Text, _activeTool == AnnotationTool.Text, false, true)
        };
        var actionSpecs = new[]
        {
            new ToolbarSpec(ToolbarAction.Undo, false, false, false),
            new ToolbarSpec(ToolbarAction.SaveImage, false, false, false),
            new ToolbarSpec(ToolbarAction.Cancel, false, false, false),
            new ToolbarSpec(ToolbarAction.Confirm, false, true, false)
        };

        const int buttonSize = 44;
        const int gap = 8;
        const int padding = 10;
        const int groupGap = 22;
        var toolWidth = ToolbarGroupWidth(toolSpecs.Length, buttonSize, gap, padding);
        var actionWidth = ToolbarGroupWidth(actionSpecs.Length, buttonSize, gap, padding);
        var toolbarHeight = buttonSize + padding * 2;
        var top = selection.Bottom + 12;
        if (top + toolbarHeight > ClientRectangle.Height - 8)
        {
            top = Math.Max(8, selection.Top - toolbarHeight - 12);
        }

        var toolLeft = ClampFloat(selection.Left, 8, ClientRectangle.Width - toolWidth - 8);
        var actionLeft = ClampFloat(selection.Right - actionWidth, 8, ClientRectangle.Width - actionWidth - 8);
        if (actionLeft < toolLeft + toolWidth + groupGap)
        {
            var combinedWidth = toolWidth + groupGap + actionWidth;
            toolLeft = ClampFloat(selection.Left + selection.Width / 2f - combinedWidth / 2f, 8, ClientRectangle.Width - combinedWidth - 8);
            actionLeft = toolLeft + toolWidth + groupGap;
        }

        DrawToolbarGroup(graphics, new RectangleF(toolLeft, top, toolWidth, toolbarHeight), toolSpecs, buttonSize, gap, padding);
        DrawToolbarGroup(graphics, new RectangleF(actionLeft, top, actionWidth, toolbarHeight), actionSpecs, buttonSize, gap, padding);
    }

    private void DrawToolbarGroup(Graphics graphics, RectangleF groupBounds, IReadOnlyList<ToolbarSpec> specs, int buttonSize, int gap, int padding)
    {
        using var toolbarBack = new SolidBrush(ToolbarBackColor);
        graphics.FillRoundedRectangle(toolbarBack, groupBounds, 14);
        using var border = new Pen(Color.FromArgb(55, 255, 255, 255));
        graphics.DrawRoundedRectangle(border, groupBounds, 14);

        var x = groupBounds.Left + padding;
        foreach (var spec in specs)
        {
            var bounds = new RectangleF(x, groupBounds.Top + padding, buttonSize, buttonSize);
            _toolbarButtons.Add(new ToolbarButton(spec.Action, bounds));
            DrawToolbarButton(graphics, bounds, spec);
            x += buttonSize + gap;
        }
    }

    private static int ToolbarGroupWidth(int buttonCount, int buttonSize, int gap, int padding)
    {
        return buttonCount * buttonSize + Math.Max(0, buttonCount - 1) * gap + padding * 2;
    }

    private void DrawToolbarButton(Graphics graphics, RectangleF bounds, ToolbarSpec spec)
    {
        var backColor = spec.Primary
            ? AccentColor
            : spec.Active
                ? Color.FromArgb(255, 22, 163, 74)
                : ToolbarItemColor;
        using var back = new SolidBrush(backColor);
        graphics.FillRoundedRectangle(back, bounds, 9);

        using var iconPen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var iconBrush = new SolidBrush(Color.White);
        DrawIcon(graphics, bounds, spec.Action, iconPen, iconBrush);

        if (spec.HasStyle)
        {
            var chevronY = bounds.Bottom - 6;
            graphics.DrawLines(iconPen, new[]
            {
                new PointF(bounds.Right - 12, chevronY - 2),
                new PointF(bounds.Right - 9, chevronY + 1),
                new PointF(bounds.Right - 6, chevronY - 2)
            });
        }
    }

    private static void DrawIcon(Graphics graphics, RectangleF bounds, ToolbarAction action, Pen pen, Brush brush)
    {
        var cx = bounds.Left + bounds.Width / 2f;
        var cy = bounds.Top + bounds.Height / 2f;
        switch (action)
        {
            case ToolbarAction.Pen:
                graphics.DrawLine(pen, bounds.Left + 11, bounds.Bottom - 10, bounds.Right - 9, bounds.Top + 10);
                graphics.FillEllipse(brush, bounds.Left + 9, bounds.Bottom - 11, 5, 5);
                break;
            case ToolbarAction.Rectangle:
                graphics.DrawRectangle(pen, bounds.Left + 9, bounds.Top + 10, bounds.Width - 18, bounds.Height - 19);
                break;
            case ToolbarAction.Arrow:
                using (var arrowCap = new AdjustableArrowCap(4, 5, true))
                {
                    pen.CustomEndCap = arrowCap;
                    graphics.DrawLine(pen, bounds.Left + 10, bounds.Bottom - 10, bounds.Right - 9, bounds.Top + 10);
                }
                break;
            case ToolbarAction.Text:
                using (var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Point))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    graphics.DrawString("T", font, brush, bounds, format);
                }
                break;
            case ToolbarAction.Undo:
                graphics.DrawArc(pen, bounds.Left + 9, bounds.Top + 10, 18, 16, 30, 260);
                graphics.DrawLines(pen, new[]
                {
                    new PointF(bounds.Left + 11, bounds.Top + 15),
                    new PointF(bounds.Left + 8, bounds.Top + 9),
                    new PointF(bounds.Left + 15, bounds.Top + 10)
                });
                break;
            case ToolbarAction.SaveImage:
                graphics.DrawRectangle(pen, bounds.Left + 11, bounds.Top + 9, bounds.Width - 22, bounds.Height - 16);
                graphics.DrawLine(pen, bounds.Left + 16, bounds.Top + 9, bounds.Left + 16, bounds.Top + 18);
                graphics.DrawLine(pen, bounds.Right - 16, bounds.Top + 9, bounds.Right - 16, bounds.Top + 18);
                graphics.DrawLine(pen, bounds.Left + 17, bounds.Bottom - 15, bounds.Right - 17, bounds.Bottom - 15);
                graphics.FillRectangle(brush, bounds.Left + 17, bounds.Top + 15, bounds.Width - 34, 4);
                break;
            case ToolbarAction.Cancel:
                graphics.DrawLine(pen, bounds.Left + 11, bounds.Top + 11, bounds.Right - 11, bounds.Bottom - 11);
                graphics.DrawLine(pen, bounds.Right - 11, bounds.Top + 11, bounds.Left + 11, bounds.Bottom - 11);
                break;
            case ToolbarAction.Confirm:
                graphics.DrawLines(pen, new[]
                {
                    new PointF(bounds.Left + 9, cy),
                    new PointF(cx - 2, bounds.Bottom - 10),
                    new PointF(bounds.Right - 8, bounds.Top + 10)
                });
                break;
        }
    }

    private void DrawStyleMenu(Graphics graphics, Rectangle selection)
    {
        _colorSwatches.Clear();
        _thicknessSwatches.Clear();

        var toolbar = _toolbarButtons.FirstOrDefault(x => x.Action == ToToolbarAction(_activeTool))?.Bounds ?? RectangleF.Empty;
        const int width = 250;
        const int height = 82;
        var left = toolbar.IsEmpty ? selection.Left : toolbar.Left;
        left = Math.Min(Math.Max(left, 8), ClientRectangle.Width - width - 8);
        var top = toolbar.IsEmpty ? selection.Bottom + 58 : toolbar.Bottom + 8;
        if (top + height > ClientRectangle.Height - 8)
        {
            top = toolbar.Top - height - 8;
        }

        _styleMenuBounds = new RectangleF(left, top, width, height);
        using var back = new SolidBrush(Color.FromArgb(245, 17, 24, 39));
        graphics.FillRoundedRectangle(back, _styleMenuBounds, 12);
        using var border = new Pen(Color.FromArgb(55, 255, 255, 255));
        graphics.DrawRoundedRectangle(border, _styleMenuBounds, 12);

        var x = left + 14;
        var y = top + 15;
        foreach (var color in Palette)
        {
            var bounds = new RectangleF(x, y, 22, 22);
            _colorSwatches.Add(new StyleSwatch(color, bounds));
            using var fill = new SolidBrush(color);
            graphics.FillEllipse(fill, bounds);
            using var edge = new Pen(Color.FromArgb(color.ToArgb() == _strokeColor.ToArgb() ? 255 : 95, 255, 255, 255), color.ToArgb() == _strokeColor.ToArgb() ? 2.3f : 1f);
            graphics.DrawEllipse(edge, bounds);
            x += 31;
        }

        x = left + 16;
        y = top + 52;
        foreach (var widthOption in StrokeWidths)
        {
            var bounds = new RectangleF(x, y - 11, 42, 22);
            _thicknessSwatches.Add(new ThicknessSwatch(widthOption, bounds));
            using var linePen = new Pen(Color.White, widthOption) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(linePen, bounds.Left + 6, y, bounds.Right - 6, y);
            if (Math.Abs(widthOption - _strokeWidth) < 0.1f)
            {
                using var selected = new Pen(AccentColor, 2f);
                graphics.DrawRoundedRectangle(selected, bounds, 7);
            }

            x += 52;
        }
    }

    private static void DrawSelectionGuide(Graphics graphics, Rectangle bounds)
    {
        var frame = Rectangle.Inflate(bounds, -4, -4);
        using var glow = new Pen(Color.FromArgb(80, AccentColor), 7f);
        using var border = new Pen(AccentColor, 2.5f);
        graphics.DrawRectangle(glow, frame);
        graphics.DrawRectangle(border, frame);
        DrawSizeLabel(graphics, bounds);
    }

    private void DrawColorSampler(Graphics graphics)
    {
        var point = ClampToBitmap(_cursorPoint);
        var color = ReadScreenPixel(point);
        var hex = ColorToHex(color);
        const int panelWidth = 154;
        const int previewSize = 112;
        const int footerHeight = 43;
        const int padding = 9;
        const int panelHeight = previewSize + footerHeight + padding * 3;
        var panel = PlaceFloatingPanel(point, panelWidth, panelHeight, 18);

        using var shadow = new SolidBrush(Color.FromArgb(88, 0, 0, 0));
        graphics.FillRoundedRectangle(shadow, new RectangleF(panel.Left + 3, panel.Top + 5, panel.Width, panel.Height), 13);
        using var back = new SolidBrush(Color.FromArgb(244, 7, 18, 34));
        using var edge = new Pen(Color.FromArgb(90, 255, 255, 255), 1.2f);
        graphics.FillRoundedRectangle(back, panel, 13);
        graphics.DrawRoundedRectangle(edge, panel, 13);

        var preview = new RectangleF(panel.Left + padding, panel.Top + padding, previewSize, previewSize);
        DrawMagnifier(graphics, point, preview);

        var footerTop = preview.Bottom + padding;
        var swatch = new RectangleF(panel.Left + padding, footerTop + 5, 24, 24);
        using (var swatchBrush = new SolidBrush(color))
        using (var swatchEdge = new Pen(Color.FromArgb(160, 255, 255, 255), 1f))
        {
            graphics.FillRoundedRectangle(swatchBrush, swatch, 5);
            graphics.DrawRoundedRectangle(swatchEdge, swatch, 5);
        }

        using var mono = new Font("Consolas", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var detail = new Font("Consolas", 8.3f, FontStyle.Regular, GraphicsUnit.Point);
        using var text = new SolidBrush(Color.White);
        using var muted = new SolidBrush(Color.FromArgb(205, 214, 226, 239));
        graphics.DrawString(hex, mono, text, swatch.Right + 8, footerTop + 1);
        graphics.DrawString($"X:{_virtualBounds.Left + point.X} Y:{_virtualBounds.Top + point.Y}", detail, muted, swatch.Right + 8, footerTop + 21);
    }

    private RectangleF PlaceFloatingPanel(Point anchor, int width, int height, int offset)
    {
        var left = anchor.X + offset;
        var top = anchor.Y + offset;
        if (left + width > ClientRectangle.Right - 8)
        {
            left = anchor.X - width - offset;
        }

        if (top + height > ClientRectangle.Bottom - 8)
        {
            top = anchor.Y - height - offset;
        }

        left = (int)ClampFloat(left, 8, ClientRectangle.Width - width - 8);
        top = (int)ClampFloat(top, 8, ClientRectangle.Height - height - 8);
        return new RectangleF(left, top, width, height);
    }

    private void DrawMagnifier(Graphics graphics, Point point, RectangleF bounds)
    {
        const int cells = 11;
        const int radius = cells / 2;
        var cellSize = bounds.Width / cells;
        for (var y = 0; y < cells; y++)
        {
            for (var x = 0; x < cells; x++)
            {
                var sample = ClampToBitmap(new Point(point.X + x - radius, point.Y + y - radius));
                using var pixel = new SolidBrush(ReadScreenPixel(sample));
                graphics.FillRectangle(pixel, bounds.Left + x * cellSize, bounds.Top + y * cellSize, cellSize + 0.5f, cellSize + 0.5f);
            }
        }

        using var grid = new Pen(Color.FromArgb(35, 255, 255, 255), 1f);
        for (var i = 1; i < cells; i++)
        {
            var offset = i * cellSize;
            graphics.DrawLine(grid, bounds.Left + offset, bounds.Top, bounds.Left + offset, bounds.Bottom);
            graphics.DrawLine(grid, bounds.Left, bounds.Top + offset, bounds.Right, bounds.Top + offset);
        }

        var center = new RectangleF(bounds.Left + radius * cellSize, bounds.Top + radius * cellSize, cellSize, cellSize);
        using var centerOuter = new Pen(Color.White, 2f);
        using var centerInner = new Pen(AccentColor, 1.5f);
        graphics.DrawRectangle(centerOuter, center.X, center.Y, center.Width, center.Height);
        graphics.DrawRectangle(centerInner, center.X + 2, center.Y + 2, center.Width - 4, center.Height - 4);
    }

    private static AnnotationTool ToAnnotationTool(ToolbarAction action)
    {
        return action switch
        {
            ToolbarAction.Rectangle => AnnotationTool.Rectangle,
            ToolbarAction.Arrow => AnnotationTool.Arrow,
            ToolbarAction.Text => AnnotationTool.Text,
            _ => AnnotationTool.Pen
        };
    }

    private static ToolbarAction ToToolbarAction(AnnotationTool tool)
    {
        return tool switch
        {
            AnnotationTool.Rectangle => ToolbarAction.Rectangle,
            AnnotationTool.Arrow => ToolbarAction.Arrow,
            AnnotationTool.Text => ToolbarAction.Text,
            _ => ToolbarAction.Pen
        };
    }

    private abstract record Annotation;
    private sealed record StrokeAnnotation(Point[] Points, Color Color, float Width) : Annotation;
    private sealed record RectangleAnnotation(Rectangle Bounds, Color Color, float Width) : Annotation;
    private sealed record ArrowAnnotation(Point Start, Point End, Color Color, float Width) : Annotation;
    private sealed record TextAnnotation(Point Location, string Text, Color Color, float FontSize) : Annotation;
    private sealed record ToolbarSpec(ToolbarAction Action, bool Active, bool Primary, bool HasStyle);
    private sealed record ToolbarButton(ToolbarAction Action, RectangleF Bounds);
    private sealed record StyleSwatch(Color Color, RectangleF Bounds);
    private sealed record ThicknessSwatch(float Width, RectangleF Bounds);
    private sealed record HandleRect(ResizeHandle Handle, RectangleF Bounds);
    private sealed record AnnotationHandleRect(AnnotationEditHandle Handle, RectangleF Bounds);
    private sealed record AnnotationHit(int Index, AnnotationEditHandle Handle);

    private enum ToolbarAction
    {
        Pen,
        Rectangle,
        Arrow,
        Text,
        Undo,
        SaveImage,
        Cancel,
        Confirm
    }

    private enum AnnotationTool
    {
        Pen,
        Rectangle,
        Arrow,
        Text
    }

    private enum ResizeHandle
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left
    }

    private enum AnnotationEditHandle
    {
        None,
        Move,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left,
        ArrowStart,
        ArrowEnd
    }
}

public enum ScreenshotCompletionAction
{
    SaveImage,
    CopyToClipboard
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        using var path = RoundedRectanglePath(bounds, radius);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF bounds, float radius)
    {
        using var path = RoundedRectanglePath(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedRectanglePath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
