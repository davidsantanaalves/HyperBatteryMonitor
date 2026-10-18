using HyperXBatteryTray;
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
    private sealed class DeviceSelector : Control
    {
        private readonly TextBox _searchBox;
        private readonly PictureBox _selectedImage;
        private readonly List<DeviceOption> _options;
        private bool _hover;
        private int _selectedIndex;
        private bool _dark;
        private SearchPopup? _popup;
        private bool _updatingText;
        private string _placeholder = string.Empty;
        private int _editOriginalIndex;
        private bool _editingDeviceSelection;
        private bool _selectionInProgress;
        private readonly ClickOutsideFilter _clickOutsideFilter;
        private AppLanguage _language = AppLanguage.English;

        public event EventHandler? SelectionChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int next = Math.Clamp(value, 0, _options.Count);
                if (_selectedIndex == next && _searchBox.Text == GetDisplayText(next)) return;
                _selectedIndex = next;
                UpdateSelectedImage();
                SetSearchText(GetDisplayText(next));
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SelectedDeviceName
        {
            get => GetCanonicalName(_selectedIndex);
            set
            {
                int index = 0;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    for (int i = 0; i < _options.Count; i++)
                    {
                        if (string.Equals(_options[i].Name,
                            HyperXDeviceManager.GetSelectionModelName(value),
                            StringComparison.OrdinalIgnoreCase))
                        {
                            index = i + 1;
                            break;
                        }
                    }
                }
                SelectedIndex = index;
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool DarkMode { get => _dark; set { _dark = value; _searchBox.BackColor = SearchBackColor; _searchBox.ForeColor = SearchTextColor; Invalidate(); } }

        private Color OutsideBackColor
        {
            get
            {
                Control? ancestor = Parent;
                while (ancestor != null)
                {
                    Color background = ancestor.BackColor;
                    if (background.A == byte.MaxValue)
                        return background;

                    ancestor = ancestor.Parent;
                }

                return _dark ? DarkBackground : LightBackground;
            }
        }

        public DeviceSelector()
        {
            _clickOutsideFilter = new ClickOutsideFilter(this);
            Application.AddMessageFilter(_clickOutsideFilter);

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            string devicesPath = AssetPaths.DevicesDirectory;
            _options = new List<DeviceOption>
            {
                new("HyperX Cloud III", Path.Combine(devicesPath, "cloud3.png")),
                new("HyperX Cloud III S", Path.Combine(devicesPath, "cloud3.png")),
                new("HyperX Cloud 2 Core", Path.Combine(devicesPath, "cloud2core.png")),
                new("HyperX Cloud Alpha", Path.Combine(devicesPath, "cloudalpha.png")),
                new("HyperX Cloud Stinger 2", Path.Combine(devicesPath, "cloudstinger2.png")),
                new("HyperX Cloud Flight S", Path.Combine(devicesPath, "cloudflights.png")),
                new("HyperX Cloud Flight Wireless", Path.Combine(devicesPath, "cloudflightwireless.png")),
                new("HyperX Cloud Stinger Core Wireless + 7.1", Path.Combine(devicesPath, "cloudstingercorewireless7.1.png")),
                new("HyperX Cloud Flight 2", Path.Combine(devicesPath, "cloudflight2.png")),
                new("HyperX Cloud Mix 2", Path.Combine(devicesPath, "cloudmix2.png")),
                new("HyperX Cloud Alpha 2 Wireless", Path.Combine(devicesPath, "cloudalpha2.png")),
                new("HyperX Cloud Stinger 3 Wireless", Path.Combine(devicesPath, "cloudstinger3.png"))
            };
            _options.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));

            _selectedImage = new PictureBox
            {
                Location = new Point(12, 9),
                Size = new Size(54, 46),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Visible = false
            };
            Controls.Add(_selectedImage);

            _searchBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Location = new Point(68, 20),
                Size = new Size(Width - 108, 24),
                Font = new Font("Segoe UI", 12f),
                Multiline = false,
                Padding = new Padding(0),
                TabStop = true,
                ReadOnly = false,
                Cursor = Cursors.IBeam,
                PlaceholderText = _placeholder
            };
            _searchBox.TextChanged += SearchBox_TextChanged;
            _searchBox.Enter += SearchBox_Enter;
            _searchBox.Leave += SearchBox_Leave;
            _searchBox.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                bool wasFocused = _searchBox.Focused;
                ShowPopup();
                if (!wasFocused)
                    BeginInvoke((MethodInvoker)(() => _searchBox.SelectAll()));
            };
            _searchBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    _popup?.Close();
                    e.Handled = true;
                }
            };
            Controls.Add(_searchBox);

            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
            Click += (_, _) => ShowPopup();
            Resize += (_, _) =>
            {
                LayoutChildren();
                ResetDisplayScroll();
            };
            HandleCreated += (_, _) => ScheduleDisplayLayoutRefresh();
            DpiChangedAfterParent += (_, _) => ScheduleDisplayLayoutRefresh();
            ApplySearchTheme();
        }

        public void SetPlaceholder(string placeholder)
        {
            _placeholder = placeholder ?? string.Empty;
            _searchBox.PlaceholderText = _placeholder;
            if (_selectedIndex == 0) SetSearchText(string.Empty);
            Invalidate();
        }

        public void SetLanguage(AppLanguage language)
        {
            _language = language;
            SetSearchText(GetDisplayText(_selectedIndex));

            if (_popup != null && !_popup.IsDisposed)
            {
                _popup.SetLanguage(language);
                _popup.RefreshItems(_searchBox.Text);
            }

            Invalidate();
        }

        private string GetCanonicalName(int index) =>
            index > 0 && index <= _options.Count
                ? _options[index - 1].Name
                : string.Empty;

        private string GetDisplayText(int index) =>
            index > 0 && index <= _options.Count
                ? Localization.DeviceDisplayName(_options[index - 1].Name, _language)
                : string.Empty;
        private Color SearchBackColor => _dark ? Color.FromArgb(38, 41, 44) : Color.White;
        private Color SearchTextColor => _dark ? Color.WhiteSmoke : LightText;

        private void SetSearchText(string value)
        {
            _updatingText = true;
            _searchBox.Text = value;
            _searchBox.SelectionStart = 0;
            _searchBox.SelectionLength = 0;
            _searchBox.ScrollToCaret();
            _updatingText = false;
        }

        private int ScaleLogical(int logicalValue) =>
            PngIconCache.ScaleLogicalToInt(logicalValue, DeviceDpi);

        private void LayoutChildren()
        {
            int imageWidth = ScaleLogical(54);
            int imageHeight = ScaleLogical(46);
            _selectedImage.Size = new Size(imageWidth, imageHeight);
            _selectedImage.Location = new Point(
                ScaleLogical(12),
                Math.Max(ScaleLogical(6), (Height - imageHeight) / 2));

            int searchX = _selectedImage.Visible ? ScaleLogical(68) : ScaleLogical(14);
            int reservedWidth = _selectedImage.Visible ? ScaleLogical(108) : ScaleLogical(52);
            _searchBox.Location = new Point(searchX, Math.Max(0, (Height - _searchBox.Height) / 2));
            _searchBox.Size = new Size(Math.Max(ScaleLogical(10), Width - reservedWidth), _searchBox.Height);
        }

        private void ResetDisplayScroll()
        {
            if (_searchBox.Focused || _editingDeviceSelection || _searchBox.TextLength == 0)
                return;

            _searchBox.SelectionStart = 0;
            _searchBox.SelectionLength = 0;
            _searchBox.ScrollToCaret();
        }

        private void ScheduleDisplayLayoutRefresh()
        {
            if (!IsHandleCreated || IsDisposed || Disposing)
                return;

            BeginInvoke((MethodInvoker)(() =>
            {
                if (!IsHandleCreated || IsDisposed || Disposing)
                    return;

                LayoutChildren();
                ResetDisplayScroll();
                Invalidate();
            }));
        }

        private void UpdateSelectedImage()
        {
            string? path = _selectedIndex > 0 && _selectedIndex <= _options.Count ? _options[_selectedIndex - 1].ImagePath : null;
            _selectedImage.Image?.Dispose();
            _selectedImage.Image = null;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    using Image source = Image.FromFile(path);
                    _selectedImage.Image = new Bitmap(source);
                    _selectedImage.Visible = true;
                }
                catch { _selectedImage.Visible = false; }
            }
            else _selectedImage.Visible = false;
            LayoutChildren();
            ResetDisplayScroll();
        }

        private void SearchBox_Enter(object? sender, EventArgs e)
        {
            if (!_editingDeviceSelection)
            {
                _editOriginalIndex = _selectedIndex;
                _editingDeviceSelection = true;
            }

            _searchBox.SelectAll();
            ShowPopup();
        }

        private void SearchBox_Leave(object? sender, EventArgs e)
        {
            if (_selectionInProgress) return;
            CommitOrRestoreDeviceSelection();
        }

        private void SearchBox_TextChanged(object? sender, EventArgs e)
        {
            if (_updatingText) return;

            if (_editingDeviceSelection)
            {
                _selectedImage.Visible = false;
                if (_selectedIndex != 0)
                {
                    _selectedIndex = 0;
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            ShowPopup();
            Invalidate();
        }

        private void CommitOrRestoreDeviceSelection()
        {
            if (!_editingDeviceSelection) return;

            string typed = _searchBox.Text.Trim();
            int matchingIndex = 0;
            for (int i = 0; i < _options.Count; i++)
            {
                string displayName = Localization.DeviceDisplayName(_options[i].Name, _language);
                if (string.Equals(displayName, typed, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_options[i].Name, typed, StringComparison.OrdinalIgnoreCase))
                {
                    matchingIndex = i + 1;
                    break;
                }
            }

            _editingDeviceSelection = false;
            _selectionInProgress = true;
            try
            {
                SelectedIndex = matchingIndex > 0 ? matchingIndex : _editOriginalIndex;
            }
            finally
            {
                _selectionInProgress = false;
            }
        }

        private void ShowPopup()
        {
            if (!IsHandleCreated) return;
            if (_popup == null || _popup.IsDisposed)
            {
                _popup = new SearchPopup(this, _options, _dark, _language, SelectOption);
            }
            _popup.SetTheme(_dark);
            _popup.Width = Width;
            _popup.RefreshItems(_searchBox.Text);
            Point screen = PointToScreen(new Point(0, Height));
            Rectangle workArea = Screen.FromControl(this).WorkingArea;
            int popupHeight = _popup.Height;
            if (screen.Y + popupHeight > workArea.Bottom)
                screen.Y = Math.Max(workArea.Top, PointToScreen(Point.Empty).Y - popupHeight);
            if (screen.X + _popup.Width > workArea.Right)
                screen.X = Math.Max(workArea.Left, workArea.Right - _popup.Width);
            _popup.Location = screen;
            Form? ownerForm = FindForm();
            if (!_popup.Visible)
            {
                if (ownerForm != null)
                    _popup.Show(ownerForm);
                else
                    _popup.Show();
            }
            _popup.BringToFront();
            if (!_searchBox.Focused)
                _searchBox.Focus();
        }

        private void SelectOption(int index)
        {
            _selectionInProgress = true;
            try
            {
                _popup?.Close();
                _editingDeviceSelection = false;
                SelectedIndex = index;
            }
            finally
            {
                _selectionInProgress = false;
            }
            _searchBox.SelectionLength = 0;
            Focus();
        }

        private void ApplySearchTheme()
        {
            _searchBox.BackColor = SearchBackColor;
            _searchBox.ForeColor = SearchTextColor;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(OutsideBackColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Color border = _selectedIndex == 0 ? Color.FromArgb(220, 53, 69) : (_dark ? Color.FromArgb(105, 112, 120) : Color.FromArgb(194, 201, 211));
            Color background = SearchBackColor;
            if (_hover && _selectedIndex != 0) background = _dark ? Color.FromArgb(43, 47, 51) : Color.FromArgb(252, 253, 255);

            float inset = 0.5f;
            RectangleF rect = new(inset, inset, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));
            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(rect, 7);
            using Brush bg = new SolidBrush(background);
            using Pen pen = new(border, 1f);
            e.Graphics.FillPath(bg, path);
            e.Graphics.DrawPath(pen, path);

            using Pen arrow = new(_dark ? Color.WhiteSmoke : LightText, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            int x = Width - 20;
            int y = Height / 2 - 2;
            e.Graphics.DrawLine(arrow, x - 4, y, x, y + 4);
            e.Graphics.DrawLine(arrow, x, y + 4, x + 4, y);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Application.RemoveMessageFilter(_clickOutsideFilter);
                _popup?.Close();
                _popup?.Dispose();
                _selectedImage?.Image?.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class ClickOutsideFilter : IMessageFilter
        {
            private readonly DeviceSelector _owner;

            public ClickOutsideFilter(DeviceSelector owner) => _owner = owner;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != 0x0201 || _owner._popup == null || _owner._popup.IsDisposed || !_owner._popup.Visible)
                    return false;

                Control? target = Control.FromHandle(m.HWnd);
                bool insideSelector = target != null && (target == _owner || _owner.Contains(target));
                bool insidePopup = target != null && (_owner._popup == target || _owner._popup.Contains(target));

                if (!insideSelector && !insidePopup)
                {
                    _owner._popup.Close();
                    Control? clicked = Control.FromHandle(m.HWnd);
                    if (clicked != null && clicked != _owner._searchBox && clicked.CanFocus)
                        clicked.Focus();
                    else
                        _owner.FindForm()?.Focus();
                }

                return false;
            }
        }

        private sealed record DeviceOption(string Name, string ImagePath);

        private sealed class SearchPopup : Form
        {
            private readonly List<DeviceOption> _options;
            private readonly Action<int> _select;
            private readonly Panel _list;
            private readonly ThemedVerticalScrollBar _listScrollBar;
            private bool _dark;
            private AppLanguage _language;

            public SearchPopup(DeviceSelector owner, List<DeviceOption> options, bool dark, AppLanguage language, Action<int> select)
            {
                _options = options; _dark = dark; _language = language; _select = select;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                ShowIcon = false;
                // Show(ownerForm) keeps the popup above its owning HBM window,
                // without pinning it over other Windows applications.
                AutoScaleMode = AutoScaleMode.None;
                Padding = new Padding(1);
                _list = new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    HorizontalScroll = { Enabled = false, Visible = false },
                    BackColor = Color.Transparent,
                    Margin = new Padding(0),
                    Padding = new Padding(0)
                };
                Controls.Add(_list);
                _listScrollBar = new ThemedVerticalScrollBar(_list, dark);
                Paint += SearchPopup_Paint;
                SetTheme(dark);
            }

            protected override bool ShowWithoutActivation => true;

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _listScrollBar.RefreshState();
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                SettingsForm.ApplyNativeScrollTheme(_list, _dark);
                _listScrollBar.RefreshState();
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                    cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                    return cp;
                }
            }

            private void SearchPopup_Paint(object? sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.SetClip(ClientRectangle);
                using Pen pen = new(_dark ? Color.FromArgb(86, 92, 98) : Color.FromArgb(194, 201, 211), 1f) { LineJoin = LineJoin.Round };
                RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));
                using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(rect, 6);
                e.Graphics.DrawPath(pen, path);
            }

            public void SetTheme(bool dark)
            {
                _dark = dark;
                BackColor = dark ? Color.FromArgb(48, 52, 56) : Color.FromArgb(225, 229, 235);
                _list.BackColor = dark ? Color.FromArgb(38, 41, 44) : Color.White;
                _list.ForeColor = dark ? Color.WhiteSmoke : LightText;
                _listScrollBar.SetTheme(dark);
                if (IsHandleCreated)
                    SettingsForm.ApplyNativeScrollTheme(_list, dark);
            }

            public void SetLanguage(AppLanguage language)
            {
                _language = language;
            }

            public void RefreshItems(string query)
            {
                const int itemHeight = 64;
                const int maxVisibleItems = 3;

                // The old AutoScrollPosition can otherwise offset every newly-created row,
                // leaving an empty block above the first result after reopening the popup.
                _list.SuspendLayout();
                try
                {
                    _list.AutoScroll = false;
                    _list.AutoScrollMinSize = Size.Empty;
                    _list.AutoScrollPosition = Point.Empty;
                    foreach (Control control in _list.Controls.Cast<Control>().ToArray())
                    {
                        _list.Controls.Remove(control);
                        control.Dispose();
                    }

                    string normalized = query.Trim();
                    int row = 0;
                    for (int i = 0; i < _options.Count; i++)
                    {
                        string displayName = Localization.DeviceDisplayName(_options[i].Name, _language);
                        if (!string.IsNullOrEmpty(normalized) &&
                            !displayName.Contains(normalized, StringComparison.OrdinalIgnoreCase) &&
                            !_options[i].Name.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                            continue;

                        Control item = CreateItem(i + 1, displayName, _options[i].ImagePath);
                        item.Location = new Point(0, row * itemHeight);
                        _list.Controls.Add(item);
                        row++;
                    }

                    Height = Math.Max(1, Math.Min(row, maxVisibleItems)) * itemHeight + 2;
                    _list.AutoScroll = true;
                    _list.AutoScrollMinSize = new Size(0, row * itemHeight);
                    _list.PerformLayout();
                    _list.AutoScrollPosition = Point.Empty;
                    int availableWidth = Math.Max(1, _list.ClientSize.Width);
                    for (int i = 0; i < _list.Controls.Count; i++)
                    {
                        Control item = _list.Controls[i];
                        item.Location = new Point(0, i * itemHeight);
                        item.Width = availableWidth;
                    }
                }
                finally
                {
                    _list.ResumeLayout(true);
                    _listScrollBar.RefreshState();
                }
            }

            private Control CreateItem(int index, string name, string? imagePath)
            {
                int y = _list.Controls.Count * 64;
                DeviceListItem item = new(name, imagePath, _dark)
                {
                    Location = new Point(0, y),
                    Width = Math.Max(1, _list.ClientSize.Width),
                    Height = 64
                };
                item.Click += (_, _) => _select(index);
                item.Cursor = Cursors.Hand;
                return item;
            }
        }

        private sealed class DeviceListItem : Control
        {
            private readonly string _name;
            private readonly string? _imagePath;
            private Image? _image;
            private readonly bool _dark;
            private bool _hover;

            public DeviceListItem(string name, string? imagePath, bool dark)
            {
                _name = name; _imagePath = imagePath; _dark = dark;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                LoadImage();
                MouseEnter += (_, _) => { _hover = true; Invalidate(); };
                MouseLeave += (_, _) => { _hover = false; Invalidate(); };
            }

            private void LoadImage()
            {
                if (string.IsNullOrWhiteSpace(_imagePath) || !File.Exists(_imagePath)) return;
                try
                {
                    using Image source = Image.FromFile(_imagePath);
                    _image = new Bitmap(source);
                }
                catch { }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (_hover)
                {
                    using Brush hover = new SolidBrush(_dark ? Color.FromArgb(48, 53, 58) : Color.FromArgb(242, 246, 251));
                    e.Graphics.FillRectangle(hover, ClientRectangle);
                }
                if (_image != null)
                    e.Graphics.DrawImage(_image, new Rectangle(10, 8, 48, 48));
                TextRenderer.DrawText(e.Graphics, _name, Font, new Rectangle(70, 0, Width - 80, Height), _dark ? Color.WhiteSmoke : LightText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _image?.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
