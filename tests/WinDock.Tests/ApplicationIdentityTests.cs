using System.IO;
using System.Runtime.InteropServices;
using WinDock;
using WinDock.Core;

internal static partial class Program
{
    private static void RunApplicationIdentityTests(string temporary)
    {
        Run("Shell catalogue desktop apps match their confirmed executable for indicators, activation and closure", () =>
        {
            var executable = Path.Combine(temporary, "Firefox Developer Edition", "firefox.exe");
            var unrelated = Path.Combine(temporary, "Firefox", "firefox.exe");
            var entry = new DockEntry(Guid.NewGuid(), "managed.lnk", SourcePath: "shell:AppsFolder\\desktop-firefox", ApplicationId: "desktop-firefox");
            var resolver = new ApplicationIdentityResolver(id => id == "desktop-firefox" ? new(executable, false) : null);
            var running = new RunningApplications(resolver);
            Check(running.Snapshot([entry], [executable], []).SetEquals([entry.Id]));
            Check(running.Snapshot([entry], [unrelated], []).Count == 0);
            Check(running.Snapshot([entry], [], []).Count == 0);
            ApplicationWindow[] windows = [new(1, 10, executable, null), new(2, 20, unrelated, null)];
            var closed = new List<nint>(); var activated = new List<nint>();
            var service = new ApplicationWindows(() => windows, h => windows.Single(w => w.Handle == h),
                h => { closed.Add(h); return 0; }, h => { activated.Add(h); return true; }, resolver);
            Check(service.CountAsync(entry, default).GetAwaiter().GetResult() == 1);
            Check(service.ActivateAsync(entry, windows[0], default).GetAwaiter().GetResult() == WindowActivationResult.Activated);
            Check(service.CloseAsync(entry, default).GetAwaiter().GetResult() == 0);
            Check(activated.SequenceEqual(new nint[] { 1 }) && closed.SequenceEqual(new nint[] { 1 }));
            // Identity can also be recovered from existing Shell source metadata alone.
            Check(running.Snapshot([entry with { ApplicationId = null }], [executable], []).Contains(entry.Id));
        });
        Run("Packaged and unresolved Shell apps never fall back to a shared executable", () =>
        {
            var executable = Path.Combine(temporary, "host.exe");
            var packaged = new DockEntry(Guid.NewGuid(), executable, SourcePath: "shell:AppsFolder\\package!app", ApplicationId: "package!app");
            var resolver = new ApplicationIdentityResolver(_ => new(executable, true));
            var tracker = new RunningApplications(resolver);
            Check(tracker.Snapshot([packaged], [executable], []).Count == 0);
            Check(tracker.Snapshot([packaged], [], ["package!app"]).Contains(packaged.Id));
            ApplicationWindow[] windows = [new(1, 10, executable, "other!app"), new(2, 20, executable, "package!app")];
            var service = new ApplicationWindows(() => windows, _ => null, _ => 0, identities: resolver);
            Check(service.ListAsync(packaged, default).GetAwaiter().GetResult().Single().Handle == 2);
            var unavailable = new ApplicationIdentityResolver(_ => throw new COMException("Unavailable Shell metadata"));
            var ordinary = new DockEntry(Guid.NewGuid(), executable);
            Check(new RunningApplications(unavailable).Snapshot([packaged, ordinary], [executable], []).SetEquals([ordinary.Id]));
            var classic = ordinary with { ApplicationId = "classic-desktop" };
            var emptyMetadata = new ApplicationIdentityResolver(_ => new(null, false));
            Check(new RunningApplications(emptyMetadata).Snapshot([classic], [executable], []).SetEquals([classic.Id]));
        });
        Run("Shell identity cache is shared between indicator and window services and expires after thirty seconds", () =>
        {
            var clock = DateTime.UtcNow; var reads = 0;
            var target = Path.Combine(temporary, "first.exe");
            var resolver = new ApplicationIdentityResolver(_ => { reads++; return new(target, false); }, () => clock);
            var entry = new DockEntry(Guid.NewGuid(), "shell.lnk", SourcePath: "shell:AppsFolder\\test");
            var tracker = new RunningApplications(resolver);
            Check(tracker.Snapshot([entry], [target], []).Contains(entry.Id));
            var window = new ApplicationWindow(1, 10, target, null);
            var service = new ApplicationWindows(() => [window], _ => window, _ => 0, identities: resolver);
            Check(service.CountAsync(entry, default).GetAwaiter().GetResult() == 1 && reads == 1);
            var previous = target; target = Path.Combine(temporary, "second.exe");
            clock = clock.AddSeconds(31);
            Check(tracker.Snapshot([entry], [previous], []).Count == 0 && reads == 2);
            Check(tracker.Snapshot([entry], [target], []).Contains(entry.Id) && reads == 2);
            var missingReads = 0;
            var missing = new ApplicationIdentityResolver(_ => { missingReads++; return null; }, () => clock);
            Check(missing.Resolve(entry).Executable is null && missing.Resolve(entry).Executable is null && missingReads == 1);
            clock = clock.AddSeconds(31); missing.Resolve(entry); Check(missingReads == 2);
        });
        Run("Explorer suppresses only its green indicator and shortcut target changes invalidate cached identities", () =>
        {
            var link = Path.Combine(temporary, "explorer-test.lnk");
            var explorer = ApplicationIdentityResolver.ExplorerExecutable;
            ShellShortcut.Write(link, null, new(explorer, "", "", true));
            var direct = new DockEntry(Guid.NewGuid(), explorer);
            var shortcut = new DockEntry(Guid.NewGuid(), link);
            var otherExecutable = Path.Combine(temporary, "another-install", "explorer.exe");
            var other = new DockEntry(Guid.NewGuid(), otherExecutable);
            var resolver = new ApplicationIdentityResolver(_ => null);
            var tracker = new RunningApplications(resolver);
            Check(resolver.Resolve(direct).IsExplorer && resolver.Resolve(shortcut).IsExplorer && !resolver.Resolve(other).IsExplorer);
            Check(tracker.Snapshot([direct, shortcut, other], [explorer, otherExecutable], []).SetEquals([other.Id]));
            ShellShortcut.Write(link, null, new(otherExecutable, "", "", true));
            File.SetLastWriteTimeUtc(link, File.GetLastWriteTimeUtc(link).AddSeconds(2));
            Check(!resolver.Resolve(shortcut).IsExplorer);
            Check(tracker.Snapshot([shortcut], [otherExecutable], []).Contains(shortcut.Id));
        });
    }
}
