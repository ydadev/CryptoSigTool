namespace CryptoSigTool;

internal sealed class PdfPageCanvas : ScrollableControl
{
    private Image? _pageImage;
    private float _zoom = 1;
    private PointF? _selectionStart;
    private RectangleF _selectionNormalized = RectangleF.Empty;

    public PdfPageCanvas()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(53, 59, 69);
        Cursor = Cursors.Cross;
        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public RectangleF SelectionNormalized => _selectionNormalized;
    public float Zoom => _zoom;
    public bool HasPage => _pageImage is not null;

    public event EventHandler? SelectionChanged;

    public void SetPage(Image image)
    {
        _pageImage?.Dispose();
        _pageImage = image;
        _selectionNormalized = RectangleF.Empty;
        _selectionStart = null;
        FitToWindow();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetZoom(float zoom)
    {
        if (_pageImage is null) return;
        _zoom = Math.Clamp(zoom, 0.15F, 4F);
        UpdateScrollArea();
        Invalidate();
    }

    public void FitToWindow()
    {
        if (_pageImage is null || ClientSize.Width < 50 || ClientSize.Height < 50) return;
        var horizontal = (ClientSize.Width - 48F) / _pageImage.Width;
        var vertical = (ClientSize.Height - 48F) / _pageImage.Height;
        _zoom = Math.Clamp(Math.Min(horizontal, vertical), 0.15F, 4F);
        AutoScrollPosition = Point.Empty;
        UpdateScrollArea();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_pageImage is null)
        {
            using var font = new Font("Segoe UI", 14F);
            TextRenderer.DrawText(e.Graphics, "Откройте PDF-файл", font, ClientRectangle, Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var bounds = GetImageBounds();
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_pageImage, bounds);
        e.Graphics.DrawRectangle(Pens.DimGray, bounds.X, bounds.Y, bounds.Width, bounds.Height);

        if (!_selectionNormalized.IsEmpty)
        {
            var selected = NormalizedToClient(_selectionNormalized, bounds);
            using var fill = new SolidBrush(Color.FromArgb(55, 36, 99, 235));
            using var pen = new Pen(Color.FromArgb(36, 99, 235), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.FillRectangle(fill, selected);
            e.Graphics.DrawRectangle(pen, selected.X, selected.Y, selected.Width, selected.Height);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _pageImage is null) return;
        var normalized = ClientToNormalized(e.Location);
        if (normalized is null) return;
        _selectionStart = normalized.Value;
        _selectionNormalized = new RectangleF(normalized.Value, SizeF.Empty);
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_selectionStart is null || _pageImage is null) return;
        var current = ClientToNormalized(e.Location, clamp: true) ?? _selectionStart.Value;
        var left = Math.Min(_selectionStart.Value.X, current.X);
        var top = Math.Min(_selectionStart.Value.Y, current.Y);
        var right = Math.Max(_selectionStart.Value.X, current.X);
        var bottom = Math.Max(_selectionStart.Value.Y, current.Y);
        _selectionNormalized = RectangleF.FromLTRB(left, top, right, bottom);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_selectionStart is null) return;
        _selectionStart = null;
        Capture = false;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollArea();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pageImage?.Dispose();
            _pageImage = null;
        }
        base.Dispose(disposing);
    }

    private RectangleF GetImageBounds()
    {
        if (_pageImage is null) return RectangleF.Empty;
        var width = _pageImage.Width * _zoom;
        var height = _pageImage.Height * _zoom;
        var contentWidth = Math.Max(ClientSize.Width, width + 40);
        var x = Math.Max(20, (contentWidth - width) / 2) + AutoScrollPosition.X;
        var y = 20 + AutoScrollPosition.Y;
        return new RectangleF(x, y, width, height);
    }

    private PointF? ClientToNormalized(Point point, bool clamp = false)
    {
        var bounds = GetImageBounds();
        if (bounds.IsEmpty) return null;
        var x = (point.X - bounds.Left) / bounds.Width;
        var y = (point.Y - bounds.Top) / bounds.Height;
        if (!clamp && (x < 0 || x > 1 || y < 0 || y > 1)) return null;
        return new PointF(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

    private static RectangleF NormalizedToClient(RectangleF value, RectangleF bounds) =>
        new(bounds.Left + value.Left * bounds.Width,
            bounds.Top + value.Top * bounds.Height,
            value.Width * bounds.Width,
            value.Height * bounds.Height);

    private void UpdateScrollArea()
    {
        if (_pageImage is null)
        {
            AutoScrollMinSize = Size.Empty;
            return;
        }
        AutoScrollMinSize = new Size(
            (int)Math.Ceiling(_pageImage.Width * _zoom + 40),
            (int)Math.Ceiling(_pageImage.Height * _zoom + 40));
    }
}
