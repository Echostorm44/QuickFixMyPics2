using Cascade.UI;
using IOPath = System.IO.Path;

namespace QuickFixMyPics2;

/// <summary>
/// The single window of Quick Fix My Pics — a drop target + file list, format / resize / delete options, and a Convert
/// action. Files arrive via launch arguments, Explorer drag-drop, a second (routed) launch, or the Add button.
/// </summary>
internal sealed class MainView : Component
{
    // ── Palette (Apple dark) ──────────────────────────────────────────
    private static readonly ColorValue Surface = new("#1C1C1E");
    private static readonly ColorValue SurfaceRaised = new("#2C2C2E");
    private static readonly ColorValue Hairline = new("#3A3A3C");
    private static readonly ColorValue Accent = new("#0A84FF");
    private static readonly ColorValue TextPrimary = new("#FFFFFF");
    private static readonly ColorValue TextSecondary = new("#98989E");
    private static readonly ColorValue Danger = new("#FF453A");

    private static readonly IReadOnlyList<SelectOption<OutputFormat>> FormatOptions =
        [new(OutputFormat.KeepOriginal, "Keep original format"),
        new(OutputFormat.Jpeg, "JPG"),
        new(OutputFormat.Png, "PNG"), new(OutputFormat.Webp, "WEBP"),
        new(OutputFormat.Gif, "GIF"), new(OutputFormat.Bmp, "BMP"),
        new(OutputFormat.Tiff, "TIFF"), new(OutputFormat.Ico, "ICO"),
        new(OutputFormat.Heic, "HEIC"),];

    // ── State ─────────────────────────────────────────────────────────
    private readonly List<string> files = [];
    private readonly HashSet<string> failedPaths = [];

    private OutputFormat format = OutputFormat.KeepOriginal;
    private bool resize;
    private int maxWidth = 1920;
    private int maxHeight = 1080;
    private bool deleteOriginals;

    private bool converting;
    private string status = "";

    public MainView()
    {
        // Drain files supplied on the command line, then listen for live sources.
        AddFiles(FileIntake.TakePending());
        FileIntake.FilesReceived += OnFilesReceived;

        // The window is constructing — this launch reached a healthy state, so defuse the
        // updater's crash-rollback for the (possibly just-applied) version.
        if (Cascade.UI.Installer.Update.Updater.IsConfigured)
        {
            Cascade.UI.Installer.Update.Updater.MarkHealthy();
        }
    }

