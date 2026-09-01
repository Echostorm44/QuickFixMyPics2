using System.Reflection;
using Cascade.UI.Installer;

namespace QuickFixMyPics2;

/// <summary>
/// The install/uninstall declaration for Quick Fix My Pics, consumed by <c>cascade package --installer</c>
/// to produce the single-file setup exe. A per-user install (no admin): files land under LocalAppData,
/// shortcuts on the Start Menu + Desktop, and a right-click <b>"Convert with Quick Fix My Pics"</b> verb is
/// added for each supported image type. Everything it writes is recorded in the install manifest, so the
/// bundled uninstaller removes it completely — files, shortcuts, the context-menu verbs, and the A/R/P entry.
/// </summary>
[Installer]
#pragma warning disable CA1812 // instantiated via reflection by the cascade package command
internal sealed class QuickFixMyPicsInstaller : CascadeInstaller
{
    /// <summary>
    /// The image types we accept as input — each gets the right-click "Convert…" verb via a
    /// per-extension <c>SystemFileAssociations\.ext\shell</c> entry (appears ONLY on these types,
    /// and never touches their default open handler). Kept in sync with
    /// <see cref="ImageConversion.SupportedInputExtensions"/>.
    /// </summary>
    private static readonly string[] ImageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff",
        ".ico", ".heic", ".heif", ".avif", ".tga", ".dds", ".psd", ".qoi",
        ".svg", ".jxl", ".hdr", ".exr",
    ];

    public override InstallerConfig Configure() => new()
    {
        AppId         = "01F8E2F6-62F4-45C9-9D72-36EE16623EBB",
        AppName       = "QuickFixMyPics2",           // drives the exe name (QuickFixMyPics2.exe) + install folder
        Version       = ResolveVersion(),
        Publisher     = "Echostorm",
        Description   = "Quick, private, offline batch image conversion and resizing.",
        InstallDir    = InstallDir.LocalAppData("QuickFixMyPics2"),
        Output        = "QuickFixMyPics2-Setup",
        RequiresAdmin = false,                         // per-user: LocalAppData + HKCU only
        Shortcuts =
        [
            new Shortcut { Name = "Quick Fix My Pics", TargetPath = "QuickFixMyPics2.exe", Location = ShortcutLocation.StartMenu },
            new Shortcut { Name = "Quick Fix My Pics", TargetPath = "QuickFixMyPics2.exe", Location = ShortcutLocation.Desktop },
        ],
        ContextMenuEntries =
        [
            new ShellContextMenuEntry
            {
                Label      = "Convert with Quick Fix My Pics",
                Command    = "QuickFixMyPics2.exe",    // resolved to the install dir; invoked as "…exe" "%1"
                Extensions = ImageExtensions,          // per-extension → shows only on supported images
                // IconPath omitted → the engine uses the app's own embedded icon.
            },
        ],
    };

    public override IReadOnlyList<InstallFile> Files =>
    [
        // The whole published app (exe + Cascade.UI/Etch/SharpImage + native deps) staged by the packager.
        InstallFile.Directory("publish/*", dest: Dir.App, recursive: true),
    ];

    /// <summary>
    /// The app assembly's version (this type is loaded from the QuickFixMyPics2 assembly by the wizard),
    /// as <c>major.minor.patch</c>; falls back to 1.0.0 when unset. CI stamps the tag version into the
    /// build, so the setup, the A/R/P entry, and upgrade detection all report the released version.
    /// </summary>
    private static string ResolveVersion()
    {
        Version? v = typeof(QuickFixMyPicsInstaller).Assembly.GetName().Version;
        return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
#pragma warning restore CA1812
