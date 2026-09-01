namespace QuickFixMyPics2;

/// <summary>
/// A tiny bridge between OS-level file sources (launch arguments, Explorer
/// drag-drop, a second single-instance launch) wired up in <c>Program</c>, and
/// the <see cref="MainView"/> that owns the file list.
///
/// <para>
/// Launch arguments arrive before the view is mounted, so they are buffered in
/// <see cref="TakePending"/>. Later sources (drops, second-instance launches)
/// arrive after mount and fire <see cref="FilesReceived"/> directly. Both run on
/// the UI thread (WM_DROPFILES is handled in the window proc; the second-instance
/// callback is marshalled via the dispatcher), so subscribers may update state
/// and re-render without further marshalling.
/// </para>
/// </summary>
internal static class FileIntake
{
    private static readonly List<string> pending = [];

    /// <summary>Raised when files arrive after the view is listening.</summary>
    public static event Action<IReadOnlyList<string>>? FilesReceived;

    /// <summary>Buffers launch-time file arguments until the view drains them.</summary>
    public static void QueueInitial(IEnumerable<string> paths)
    {
        pending.AddRange(FilterImages(paths));
    }

    /// <summary>Returns and clears any buffered launch-time files.</summary>
    public static IReadOnlyList<string> TakePending()
    {
        if (pending.Count == 0)
        {
            return [];
        }

        var result = pending.ToArray();
        pending.Clear();
        return result;
    }

    /// <summary>Delivers files from a live source (drop or second instance).</summary>
    public static void Receive(IEnumerable<string> paths)
    {
        var images = FilterImages(paths);
        if (images.Count > 0)
        {
            FilesReceived?.Invoke(images);
        }
    }

    // Accept only real image files, and expand any dropped directories one level
    // so dropping a folder of pictures "just works".
    private static List<string> FilterImages(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    if (ImageConversion.IsSupportedInput(file))
                    {
                        result.Add(file);
                    }
                }
            }
            else if (File.Exists(path) && ImageConversion.IsSupportedInput(path))
            {
                result.Add(path);
            }
        }

        return result;
    }
}
