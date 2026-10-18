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
    private sealed class BatteryIconControl : Control
    {
        private const int IconSize = 46;
        // Coordinates of the transparent interior on the supplied 46x46 battery PNG.
        // Only this area receives the dynamic battery-level fill.
        private static readonly Rectangle InteriorPixels = new(9, 18, 26, 12);

        private Bitmap? _darkTemplate;
        private Bitmap? _lightTemplate;
        private int _battery;
        private bool _connected;
        private bool _charging;
        private bool _dark;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Battery { get => _battery; set { _battery = Math.Clamp(value, 0, 100); Invalidate(); } }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Connected { get => _connected; set { _connected = value; Invalidate(); } }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Charging { get => _charging; set { _charging = value; Invalidate(); } }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool DarkMode { get => _dark; set { _dark = value; Invalidate(); } }

        public BatteryIconControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            LoadTemplates();
        }

        private void LoadTemplates()
        {
            DisposeTemplate(ref _darkTemplate);
            DisposeTemplate(ref _lightTemplate);
            _darkTemplate = LoadTemplate(true, "battery-dark-46x46.png");
            _lightTemplate = LoadTemplate(false, "battery-light-46x46.png");
        }

        private static Bitmap? LoadTemplate(bool darkMode, string fileName)
        {
            try
            {
                string filePath = AssetPaths.GetThemeIconPath(darkMode, fileName);
                if (!File.Exists(filePath))
                    return null;

                using Bitmap source = new(filePath);
                Bitmap template = new(IconSize, IconSize, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(template))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImageUnscaled(source, 0, 0);
                }

                // The supplied PNG already contains the battery frame. Clear only the
                // center area so the dynamic charge level can be painted underneath it.
                for (int y = InteriorPixels.Top; y < InteriorPixels.Bottom; y++)
                {
                    for (int x = InteriorPixels.Left; x < InteriorPixels.Right; x++)
                    {
                        Color pixel = template.GetPixel(x, y);
                        template.SetPixel(x, y, Color.FromArgb(0, pixel.R, pixel.G, pixel.B));
                    }
                }

                return template;
            }
            catch
            {
                return null;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            Bitmap? template = _dark ? _darkTemplate : _lightTemplate;
            if (template == null)
                return;

            float scale = Math.Min(Width / (float)IconSize, Height / (float)IconSize);
            if (scale <= 0)
                return;

            float drawWidth = IconSize * scale;
            float drawHeight = IconSize * scale;
            float ox = (Width - drawWidth) / 2f;
            float oy = (Height - drawHeight) / 2f;

            RectangleF interior = new(
                ox + InteriorPixels.X * scale,
                oy + InteriorPixels.Y * scale,
                InteriorPixels.Width * scale,
                InteriorPixels.Height * scale);

            Color cardBackground = _dark
                ? Color.FromArgb(46, 50, 54)
                : Color.FromArgb(248, 249, 251);

            // Restore the background inside the transparent charge cavity.
            using (Brush backgroundBrush = new SolidBrush(cardBackground))
                e.Graphics.FillRectangle(backgroundBrush, interior);

            if (_connected && _battery > 0)
            {
                Color green = Color.FromArgb(52, 211, 85);
                Color yellow = Color.FromArgb(250, 204, 21);
                Color orange = Color.FromArgb(249, 115, 22);
                Color red = Color.FromArgb(239, 68, 68);
                Color active = GetBatteryLevelColor(_battery, green, yellow, orange, red);

                float fillWidth = interior.Width * (_battery / 100f);
                if (fillWidth > 0)
                {
                    RectangleF fillBounds = new(interior.Left, interior.Top, fillWidth, interior.Height);
                    GraphicsState state = e.Graphics.Save();
                    try
                    {
                        e.Graphics.SetClip(interior, CombineMode.Intersect);
                        using LinearGradientBrush gradient = new(
                            new PointF(interior.Left, interior.Top),
                            new PointF(interior.Right, interior.Top),
                            Blend(active, Color.White, 0.08),
                            active);
                        e.Graphics.FillRectangle(gradient, fillBounds);
                    }
                    finally
                    {
                        e.Graphics.Restore(state);
                    }
                }
            }

            // Draw the PNG frame over the dynamic fill.
            e.Graphics.DrawImage(template, new RectangleF(ox, oy, drawWidth, drawHeight));

            if (_connected && _charging)
                DrawChargingBolt(e.Graphics, interior);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeTemplate(ref _darkTemplate);
                DisposeTemplate(ref _lightTemplate);
            }
            base.Dispose(disposing);
        }

        private static void DisposeTemplate(ref Bitmap? bitmap)
        {
            bitmap?.Dispose();
            bitmap = null;
        }

        private static void DrawChargingBolt(Graphics graphics, RectangleF bounds)
        {
            float width = Math.Min(bounds.Width * 0.42f, 15f);
            float height = Math.Min(bounds.Height * 0.72f, 24f);
            if (width <= 2f || height <= 2f) return;

            float left = bounds.Left + (bounds.Width - width) / 2f;
            float top = bounds.Top + (bounds.Height - height) / 2f;
            PointF[] points =
            {
                new(left + width * 0.58f, top),
                new(left + width * 0.08f, top + height * 0.56f),
                new(left + width * 0.48f, top + height * 0.56f),
                new(left + width * 0.30f, top + height),
                new(left + width * 0.92f, top + height * 0.34f),
                new(left + width * 0.52f, top + height * 0.34f)
            };

            using Brush fill = new SolidBrush(Color.WhiteSmoke);
            using Pen outline = new(Color.FromArgb(90, 0, 0, 0), Math.Max(1f, width * 0.08f))
            {
                LineJoin = LineJoin.Round
            };
            graphics.FillPolygon(fill, points);
            graphics.DrawPolygon(outline, points);
        }

        private static Color GetBatteryLevelColor(int battery, Color green, Color yellow, Color orange, Color red)
        {
            battery = Math.Clamp(battery, 0, 100);
            if (battery >= 50)
                return green;
            if (battery >= 30)
                return Blend(yellow, green, (battery - 30) / 20.0);
            if (battery >= 10)
                return Blend(orange, yellow, (battery - 10) / 20.0);
            return Blend(red, orange, battery / 10.0);
        }

        private static Color Blend(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }
    }
}
