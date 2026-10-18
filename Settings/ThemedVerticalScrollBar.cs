using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HyperXBatteryTray.Settings;

/// <summary>
/// A themed, owner-drawn overlay for WinForms vertical scrollbars. It covers the
/// native scrollbar *only* when scrolling is possible, without replacing the
/// target's scroll implementation (wheel, touchpad, keys, and selection remain native).
/// Add it as a sibling of the target in a normal Panel/Form (not a TableLayoutPanel).
/// </summary>
internal sealed class ThemedVerticalScrollBar : Control
{
    private const int SbVert = 1;
    private const uint SifAll = 0x17;
    private const int WmVScroll = 0x0115;
    private const int SbThumbPosition = 4;
    private const int EmSetScrollPos = 0x04DE;

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public uint cbSize;
        public uint fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, ref ScrollPoint point);

    private readonly Control _target;
    private Control? _host;
    private bool _dark;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartPos;
    private int _min;
    private int _max;
    private int _value;
    private int _page;
    private bool _hover;

    internal ThemedVerticalScrollBar(Control target, bool dark)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _dark = dark;
        Width = SystemInformation.VerticalScrollBarWidth;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Cursor = Cursors.Default;
        Visible = false;

        _target.ParentChanged += TargetChanged;
        _target.HandleCreated += TargetChanged;
        _target.SizeChanged += TargetChanged;
        _target.LocationChanged += TargetChanged;
        _target.VisibleChanged += TargetChanged;
        _target.Layout += TargetLayoutChanged;
        _target.MouseWheel += TargetChangedMouse;
        _target.KeyUp += TargetChangedKey;
        _target.Disposed += TargetDisposed;
        if (_target is ScrollableControl scrollable)
            scrollable.Scroll += TargetScrolled;
        if (_target is RichTextBox richText)
        {
            richText.VScroll += TargetChanged;
            richText.TextChanged += TargetChanged;
        }
        AttachToParent();
    }

    internal void SetTheme(bool dark)
    {
        if (_dark == dark) return;
        _dark = dark;
        Invalidate();
    }

    internal void RefreshState()
    {
        if (IsDisposed || _target.IsDisposed) return;
        AttachToParent();
        if (_host is null || !_target.IsHandleCreated || !_target.Visible)
        {
            Visible = false;
            return;
        }

        if (_target is ScrollableControl sc && !sc.VerticalScroll.Visible)
        {
            Visible = false;
            return;
        }

        ScrollInfo info = new() { cbSize = (uint)Marshal.SizeOf<ScrollInfo>(), fMask = SifAll };
        if (!GetScrollInfo(_target.Handle, SbVert, ref info))
        {
            Visible = false;
            return;
        }

        long range = (long)info.nMax - info.nMin + 1;
        if (range <= 0 || info.nPage == 0 || range <= info.nPage)
        {
            Visible = false;
            return;
        }

        _min = info.nMin;
        _max = Math.Max(info.nMin, (int)Math.Min(int.MaxValue, (long)info.nMax - info.nPage + 1));
        _value = Math.Clamp(info.nPos, _min, _max);
        _page = (int)Math.Min(int.MaxValue, info.nPage);
        Rectangle expected = new(
            _target.Right - SystemInformation.VerticalScrollBarWidth,
            _target.Top,
            SystemInformation.VerticalScrollBarWidth,
            _target.Height);
        if (Bounds != expected)
            Bounds = expected;
        Visible = true;
        if (Parent != null && Parent.Controls.GetChildIndex(this) != 0)
            BringToFront();
        Invalidate();
    }

    private void AttachToParent()
    {
        Control? parent = _target.Parent;
        if (parent == null || ReferenceEquals(_host, parent)) return;
        if (_host != null)
        {
            _host.Resize -= OnTrackedParentChanged;
            _host.Layout -= ParentLayoutChanged;
            if (ReferenceEquals(Parent, _host))
                _host.Controls.Remove(this);
        }
        _host = parent;
        _host.Controls.Add(this);
        _host.Resize += OnTrackedParentChanged;
        _host.Layout += ParentLayoutChanged;
        BringToFront();
    }

    private void TargetDisposed(object? sender, EventArgs e) => Dispose();
    private void TargetScrolled(object? sender, ScrollEventArgs e) => DeferSync();
    private void TargetChangedKey(object? sender, KeyEventArgs e) => DeferSync();
    private void TargetChangedMouse(object? sender, MouseEventArgs e) => DeferSync();
    private void TargetChanged(object? sender, EventArgs e) => DeferSync();
    private void TargetLayoutChanged(object? sender, LayoutEventArgs e) => DeferSync();
    private void OnTrackedParentChanged(object? sender, EventArgs e) => DeferSync();
    private void ParentLayoutChanged(object? sender, LayoutEventArgs e) => DeferSync();

    // Queue after the WinForms/native scroll or layout finishes. Avoid triggering
    // layout recursively from Scroll and Layout event handlers.
    private bool _syncPending;
    private void DeferSync()
    {
        if (_syncPending || IsDisposed || Disposing) return;
        if (!IsHandleCreated)
        {
            RefreshState();
            return;
        }
        _syncPending = true;
        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                _syncPending = false;
                if (!IsDisposed && !Disposing)
                    RefreshState();
            }));
        }
        catch (InvalidOperationException) { _syncPending = false; }
    }

    private Rectangle ThumbBounds
    {
        get
        {
            int track = Math.Max(1, Height);
            long total = Math.Max(1, (long)_max - _min + _page);
            int thumbHeight = Math.Clamp((int)((long)track * _page / total),
                Math.Min(Scale(28), track), track);
            int travel = track - thumbHeight;
            int top = _max > _min ? (int)((long)travel * (_value - _min) / (_max - _min)) : 0;
            return new Rectangle(Scale(3), top + Scale(2),
                Math.Max(3, Width - Scale(6)), Math.Max(3, thumbHeight - Scale(4)));
        }
    }

    private int Scale(int v) => (int)Math.Round(v * (DeviceDpi / 96f));

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color track = _dark ? Color.FromArgb(37, 40, 44) : Color.FromArgb(243, 245, 248);
        Color thumb = _dark
            ? (_hover || _dragging ? Color.FromArgb(140, 147, 157) : Color.FromArgb(101, 109, 119))
            : (_hover || _dragging ? Color.FromArgb(139, 147, 158) : Color.FromArgb(176, 184, 194));
        e.Graphics.Clear(track);
        Rectangle rect = ThumbBounds;
        if (rect.Width > 0 && rect.Height > 0)
        {
            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(rect, Math.Min(Scale(5), rect.Width / 2));
            using SolidBrush brush = new(thumb);
            e.Graphics.FillPath(brush, path);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Rectangle thumb = ThumbBounds;
        if (thumb.Contains(e.Location))
        {
            _dragging = true;
            _dragStartY = e.Y;
            _dragStartPos = _value;
            Capture = true;
        }
        else
        {
            int step = Math.Max(1, _page);
            ScrollTo(_value + (e.Y < thumb.Top ? -step : step));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        int travel = Math.Max(1, Height - ThumbBounds.Height - Scale(4));
        int next = _dragStartPos + (int)Math.Round((double)(e.Y - _dragStartY) * (_max - _min) / travel);
        ScrollTo(next);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = false;
            Capture = false;
            RefreshState();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // Consume wheel input while the pointer is over the scrollbar; do not
        // bubble it to a scrollable parent and apply the delta twice.
        if (_target.IsDisposed || e.Delta == 0) return;
        int lineCount = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        int delta = Math.Max(1, _page / 8);
        ScrollTo(_value - Math.Sign(e.Delta) * Math.Min(_page, lineCount * delta));
    }

    private void ScrollTo(int targetPosition)
    {
        if (_target.IsDisposed || !_target.IsHandleCreated) return;
        targetPosition = Math.Clamp(targetPosition, _min, _max);
        if (_target is ScrollableControl scrollable)
        {
            int x = Math.Max(0, -scrollable.AutoScrollPosition.X);
            scrollable.AutoScrollPosition = new Point(x, targetPosition);
        }
        else if (_target is RichTextBox)
        {
            // Rich Edit uses pixel scrolling; EM_SETSCROLLPOS accepts full 32-bit
            // coordinates, unlike WM_VSCROLL's 16-bit thumb position.
            ScrollPoint pos = new() { X = 0, Y = targetPosition };
            _ = SendMessage(_target.Handle, EmSetScrollPos, IntPtr.Zero, ref pos);
        }
        else
        {
            int vscroll = (targetPosition << 16) | SbThumbPosition;
            _ = SendMessage(_target.Handle, WmVScroll, new IntPtr(vscroll), IntPtr.Zero);
        }
        _value = targetPosition;
        Invalidate();
        DeferSync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _target.ParentChanged -= TargetChanged;
            _target.HandleCreated -= TargetChanged;
            _target.SizeChanged -= TargetChanged;
            _target.LocationChanged -= TargetChanged;
            _target.VisibleChanged -= TargetChanged;
            _target.Layout -= TargetLayoutChanged;
            _target.MouseWheel -= TargetChangedMouse;
            _target.KeyUp -= TargetChangedKey;
            _target.Disposed -= TargetDisposed;
            if (_target is ScrollableControl sc)
                sc.Scroll -= TargetScrolled;
            if (_target is RichTextBox rich)
            {
                rich.VScroll -= TargetChanged;
                rich.TextChanged -= TargetChanged;
            }
            if (_host != null)
            {
                _host.Resize -= OnTrackedParentChanged;
                _host.Layout -= ParentLayoutChanged;
            }
        }
        base.Dispose(disposing);
    }
}
