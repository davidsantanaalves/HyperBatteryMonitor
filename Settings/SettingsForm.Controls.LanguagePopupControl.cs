using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using HyperXBatteryTray.Devices;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm : Form
{
    private const int LanguagePopupItemLogicalHeight = 30;
    private const int LanguagePopupBorderLogicalWidth = 1;
    private const int LanguagePopupMinimumVisibleItems = 3;
    private const int LanguagePopupEdgeMarginLogical = 4;

    private sealed class LanguagePopupControl : ScrollableControl
    {
        private string[] _items = Array.Empty<string>();
        private int _selectedIndex = -1;
        private int _hoverIndex = -1;
        private bool _darkMode;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string[] Items
        {
            get => _items;
            set
            {
                _items = value ?? Array.Empty<string>();
                AutoScrollMinSize = new Size(0, _items.Length * ItemHeight + BorderWidth * 2);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                _selectedIndex = value >= 0 && value < _items.Length ? value : -1;
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool DarkMode
        {
            get => _darkMode;
            set
            {
                _darkMode = value;
                Invalidate();
            }
        }

        public event Action<int>? ItemClicked;
        public event EventHandler? Dismissed;

        private int ItemHeight => PngIconCache.ScaleLogicalToInt(LanguagePopupItemLogicalHeight, DeviceDpi);
        private int BorderWidth => PngIconCache.ScaleLogicalToInt(LanguagePopupBorderLogicalWidth, DeviceDpi);

        public LanguagePopupControl()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AutoScroll = true;
            BackColor = Color.FromArgb(38, 41, 44);
        }

        public void ApplyTheme(bool dark)
        {
            DarkMode = dark;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color background = _darkMode ? Color.FromArgb(38, 41, 44) : Color.White;
            Color border = _darkMode ? Color.FromArgb(105, 112, 120) : Color.FromArgb(194, 201, 211);
            Color hover = _darkMode ? Color.FromArgb(30, 66, 99) : Color.FromArgb(224, 238, 255);
            Color foreground = _darkMode ? Color.WhiteSmoke : LightText;

            using SolidBrush backgroundBrush = new(background);
            using Pen borderPen = new(border, 1f);
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
            e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            int scrollY = AutoScrollPosition.Y;
            for (int i = 0; i < _items.Length; i++)
            {
                Rectangle itemBounds = new(BorderWidth, BorderWidth + i * ItemHeight + scrollY,
                    Math.Max(1, ClientSize.Width - BorderWidth * 2), ItemHeight);
                if (itemBounds.Bottom < 0 || itemBounds.Top > ClientSize.Height)
                    continue;
                bool highlighted = i == _hoverIndex || (i == _selectedIndex && _hoverIndex < 0);

                if (highlighted)
                {
                    using SolidBrush hoverBrush = new(hover);
                    e.Graphics.FillRectangle(hoverBrush, itemBounds);
                }

                TextRenderer.DrawText(e.Graphics, _items[i], Font,
                    new Rectangle(itemBounds.X + ScaleLogical(10), itemBounds.Y, itemBounds.Width - ScaleLogical(20), itemBounds.Height),
                    foreground,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private int ScaleLogical(int logicalValue) =>
            PngIconCache.ScaleLogicalToInt(logicalValue, DeviceDpi);

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        public void ScrollSelectedIntoView()
        {
            EnsureItemVisible(_selectedIndex);
        }

        private void EnsureItemVisible(int index)
        {
            if (index < 0 || index >= _items.Length) return;

            int visibleTop = VerticalScroll.Value;
            int visibleHeight = ClientSize.Height;
            int itemTop = BorderWidth + index * ItemHeight;
            int itemBottom = itemTop + ItemHeight;

            if (itemTop < visibleTop)
            {
                AutoScrollPosition = new Point(0, itemTop);
            }
            else if (itemBottom > visibleTop + visibleHeight)
            {
                AutoScrollPosition = new Point(0, Math.Max(0, itemBottom - visibleHeight));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexFromPoint(e.Location);
            if (_hoverIndex != index)
            {
                _hoverIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex != -1)
            {
                _hoverIndex = -1;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;

            int index = IndexFromPoint(e.Location);
            if (index >= 0)
                ItemClicked?.Invoke(index);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Down)
            {
                int next = Math.Min(_items.Length - 1, Math.Max(0, (_hoverIndex >= 0 ? _hoverIndex : _selectedIndex) + 1));
                _hoverIndex = next;
                EnsureItemVisible(next);
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                int next = Math.Max(0, (_hoverIndex >= 0 ? _hoverIndex : _selectedIndex) - 1);
                _hoverIndex = next;
                EnsureItemVisible(next);
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                int index = _hoverIndex >= 0 ? _hoverIndex : _selectedIndex;
                if (index >= 0)
                    ItemClicked?.Invoke(index);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Dismissed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private int IndexFromPoint(Point point)
        {
            int contentY = point.Y - AutoScrollPosition.Y - BorderWidth;
            int index = contentY / ItemHeight;
            return point.X >= BorderWidth && point.X < ClientSize.Width - BorderWidth &&
                   contentY >= 0 && index >= 0 && index < _items.Length ? index : -1;
        }
    }
}
