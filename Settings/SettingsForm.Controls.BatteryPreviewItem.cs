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
    private sealed class BatteryPreviewItem : Control
    {
        private const int PreviewIconLogicalSize = 24;
        private const int PreviewLabelHorizontalInsetLogical = 4;
        private const int PreviewMinimumLineHeightLogical = 14;
        private const int PreviewMinimumIconLogicalSize = 20;
        private const int PreviewIconLabelGapLogical = 2;

        private readonly BatteryPreviewKind _kind;
        private readonly Color _accentColor;
        private readonly string _label;
        private readonly bool _showTile;
        private readonly string? _levelIconStem;
        private Bitmap? _darkIcon;
        private Bitmap? _lightIcon;
        private Bitmap? _darkChargingIcon;
        private Bitmap? _lightChargingIcon;
        private Bitmap? _darkLevelIcon;
        private Bitmap? _lightLevelIcon;
        private bool _dark;

        public BatteryPreviewItem(
            BatteryPreviewKind kind,
            Color accentColor,
            string label,
            string? levelIconStem = null,
            bool showTile = false)
        {
            _kind = kind;
            _accentColor = accentColor;
            _label = label;
            _levelIconStem = levelIconStem;
            _showTile = showTile;

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            LoadBitmaps();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool DarkMode
        {
            get => _dark;
            set
            {
                if (_dark == value)
                    return;
                _dark = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (_showTile)
            {
                int tileInset = ScaleLogical(1);
                RectangleF tile = new(tileInset, tileInset, Math.Max(1, Width - ScaleLogical(2)), Math.Max(1, Height - ScaleLogical(2)));
                Color tileBackground = _dark ? Color.FromArgb(23, 27, 30) : Color.FromArgb(245, 247, 250);
                Color tileBorder = _dark ? Color.FromArgb(57, 63, 69) : Color.FromArgb(211, 216, 224);
                using Brush tileBrush = new SolidBrush(tileBackground);
                using Pen tilePen = new(tileBorder, 1f);
                e.Graphics.FillRoundedRectangle(tileBrush, tile, ScaleLogical(8));
                e.Graphics.DrawRoundedRectangle(tilePen, tile, ScaleLogical(8));
            }

            using Font labelFont = new("Segoe UI", 8.2f);
            int labelHorizontalInset = ScaleLogical(PreviewLabelHorizontalInsetLogical);
            int availableLabelWidth = Math.Max(1, Width - labelHorizontalInset * 2);
            Size singleLineSize = TextRenderer.MeasureText(
                _label,
                labelFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            int lineHeight = Math.Max(ScaleLogical(PreviewMinimumLineHeightLogical), TextRenderer.MeasureText(
                "Ag",
                labelFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height);
            bool needsSecondLine = singleLineSize.Width > availableLabelWidth;
            int desiredLabelHeight = lineHeight * (needsSecondLine ? 2 : 1);
            int minimumIconAreaHeight = ScaleLogical(PreviewMinimumIconLogicalSize) + ScaleLogical(PreviewIconLabelGapLogical);
            int labelHeight = Math.Min(
                desiredLabelHeight,
                Math.Max(lineHeight, Height - minimumIconAreaHeight));
            int iconAreaHeight = Math.Max(
                ScaleLogical(PreviewMinimumIconLogicalSize),
                Height - labelHeight - ScaleLogical(PreviewIconLabelGapLogical));
            int availableIconSize = Math.Max(
                1,
                Math.Min(
                    Width - ScaleLogical(12),
                    iconAreaHeight - ScaleLogical(PreviewIconLabelGapLogical)));
            int iconSize = Math.Min(ScaleLogical(PreviewIconLogicalSize), availableIconSize);

            float contentHeight = iconSize + labelHeight;
            float contentTop = _showTile
                ? Math.Max(0f, (Height - contentHeight) / 2f)
                : 1f;

            RectangleF iconRect = new(
                (Width - iconSize) / 2f,
                contentTop,
                iconSize,
                iconSize);

            Bitmap? source = _kind == BatteryPreviewKind.Charging
                ? (_dark ? _darkChargingIcon : _lightChargingIcon)
                : _kind == BatteryPreviewKind.Level
                    ? (_dark ? _darkLevelIcon : _lightLevelIcon)
                    : (_dark ? _darkIcon : _lightIcon);

            if (source != null)
            {
                switch (_kind)
                {
                    case BatteryPreviewKind.Glow:
                        DrawGlow(e.Graphics, source, iconRect, _accentColor);
                        e.Graphics.DrawImage(source, iconRect);
                        break;
                    case BatteryPreviewKind.Level:
                        // Use the exact tray assets already used by the application
                        // (dark_green/light_green, etc.). Do not alter tray rendering.
                        e.Graphics.DrawImage(source, iconRect);
                        break;
                    case BatteryPreviewKind.Solid:
                        using (Bitmap solid = Colorize(source, _accentColor))
                            e.Graphics.DrawImage(solid, iconRect);
                        break;
                    default:
                        e.Graphics.DrawImage(source, iconRect);
                        break;
                }
            }

            Color text = _dark ? Color.WhiteSmoke : LightText;
            Rectangle labelRect = _showTile
                ? new Rectangle(labelHorizontalInset, (int)Math.Round(contentTop + iconSize), availableLabelWidth, labelHeight)
                : new Rectangle(labelHorizontalInset, Height - labelHeight, availableLabelWidth, labelHeight);
            TextRenderer.DrawText(
                e.Graphics,
                _label,
                labelFont,
                labelRect,
                text,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
        }

        private int ScaleLogical(int logicalValue) =>
            PngIconCache.ScaleLogicalToInt(logicalValue, DeviceDpi);

        private void LoadBitmaps()
        {
            _darkIcon = LoadIconBitmap(true, "dark.ico");
            _lightIcon = LoadIconBitmap(false, "light.ico");
            _darkChargingIcon = LoadIconBitmap(true, "dark_charging.ico");
            _lightChargingIcon = LoadIconBitmap(false, "light_charging.ico");

            if (!string.IsNullOrWhiteSpace(_levelIconStem))
            {
                _darkLevelIcon = LoadIconBitmap(true, $"dark_{_levelIconStem}.ico");
                _lightLevelIcon = LoadIconBitmap(false, $"light_{_levelIconStem}.ico");
            }
        }

        private static Bitmap? LoadIconBitmap(bool darkMode, string fileName)
        {
            try
            {
                string path = AssetPaths.GetThemeIconPath(darkMode, fileName);
                if (!File.Exists(path))
                    return null;

                using Icon icon = new(path);
                using Bitmap source = icon.ToBitmap();
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
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

        private static void DrawGlow(Graphics graphics, Bitmap source, RectangleF bounds, Color glowColor)
        {
            using Bitmap tinted = Colorize(source, glowColor);
            ColorMatrix alphaMatrix = new();
            alphaMatrix.Matrix33 = 0.12f;
            using ImageAttributes attributes = new();
            attributes.SetColorMatrix(alphaMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

            for (int radius = 3; radius >= 1; radius--)
            {
                float alpha = 0.12f + (3 - radius) * 0.05f;
                alphaMatrix.Matrix33 = alpha;
                attributes.SetColorMatrix(alphaMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                graphics.DrawImage(
                    tinted,
                    Rectangle.Round(new RectangleF(bounds.X - radius, bounds.Y - radius, bounds.Width + radius * 2, bounds.Height + radius * 2)),
                    0,
                    0,
                    tinted.Width,
                    tinted.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _darkIcon?.Dispose();
                _lightIcon?.Dispose();
                _darkChargingIcon?.Dispose();
                _lightChargingIcon?.Dispose();
                _darkLevelIcon?.Dispose();
                _lightLevelIcon?.Dispose();
                _darkIcon = null;
                _lightIcon = null;
                _darkChargingIcon = null;
                _lightChargingIcon = null;
            }
            base.Dispose(disposing);
        }
    }
}
