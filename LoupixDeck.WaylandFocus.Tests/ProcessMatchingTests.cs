using LoupixDeck.WaylandFocus;

namespace LoupixDeck.WaylandFocus.Tests;

/// <summary>
/// Exercises the layered /proc + desktop-file resolution against fake
/// process trees — the same shapes the live COSMIC session produced:
/// VS Code ("code" binary, resolvable via the desktop file's Exec) and
/// Vivaldi ("vivaldi-bin" binary, resolvable via StartupWMClass prefix,
/// with a swarm of --type= worker processes to filter out).
/// </summary>
public class ProcessMatchingTests
{
    [Fact]
    public void ExactCommMatch_WinsWithoutAnyDesktopFile()
    {
        using var proc = new FakeProc()
            .AddProcess(100, "sleep", "sleep", "30")
            .AddProcess(200, "kitty", "kitty");

        var matches = ProcessNamer.FindByAppId("kitty", proc.Root, []);

        var match = Assert.Single(matches);
        Assert.Equal(200, match.Pid);
        Assert.Equal("kitty", match.Name);
    }

    [Fact]
    public void VSCode_ResolvesViaDesktopFileExecBasename()
    {
        using var proc = new FakeProc()
            .AddProcess(44960, "code", "/usr/share/code/code")
            .AddProcess(45030, "code", "/usr/share/code/code", "--type=renderer", "--enable-gpu");
        var desktopDir = proc.AddDesktopDir(
            ("com.microsoft.VSCode.desktop",
            [
                "[Desktop Entry]",
                "Name=Visual Studio Code",
                "Exec=/usr/share/code/code %F",
                "StartupWMClass=com.microsoft.VSCode",
            ]));

        var matches = ProcessNamer.FindByAppId("com.microsoft.VSCode", proc.Root, [desktopDir]);

        // The renderer worker is filtered out; the main process remains.
        var match = Assert.Single(matches);
        Assert.Equal(44960, match.Pid);
        Assert.Equal("code", match.Name);
    }

    [Fact]
    public void Vivaldi_ResolvesViaStartupWmClassPrefix_AndDropsWorkers()
    {
        using var proc = new FakeProc()
            .AddProcess(33090, "vivaldi-bin", "/opt/vivaldi/vivaldi-bin", "--ozone-platform=wayland")
            .AddProcess(33147, "vivaldi-bin", "/opt/vivaldi/vivaldi-bin", "--type=gpu-process")
            .AddProcess(33180, "vivaldi-bin", "/opt/vivaldi/vivaldi-bin", "--type=renderer");
        var desktopDir = proc.AddDesktopDir(
            ("vivaldi-stable.desktop",
            [
                "[Desktop Entry]",
                "Name=Vivaldi",
                "Exec=/usr/bin/vivaldi-stable %U",
                "StartupWMClass=vivaldi",
            ]));

        var matches = ProcessNamer.FindByAppId("vivaldi-stable", proc.Root, [desktopDir]);

        var match = Assert.Single(matches);
        Assert.Equal(33090, match.Pid);
        Assert.Equal("vivaldi-bin", match.Name);
    }

    [Fact]
    public void SpaceRewrittenCmdline_IsStillDetectedAsWorker()
    {
        // The argv rewrite (setproctitle-style, observed with Chromium-family
        // apps) turns /proc cmdline into one space-joined string; the worker
        // marker must survive that rewrite.
        using var proc = new FakeProc()
            .AddProcessRaw(33090, "vivaldi-bin",
                "/opt/vivaldi/vivaldi-bin --ozone-platform=wayland --restart")
            .AddProcessRaw(33180, "vivaldi-bin",
                "/opt/vivaldi/vivaldi-bin --type=renderer --field-trial-handle=3");
        var desktopDir = proc.AddDesktopDir(
            ("vivaldi-stable.desktop",
            [
                "[Desktop Entry]",
                "Exec=/usr/bin/vivaldi-stable %U",
                "StartupWMClass=vivaldi",
            ]));

        var matches = ProcessNamer.FindByAppId("vivaldi-stable", proc.Root, [desktopDir]);

        var match = Assert.Single(matches);
        Assert.Equal(33090, match.Pid);
        Assert.Equal("vivaldi-bin", match.Name);
    }

