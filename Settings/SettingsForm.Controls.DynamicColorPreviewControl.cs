using HyperXBatteryTray;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm : Form
{
    private sealed class DynamicColorPreviewControl : Control
    {
        private const int PreviewIconLogicalSize = 30;
        private const int PreviewLabelLogicalHeight = 24;
        private const int PreviewLabelTopGapLogical = 2;

        private readonly bool _dark;
        private readonly AppLanguage _language;
        private readonly List<BatteryColorSettings> _colors;
        private readonly ToggleSwitchControl _gradientToggle;
        private readonly CriticalBatteryNumericControl _gradientStep;
        private Bitmap? _darkIcon;
        private Bitmap? _lightIcon;
        private Bitmap? _darkCharging;
        private Bitmap? _lightCharging;

        public DynamicColorPreviewControl(
            bool dark,
            AppLanguage language,
            IEnumerable<BatteryColorSettings> colors,
            ToggleSwitchControl gradientToggle,
            CriticalBatteryNumericControl gradientStep)
        {
            _dark = dark;
            _language = language;
            _colors = colors.ToList();
            _gradientToggle = gradientToggle;
            _gradientStep = gradientStep;

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Color.Transparent;
            LoadBitmaps();
        }

        public void RefreshPreview() => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            List<BatteryColorSettings> sorted = _colors
                .OrderByDescending(c => c.MinimumPercent)
                .ToList();

            int high = Math.Clamp(sorted[0].MinimumPercent, 0, 100);
            int medium = Math.Clamp(sorted[1].MinimumPercent, 0, 100);
            int low = Math.Clamp(sorted[2].MinimumPercent, 0, 100);

            int[] samples =
            {
                high,
                medium,
                low,
                -1
            };

            string[] labels =
            {
                $">= {high}%",
                $"{Math.Max(medium, high - 1)} – {medium}%",
                $"{Math.Max(low, medium - 1)} – {low}%",
                Localization.Get("BatteryPreviewCharging", _language)
            };

            Bitmap? charging = _dark ? _darkCharging : _lightCharging;
            Bitmap? normal = _dark ? _darkIcon : _lightIcon;

            int iconSize = ScaleLogical(PreviewIconLogicalSize);
            int labelTop = iconSize + ScaleLogical(PreviewLabelTopGapLogical);
            int labelHeight = Math.Max(ScaleLogical(PreviewLabelLogicalHeight), Height - labelTop);
            float slotWidth = Math.Max(1f, Width / (float)samples.Length);

            for (int i = 0; i < samples.Length; i++)
            {
                float x = i * slotWidth;
                RectangleF iconRect = new(x + (slotWidth - iconSize) / 2f, 0, iconSize, iconSize);

                if (i == samples.Length - 1)
                {
                    if (charging != null)
                        e.Graphics.DrawImage(charging, iconRect);
                }
                else if (normal != null)
                {
                    Color color = GetBatteryColor(samples[i], sorted);
                    using Bitmap tinted = Colorize(normal, color);
                    e.Graphics.DrawImage(tinted, iconRect);
                }

                Rectangle labelRect = new(
                    (int)Math.Floor(x),
                    labelTop,
                    (int)Math.Ceiling(slotWidth),
                    Math.Max(1, labelHeight));
                using Font font = new("Segoe UI", 7.2f);
                TextRenderer.DrawText(
                    e.Graphics,
                    labels[i],
                    font,
                    labelRect,
                    _dark ? Color.WhiteSmoke : LightText,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.Top |
                    TextFormatFlags.WordBreak |
                    TextFormatFlags.NoPrefix |
                    TextFormatFlags.EndEllipsis);
            }
        }

        private int ScaleLogical(int logicalValue) =>
            PngIconCache.ScaleLogicalToInt(logicalValue, DeviceDpi);

        private Color GetBatteryColor(int battery, List<BatteryColorSettings> colors)
        {
            battery = Math.Clamp(battery, 0, 100);

            int activeIndex = -1;
            for (int i = 0; i < colors.Count; i++)
            {
                if (battery >= colors[i].MinimumPercent)
                {
                    activeIndex = i;
                    break;
                }
            }

            if (activeIndex < 0)
                activeIndex = colors.Count - 1;

            Color active = colors[activeIndex].Color;

            if (!_gradientToggle.Checked ||
                _gradientStep.Value <= 0 ||
                activeIndex >= colors.Count - 1)
                return active;

            Color lower = colors[activeIndex + 1].Color;
            int transition = Math.Clamp(_gradientStep.Value, 0, 50);
            int start = colors[activeIndex].MinimumPercent;
            int end = Math.Max(colors[activeIndex + 1].MinimumPercent, start - transition);

            if (battery >= end && battery < start)
            {
                double t = (start - battery) /
                           (double)Math.Max(1, start - end);
                return Blend(active, lower, t);
            }

            return active;
        }

        private static Color Blend(Color first, Color second, double t)
        {
            t = Math.Clamp(t, 0d, 1d);
            int r = (int)Math.Round(first.R + (second.R - first.R) * t);
            int g = (int)Math.Round(first.G + (second.G - first.G) * t);
            int b = (int)Math.Round(first.B + (second.B - first.B) * t);
            return Color.FromArgb(255, r, g, b);
        }

        private static Bitmap Colorize(Bitmap source, Color color)
        {
            Bitmap result = new(source.Width, source.Height, PixelFormat.Format32bppArgb);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Color pixel = source.GetPixel(x, y);
                    result.SetPixel(x, y, Color.FromArgb(pixel.A, color.R, color.G, color.B));
                }
            }
            return result;
        }

        private void LoadBitmaps()
        {
            _darkIcon = LoadIcon(true, "dark.ico");
            _lightIcon = LoadIcon(false, "light.ico");
            _darkCharging = LoadIcon(true, "dark_charging.ico");
            _lightCharging = LoadIcon(false, "light_charging.ico");
        }

        private static Bitmap? LoadIcon(bool darkMode, string file)
        {
            try
            {
                string path = AssetPaths.GetThemeIconPath(darkMode, file);
                if (!File.Exists(path))
                    return null;

                using Icon icon = new(path);
                using Bitmap bitmap = icon.ToBitmap();
                return new Bitmap(bitmap);
            }
            catch
            {
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _darkIcon?.Dispose();
                _lightIcon?.Dispose();
                _darkCharging?.Dispose();
                _lightCharging?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
