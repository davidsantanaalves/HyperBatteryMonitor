using HyperXBatteryTray;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace HyperXBatteryTray.Settings;

internal sealed class PngIconCache : IDisposable
{
    internal const int LogicalDpi = 96;

    private readonly Dictionary<BitmapCacheKey, Bitmap> _bitmaps = new();
    private readonly Dictionary<AssetSizeCacheKey, int[]> _assetSizes = new();
    private bool _disposed;

    public Bitmap Get(string iconKey, bool darkMode, int sizePx, int dpi = LogicalDpi)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int requestedSize = Math.Max(1, sizePx);
        BitmapCacheKey cacheKey = new(iconKey, darkMode, requestedSize, Math.Max(1, dpi));
        if (_bitmaps.TryGetValue(cacheKey, out Bitmap? bitmap))
            return bitmap;

        string filePath = GetIconPath(iconKey, darkMode, requestedSize);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PNG icon '{Path.GetFileName(filePath)}' was not found.", filePath);

        using Bitmap source = new(filePath);
        bitmap = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        _bitmaps.Add(cacheKey, bitmap);
        return bitmap;
    }

    public void Draw(Graphics graphics, string iconKey, RectangleF bounds, bool darkMode, int dpi)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        int targetSize = Math.Max(1, (int)Math.Ceiling(Math.Max(bounds.Width, bounds.Height)));
        int assetSize = ResolveAssetSize(iconKey, darkMode, targetSize);
        Bitmap bitmap = Get(iconKey, darkMode, assetSize, dpi);

        GraphicsState state = graphics.Save();
        try
        {
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(bitmap, bounds);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    public void DrawLogical(Graphics graphics, string iconKey, RectangleF logicalBounds, bool darkMode, int dpi)
    {
        Draw(graphics, iconKey, ScaleLogical(logicalBounds, dpi), darkMode, dpi);
    }

    public void Prewarm(IEnumerable<PngIconRequest> requests, bool darkMode, int dpi)
    {
        foreach (PngIconRequest request in requests)
        {
            int targetSize = ScaleLogicalToInt(request.LogicalSize, dpi);
            int assetSize = ResolveAssetSize(request.IconKey, darkMode, targetSize);
            _ = Get(request.IconKey, darkMode, assetSize, dpi);
        }
    }

    public void ClearBitmaps()
    {
        foreach (Bitmap bitmap in _bitmaps.Values)
            bitmap.Dispose();
        _bitmaps.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ClearBitmaps();
        _assetSizes.Clear();
        _disposed = true;
    }

    internal static float ScaleLogical(float logicalValue, int dpi)
    {
        int effectiveDpi = Math.Max(1, dpi);
        return logicalValue * effectiveDpi / LogicalDpi;
    }

    internal static int ScaleLogicalToInt(int logicalValue, int dpi)
    {
        return Math.Max(1, (int)Math.Round(ScaleLogical(logicalValue, dpi), MidpointRounding.AwayFromZero));
    }

    internal static RectangleF ScaleLogical(RectangleF logicalBounds, int dpi)
    {
        float scale = Math.Max(1, dpi) / (float)LogicalDpi;
        return new RectangleF(
            logicalBounds.X * scale,
            logicalBounds.Y * scale,
            logicalBounds.Width * scale,
            logicalBounds.Height * scale);
    }

    private static string GetIconPath(string iconKey, bool darkMode, int sizePx)
    {
        string suffix = darkMode ? "dark" : "light";
        string directory = AssetPaths.GetThemeIconsDirectory(darkMode);

        string standardPath = Path.Combine(
            directory,
            $"{iconKey}-{suffix}-{sizePx}x{sizePx}.png");
        if (File.Exists(standardPath))
            return standardPath;

        string themePrefixPath = Path.Combine(
            directory,
            $"{suffix}_{iconKey}-{sizePx}x{sizePx}.png");
        if (File.Exists(themePrefixPath))
            return themePrefixPath;

        string sharedPath = Path.Combine(
            AssetPaths.CommonIconsDirectory,
            $"{iconKey}-{sizePx}x{sizePx}.png");

        return File.Exists(sharedPath)
            ? sharedPath
            : standardPath;
    }

    private int ResolveAssetSize(string iconKey, bool darkMode, int requestedSize)
    {
        int[] availableSizes = GetAvailableAssetSizes(iconKey, darkMode);
        if (availableSizes.Length == 0)
        {
            string theme = darkMode ? "Dark" : "Light";
            throw new FileNotFoundException(
                $"No PNG assets were found for icon '{iconKey}' in theme '{theme}'.");
        }

        foreach (int size in availableSizes)
        {
            if (size >= requestedSize)
                return size;
        }

        return availableSizes[^1];
    }

    private int[] GetAvailableAssetSizes(string iconKey, bool darkMode)
    {
        AssetSizeCacheKey cacheKey = new(iconKey, darkMode);
        if (_assetSizes.TryGetValue(cacheKey, out int[]? cached))
            return cached;

        string suffix = darkMode ? "dark" : "light";
        string themeDirectory = AssetPaths.GetThemeIconsDirectory(darkMode);
        string sharedDirectory = AssetPaths.CommonIconsDirectory;

        List<int> sizes = new();
        CollectAssetSizes(
            sizes,
            themeDirectory,
            new[] { $"{iconKey}-{suffix}-", $"{suffix}_{iconKey}-" });
        CollectAssetSizes(
            sizes,
            sharedDirectory,
            new[] { $"{iconKey}-" });

        int[] resolved = sizes.Distinct().OrderBy(size => size).ToArray();
        _assetSizes.Add(cacheKey, resolved);
        return resolved;
    }

    private static void CollectAssetSizes(
        ICollection<int> sizes,
        string directory,
        IEnumerable<string> prefixes)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (string prefix in prefixes)
        {
            foreach (string filePath in Directory.EnumerateFiles(
                directory,
                $"{prefix}*x*.png",
                SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string dimensions = fileName[prefix.Length..];
                int separator = dimensions.IndexOf('x');
                if (separator <= 0)
                    continue;

                if (!int.TryParse(
                        dimensions[..separator],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int width) ||
                    width <= 0)
                {
                    continue;
                }

                if (!int.TryParse(
                        dimensions[(separator + 1)..],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int height) ||
                    height != width)
                {
                    continue;
                }

                sizes.Add(width);
            }
        }
    }

    private readonly record struct BitmapCacheKey(string IconKey, bool DarkMode, int SizePx, int Dpi);
    private readonly record struct AssetSizeCacheKey(string IconKey, bool DarkMode);
}

internal readonly record struct PngIconRequest(string IconKey, int LogicalSize);