    private void OnFilesReceived(IReadOnlyList<string> paths)
    {
        // Already on the UI thread (window proc / dispatcher). Update + re-render.
        AddFiles(paths);
        Invalidate();
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (!files.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(path);
            }
        }
    }

    // ── Render ────────────────────────────────────────────────────────

    protected override Node Render() =>
        new Column(
            spacing: 20,
            children: [FileArea().Expand(), OptionsPanel(), ActionArea(),])
        .Padding(EdgeInsets.All(28))
        .Background(new ColorValue("#000000"));

    // ── File area: drop zone (empty) or file list ─────────────────────

    private Node FileArea()
    {
        Node inner = files.Count == 0 ? EmptyDropZone() : FileList();

        return new Column(children: [inner.Expand()])
            .Background(Surface)
            .CornerRadius(14)
            .Border(Hairline, 1, 14)
            .Padding(EdgeInsets.All(files.Count == 0 ? 0 : 10));
    }

    // Lucide "image" glyph — anchors the empty state
    private static readonly Icon PhotoIcon = new(["M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z", "M11 9a2 2 0 1 1-4 0 2 2 0 0 1 4 0z", "m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21",],
        new Size(24, 24), 24f, "Images");

    private Node EmptyDropZone() =>
        new Center(
            new Column(
                spacing: 16,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: [ new IconView(PhotoIcon, size: 46).Color(new ColorValue("#55555A")), new Column(
                        spacing: 6,
                        crossAxisAlignment: CrossAxisAlignment.Center,
                        children:[ new Label("Drop images here")
                                .FontSize(18).Color(TextPrimary), new Label("PNG · JPG · WEBP · HEIC and more")
                                .FontSize(13).Color(TextSecondary), ]), new Button("Add Images", () => _ = AddViaPickerAsync())
                        .Margin(0, 6), ]));

    // Lucide "x" — used as the per-row remove affordance.
    private static readonly Icon CloseIcon = new(["M18 6 6 18", "m6 6 12 12"], new Size(24, 24), 16f, "Remove");

    private Node FileList()
    {
        var rows = new List<Node>(files.Count);
        foreach (string path in files)
        {
            rows.Add(FileRow(path));
        }

        return new Column(
            spacing: 12,
            children: [ new Row(
                    crossAxisAlignment: CrossAxisAlignment.Center,
                    children:[ new Label($"{files.Count} image{(files.Count == 1 ? "" : "s")}")
                            .FontSize(13).Color(TextSecondary),
                        new Spacer(),
                        new Button("Add", () => _ = AddViaPickerAsync()).Variant("ghost"),
                        new Button("Clear", ClearFiles).Variant("ghost"), ]),
                        new ListView<string>(files.ToArray(), FileRow)
                    .ItemHeight(56f)
                    .Reorderable(true)
                    .OnReorder((from, to) =>
                    {
                        if (from < 0 || from >= files.Count)
                        {
                            return;
                        }
                        string item = files[from];
                        files.RemoveAt(from);
                        files.Insert(Math.Clamp(to, 0, files.Count), item);
                        Invalidate();
                    })
                    .Expand(), ]);
    }

    private Node FileRow(string path)
    {
        bool failed = failedPaths.Contains(path);
        string? dir = IOPath.GetDirectoryName(path);
        string folder = IOPath.GetFileName(dir ?? string.Empty);
        if (folder.Length == 0)
        {
            folder = dir ?? string.Empty;
        }

        return new Row(
            spacing: 12,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: [ new Image(path).Size(40, 40).Fit(ImageFit.Cover).CornerRadius(8), new Column(
                    spacing: 2,
                    crossAxisAlignment: CrossAxisAlignment.Start,
                    children:[ new Label(IOPath.GetFileName(path))
                            .FontSize(14).Color(failed ? Danger : TextPrimary)
                            .MaxLines(1).Overflow(TextOverflow.Ellipsis), new Label(failed ? "Couldn't convert" : folder)
                            .FontSize(11).Color(failed ? Danger : TextSecondary)
                            .MaxLines(1).Overflow(TextOverflow.Ellipsis), ]).Expand(), new IconButton(CloseIcon, () => RemoveFile(path)), ])
            .Padding(EdgeInsets.Symmetric(horizontal: 10, vertical: 8))
            .Background(SurfaceRaised)
            .CornerRadius(10);
    }

    // ── Options ───────────────────────────────────────────────────────

    private Node OptionsPanel()
    {
        var children = new List<Node>
        {
            new Row(
                crossAxisAlignment: CrossAxisAlignment.Center,
                children:[ new Label("Convert to").FontSize(15).Color(TextPrimary).Expand(), new Select<OutputFormat>(
                        Bind(format, v => format = v),
                        FormatOptions).Width(220), ]),
            Divider(),
            new Toggle(
                Bind(resize, v =>
        {
            resize = v;
            Invalidate();
        }),
                "Resize",
                "Scale to fit these dimensions, up or down — aspect ratio is always locked"),
        };

        if (resize)
        {
            children.Add(new Row(
                spacing: 10,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: [ new Label("Fit size").FontSize(14).Color(TextSecondary).Expand(), new NumberInput<int>(
                        Bind(maxWidth, v => maxWidth = v),
                        min: 1, max: 20000, step: 1, format: "0").Width(90), new Label("×").FontSize(14).Color(TextSecondary), new NumberInput<int>(
                        Bind(maxHeight, v => maxHeight = v),
                        min: 1, max: 20000, step: 1, format: "0").Width(90), new Label("px").FontSize(13).Color(TextSecondary), ]));
        }

        children.Add(Divider());
        children.Add(new Toggle(
            Bind(deleteOriginals, v => deleteOriginals = v),
            "Delete originals",
            "Remove each source file after it is converted"));

        return new Column(spacing: 14, children: [.. children])
            .Padding(EdgeInsets.All(18))
            .Background(Surface)
            .CornerRadius(14)
            .Border(Hairline, 1, 14);
    }

    private static Node Divider() =>
        new Column(children: []).Height(1).Background(new ColorValue("#2A2A2C"));

    // ── Action area ───────────────────────────────────────────────────

    private Node ActionArea()
    {
        bool canConvert = files.Count > 0 && !converting;
        string label = converting
            ? "Converting…"
            : files.Count == 0
                ? "Convert"
                : $"Convert {files.Count} image{(files.Count == 1 ? "" : "s")}";

        var children = new List<Node>
        {
            new Button(label, () => _ = ConvertAsync())
                .Disabled(!canConvert)
                .Height(44),
        };

        if (converting)
        {
            // Indeterminate (animated) — a single image finishes as one unit, so a determinate bar
            // just sat at 0 then jumped to done. The status label carries batch position.
            children.Add(new ProgressBar(ProgressMode.Indeterminate).FillColor(Accent).Height(6));
        }

        if (status.Length > 0)
        {
            children.Add(new Label(status)
                .FontSize(13)
                .Color(failedPaths.Count > 0 ? Danger : TextSecondary)
                .TextAlign(TextAlignment.Center));
        }

        return new Column(spacing: 10, children: [.. children]);
    }

    // ── Handlers ──────────────────────────────────────────────────────

    private void RemoveFile(string path)
    {
        files.Remove(path);
        failedPaths.Remove(path);
        Invalidate();
    }

    private void ClearFiles()
    {
        files.Clear();
        failedPaths.Clear();
        status = "";
        Invalidate();
    }

    private async Task AddViaPickerAsync()
    {
        var results = await FilePicker.OpenMultipleAsync(
            title: "Add images",
            filters: [ new FileFilter("Images",
                    "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp",
                    "*.tif", "*.tiff", "*.ico", "*.heic", "*.heif", "*.avif"), new FileFilter("All files", "*.*"), ]);

        // Resumes on the UI thread (Cascade synchronization context).
        AddFiles(results.Select(r => r.Path).Where(ImageConversion.IsSupportedInput));
        Invalidate();
    }

    private async Task ConvertAsync()
    {
        if (files.Count == 0 || converting)
        {
            return;
        }

        var options = new ConversionOptions
        {
            Format = format,
            Resize = resize,
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            DeleteOriginals = deleteOriginals,
        };

        converting = true;
        status = "";
        failedPaths.Clear();
        Invalidate();

        var batch = files.ToArray();
        var succeeded = new List<string>();
        int failed = 0;

        for (int i = 0; i < batch.Length; i++)
        {
            string input = batch[i];

            // Show which file is in flight (and batch position) — a single conversion otherwise
            // looks frozen. The decode/encode runs off the UI thread below.
            status = batch.Length > 1
                ? $"Converting {IOPath.GetFileName(input)} ({i + 1}/{batch.Length})…"
                : $"Converting {IOPath.GetFileName(input)}…";
            Invalidate();

            var result = await Task.Run(() => ImageConversion.ConvertOne(input, options));

            if (result.Succeeded)
            {
                succeeded.Add(input);
            }
            else
            {
                failed++;
                failedPaths.Add(input);
            }

            // Resumes on the UI thread — safe to touch state and re-render.
            Invalidate();
        }

        // Drop the ones that converted; keep failures visible for a retry.
        foreach (string done in succeeded)
        {
            files.Remove(done);
        }

        converting = false;
        int ok = succeeded.Count;
        status = failed == 0
            ? $"Converted {ok} image{(ok == 1 ? "" : "s")}"
            : $"Converted {ok}, {failed} failed";
        Invalidate();
    }
}