    [Fact]
    public void LanguageServerWorkers_LoseToTheBareMainProcess()
    {
        // VS Code's language servers run as "code <script-path>" — no
        // --type= marker, but carrying arguments; the main process is bare.
        using var proc = new FakeProc()
            .AddProcess(44960, "code", "/usr/share/code/code")
            .AddProcess(45525, "code", "/usr/share/code/code", "/opt/app/jsonServerMain", "--node-ipc")
            .AddProcess(45613, "code", "/usr/share/code/code", "/home/user/.vscode/ext/server.bundle.js");
        var desktopDir = proc.AddDesktopDir(
            ("com.microsoft.VSCode.desktop",
            [
                "[Desktop Entry]",
                "Exec=/usr/share/code/code %F",
                "StartupWMClass=com.microsoft.VSCode",
            ]));

        var matches = ProcessNamer.FindByAppId("com.microsoft.VSCode", proc.Root, [desktopDir]);

        // The bare main process ranks first; workers stay listed but ranked below.
        Assert.Equal(44960, matches[0].Pid);
        Assert.Equal(3, matches.Count);
    }

    [Fact]
    public void FirefoxContentProcesses_AreDroppedViaContentProcMarker()
    {
        // Firefox stamps its content processes with -contentproc instead of
        // --type=; the worker-marker list covers both families.
        using var proc = new FakeProc()
            .AddProcess(700, "firefox", "/usr/lib/firefox/firefox")
            .AddProcess(710, "firefox", "/usr/lib/firefox/firefox", "-contentproc", "-childID", "3")
            .AddProcess(720, "firefox", "/usr/lib/firefox/firefox", "-contentproc", "-childID", "4");

        var matches = ProcessNamer.FindByAppId("firefox", proc.Root, []);

        var match = Assert.Single(matches);
        Assert.Equal(700, match.Pid);
        Assert.Equal("firefox", match.Name);
    }

    [Fact]
    public void FlatpakExec_DonatesTheAppId_NeverTheLauncher()
    {
        // "Exec=/usr/bin/flatpak run org.example.MyApp" must yield the app
        // id — donating "flatpak" would match every broker process for
        // every flatpak app's window.
        using var proc = new FakeProc()
            .AddProcess(900, "myapp", "/app/bin/myapp")
            .AddProcess(901, "flatpak", "/usr/bin/flatpak", "run", "--nofiles", "org.example.Other");
        var desktopDir = proc.AddDesktopDir(
            ("org.example.MyApp.desktop",
            [
                "[Desktop Entry]",
                "Exec=/usr/bin/flatpak run org.example.MyApp %U",
            ]));

        var matches = ProcessNamer.FindByAppId("org.example.MyApp", proc.Root, [desktopDir]);

        var match = Assert.Single(matches);
        Assert.Equal(900, match.Pid);
        Assert.Equal("myapp", match.Name);
    }

    [Fact]
    public void DesktopFileMatching_ByStartupWmClass_WhenStemDiffers()
    {
        using var proc = new FakeProc().AddProcess(500, "jetbrains-idea", "/opt/idea/bin/idea");
        var desktopDir = proc.AddDesktopDir(
            ("idea.desktop",
            [
                "[Desktop Entry]",
                "Exec=/opt/idea/bin/idea %F",
                "StartupWMClass=jetbrains-idea",
            ]));

        // app_id "jetbrains-idea" matches the StartupWMClass, not the file stem.
        var matches = ProcessNamer.FindByAppId("jetbrains-idea", proc.Root, [desktopDir]);

        Assert.Equal(500, Assert.Single(matches).Pid);
    }

    [Fact]
    public void MultipleMainProcesses_PreferNewest()
    {
        using var proc = new FakeProc()
            .AddProcess(100, "konversation", "konversation")
            .AddProcess(250, "konversation", "konversation");

        var matches = ProcessNamer.FindByAppId("konversation", proc.Root, []);

        Assert.Equal([250, 100], matches.Select(m => m.Pid).ToArray());
    }

    [Fact]
    public void NoMatch_ReturnsEmpty()
    {
        using var proc = new FakeProc().AddProcess(100, "something-else", "something-else");

        Assert.Empty(ProcessNamer.FindByAppId("unknown.app", proc.Root, []));
    }
}
