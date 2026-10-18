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
    private sealed class RoundedLanguageSelector : UserControl
    {
        private bool _darkMode;
        private LanguagePopupControl? _popup;
        private ThemedVerticalScrollBar? _popupScrollBar;
        private LanguagePopupMessageFilter? _popupMessageFilter;
        private int _selectedIndex = -1;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool DarkMode
        {
            get => _darkMode;
            set
            {
                if (_darkMode == value) return;
                _darkMode = value;
                _popup?.ApplyTheme(_darkMode);
                _popupScrollBar?.SetTheme(_darkMode);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IList<string> Items { get; } = new List<string>();

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int normalized = value >= 0 && value < Items.Count ? value : -1;
                if (_selectedIndex == normalized) return;
                _selectedIndex = normalized;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? SelectedItem => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;

        public event EventHandler? SelectedIndexChanged;

        public RoundedLanguageSelector()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            TabStop = true;
            BackColor = Color.Transparent;
            Padding = new Padding(0);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color background = _darkMode ? Color.FromArgb(31, 34, 37) : Color.White;
            Color border = _darkMode ? Color.FromArgb(105, 112, 120) : Color.FromArgb(194, 201, 211);
            Color foreground = _darkMode ? Color.WhiteSmoke : LightText;

            Rectangle bounds = new(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using GraphicsPath path = RoundedPath(bounds, ScaleLogical(6));
            using SolidBrush backgroundBrush = new(background);
            using Pen borderPen = new(border, 1f);
            e.Graphics.FillPath(backgroundBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            string text = SelectedItem ?? string.Empty;
            int textLeft = ScaleLogical(11);
            int reservedRight = ScaleLogical(43);
            Rectangle textBounds = new(textLeft, 1, Math.Max(1, Width - reservedRight), Math.Max(1, Height - 2));
            TextRenderer.DrawText(e.Graphics, text, Font, textBounds, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            int centerX = Width - ScaleLogical(13);
            int centerY = Height / 2;
            int arrowHalfWidth = ScaleLogical(4);
            int arrowTopOffset = ScaleLogical(2);
            int arrowBottomOffset = ScaleLogical(3);
            using SolidBrush arrowBrush = new(foreground);
            Point[] arrow =
            {
                new(centerX - arrowHalfWidth, centerY - arrowTopOffset),
                new(centerX + arrowHalfWidth, centerY - arrowTopOffset),
                new(centerX, centerY + arrowBottomOffset)
            };
            e.Graphics.FillPolygon(arrowBrush, arrow);
        }

        private int ScaleLogical(int logicalValue) =>
            PngIconCache.ScaleLogicalToInt(logicalValue, DeviceDpi);

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            if (_popup != null)
                ClosePopup();
            else
                OpenPopup();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                if (_popup != null)
                    ClosePopup();
                else
                    OpenPopup();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Down)
            {
                SelectedIndex = Math.Min(Items.Count - 1, Math.Max(0, _selectedIndex + 1));
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                SelectedIndex = Math.Max(0, _selectedIndex - 1);
                e.Handled = true;
            }
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            if (_popup == null) return;

            BeginInvoke(new Action(() =>
            {
                if (_popup != null && !_popup.ContainsFocus && !ContainsFocus)
                    ClosePopup();
            }));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_popup != null)
                ClosePopup();
            Invalidate();
        }

        private void OpenPopup()
        {
            Form? form = FindForm();
            if (form == null || Items.Count == 0) return;

            int desiredHeight =
                Items.Count * ScaleLogical(LanguagePopupItemLogicalHeight) +
                ScaleLogical(LanguagePopupBorderLogicalWidth * 2);
            Point belowLocation = form.PointToClient(PointToScreen(new Point(0, Height)));
            Point selectorLocation = form.PointToClient(PointToScreen(Point.Empty));
            int spaceBelow = Math.Max(0, form.ClientSize.Height - belowLocation.Y);
            int spaceAbove = Math.Max(0, selectorLocation.Y);
            bool openBelow = spaceBelow >= spaceAbove;
            int availableHeight = openBelow ? spaceBelow : spaceAbove;
            int minimumPopupHeight = Math.Min(
                desiredHeight,
                ScaleLogical(LanguagePopupItemLogicalHeight * LanguagePopupMinimumVisibleItems));
            int popupHeight = Math.Min(
                desiredHeight,
                Math.Max(
                    minimumPopupHeight,
                    availableHeight - ScaleLogical(LanguagePopupEdgeMarginLogical)));

            _popup = new LanguagePopupControl
            {
                Items = Items.ToArray(),
                SelectedIndex = _selectedIndex,
                Font = Font,
                DarkMode = _darkMode,
                Size = new Size(Width, popupHeight)
            };
            _popup.ItemClicked += Popup_ItemClicked;
            _popup.Dismissed += Popup_Dismissed;
            _popupMessageFilter = new LanguagePopupMessageFilter(this);
            Application.AddMessageFilter(_popupMessageFilter);

            _popup.Location = openBelow
                ? belowLocation
                : new Point(selectorLocation.X, selectorLocation.Y - popupHeight);
            form.Controls.Add(_popup);
            _popup.BringToFront();
            _popupScrollBar = new ThemedVerticalScrollBar(_popup, _darkMode);
            _popup.ScrollSelectedIntoView();
            _popupScrollBar.RefreshState();
            _popup.Focus();
        }

        private void Popup_ItemClicked(int index)
        {
            SelectedIndex = index;
            ClosePopup();
        }

        private void Popup_Dismissed(object? sender, EventArgs e)
        {
            ClosePopup();
        }

        public void ClosePopup()
        {
            if (_popup == null) return;

            LanguagePopupControl popup = _popup;
            _popup = null;
            if (_popupMessageFilter != null)
            {
                Application.RemoveMessageFilter(_popupMessageFilter);
                _popupMessageFilter = null;
            }
            popup.ItemClicked -= Popup_ItemClicked;
            popup.Dismissed -= Popup_Dismissed;
            _popupScrollBar?.Dispose();
            _popupScrollBar = null;
            if (popup.Parent != null)
                popup.Parent.Controls.Remove(popup);
            popup.Dispose();
            Focus();
            Invalidate();
        }

        private sealed class LanguagePopupMessageFilter : IMessageFilter
        {
            private readonly RoundedLanguageSelector _owner;

            public LanguagePopupMessageFilter(RoundedLanguageSelector owner)
            {
                _owner = owner;
            }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != 0x0201 || _owner._popup == null)
                    return false;

                Point screenPoint = Control.MousePosition;
                bool insideSelector = _owner.RectangleToScreen(_owner.ClientRectangle).Contains(screenPoint);
                bool insidePopup = _owner._popup.RectangleToScreen(_owner._popup.ClientRectangle).Contains(screenPoint);

                if (!insideSelector && !insidePopup)
                    _owner.ClosePopup();

                return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ClosePopup();
            base.Dispose(disposing);
        }

        private static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new();
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
