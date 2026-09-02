using System.Text.RegularExpressions;
using SharpImage;

namespace QuickFixMyPics2;

/// <summary>
/// The output format the user can convert to. <see cref="KeepOriginal"/> leaves
/// each file in its current format (useful when only resizing).
/// </summary>
internal enum OutputFormat
{
    KeepOriginal,
    Png,
    Jpeg,
    Webp,
    Gif,
    Bmp,
    Tiff,
    Ico,
    Heic,
}

/// <summary>The user's choices for a conversion run.</summary>
internal sealed record ConversionOptions
{
    public OutputFormat Format { get; init; } = OutputFormat.KeepOriginal;

    /// <summary>Resize each image to fit within <see cref="MaxWidth"/>×<see cref="MaxHeight"/>.</summary>
    public bool Resize { get; init; }

    public int MaxWidth { get; init; } = 1920;

    public int MaxHeight { get; init; } = 1080;

    /// <summary>Delete each source file after it is successfully converted.</summary>
    public bool DeleteOriginals { get; init; }
}

/// <summary>The result of converting one file.</summary>
internal readonly record struct ConversionResult(string InputPath, string? OutputPath, string? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// Converts image files using SharpImage. Format is chosen by output extension;
/// resizing preserves aspect ratio (fit-within, never distorted); output never
/// clobbers an existing file (a "(n)" suffix is added when needed).
/// </summary>
internal static partial class ImageConversion
{
    /// <summary>File extensions we accept as input (drag-drop / Add / launch args).</summary>
    public static readonly IReadOnlySet<string> SupportedInputExtensions = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff",
        ".ico", ".heic", ".heif", ".avif", ".tga", ".dds", ".psd", ".qoi",
        ".svg", ".jxl", ".hdr", ".exr",
    };

    public static bool IsSupportedInput(string path) =>
        SupportedInputExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Input extensions we can also *write* back out. A few formats decode-only (e.g. .avif, .jxl):
    /// "Keep original format" on one of those falls back to PNG so the conversion still succeeds.
    /// </summary>
    private static readonly IReadOnlySet<string> WritableExtensions = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff",
        ".ico", ".heic", ".heif", ".tga", ".dds", ".psd", ".qoi", ".svg", ".hdr", ".exr",
    };

    /// <summary>The file extension (including dot) a format writes to.</summary>
    public static string Extension(OutputFormat format) => format switch
    {
        OutputFormat.Png => ".png",
        OutputFormat.Jpeg => ".jpg",
        OutputFormat.Webp => ".webp",
        OutputFormat.Gif => ".gif",
        OutputFormat.Bmp => ".bmp",
        OutputFormat.Tiff => ".tiff",
        OutputFormat.Ico => ".ico",
        OutputFormat.Heic => ".heic",
        _ => string.Empty,
    };

    /// <summary>Converts a single file. Never throws — failures are returned as an error.</summary>
    public static ConversionResult ConvertOne(string inputPath, ConversionOptions options)
    {
        // Convert into memory, write the output, and only THEN delete the source. The
        // original is never removed before a complete converted copy exists on disk —
        // a failed write must never cost the user their file. Many a slip betwixt cup
        // and lip: we don't assume it's safe, we make it safe.
        string? tempPath = null;
        bool sourceDeleted = false;
        try
        {
            string directory = Path.GetDirectoryName(inputPath) ?? ".";
            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            string targetExt = options.Format == OutputFormat.KeepOriginal
                ? Path.GetExtension(inputPath)
                : Extension(options.Format);

            // "Keep original format" on a decode-only input (e.g. AVIF/JXL — we can read them but not
            // write them) would fail; fall back to PNG so resizing/keeping such files still works.
            if (options.Format == OutputFormat.KeepOriginal && !WritableExtensions.Contains(targetExt))
            {
                targetExt = ".png";
            }

            using var pipeline = ImagePipeline.Load(inputPath);

            // Compose the requested resize with any format-imposed limit, then
            // resize once. ICO cannot exceed 256×256, so clamp to that for icons
            // regardless of the resize toggle (mirrors the original app's behaviour).
            var (curW, curH) = pipeline.Size;
            int targetW = (int)curW;
            int targetH = (int)curH;

            if (options.Resize)
            {
                // Resize scales up as well as down, so the user can enlarge small images too.
                (targetW, targetH) = FitWithin(targetW, targetH, options.MaxWidth, options.MaxHeight, allowUpscale: true);
            }

            if (options.Format == OutputFormat.Ico)
            {
                (targetW, targetH) = FitWithin(targetW, targetH, 256, 256);
            }

            if (targetW != curW || targetH != curH)
            {
                pipeline.Resize(targetW, targetH);
            }

            // Write the converted image to a sibling temp file FIRST. If Save throws,
            // the source is still untouched — nothing is lost.
            tempPath = EnsureUnique(Path.Combine(directory, baseName + ".qfmp-converting" + targetExt));
            pipeline.Save(tempPath);

            // The converted bytes are now safely on disk. ONLY NOW is it safe to remove
            // the source (if requested) — deleting it frees its name so a same-format
            // conversion can take the exact original name below.
            if (options.DeleteOriginals)
            {
                File.Delete(inputPath);
                sourceDeleted = true;
            }

            string outputPath = EnsureUnique(Path.Combine(directory, baseName + targetExt));
            File.Move(tempPath, outputPath);
            tempPath = null; // moved into place — nothing left to clean up

            return new ConversionResult(inputPath, outputPath, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return new ConversionResult(inputPath, null, ex.Message);
        }
        finally
        {
            // Clean up a leftover temp file — but ONLY while the source still exists as a
            // fallback. If the source was already deleted and the final move didn't finish,
            // this temp is the sole surviving copy of the user's converted image, so we keep
            // it rather than delete their only data.
            if (tempPath is not null && !sourceDeleted && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Scales (origW, origH) to fit within (maxW, maxH) while preserving aspect ratio.
    /// With <paramref name="allowUpscale"/> false (the default) it only shrinks — an image already
    /// inside the box is returned unchanged (a "fit within a maximum" cap, e.g. the 256px ICO clamp).
    /// With <paramref name="allowUpscale"/> true it scales up *or* down so the image fills the box as
    /// large as possible (the Resize option, which lets the user enlarge as well as shrink).
    /// Each dimension is at least 1px.
    /// </summary>
    public static (int Width, int Height) FitWithin(int origW, int origH, int maxW, int maxH, bool allowUpscale = false)
    {
        if (origW <= 0 || origH <= 0)
        {
            return (Math.Max(1, origW), Math.Max(1, origH));
        }

        double scale = Math.Min((double)maxW / origW, (double)maxH / origH);
        if (!allowUpscale && scale >= 1.0)
        {
            return (origW, origH); // already fits — cap only, don't upscale
        }

        int w = Math.Max(1, (int)Math.Round(origW * scale));
        int h = Math.Max(1, (int)Math.Round(origH * scale));
        return (w, h);
    }

    /// <summary>
    /// Returns <paramref name="path"/> if free, otherwise the first available
    /// "name (n).ext" variant. Mirrors the original app's duplicate handling.
    /// </summary>
    public static string EnsureUnique(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path) ?? ".";
        string ext = Path.GetExtension(path);
        string name = Path.GetFileNameWithoutExtension(path);

        // If the name already ends in " (n)", continue counting from n.
        int counter = 1;
        Match m = TrailingCounter().Match(name);
        if (m.Success)
        {
            name = m.Groups[1].Value.TrimEnd();
            counter = int.Parse(m.Groups[2].Value) + 1;
        }

        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name} ({counter}){ext}");
            counter++;
        }
        while (File.Exists(candidate));

        return candidate;
    }

    [GeneratedRegex(@"^(.*)\((\d+)\)\s*$")]
    private static partial Regex TrailingCounter();
}
