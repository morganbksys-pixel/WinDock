using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinDock;
using WinDock.Core;

internal static class Program
{
    private static int passed;
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);

    private static bool IsAbove(nint upper, nint lower)
    {
        for (var current = GetWindow(lower, 3); current != 0; current = GetWindow(current, 3)) // GW_HWNDPREV
            if (current == upper) return true;
        return false;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--running-smoke")
        {
            var entry = new DockEntry(Guid.NewGuid(), args[1]);
            var running = StaWorker.RunAsync(_ => new RunningApplications().Snapshot([entry]).Contains(entry.Id), default).GetAwaiter().GetResult();
            var windows = new ApplicationWindows().CountAsync(entry, default).GetAwaiter().GetResult();
            Console.WriteLine($"Running: {running}; recognized windows: {windows}. Read-only check; no close request sent.");
            return running && windows > 0 ? 0 : 1;
        }
        if (args.Length == 2 && args[0] == "--hold")
        {
            File.WriteAllText(args[1], "ready");
            Thread.Sleep(30000);
            return 0;
        }
        if (args.Length == 3 && args[0] == "--record")
        {
            File.WriteAllText(args[1], Environment.CurrentDirectory + "|" + args[2]);
            return 0;
        }
        var temporary = Path.Combine(Path.GetTempPath(), "WinDock.Tests-" + Guid.NewGuid());
        Directory.CreateDirectory(temporary);
        try
        {
            Run("Versioned Squirrel applications share indicator and window identity without matching unrelated executables", () =>
            {
                var root = Path.Combine(temporary, "AnthropicClaude");
                Directory.CreateDirectory(root);
                var launcher = Path.Combine(root, "claude.exe");
                var versioned = Path.Combine(root, "app-4.4212.0", "claude.exe");
                var commandLine = Path.Combine(temporary, ".local", "bin", "claude.exe");
                Check(!RunningApplications.MatchesExecutable(launcher, versioned));
                File.WriteAllText(Path.Combine(root, "Update.exe"), "test marker");
                Check(RunningApplications.MatchesExecutable(launcher, versioned));
                Check(RunningApplications.MatchesExecutable(launcher, versioned.ToUpperInvariant()));
                Check(RunningApplications.MatchesExecutable(launcher, Path.Combine(root, "app-4.5000.0", "claude.exe")));
                Check(!RunningApplications.MatchesExecutable(launcher, commandLine));
                Check(!RunningApplications.MatchesExecutable(launcher, Path.Combine(root + "-other", "app-4.4212.0", "claude.exe")));
                Check(!RunningApplications.MatchesExecutable(launcher, Path.Combine(root, "app-invalid", "claude.exe")));
                Check(!RunningApplications.MatchesExecutable(launcher, Path.Combine(root, "app-4.4212.0", "other.exe")));
                Check(!RunningApplications.MatchesExecutable(launcher, Path.Combine(root, "app-4.4212.0", "nested", "claude.exe")));
                var entry = new DockEntry(Guid.NewGuid(), launcher);
                var tracker = new RunningApplications();
                Check(tracker.Snapshot([entry], [versioned], []).Contains(entry.Id));
                Check(!tracker.Snapshot([entry], [commandLine], []).Contains(entry.Id));
                Check(!tracker.Snapshot([entry with { SourcePath = "shell:AppsFolder\\other!App" }], [versioned], []).Contains(entry.Id));
                ApplicationWindow[] windows = [new(1, 10, versioned, null), new(2, 11, commandLine, null)];
                var sent = new List<nint>();
                var service = new ApplicationWindows(() => windows, handle => windows.Single(w => w.Handle == handle),
                    handle => { sent.Add(handle); return 0; });
                Check(service.CountAsync(entry, default).GetAwaiter().GetResult() == 1);
                Check(service.CloseAsync(entry, default).GetAwaiter().GetResult() == 0);
                Check(sent.SequenceEqual(new nint[] { 1 }));
                var replaced = new ApplicationWindows(() => windows, handle => windows[0] with { Path = commandLine },
                    _ => throw new InvalidOperationException("Unrelated process must never receive WM_CLOSE"));
                Check(replaced.CloseAsync(entry, default).GetAwaiter().GetResult() == 0);
            });
            Run("Application closure matches exact identities and revalidates disappearing or reused windows", () =>
            {
                var executable = Path.Combine(temporary, "app.exe");
                var entry = new DockEntry(Guid.NewGuid(), executable);
                ApplicationWindow[] windows = [new(1, 10, executable, null), new(2, 11, executable, null),
                    new(3, 12, Path.Combine(temporary, "other", "app.exe"), null), new(4, 13, null, "package!app")];
                var sent = new List<nint>();
                var service = new ApplicationWindows(() => windows,
                    handle => windows.SingleOrDefault(window => window.Handle == handle),
                    handle => { sent.Add(handle); return handle == 2 ? 5 : 0; });
                Check(service.CountAsync(entry, default).GetAwaiter().GetResult() == 2);
                Check(service.CloseAsync(entry, default).GetAwaiter().GetResult() == 1);
                Check(sent.SequenceEqual(new nint[] { 1, 2 }));
                Check(service.CountAsync(new(Guid.NewGuid(), temporary), default).GetAwaiter().GetResult() == 0);
                Check(service.CountAsync(entry with { IsSeparator = true }, default).GetAwaiter().GetResult() == 0);
                var store = entry with { SourcePath = "shell:AppsFolder\\package!app", ApplicationId = "package!app" };
                Check(service.CountAsync(store, default).GetAwaiter().GetResult() == 1);
                sent.Clear();
                var vanished = new ApplicationWindows(() => windows, handle => handle == 1 ? null : windows[1] with { ProcessId = 99 },
                    handle => { sent.Add(handle); return 0; });
                Check(vanished.CloseAsync(entry, default).GetAwaiter().GetResult() == 0 && sent.Count == 0);
                var expired = new ApplicationWindows(() => windows, handle => windows.Single(w => w.Handle == handle), _ => 1400);
                Check(expired.CloseAsync(entry, default).GetAwaiter().GetResult() == 0);
            });
            Run("Window listing and activation revalidate identities and report native refusal", () =>
            {
                var entry = new DockEntry(Guid.NewGuid(), Path.Combine(temporary, "activate.exe"));
                var candidate = new ApplicationWindow(1, 10, entry.Path, null, "Document _1");
                ApplicationWindow? current = candidate;
                var activated = new List<nint>();
                var allow = true;
                var service = new ApplicationWindows(() => [candidate, candidate, new(2, 20, "other.exe", null)],
                    _ => current, _ => 0, handle => { activated.Add(handle); return allow; });
                var found = service.ListAsync(entry, default).GetAwaiter().GetResult();
                Check(found.Count == 1 && found[0].Title == "Document _1");
                Check(service.ListAsync(entry with { IsSeparator = true }, default).GetAwaiter().GetResult().Count == 0);
                Check(service.ActivateAsync(entry, candidate, default).GetAwaiter().GetResult() == WindowActivationResult.Activated);
                allow = false;
                Check(service.ActivateAsync(entry, candidate, default).GetAwaiter().GetResult() == WindowActivationResult.Refused);
                foreach (var replaced in new ApplicationWindow?[] { null, candidate with { ProcessId = 99 },
                    candidate with { Path = "other.exe" }, candidate with { ApplicationId = "other!app" } })
                {
                    current = replaced;
                    Check(service.ActivateAsync(entry, candidate, default).GetAwaiter().GetResult() == WindowActivationResult.Unavailable);
                }
                Check(activated.Count == 2);
                current = candidate;
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                try { service.ActivateAsync(entry, candidate, canceled.Token).GetAwaiter().GetResult(); Check(false); }
                catch (OperationCanceledException) { }
                Check(activated.Count == 2);
            });
            Run("Groups preserve legacy settings and create a single exact pre-group backup", () =>
            {
                var path = Path.Combine(temporary, "groups-backup.json");
                var store = new SettingsStore(path);
                File.WriteAllText(path, "{\"Entries\":[{\"Id\":\"" + Guid.NewGuid() + "\",\"Path\":\"app.exe\"}]}");
                var settings = store.Load(out var warning);
                Check(warning is null && !settings.Entries[0].IsGroup && settings.Entries[0].GroupId is null);
                var original = File.ReadAllBytes(path);
                var group = new DockEntry(Guid.NewGuid(), "", DisplayName: "Travail été", IsGroup: true);
                settings.Entries.Add(group); settings.Entries[0] = settings.Entries[0] with { GroupId = group.Id };
                store.Save(settings); store.Save(settings);
                var backups = Directory.GetFiles(temporary, "groups-backup.json.before-groups-*");
                Check(backups.Length == 1 && File.ReadAllBytes(backups[0]).SequenceEqual(original));
                var loaded = store.Load(out warning);
                Check(warning is null && loaded.Entries.SequenceEqual(settings.Entries));
                Check(!EntryRules.IsValid([group with { GroupId = group.Id }]));
                Check(!EntryRules.IsValid([group with { IsSeparator = true }]));
                Check(!EntryRules.IsValid([group with { DisplayName = " " }]));
                Check(!EntryRules.IsValid([group, new(Guid.NewGuid(), "", true, GroupId: group.Id)]));
                Check(!EntryRules.IsValid([new(Guid.NewGuid(), "app.exe", GroupId: Guid.NewGuid())]));
                Check(!EntryRules.IsValid([group, group]));
            });
            Run("Group transfers and dissolution preserve sibling order and reject nesting", () =>
            {
                var a = new DockEntry(Guid.NewGuid(), "", DisplayName: "A", IsGroup: true);
                var b = new DockEntry(Guid.NewGuid(), "", DisplayName: "B", IsGroup: true);
                var one = new DockEntry(Guid.NewGuid(), "one.exe");
                var two = new DockEntry(Guid.NewGuid(), "two.exe");
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                var entries = new List<DockEntry> { one, a, two, b, separator };
                var moved = EntryRules.MoveTo(entries, one.Id, a.Id);
                moved = EntryRules.MoveTo(moved, two.Id, a.Id, one.Id);
                Check(entries.All(e => e.GroupId is null));
                Check(moved.Where(e => e.GroupId == a.Id).Select(e => e.Id).SequenceEqual(new[] { two.Id, one.Id }));
                Check(EntryRules.MoveTo(moved, a.Id, b.Id).SequenceEqual(moved));
                Check(EntryRules.MoveTo(moved, separator.Id, a.Id).SequenceEqual(moved));
                Check(EntryRules.MoveTo(moved, one.Id, Guid.NewGuid()).SequenceEqual(moved));
                Check(EntryRules.MoveTo(moved, one.Id, null, two.Id).SequenceEqual(moved));
                moved = EntryRules.MoveTo(moved, two.Id, b.Id);
                Check(moved.Single(e => e.Id == two.Id).GroupId == b.Id);
                moved = EntryRules.MoveTo(moved, two.Id, a.Id, one.Id);
                var reordered = EntryRules.MoveTo(moved, b.Id, null, a.Id);
                Check(reordered.Where(e => e.GroupId is null).Select(e => e.Id).SequenceEqual(new[] { b.Id, a.Id, separator.Id }));
                Check(reordered.Where(e => e.GroupId == a.Id).Select(e => e.Id).SequenceEqual(new[] { two.Id, one.Id }));
                var dissolved = EntryRules.Dissolve(moved, a.Id);
                Check(dissolved.Select(e => e.Id).SequenceEqual(new[] { two.Id, one.Id, b.Id, separator.Id }));
                Check(dissolved.All(e => e.GroupId is null) && EntryRules.IsValid(dissolved));
                Check(EntryRules.Dissolve(dissolved, b.Id).Select(e => e.Id).SequenceEqual(new[] { two.Id, one.Id, separator.Id }));
            });
            Run("Managed group moves never retire live copies and failed backups leave settings unchanged", () =>
            {
                var root = Path.Combine(temporary, "group-managed"); Directory.CreateDirectory(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var service = new ManagedShortcuts(store);
                var original = Path.Combine(root, "original.lnk");
                ShellShortcut.Write(original, null, new(Environment.ProcessPath!, "--sample", root, true));
                var entry = service.Import(new(Guid.NewGuid(), original));
                var group = new DockEntry(Guid.NewGuid(), "", DisplayName: "Dev", IsGroup: true);
                var other = new DockEntry(Guid.NewGuid(), root);
                var settings = new DockSettings { Entries = [entry, other] }; store.Save(settings);
                var before = File.ReadAllBytes(Path.Combine(root, "settings.json"));
                using (var locked = new FileStream(Path.Combine(root, "settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    try { service.Commit(settings, EntryRules.GroupPair(settings.Entries, entry.Id, other.Id, group.Id)); Check(false); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    Check(settings.Entries.SequenceEqual(new[] { entry, other }) && File.Exists(entry.Path));
                }
                Check(File.ReadAllBytes(Path.Combine(root, "settings.json")).SequenceEqual(before));
                service.Commit(settings, EntryRules.GroupPair(settings.Entries, entry.Id, other.Id, group.Id));
                Check(File.Exists(entry.Path) && File.Exists(original));
                Check(ShellShortcut.Read(entry.Path).Arguments == "--sample");
                using (var locked = new FileStream(Path.Combine(root, "settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    try { service.Commit(settings, EntryRules.MoveTo(settings.Entries, entry.Id, null)); Check(false); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    Check(settings.Entries.Single(e => e.Id == entry.Id).GroupId == group.Id && File.Exists(entry.Path));
                }
                service.Commit(settings, EntryRules.MoveTo(settings.Entries, entry.Id, null));
                Check(settings.Entries.Count == 2 && settings.Entries.All(e => !e.IsGroup && e.GroupId is null) && File.Exists(entry.Path));
                Check(!service.IsMissing(group) && service.Import(group) == group);
                Check(new RunningApplications().Snapshot([group], [], []).Count == 0);
                Check(ApplicationCatalog.ExistingKeys([group]).Count == 0);
                Check(new ApplicationWindows().ListAsync(group, default).GetAwaiter().GetResult().Count == 0);
            });
            Run("Pair grouping and automatic dissolution are atomic and preserve root positions", () =>
            {
                var a = new DockEntry(Guid.NewGuid(), "", DisplayName: "A", IsGroup: true);
                var empty = new DockEntry(Guid.NewGuid(), "", DisplayName: "Vide", IsGroup: true);
                var source = new DockEntry(Guid.NewGuid(), "missing.exe", GroupId: a.Id);
                var survivor = new DockEntry(Guid.NewGuid(), temporary, GroupId: a.Id);
                var target = new DockEntry(Guid.NewGuid(), "target.exe");
                var id = Guid.NewGuid();
                DockEntry[] original = [a, source, survivor, target, empty];
                var paired = EntryRules.GroupPair(original, source.Id, target.Id, id);
                Check(paired.Where(e => e.GroupId is null).Select(e => e.Id).SequenceEqual(new[] { survivor.Id, id }));
                Check(paired.Where(e => e.GroupId == id).Select(e => e.Id).SequenceEqual(new[] { target.Id, source.Id }));
                Check(original.Length == 5 && original[1].GroupId == a.Id);
                Check(EntryRules.GroupPair(original, target.Id, target.Id, Guid.NewGuid()).SequenceEqual(original));
                Check(EntryRules.GroupPair(original, a.Id, target.Id, Guid.NewGuid()).SequenceEqual(original));
                Check(EntryRules.GroupPair(original, target.Id, survivor.Id, Guid.NewGuid()).SequenceEqual(original));
                var dissolved = EntryRules.NormalizeGroups(EntryRules.MoveTo(paired, target.Id, null));
                Check(dissolved.Select(e => e.Id).SequenceEqual(new[] { survivor.Id, source.Id, target.Id }));
                Check(dissolved.All(e => e.GroupId is null && !e.IsGroup));
            });
            Run("Legacy undersized groups normalize on migration without treating configuration as invalid", () =>
            {
                var store = new SettingsStore(Path.Combine(temporary, "legacy-small-groups.json"));
                var empty = new DockEntry(Guid.NewGuid(), "", DisplayName: "Vide", IsGroup: true);
                var one = new DockEntry(Guid.NewGuid(), "", DisplayName: "Un seul", IsGroup: true);
                var member = new DockEntry(Guid.NewGuid(), "missing.exe", GroupId: one.Id);
                store.Save(new DockSettings { Entries = [empty, one, member] });
                var settings = store.Load(out var warning); Check(warning is null && settings.Entries.Count == 3);
                new ManagedShortcuts(store).Migrate(settings);
                Check(settings.Entries.SequenceEqual(new[] { member with { GroupId = null } }));
                Check(store.Load(out warning).Entries.SequenceEqual(settings.Entries) && warning is null);
            });
            Run("Grouping hover arms only a stable center target after 500 ms and resets on cancellation", () =>
            {
                var state = new GroupingHover();
                var source = Guid.NewGuid(); var a = new DockEntry(Guid.NewGuid(), "a.exe"); var b = new DockEntry(Guid.NewGuid(), "b.exe");
                state.Update(source, a, 1000); Check(!state.IsReady(1499) && state.IsReady(1500));
                state.Update(source, b, 1501); Check(!state.IsReady(2000) && state.IsReady(2001));
                state.Update(Guid.NewGuid(), b, 2002); Check(!state.IsReady(2002));
                state.Clear(); Check(state.Target is null && !state.IsReady(9999));
                state.Update(source, a with { Id = source }, 0); Check(state.Target is null);
                state.Update(null, a, 0); Check(state.Target is null);
                state.Update(source, a with { IsSeparator = true }, 0); Check(state.Target is null);
                state.Update(source, a with { GroupId = Guid.NewGuid() }, 0); Check(state.Target is null);
                Check(GroupingHover.IsCenter(new Size(60, 60), new Point(30, 30)));
                Check(!GroupingHover.IsCenter(new Size(60, 60), new Point(5, 30)) && !GroupingHover.IsCenter(new Size(60, 60), new Point(59, 30)));
            });
            Run("Group geometry stays inside negative-origin work areas at every edge and DPI", () =>
            {
                foreach (var scale in new[] { 1.0, 1.5, 2.0 })
                foreach (var edge in Enum.GetValues<DockEdge>())
                {
                    var work = new PixelRect(-1920, -400, 1920, 1080);
                    var anchor = DockGeometry.Calculate(work, edge, 80 * scale, 80 * scale, 1).Window;
                    var placed = GroupGeometry.Place(work, anchor, edge, 360 * scale, 400 * scale, scale);
                    Check(placed.X >= work.X && placed.Y >= work.Y && placed.Right <= work.Right && placed.Bottom <= work.Bottom);
                    Check(edge switch { DockEdge.Bottom => placed.Bottom <= anchor.Y, DockEdge.Top => placed.Y >= anchor.Bottom, DockEdge.Left => placed.X >= anchor.Right, _ => placed.Right <= anchor.X });
                    placed = GroupGeometry.Place(work, anchor, edge, 5000, 5000, scale);
                    Check(placed == work);
                }
            });
            Run("Default dock initializes only missing settings and preserves empty or recovered configurations", () =>
            {
                var root = Path.Combine(temporary, "first-defaults");
                var sources = CreateDefaultSources(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var environment = new FakeDefaultDockEnvironment(sources);
                var service = new DefaultDockService(store, environment);
                var settings = store.Load(out var warning, out var status);
                Check(warning is null && status == SettingsLoadStatus.Missing);
                DefaultDockInitializer.InitializeAsync(settings, status, service, default).GetAwaiter().GetResult();
                Check(settings.ShowRecycleBin && settings.AutoHide && settings.Edge == DockEdge.Bottom);
                Check(settings.Entries.Where(e => !e.IsSeparator).Select(ManagedShortcuts.Name).SequenceEqual(new[] { "Explorateur", "Navigateur test", "Messagerie test", "Dossier personnel", "Téléchargements", "Documents" }));
                Check(settings.Entries.Count(e => e.IsSeparator) == 2 && settings.Entries.All(e => !e.IsGroup));
                settings = store.Load(out warning, out status); Check(status == SettingsLoadStatus.Loaded && warning is null);
                DefaultDockInitializer.InitializeAsync(settings, status, service, default).GetAwaiter().GetResult(); Check(environment.Calls == 1);
                new ManagedShortcuts(store).Commit(settings, []); settings.ShowRecycleBin = false; store.Save(settings);
                settings = store.Load(out warning, out status);
                DefaultDockInitializer.InitializeAsync(settings, status, service, default).GetAwaiter().GetResult();
                Check(settings.Entries.Count == 0 && !settings.ShowRecycleBin && environment.Calls == 1);
                File.WriteAllText(Path.Combine(root, "settings.json"), "{ invalid");
                settings = store.Load(out warning, out status); Check(status == SettingsLoadStatus.Recovered && warning is not null);
                DefaultDockInitializer.InitializeAsync(settings, status, service, default).GetAwaiter().GetResult();
                Check(settings.Entries.Count == 0 && !settings.ShowRecycleBin && environment.Calls == 1);
            });
            Run("Default preparation deduplicates redirected folders and disposing a selection cleans staged copies", () =>
            {
                var root = Path.Combine(temporary, "defaults-dedup");
                var sources = CreateDefaultSources(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                sources = sources with { Downloads = sources.Home, Browser = new("Même application", sources.Explorer) };
                var service = new DefaultDockService(store, new FakeDefaultDockEnvironment(sources));
                string[] staged;
                using (var prepared = service.PrepareAsync(default).GetAwaiter().GetResult())
                {
                    Check(prepared.Entries.Where(e => !e.IsSeparator).Select(ManagedShortcuts.Name).SequenceEqual(new[] { "Explorateur", "Messagerie test", "Dossier personnel", "Documents" }));
                    staged = prepared.Entries.Where(e => ShellShortcut.IsLink(e.Path)).Select(e => e.Path).ToArray();
                    Check(staged.Length == 2 && staged.All(File.Exists));
                    Check(!File.Exists(Path.Combine(root, "settings.json")));
                    Check(prepared.Entries.Single(e => e.DisplayName == "Documents").Path == sources.Documents);
                }
                Check(staged.All(path => !File.Exists(path)) && File.Exists(sources.Mail!.Application!.ShortcutPath));
                var absent = Path.Combine(root, "absent");
                service = new DefaultDockService(store, new FakeDefaultDockEnvironment(new(null, null, null, absent, absent, absent)));
                using var empty = service.PrepareAsync(default).GetAwaiter().GetResult();
                Check(empty.Entries.Count == 0 && !Directory.Exists(absent));
                var settings = new DockSettings(); empty.Apply(settings);
                Check(settings.ShowRecycleBin && store.Load(out _).ShowRecycleBin);
            });
            Run("Browser selection prefers registered identities and never imports URL-specific launch arguments", () =>
            {
                var executable = Path.Combine(temporary, "browser.exe");
                var generic = new CatalogApplication("Navigateur", "browser!app", "generic.lnk", ApplicationCatalog.LaunchKey(new(executable, "", "", true)));
                var profile = generic with { ShortcutPath = "profile.lnk", LaunchKey = ApplicationCatalog.LaunchKey(new(executable, "https://example.invalid/forced", "", true)) };
                var chosen = WindowsDefaultDockEnvironment.SelectApplication(new("browser!app", null, null), [profile], _ => false);
                Check(chosen?.Application is { ShortcutPath: null, ApplicationId: "browser!app" });
                chosen = WindowsDefaultDockEnvironment.SelectApplication(new(null, executable, "Défaut"), [generic], _ => true);
                Check(chosen is { Application: { ShortcutPath: null, ApplicationId: "browser!app" }, Name: "Défaut" });
                chosen = WindowsDefaultDockEnvironment.SelectApplication(new(null, executable, "Défaut"), [profile], _ => true);
                Check(chosen?.Executable == executable && chosen.Application is null && chosen.Name == "Défaut");
                Check(WindowsDefaultDockEnvironment.SelectApplication(new(null, executable, null), [], _ => false) is null);
                Check(WindowsDefaultDockEnvironment.SelectApplication(new(null, Path.Combine(temporary, "explorer.exe"), null), [], _ => true) is null);
                var storeOnly = generic with { ShortcutPath = null, LaunchKey = "shell:browser!app" };
                Check(WindowsDefaultDockEnvironment.SelectApplication(new("browser!app", null, null), [storeOnly], _ => false)?.Application == storeOnly);
            });
            Run("Targeted discovery reads HTTPS and mailto associations with only an available metadata snapshot", () =>
            {
                var root = Path.Combine(temporary, "targeted-associations"); Directory.CreateDirectory(root);
                var browser = Path.Combine(root, "browser.exe"); var mail = Path.Combine(root, "mail.exe");
                File.WriteAllText(browser, "not executed"); File.WriteAllText(mail, "not executed");
                var protocols = new List<string>(); var snapshots = 0; var folders = 0;
                var environment = new WindowsDefaultDockEnvironment(protocol =>
                {
                    protocols.Add(protocol);
                    return protocol == "https" ? new(null, browser, "Navigateur") : new("mail!app", mail, "Messagerie");
                }, () => { snapshots++; return []; }, _ => { folders++; return null; }, root);
                var result = StaWorker.RunAsync(environment.Resolve, default).GetAwaiter().GetResult();
                Check(protocols.SequenceEqual(new[] { "https", "mailto" }) && snapshots == 1 && folders == 3);
                Check(result.Browser?.Executable == browser && result.Mail?.Application?.ApplicationId == "mail!app" && result.Mail.Executable == mail);
                Check(result.Mail!.IconPath == mail && result.Mail.IconIndex == 0);
                Check(WindowsDefaultDockEnvironment.SelectApplication(new(null, null, null), [], File.Exists) is null);
                var resource = WindowsDefaultDockEnvironment.ParseIconResource("\"" + mail + "\",-123", File.Exists);
                Check(resource == (mail, -123));
            });
            Run("Mail duplicates are omitted and stale app identities fall back to an executable with no compose arguments", () =>
            {
                var root = Path.Combine(temporary, "mail-duplicates"); var sources = CreateDefaultSources(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var browser = sources.Browser!;
                using (var prepared = new DefaultDockService(store, new FakeDefaultDockEnvironment(sources with { Mail = browser })).PrepareAsync(default).GetAwaiter().GetResult())
                    Check(prepared.Entries.Count(e => !e.IsSeparator) == 5);
                var invalid = sources.Mail!.Application! with { ApplicationId = null, ShortcutPath = null };
                var fallback = sources.Mail with { Application = invalid };
                fallback = fallback with { Application = invalid with { ShortcutPath = Path.Combine(root, "missing.lnk") } };
                using var selection = new DefaultDockService(store, new FakeDefaultDockEnvironment(sources with { Mail = fallback })).PrepareAsync(default).GetAwaiter().GetResult();
                var entry = selection.Entries.Single(e => e.DisplayName == "Messagerie test");
                var details = ShellShortcut.Read(entry.Path);
                Check(details.Target == sources.Mail.Executable && details.Arguments.Length == 0 && entry.ApplicationId is null);
                Check(File.Exists(sources.Mail.Application!.ShortcutPath));
            });
            Run("Packaged mail icon resources resolve without a scan and cache as reusable multi-frame ICOs", () =>
            {
                var root = Path.Combine(temporary, "mail-icons"); var sources = CreateDefaultSources(root);
                var assets = Path.Combine(root, "assets"); Directory.CreateDirectory(assets);
                void WritePng(string path, int size)
                {
                    var pixels = new byte[size * size * 4];
                    for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 220; pixels[i + 1] = 100; pixels[i + 3] = 255; }
                    var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4); bitmap.Freeze();
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(path); encoder.Save(file);
                }
                var png = Path.Combine(assets, "AppList.scale-100.png"); WritePng(png, 44);
                var large = Path.Combine(assets, "AppList.targetsize-256.png"); WritePng(large, 256);
                var original = File.ReadAllBytes(large); var indirectReads = 0;
                var environment = new WindowsDefaultDockEnvironment(protocol => protocol == "mailto"
                    ? new("mail!app", null, "Messagerie", "@{package?ms-resource://Application/Files/Assets/AppList.png}") : new(null, null, null),
                    () => [], _ => null, root, reference => { indirectReads++; return png; });
                var resolved = StaWorker.RunAsync(environment.Resolve, default).GetAwaiter().GetResult();
                Check(indirectReads == 1 && resolved.Mail?.IconPath == png && resolved.Mail.Application?.ApplicationId == "mail!app");
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var service = new DefaultDockService(store, new FakeDefaultDockEnvironment(sources with { Mail = sources.Mail! with { IconPath = png } }));
                using (var prepared = service.PrepareAsync(default).GetAwaiter().GetResult())
                {
                    var icon = Directory.GetFiles(Path.Combine(root, "IconCache"), "*.ico").Single();
                    using var file = File.OpenRead(icon);
                    var decoder = new IconBitmapDecoder(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    Check(decoder.Frames.Count == 6 && decoder.Frames.Max(frame => frame.PixelWidth) == 256);
                    Check(prepared.Entries.Any(e => e.DisplayName == "Messagerie test"));
                }
                using (var prepared = service.PrepareAsync(default).GetAwaiter().GetResult())
                    Check(Directory.GetFiles(Path.Combine(root, "IconCache"), "*.ico").Length == 1);
                Check(File.ReadAllBytes(large).SequenceEqual(original) && Directory.GetFiles(Path.Combine(root, "Shortcuts")).Length == 0);
            });
            Run("Default restoration commits content and recycle bin together while retaining preferences and old files on failure", () =>
            {
                var root = Path.Combine(temporary, "defaults-atomic"); var sources = CreateDefaultSources(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json")); var managed = new ManagedShortcuts(store);
                var oldCopy = managed.CreateApplication(Environment.ProcessPath!, "Ancienne application");
                var group = new DockEntry(Guid.NewGuid(), "", DisplayName: "Ancien groupe", IsGroup: true);
                var settings = new DockSettings { Edge = DockEdge.Right, MonitorDevice = "saved-monitor", AutoHide = false, BackgroundOpacity = 0.37, Entries = [group, oldCopy with { GroupId = group.Id }, new(Guid.NewGuid(), root, GroupId: group.Id)] };
                store.Save(settings); var original = settings.Entries.ToArray(); var saved = File.ReadAllBytes(Path.Combine(root, "settings.json"));
                var service = new DefaultDockService(store, new FakeDefaultDockEnvironment(sources));
                string[] staged;
                using (var prepared = service.PrepareAsync(default).GetAwaiter().GetResult())
                {
                    staged = prepared.Entries.Where(e => ShellShortcut.IsLink(e.Path)).Select(e => e.Path).ToArray();
                    using (var locked = new FileStream(Path.Combine(root, "settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        try { prepared.Apply(settings); Check(false); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                        Check(settings.Entries.SequenceEqual(original) && !settings.ShowRecycleBin && File.Exists(oldCopy.Path));
                    }
                }
                Check(staged.All(path => !File.Exists(path)) && File.ReadAllBytes(Path.Combine(root, "settings.json")).SequenceEqual(saved));
                using (var prepared = service.PrepareAsync(default).GetAwaiter().GetResult()) prepared.Apply(settings);
                Check(settings.ShowRecycleBin && settings.Entries.All(e => !e.IsGroup && e.GroupId is null) && !File.Exists(oldCopy.Path));
                Check(settings.Edge == DockEdge.Right && settings.MonitorDevice == "saved-monitor" && settings.BackgroundOpacity == 0.37 && !settings.AutoHide);
                Check(File.Exists(sources.Mail!.Application!.ShortcutPath) && File.Exists(sources.Browser!.Executable));
                Check(store.Load(out _).Entries.SequenceEqual(settings.Entries));
            });
            Run("Default preparation rolls back partial staging and canceled discovery", () =>
            {
                var root = Path.Combine(temporary, "defaults-staging-failure"); var sources = CreateDefaultSources(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var environment = new FakeDefaultDockEnvironment(sources);
                var service = new DefaultDockService(store, environment);
                using (var locked = new FileStream(sources.Mail!.Application!.ShortcutPath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    try { service.PrepareAsync(default).GetAwaiter().GetResult(); Check(false); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                Check(Directory.GetFiles(Path.Combine(root, "Shortcuts")).Length == 0 && !File.Exists(Path.Combine(root, "settings.json")));
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                try { service.PrepareAsync(cancellation.Token).GetAwaiter().GetResult(); Check(false); }
                catch (OperationCanceledException) { }
                Check(environment.Calls == 1 && File.Exists(sources.Mail!.Application!.ShortcutPath));
            });
            Run("Startup shortcut toggles safely and preserves an existing registration on failure", () =>
            {
                var startupDirectory = Path.Combine(temporary, "démarrage test");
                var executable = Environment.ProcessPath!;
                var registration = new StartupRegistration(startupDirectory, executable);
                Check(!registration.IsEnabled);
                registration.SetEnabled(true);
                var shortcut = Path.Combine(startupDirectory, "WinDock.lnk");
                var details = ShellShortcut.Read(shortcut);
                Check(details.Target == executable && details.Arguments == "" && details.WorkingDirectory == Path.GetDirectoryName(executable));
                registration.SetEnabled(true);
                var original = File.ReadAllBytes(shortcut);
                try
                {
                    new StartupRegistration(startupDirectory, Path.Combine(temporary, "absent.exe")).SetEnabled(true);
                    throw new InvalidOperationException("Missing executable was accepted");
                }
                catch (FileNotFoundException) { }
                Check(File.ReadAllBytes(shortcut).SequenceEqual(original));
                registration.SetEnabled(false);
                registration.SetEnabled(false);
                Check(!registration.IsEnabled && File.Exists(executable));
            });
            Run("Missing settings use defaults", () =>
            {
                var settings = new SettingsStore(Path.Combine(temporary, "missing.json")).Load(out var warning);
                Check(warning is null && settings.Edge == DockEdge.Bottom && settings.Entries.Count == 0);
            });
            Run("Settings round trip preserves order, IDs, Unicode and screen", () =>
            {
                var store = new SettingsStore(Path.Combine(temporary, "nested", "settings.json"));
                var entries = new List<DockEntry> { new(Guid.NewGuid(), @"C:\Applications\Éditeur été.lnk"), new(Guid.NewGuid(), @"D:\Mes dossiers") };
                store.Save(new() { Edge = DockEdge.Left, MonitorDevice = @"\\.\DISPLAY2", Entries = entries });
                var result = store.Load(out var warning);
                Check(warning is null && result.Edge == DockEdge.Left && result.MonitorDevice == @"\\.\DISPLAY2" && result.Entries.SequenceEqual(entries));
                result.Entries.RemoveAt(0);
                store.Save(result);
                Check(store.Load(out _).Entries.SequenceEqual(entries.Skip(1)));
            });
            Run("Invalid JSON is backed up without losing original content", () =>
            {
                var path = Path.Combine(temporary, "corrupt.json");
                File.WriteAllText(path, "{broken");
                var settings = new SettingsStore(path).Load(out var warning);
                Check(warning is not null && settings.Entries.Count == 0);
                var backup = Directory.GetFiles(temporary, "corrupt.json.invalid-*").Single();
                Check(File.ReadAllText(backup) == "{broken");
            });
            Run("Legacy settings and separator/opacity persistence", () =>
            {
                var path = Path.Combine(temporary, "legacy.json");
                var id = Guid.NewGuid();
                File.WriteAllText(path, $"{{\"Entries\":[{{\"Id\":\"{id}\",\"Path\":\"app.exe\"}}]}}");
                var store = new SettingsStore(path);
                var settings = store.Load(out var warning);
                Check(warning is null && !settings.Entries[0].IsSeparator && settings.Entries[0].Id == id);
                Check(settings.BackgroundOpacity == 238.0 / 255);
                Check(settings.AutoHide && !settings.ShowRecycleBin);
                settings.AutoHide = false;
                settings.ShowRecycleBin = true;
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                settings.Entries.Add(separator);
                settings.BackgroundOpacity = 0.35;
                EntryRules.Move(settings.Entries, separator.Id, id);
                store.Save(settings);
                var loaded = store.Load(out warning);
                Check(warning is null && loaded.Entries[0] == separator && loaded.BackgroundOpacity == 0.35);
                Check(loaded.Entries[1].Id == id);
                Check(!loaded.AutoHide && loaded.ShowRecycleBin);
            });
            Run("Structurally invalid settings reset safely", () =>
            {
                foreach (var json in new[] { "null", "{\"Entries\":null}", "{\"Edge\":99}", "{\"Entries\":[null]}", "{\"BackgroundOpacity\":2}", "{\"BackgroundOpacity\":0}" })
                {
                    var path = Path.Combine(temporary, Guid.NewGuid() + ".json");
                    File.WriteAllText(path, json);
                    Check(new SettingsStore(path).Load(out var warning).Entries.Count == 0 && warning is not null);
                }
            });
            Run("Move forward, backward, to end and onto self", () =>
            {
                var a = new DockEntry(Guid.NewGuid(), "a");
                var b = new DockEntry(Guid.NewGuid(), "b");
                var c = new DockEntry(Guid.NewGuid(), "c");
                List<DockEntry> entries = [a, b, c];
                EntryRules.Move(entries, a.Id, c.Id); Check(entries.SequenceEqual([b, a, c]));
                EntryRules.Move(entries, c.Id, b.Id); Check(entries.SequenceEqual([c, b, a]));
                EntryRules.Move(entries, c.Id, null); Check(entries.SequenceEqual([b, a, c]));
                EntryRules.Move(entries, a.Id, a.Id); Check(entries.SequenceEqual([b, a, c]));
                EntryRules.Move(entries, Guid.NewGuid(), null); Check(entries.SequenceEqual([b, a, c]));
            });
            Run("Accepted targets and names with spaces and accents", () =>
            {
                var folder = Path.Combine(temporary, "Dossier été"); Directory.CreateDirectory(folder);
                Check(EntryRules.IsSupported(folder) && EntryRules.DisplayName(folder) == "Dossier été");
                foreach (var extension in new[] { ".exe", ".LNK", ".txt" })
                {
                    var path = Path.Combine(folder, "Éditeur test" + extension); File.WriteAllText(path, "");
                    Check(EntryRules.IsSupported(path) == (extension != ".txt"));
                }
                Check(!EntryRules.IsSupported(Path.Combine(folder, "absent.exe")));
            });
            Run("All edges respect work area and a three-pixel trigger", () =>
            {
                var work = new PixelRect(0, 0, 1920, 1040);
                foreach (var edge in Enum.GetValues<DockEdge>())
                {
                    var p = DockGeometry.Calculate(work, edge, 400, 78, 1);
                    Check(p.Window.X >= work.X && p.Window.Y >= work.Y && p.Window.Right <= work.Right && p.Window.Bottom <= work.Bottom);
                    Check(edge is DockEdge.Top or DockEdge.Bottom ? p.Trigger.Height == 3 : p.Trigger.Width == 3);
                    Check(DockGeometry.InteractionArea(p).Contains(p.Trigger.X + 1, p.Trigger.Y + 1));
                    if (edge == DockEdge.Bottom) Check(p.Window.Bottom == 1032 && p.Trigger.Bottom == 1040);
                    if (edge == DockEdge.Top) Check(p.Window.Y == 8 && p.Trigger.Y == 0);
                    if (edge == DockEdge.Left) Check(p.Window.X == 8 && p.Trigger.X == 0);
                    if (edge == DockEdge.Right) Check(p.Window.Right == 1912 && p.Trigger.Right == 1920);
                }
            });
            Run("Negative monitor origins and mixed DPI stay in physical pixels", () =>
            {
                var p = DockGeometry.Calculate(new(-2560, -200, 2560, 1400), DockEdge.Bottom, 400, 78, 1.5);
                Check(p.Window.Width == 600 && p.Window.Height == 117 && p.Window.X == -1580 && p.Window.Bottom == 1188);
                Check(p.Trigger.Height == 3 && p.Trigger.Bottom == 1200);
            });
            Run("Long docks clamp to available space", () =>
            {
                var p = DockGeometry.Calculate(new(0, 0, 1280, 720), DockEdge.Top, 10000, 78, 2);
                Check(p.Window.Width == 1248 && p.Window.X == 16 && p.Window.Height == 156);
            });
            Run("WPF window and native handle initialize on every edge", () =>
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                app.Resources = new ResourceDictionary { Source = new Uri("/WinDock;component/Styles.xaml", UriKind.Relative) };
                foreach (var edge in Enum.GetValues<DockEdge>())
                foreach (var count in new[] { 0, 1, 8, 80 })
                {
                    var settings = new DockSettings { Edge = edge, MonitorDevice = "disconnected-screen", BackgroundOpacity = 0.35 };
                    for (var i = 0; i < count; i++)
                        settings.Entries.Add(i % 3 == 1 ? new(Guid.NewGuid(), "", true) : new(Guid.NewGuid(), temporary));
                    var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "window.json")));
                    Check(new WindowInteropHelper(window).EnsureHandle() != 0);
                    window.Show();
                    PumpUntil(() => window.OwnedWindows.Cast<Window>().Any(w => w is IndicatorWindow));
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    Check(window.Width > 0 && window.Height > 0);
                    Check(window.Icon is BitmapSource dockIcon && dockIcon.PixelWidth == 256 && dockIcon.PixelHeight == 256);
                    window.UpdateLayout();
                    var frame = (Border)window.Content;
                    Check(frame.Background.Opacity == 0.35);
                    var panel = (StackPanel)((Viewbox)frame.Child).Child;
                    var buttons = panel.Children.OfType<Button>().ToArray();
                    Check(buttons.Length == count);
                    if (count == 0) Check(panel.Children.OfType<TextBlock>().Single().Text == "Clic droit pour ajouter" && window.AllowDrop);
                    var commonActions = new[] { "Ajouter…", "Ajouter un séparateur", "Réglages…", "Quitter WinDock" };
                    Check(window.ContextMenu.Items.OfType<MenuItem>().Select(item => (string)item.Header).SequenceEqual(commonActions));
                    foreach (var button in buttons)
                    {
                        var entry = (DockEntry)button.Tag;
                        var actions = entry.IsSeparator ? new[] { "Retirer du dock" } : new[] { "Lancer à nouveau", "Modifier / réparer…", "Retirer du dock" };
                        Check(button.ContextMenu.Items.OfType<MenuItem>().Select(item => (string)item.Header).SequenceEqual(actions.Concat(commonActions)));
                        var bounds = button.TransformToAncestor(frame).TransformBounds(new Rect(button.RenderSize));
                        if (!(bounds.Left >= 8 && bounds.Top >= 8 && bounds.Right <= frame.ActualWidth - 8 && bounds.Bottom <= frame.ActualHeight - 8))
                            throw new InvalidOperationException($"Clipped button: {edge}, {count} entries, bounds {bounds}, frame {frame.ActualWidth} x {frame.ActualHeight}");
                    }
                    var indicator = window.OwnedWindows.Cast<Window>().OfType<IndicatorWindow>().Single();
                    Check(!indicator.ShowActivated && !indicator.ShowInTaskbar && !indicator.IsHitTestVisible);
                    Check(edge is DockEdge.Top or DockEdge.Bottom ? indicator.Width > indicator.Height : indicator.Height > indicator.Width);
                    window.Close();
                    Check(!indicator.IsVisible);
                }
            });
            Run("Settings restoration previews the exact selection and supports cancellation and confirmation", () =>
            {
                var root = Path.Combine(temporary, "defaults-ui"); var sources = CreateDefaultSources(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var old = new DockEntry(Guid.NewGuid(), root, DisplayName: "Favori personnel");
                var settings = new DockSettings { Entries = [old], Edge = DockEdge.Left, BackgroundOpacity = 0.42, AutoHide = false };
                store.Save(settings);
                var environment = new FakeDefaultDockEnvironment(sources);
                var window = new DockWindow(settings, store, new FakeRecycleBin(), new FakeApplicationWindows(), new DefaultDockService(store, environment)); window.Show();
                try
                {
                    window.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Réglages…")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    var dialog = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Réglages — WinDock");
                    var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
                    var restore = panel.Children.OfType<Button>().Single(b => Equals(b.Content, "Restaurer le dock par défaut…"));
                    var recycle = panel.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Afficher la corbeille"));
                    foreach (var accept in new[] { false, true })
                    {
                        var seen = false;
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                        timer.Tick += (_, _) =>
                        {
                            var confirmation = Application.Current.Windows.OfType<DefaultDockConfirmation>().FirstOrDefault();
                            if (confirmation is null || !confirmation.IsLoaded) return;
                            Check(!restore.IsEnabled);
                            if (!confirmation.RestoreButton.IsEnabled) return;
                            timer.Stop(); seen = true;
                            if (args.Length == 2 && args[0] == "--preview" && !accept) SavePreview(confirmation, Path.Combine(args[1], "default-restore-confirmation.png"));
                            (accept ? confirmation.RestoreButton : confirmation.CancelButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        };
                        timer.Start();
                        try
                        {
                            restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            PumpUntil(() => seen && restore.IsEnabled, 10000);
                        }
                        finally { timer.Stop(); }
                        if (!accept)
                        {
                            Check(settings.Entries.SequenceEqual(new[] { old }) && recycle.IsChecked == false);
                            Check(Directory.GetFiles(Path.Combine(root, "Shortcuts")).Length == 0);
                        }
                        else Check(settings.Entries.Count == 8 && recycle.IsChecked == true && settings.ShowRecycleBin);
                    }
                    Check(environment.Calls == 2 && settings.Edge == DockEdge.Left && settings.BackgroundOpacity == 0.42 && !settings.AutoHide);
                    if (args.Length == 2 && args[0] == "--preview") SavePreview(dialog, Path.Combine(args[1], "default-restore-settings.png"));
                    dialog.Close();
                }
                finally { window.Close(); }
            });
            Run("Confirmation renders before slow discovery, cancels immediately and discards a late selection", () =>
            {
                var root = Path.Combine(temporary, "defaults-pending"); var sources = CreateDefaultSources(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var settings = new DockSettings { Entries = [new(Guid.NewGuid(), root)] }; store.Save(settings);
                var completion = new TaskCompletionSource<PreparedDefaultDock>();
                CancellationToken requested = default;
                var watch = new Stopwatch(); long openingMilliseconds = -1;
                var service = new FakeDefaultDockService(token =>
                {
                    var dialog = Application.Current.Windows.OfType<DefaultDockConfirmation>().Single();
                    Check(dialog.HasRenderedContent && dialog.IsVisible && dialog.CancelButton.IsEnabled && !dialog.RestoreButton.IsEnabled);
                    openingMilliseconds = watch.ElapsedMilliseconds;
                    requested = token; return completion.Task;
                });
                var window = new DockWindow(settings, store, new FakeRecycleBin(), new FakeApplicationWindows(), service); window.Show();
                DefaultDockConfirmation? confirmation = null;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                try
                {
                    window.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Réglages…")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    var settingsDialog = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Réglages — WinDock");
                    timer.Tick += (_, _) =>
                    {
                        confirmation = Application.Current.Windows.OfType<DefaultDockConfirmation>().FirstOrDefault();
                        if (confirmation?.PreparationTask is null || service.Calls == 0) return;
                        timer.Stop();
                        Check(confirmation.Status.Text == "Préparation de la sélection…" && !confirmation.RestoreButton.IsEnabled);
                        var duplicate = window.RestoreDefaultsAsync(); Check(duplicate.IsCompleted && !duplicate.Result && service.Calls == 1);
                        if (args.Length == 2 && args[0] == "--preview") SavePreview(confirmation, Path.Combine(args[1], "default-restore-loading.png"));
                        settingsDialog.Close(); // Closing the owner must also cancel its modal confirmation.
                    };
                    timer.Start(); watch.Start();
                    var first = window.RestoreDefaultsAsync();
                    PumpUntil(() => first.IsCompleted);
                    Check(!first.Result && requested.IsCancellationRequested && openingMilliseconds >= 0);
                    Console.WriteLine("Instrumented confirmation first render: " + openingMilliseconds + " ms; discovery still blocked.");
                    window.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Réglages…")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    var reopened = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Réglages — WinDock");
                    var reopenedPanel = (StackPanel)((ScrollViewer)reopened.Content).Content;
                    Check(reopenedPanel.Children.OfType<Button>().Single(b => Equals(b.Content, "Restaurer le dock par défaut…")).IsEnabled);
                    var preparation = new DefaultDockService(store, new FakeDefaultDockEnvironment(sources)).PrepareAsync(default);
                    PumpUntil(() => preparation.IsCompleted);
                    var prepared = preparation.GetAwaiter().GetResult();
                    var staged = prepared.Entries.Where(e => ShellShortcut.IsLink(e.Path)).Select(e => e.Path).ToArray();
                    completion.SetResult(prepared); PumpUntil(() => confirmation!.PreparationTask!.IsCompleted);
                    Check(staged.All(path => !File.Exists(path)) && settings.Entries.Count == 1 && !settings.ShowRecycleBin);
                }
                finally { timer.Stop(); window.Close(); }
            });
            Run("Preparation errors remain in the confirmation and retry never applies without acceptance", () =>
            {
                var root = Path.Combine(temporary, "defaults-retry"); var sources = CreateDefaultSources(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var native = new DefaultDockService(store, new FakeDefaultDockEnvironment(sources));
                var calls = 0;
                var service = new FakeDefaultDockService(token => ++calls == 1
                    ? Task.FromException<PreparedDefaultDock>(new IOException("Erreur simulée")) : native.PrepareAsync(token));
                using var dialog = new DefaultDockConfirmation(service, null);
                dialog.Show();
                try
                {
                    PumpUntil(() => dialog.RetryButton.IsVisible);
                    Check(!dialog.RestoreButton.IsEnabled && dialog.CancelButton.IsEnabled && dialog.Status.Text.Contains("Erreur simulée"));
                    if (args.Length == 2 && args[0] == "--preview") SavePreview(dialog, Path.Combine(args[1], "default-restore-error.png"));
                    dialog.RetryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => dialog.RestoreButton.IsEnabled);
                    Check(calls == 2 && !File.Exists(Path.Combine(root, "settings.json")));
                    var staged = Directory.GetFiles(Path.Combine(root, "Shortcuts")); Check(staged.Length == 3);
                    dialog.Close();
                    Check(staged.All(path => !File.Exists(path)) && dialog.TakeSelection() is null);
                }
                finally { dialog.Close(); }
            });
            Run("Pair drop opens a dock-styled group and inline editing manages focus and names", () =>
            {
                var first = new DockEntry(Guid.NewGuid(), Environment.ProcessPath!, DisplayName: "Application A");
                var second = new DockEntry(Guid.NewGuid(), temporary, DisplayName: "Documents");
                var settings = new DockSettings { Entries = [first, second], BackgroundOpacity = 0.2 };
                var store = new SettingsStore(Path.Combine(temporary, "group-ui.json")); store.Save(settings);
                var window = new DockWindow(settings, store, new FakeRecycleBin(), new FakeApplicationWindows()); window.Show();
                GroupPanel? Panel() => (GroupPanel?)typeof(DockWindow).GetField("groupPanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window);
                try
                {
                    var foreground = Native.GetForegroundWindow();
                    Check(window.CreateGroupFromDrop(first.Id, second.Id));
                    var group = settings.Entries.Single(e => e.IsGroup); var panel = Panel()!;
                    Check(Native.GetForegroundWindow() == foreground);
                    Check(group.DisplayName == "Nouveau groupe" && panel.Items.Children.Count == 2);
                    Check(settings.Entries.Where(e => e.GroupId == group.Id).Select(e => e.Id).SequenceEqual(new[] { second.Id, first.Id }));
                    var dockSurface = (Border)window.Content; var groupSurface = (Border)panel.Content;
                    Check(ReferenceEquals(dockSurface.Background, groupSurface.Background) && groupSurface.Background.Opacity == 0.2);
                    Check(groupSurface.CornerRadius == dockSurface.CornerRadius && groupSurface.BorderBrush.ToString() == dockSurface.BorderBrush.ToString());
                    Check(panel.Opacity == 1 && Equals(panel.AddButton.Content, "+") && System.Windows.Automation.AutomationProperties.GetName(panel.AddButton) == "Ajouter au groupe");
                    Check(!window.ContextMenu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, "Créer un groupe…")));
                    if (args.Length == 2 && args[0] == "--preview") SavePreview(panel, Path.Combine(args[1], "group-transparent.png"));
                    panel.BeginRename(); Check(panel.IsEditing && panel.TitleEditor.IsKeyboardFocused);
                    panel.TitleEditor.Text = "  Travail  "; Check(panel.FinishRename(true));
                    Check(!panel.IsEditing && settings.Entries.Single(e => e.IsGroup).DisplayName == "Travail");
                    panel.BeginRename(); panel.TitleEditor.Text = " "; panel.FinishRename(true);
                    Check(settings.Entries.Single(e => e.IsGroup).DisplayName == "Travail");
                    panel.BeginRename(); panel.TitleEditor.Text = "Annuler";
                    panel.TitleEditor.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(panel)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                    Check(!panel.IsEditing && panel.TitleLabel.Text == "Travail" && !panel.ShouldDismissOnEscape(true));
                    panel.ShouldDismissOnEscape(false);
                    panel.BeginRename(); panel.TitleEditor.Text = "Développement";
                    if (args.Length == 2 && args[0] == "--preview") SavePreview(panel, Path.Combine(args[1], "group-title-edit.png"));
                    panel.AddButton.Focus();
                    Check(settings.Entries.Single(e => e.IsGroup).DisplayName == "Développement" && !panel.IsEditing);
                    window.CloseGroup();
                    group = settings.Entries.Single(e => e.IsGroup);
                    window.TrackGroupHover(group, 1000); window.TrackGroupHover(group, 1499); Check(Panel() is null);
                    window.TrackGroupHover(group, 1500); Check(Panel() is not null);
                    var dismissal = (ContextMenuDismissal)typeof(DockWindow).GetField("groupDismissal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                    dismissal.HandlePointerDown(new Point(-100000, -100000)); PumpUntil(() => Panel() is null);
                    window.ToggleGroup(group); window.UpdateVisibility(true, false, false, 2000); Check(Panel() is null);
                    window.UpdateVisibility(false, true, false, 3000); window.UpdateVisibility(false, true, false, 3200);
                    window.ToggleGroup(settings.Entries.Single(e => e.IsGroup));
                    var movingButton = Panel()!.Items.Children.OfType<Button>().Single(b => b.Tag is DockEntry e && e.Id == first.Id);
                    var move = movingButton.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Déplacer vers…"));
                    move.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Dock")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check(Panel() is null);
                    Check(settings.Entries.All(e => !e.IsGroup && e.GroupId is null));
                    Check(store.Load(out var warning).Entries.SequenceEqual(settings.Entries) && warning is null);
                }
                finally { window.Close(); }
            });
            Run("Grouping feedback is non-activating and previews pending and armed pair states", () =>
            {
                var display = Native.Displays().Single(d => d.Primary);
                var target = new PixelRect(display.Work.X + 100, display.Work.Y + 100, 60 * display.Scale, 60 * display.Scale);
                var source = Native.Icon(Environment.ProcessPath!); var destination = Native.Icon(temporary);
                var feedback = new GroupingFeedback();
                try
                {
                    var foreground = Native.GetForegroundWindow();
                    feedback.Update(target, display, "Maintenir pour grouper", false, source, destination);
                    Check(!feedback.IsHitTestVisible && !feedback.ShowActivated && Native.GetForegroundWindow() == foreground);
                    if (args.Length == 2 && args[0] == "--preview") SavePreview(feedback, Path.Combine(args[1], "group-hover-pending.png"));
                    feedback.Update(target, display, "Relâcher pour créer un groupe", true, source, destination);
                    if (args.Length == 2 && args[0] == "--preview") SavePreview(feedback, Path.Combine(args[1], "group-hover-ready.png"));
                }
                finally { feedback.Close(); }
            });
            Run("Drop pipeline imports into groups, deduplicates globally and transfers between all surfaces", () =>
            {
                var a = new DockEntry(Guid.NewGuid(), "", DisplayName: "A", IsGroup: true);
                var b = new DockEntry(Guid.NewGuid(), "", DisplayName: "B", IsGroup: true);
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                var seedA = new DockEntry(Guid.NewGuid(), "a1.exe", GroupId: a.Id);
                var seedA2 = new DockEntry(Guid.NewGuid(), "a2.exe", GroupId: a.Id);
                var seedB = new DockEntry(Guid.NewGuid(), "b1.exe", GroupId: b.Id);
                var seedB2 = new DockEntry(Guid.NewGuid(), "b2.exe", GroupId: b.Id);
                var settings = new DockSettings { Entries = [a, b, separator, seedA, seedA2, seedB, seedB2] };
                var root = Path.Combine(temporary, "group-drops"); Directory.CreateDirectory(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json")); store.Save(settings);
                var source = Path.Combine(root, "Mon application.lnk");
                ShellShortcut.Write(source, null, new(Environment.ProcessPath!, "--group-test", root, true));
                var window = new DockWindow(settings, store, new FakeRecycleBin(), new FakeApplicationWindows()); window.Show();
                try
                {
                    var fileDrop = new DataObject(DataFormats.FileDrop, new[] { source, temporary });
                    window.DropEntries(fileDrop, a.Id);
                    var members = settings.Entries.Where(e => e.GroupId == a.Id).ToArray(); Check(members.Length == 4);
                    var copy = members.Single(e => ShellShortcut.IsLink(e.Path));
                    Check(copy.SourcePath == source && File.Exists(copy.Path) && copy.Path != source);
                    window.DropEntries(fileDrop, b.Id); Check(settings.Entries.Count == 9);
                    var moving = new DataObject("WinDock.EntryId", copy.Id.ToString());
                    window.DropEntries(moving, b.Id); Check(settings.Entries.Single(e => e.Id == copy.Id).GroupId == b.Id);
                    window.DropEntries(moving, null, a.Id); Check(settings.Entries[0].Id == copy.Id);
                    window.DropEntries(moving, a.Id, members.Single(e => e.Path == temporary).Id);
                    Check(settings.Entries.Where(e => e.GroupId == a.Id).Select(e => e.Id).TakeLast(2).First() == copy.Id);
                    var snapshot = settings.Entries.ToArray();
                    window.DropEntries(new DataObject("WinDock.EntryId", a.Id.ToString()), b.Id);
                    window.DropEntries(new DataObject("WinDock.EntryId", separator.Id.ToString()), b.Id);
                    window.DropEntries(moving, Guid.NewGuid());
                    Check(settings.Entries.SequenceEqual(snapshot) && File.Exists(copy.Path) && File.Exists(source));
                    Check(ShellShortcut.Read(copy.Path).Arguments == "--group-test");
                    Check(store.Load(out var warning).Entries.SequenceEqual(settings.Entries) && warning is null);
                }
                finally { window.Close(); }
            });
            Run("Group panels scroll, fit every edge and reject results after closure", () =>
            {
                foreach (var edge in Enum.GetValues<DockEdge>())
                {
                    var group = new DockEntry(Guid.NewGuid(), "", DisplayName: "Outils de développement", IsGroup: true);
                    var members = Enumerable.Range(0, 45).Select(i => new DockEntry(Guid.NewGuid(), Environment.ProcessPath!, DisplayName: "Application " + i + " — nom long", GroupId: group.Id)).ToArray();
                    var settings = new DockSettings { Edge = edge, Entries = [group, ..members] };
                    var service = new FakeApplicationWindows();
                    var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "group-edges-" + edge + ".json")), new FakeRecycleBin(), service); window.Show();
                    GroupPanel? Panel() => (GroupPanel?)typeof(DockWindow).GetField("groupPanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window);
                    try
                    {
                        window.ToggleGroup(group); var panel = Panel()!;
                        Check(panel.Items.Children.Count == 45 && panel.Items.Columns == 4 && panel.Scroll.ScrollableHeight > 0);
                        var origin = panel.PointToScreen(new Point());
                        var dpi = VisualTreeHelper.GetDpi(panel).DpiScaleX;
                        var monitor = Native.Displays().Single(d => d.Primary);
                        Check(origin.X >= monitor.Work.X - 1 && origin.Y >= monitor.Work.Y - 1);
                        Check(origin.X + panel.ActualWidth * dpi <= monitor.Work.Right + 1 && origin.Y + panel.ActualHeight * dpi <= monitor.Work.Bottom + 1);
                        var popup = new WindowInteropHelper(panel).Handle;
                        var dock = new WindowInteropHelper(window).Handle;
                        var refresh = typeof(DockWindow).GetMethod("RefreshPlacement", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                        refresh.Invoke(window, null); Check(IsAbove(popup, dock));
                        if (args.Length == 2 && args[0] == "--preview" && edge == DockEdge.Bottom) SavePreview(panel, Path.Combine(args[1], "group-scroll.png"));
                        var entry = members[0]; var button = (Button)panel.Items.Children[0];
                        var request = window.OpenEntryAsync(entry, button);
                        window.CloseGroup(); service.Lists[^1].SetResult([new(1, 10, entry.Path, null)]);
                        PumpUntil(() => request.IsCompleted); Check(service.Activated.Count == 0);
                        window.ToggleGroup(group); panel = Panel()!; button = (Button)panel.Items.Children[0];
                        request = window.OpenEntryAsync(entry, button); service.Lists[^1].SetResult([new(1, 10, entry.Path, null)]);
                        PumpUntil(() => request.IsCompleted); Check(service.Activated.Count == 1 && Panel() is null);
                        window.ToggleGroup(group); settings.Edge = edge == DockEdge.Bottom ? DockEdge.Top : DockEdge.Bottom;
                        refresh.Invoke(window, null); Check(Panel() is null);
                    }
                    finally { window.Close(); }
                }
            });
            Run("Click restores a single window, offers a chooser and ignores concurrent or obsolete searches", () =>
            {
                var entry = new DockEntry(Guid.NewGuid(), Environment.ProcessPath!);
                var settings = new DockSettings { Entries = [entry] };
                var service = new FakeApplicationWindows();
                var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "activate-menu.json")), new FakeRecycleBin(), service);
                window.Show();
                try
                {
                    var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                    var button = panel.Children.OfType<Button>().Single();
                    var candidate = new ApplicationWindow(1, 10, entry.Path, null, "Document _1");
                    var first = window.OpenEntryAsync(entry, button);
                    var duplicate = window.OpenEntryAsync(entry, button);
                    Check(service.Lists.Count == 1 && duplicate.IsCompleted);
                    service.Lists[0].SetResult([candidate]);
                    PumpUntil(() => first.IsCompleted);
                    first.GetAwaiter().GetResult();
                    Check(service.Activated.SequenceEqual(new[] { candidate }));
                    var multiple = window.OpenEntryAsync(entry, button);
                    service.Lists[^1].SetResult([candidate, candidate with { Handle = 2, Title = "" }]);
                    PumpUntil(() => multiple.IsCompleted);
                    var menu = (ContextMenu)typeof(DockWindow).GetField("activeMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                    Check(menu.IsOpen && menu.PlacementTarget == button);
                    Check(((TextBlock)((MenuItem)menu.Items[0]).Header).Text == "Document _1");
                    Check(((TextBlock)((MenuItem)menu.Items[1]).Header).Text == "Fenêtre sans titre");
                    Check(Equals(((MenuItem)menu.Items[^1]).Header, "Lancer à nouveau"));
                    if (args.Length == 2 && args[0] == "--preview")
                    {
                        menu.UpdateLayout();
                        Directory.CreateDirectory(args[1]);
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth * 2), (int)Math.Ceiling(menu.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        bitmap.Render(menu);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(args[1], "window-menu.png")); encoder.Save(stream);
                    }
                    ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check(!menu.IsOpen && service.Activated.Count == 2 && service.Activated[^1].Handle == 2);
                    var failure = window.OpenEntryAsync(entry, button);
                    service.Lists[^1].SetException(new IOException("Unavailable"));
                    PumpUntil(() => failure.IsCompleted);
                    menu = (ContextMenu)typeof(DockWindow).GetField("activeMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                    Check(menu.IsOpen && !((MenuItem)menu.Items[0]).IsEnabled && service.Activated.Count == 2);
                    menu.IsOpen = false;
                    var interrupted = window.OpenEntryAsync(entry, button);
                    button.ContextMenu.PlacementTarget = button;
                    button.ContextMenu.IsOpen = true;
                    service.Lists[^1].SetResult([candidate]);
                    PumpUntil(() => interrupted.IsCompleted);
                    Check(service.Activated.Count == 2 && button.ContextMenu.IsOpen);
                    button.ContextMenu.IsOpen = false;
                    var obsolete = window.OpenEntryAsync(entry, button);
                    settings.Entries.Clear();
                    service.Lists[^1].SetResult([candidate]);
                    PumpUntil(() => obsolete.IsCompleted);
                    Check(service.Activated.Count == 2);
                    settings.Entries.Add(entry);
                    var closing = window.OpenEntryAsync(entry, button);
                    window.Close();
                    service.Lists[^1].SetResult([candidate]);
                    PumpUntil(() => closing.IsCompleted);
                    Check(service.Activated.Count == 2);
                }
                finally { window.Close(); }
            });
            Run("Click with no windows and explicit relaunch preserve shortcut arguments and working directory", () =>
            {
                var work = Path.Combine(temporary, "relaunch-work"); Directory.CreateDirectory(work);
                var output = Path.Combine(work, "result.txt");
                var link = Path.Combine(work, "app.lnk");
                ShellShortcut.Write(link, null, new(Environment.ProcessPath!, "--record \"" + output + "\" \"été espaces\"", work, true));
                var entry = new DockEntry(Guid.NewGuid(), link);
                var service = new FakeApplicationWindows();
                var window = new DockWindow(new DockSettings { Entries = [entry] }, new SettingsStore(Path.Combine(work, "settings.json")), new FakeRecycleBin(), service);
                window.Show();
                try
                {
                    var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                    var button = panel.Children.OfType<Button>().Single();
                    var opening = window.OpenEntryAsync(entry, button);
                    service.Lists[^1].SetResult([]);
                    PumpUntil(() => opening.IsCompleted && File.Exists(output), 10000);
                    opening.GetAwaiter().GetResult();
                    Check(File.ReadAllText(output) == work + "|été espaces");
                    File.Delete(output);
                    var menu = button.ContextMenu;
                    var launch = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Lancer à nouveau"));
                    launch.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    PumpUntil(() => File.Exists(output), 10000);
                    Check(File.ReadAllText(output) == work + "|été espaces" && service.Lists.Count == 1);
                    File.Delete(output);
                    var choosing = window.OpenEntryAsync(entry, button);
                    service.Lists[^1].SetResult([new(1, 10, entry.Path, null, "Premier"), new(2, 10, entry.Path, null, "Second")]);
                    PumpUntil(() => choosing.IsCompleted);
                    var chooser = (ContextMenu)typeof(DockWindow).GetField("activeMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                    ((MenuItem)chooser.Items[^1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    PumpUntil(() => File.Exists(output), 10000);
                    Check(!chooser.IsOpen && File.ReadAllText(output) == work + "|été espaces");
                }
                finally { window.Close(); }
            });
            Run("Native activation restores a dedicated minimized window without changing maximized state", () =>
            {
                var target = new Window { Title = "WinDock activation test", Width = 240, Height = 120 };
                target.Show();
                try
                {
                    var entry = new DockEntry(Guid.NewGuid(), Environment.ProcessPath!);
                    var candidate = new ApplicationWindow(new WindowInteropHelper(target).Handle, Environment.ProcessId, entry.Path, null);
                    var service = new ApplicationWindows(() => [candidate], _ => candidate, _ => 0);
                    target.WindowState = WindowState.Minimized;
                    var request = service.ActivateAsync(entry, candidate, default);
                    PumpUntil(() => request.IsCompleted && target.WindowState != WindowState.Minimized);
                    Check(request.GetAwaiter().GetResult() != WindowActivationResult.Unavailable);
                    target.WindowState = WindowState.Maximized;
                    request = service.ActivateAsync(entry, candidate, default);
                    PumpUntil(() => request.IsCompleted);
                    Check(target.WindowState == WindowState.Maximized);
                }
                finally { target.Close(); }
            });
            Run("Fixed dock remains visible and restores after full screen; auto hide still works", () =>
            {
                var settings = new DockSettings { AutoHide = false };
                var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "fixed.json")));
                try
                {
                    window.UpdateVisibility(false, false, false, 1000);
                    window.UpdateVisibility(false, false, false, 3000);
                    Check(window.IsHitTestVisible);
                    window.UpdateVisibility(true, false, false, 4000);
                    Check(!window.IsHitTestVisible);
                    window.UpdateVisibility(false, false, false, 5000);
                    Check(window.IsHitTestVisible);
                    settings.AutoHide = true;
                    window.UpdateVisibility(false, false, false, 6000);
                    window.UpdateVisibility(false, false, false, 6700);
                    Check(!window.IsHitTestVisible);
                    window.UpdateVisibility(false, true, true, 7000);
                    window.UpdateVisibility(false, true, true, 7160);
                    Check(window.IsHitTestVisible);
                }
                finally { window.Close(); }
            });
            Run("Recycle icon fallback caches each successful state and retries unavailable images", () =>
            {
                var icon = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
                icon.Freeze();
                var attempts = new List<bool>();
                var fallbacks = new List<bool>();
                var service = new RecycleBinService(empty => { attempts.Add(empty); return null; },
                    empty => { fallbacks.Add(empty); return icon; });
                Check(ReferenceEquals(service.GetIcon(true), icon));
                Check(ReferenceEquals(service.GetIcon(true), icon));
                Check(ReferenceEquals(service.GetIcon(false), icon));
                Check(attempts.SequenceEqual(new[] { true, false }) && fallbacks.SequenceEqual(attempts));
                var unavailable = new RecycleBinService(empty => { attempts.Add(empty); return null; }, _ => null);
                Check(unavailable.GetIcon(true) is null && unavailable.GetIcon(true) is null && attempts.Count == 4);
                var preferred = new RecycleBinService(_ => icon, _ => throw new InvalidOperationException("Unexpected fallback"));
                Check(ReferenceEquals(preferred.GetIcon(false), icon));
            });
            Run("Recycle bin retains state on failure, stays last, and uses only simulated actions", () =>
            {
                var pixels = new byte[256 * 256 * 4];
                var highResolutionIcon = BitmapSource.Create(256, 256, 96, 96, PixelFormats.Bgra32, null, pixels, 256 * 4);
                highResolutionIcon.Freeze();
                var fake = new FakeRecycleBin { Icon = highResolutionIcon };
                var settings = new DockSettings { ShowRecycleBin = true, Entries = [new(Guid.NewGuid(), temporary), new(Guid.NewGuid(), "", true)] };
                var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "recycle.json")), fake);
                try
                {
                    var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                    var button = panel.Children.OfType<Button>().Last();
                    Check(button.Tag is null && panel.Children.Count == 3 && settings.Entries.Count == 2);
                    string Label() => ((TextBlock)((Border)((ToolTip)button.ToolTip).Content).Child).Text;
                    void Refresh()
                    {
                        var task = window.RefreshRecycleAsync();
                        var duplicate = window.RefreshRecycleAsync();
                        Check(duplicate.IsCompleted);
                        PumpUntil(() => task.IsCompleted);
                        task.GetAwaiter().GetResult();
                    }
                    Refresh();
                    Check(Label().Contains("indisponible"));
                    var displayedIcon = (Image)button.Content;
                    Check(ReferenceEquals(displayedIcon.Source, highResolutionIcon) && highResolutionIcon.IsFrozen);
                    Check(displayedIcon.Width == 48 && displayedIcon.Height == 48 && highResolutionIcon.PixelWidth == 256);
                    Check(RenderOptions.GetBitmapScalingMode(displayedIcon) == BitmapScalingMode.HighQuality);
                    fake.EmptyState = true;
                    Refresh();
                    Check(Label() == "Corbeille vide");
                    fake.EmptyState = false;
                    Refresh();
                    Check(Label() == "Corbeille pleine");
                    fake.Icon = null;
                    fake.EmptyState = null;
                    Refresh();
                    Check(Label() == "Corbeille pleine");
                    Check(ReferenceEquals(((Image)button.Content).Source, highResolutionIcon));
                    fake.ThrowOnRead = true;
                    Refresh();
                    Check(Label() == "Corbeille pleine" && fake.MaximumConcurrentReads == 1 && fake.Reads == 5);
                    fake.ThrowOnRead = false;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => fake.Opens == 1 && fake.Reads >= 6);
                    var menuItems = button.ContextMenu.Items.OfType<MenuItem>().ToArray();
                    Check(menuItems.Select(i => (string)i.Header).SequenceEqual(new[] { "Ouvrir", "Vider la corbeille…", "Ajouter…", "Ajouter un séparateur", "Réglages…", "Quitter WinDock" }));
                    menuItems[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    PumpUntil(() => fake.Empties == 1 && Label() == "Corbeille vide");
                    Check(settings.Entries.Count == 2);
                    settings.ShowRecycleBin = false;
                    var reads = fake.Reads;
                    window.RefreshRecycleAsync().GetAwaiter().GetResult();
                    Check(reads == fake.Reads);
                }
                finally { window.Close(); }
            });
            Run("Settings restore, activate without permanent topmost, and persist switches", () =>
            {
                var store = new SettingsStore(Path.Combine(temporary, "settings-switches.json"));
                var settings = new DockSettings();
                var window = new DockWindow(settings, store, new FakeRecycleBin());
                var item = window.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Réglages…"));
                try
                {
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    var dialog = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Réglages — WinDock");
                    Check(!dialog.Topmost && dialog.IsActive);
                    dialog.WindowState = WindowState.Minimized;
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check(dialog.WindowState == WindowState.Normal && !dialog.Topmost && dialog.IsActive);
                    var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
                    dialog.MaxHeight = 260;
                    dialog.UpdateLayout();
                    Check(((ScrollViewer)dialog.Content).ScrollableHeight > 0 && dialog.ActualHeight <= 260);
                    var autoHide = panel.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Masquage automatique"));
                    autoHide.IsChecked = false;
                    autoHide.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                    var recycle = panel.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Afficher la corbeille"));
                    recycle.IsChecked = true;
                    recycle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                    Check(!store.Load(out _).AutoHide && store.Load(out _).ShowRecycleBin);
                    var dockPanel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                    Check(dockPanel.Children.Count == 1 && dockPanel.Children[0] is Button { Tag: null });
                    var other = new Window { Width = 200, Height = 100 };
                    try { other.Show(); other.Activate(); Check(!dialog.Topmost && other.IsActive); }
                    finally { other.Close(); }
                    dialog.Close();
                }
                finally { window.Close(); }
            });
            Run("Dock labels use opaque rounded styling and stay within the screen on all edges", () =>
            {
                foreach (var edge in Enum.GetValues<DockEdge>())
                {
                    var settings = new DockSettings { AutoHide = false, Edge = edge, Entries = [new(Guid.NewGuid(), temporary, DisplayName: "Une application avec un nom très long pour vérifier le retour à la ligne du libellé")] };
                    var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "labels.json")));
                    try
                    {
                        window.Show();
                        window.UpdateLayout();
                        var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                        var button = panel.Children.OfType<Button>().Single();
                        var label = (ToolTip)button.ToolTip;
                        label.IsOpen = true;
                        PumpUntil(() => label.ActualWidth > 0 && PresentationSource.FromVisual(label) is not null);
                        var border = (Border)label.Content;
                        Check(border.CornerRadius.TopLeft == 8 && ((SolidColorBrush)border.Background).Color.A == 255);
                        Check(((TextBlock)border.Child).FontSize == 12 && !label.IsHitTestVisible && label.ActualWidth <= 281);
                        var origin = label.PointToScreen(new Point());
                        var end = label.PointToScreen(new Point(label.ActualWidth, label.ActualHeight));
                        Check(Native.Displays().Any(d => origin.X >= d.Work.X - 1 && origin.Y >= d.Work.Y - 1 && end.X <= d.Work.Right + 1 && end.Y <= d.Work.Bottom + 1));
                        if (edge == DockEdge.Bottom) Check(end.Y <= button.PointToScreen(new Point()).Y);
                        if (args.Length == 2 && args[0] == "--preview" && edge == DockEdge.Bottom)
                        {
                            Directory.CreateDirectory(args[1]);
                            var labelBitmap = new RenderTargetBitmap((int)Math.Ceiling(label.ActualWidth * 2), (int)Math.Ceiling(label.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                            labelBitmap.Render(label);
                            var labelEncoder = new PngBitmapEncoder(); labelEncoder.Frames.Add(BitmapFrame.Create(labelBitmap));
                            using var labelStream = File.Create(Path.Combine(args[1], "label.png"));
                            labelEncoder.Save(labelStream);
                        }
                        button.ContextMenu.PlacementTarget = button;
                        button.ContextMenu.IsOpen = true;
                        PumpUntil(() => !label.IsOpen);
                        button.ContextMenu.IsOpen = false;
                        label.IsOpen = true;
                        window.UpdateVisibility(true, false, false, 1000);
                        Check(!label.IsOpen);
                    }
                    finally { window.Close(); }
                }
            });
            Run("Dock menus stay above the dock across periodic placement updates", () =>
            {
                foreach (var edge in Enum.GetValues<DockEdge>())
                {
                    var settings = new DockSettings { Edge = edge, Entries = [new(Guid.NewGuid(), temporary)] };
                    var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "menu-order.json")));
                    try
                    {
                        window.Show();
                        PumpUntil(() => window.OwnedWindows.Cast<Window>().Any(w => w is IndicatorWindow));
                        var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                        var button = panel.Children.OfType<Button>().Single();
                        var menu = button.ContextMenu;
                        menu.PlacementTarget = button;
                        menu.IsOpen = true;
                        PumpUntil(() => PresentationSource.FromVisual(menu) is HwndSource);
                        var popup = ((HwndSource)PresentationSource.FromVisual(menu)).Handle;
                        var dock = new WindowInteropHelper(window).Handle;
                        var watch = Stopwatch.StartNew();
                        PumpUntil(() =>
                        {
                            Check(menu.IsOpen && IsAbove(popup, dock));
                            return watch.ElapsedMilliseconds >= 2300;
                        });
                        menu.IsOpen = false;
                    }
                    finally { window.Close(); }
                }
            });
            Run("Non-activating menu keeps inside clicks and dismisses outside clicks", () =>
            {
                var owner = new Window { Width = 200, Height = 100, ShowActivated = false, ShowInTaskbar = false };
                var target = new Button { Content = "Test" };
                owner.Content = target;
                owner.Show();
                var menu = new ContextMenu { PlacementTarget = target, Items = { new MenuItem { Header = "Test action" } } };
                menu.IsOpen = true;
                owner.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                using (var dismissal = new ContextMenuDismissal(menu, new WindowInteropHelper(owner).Handle))
                {
                    Check(menu.IsOpen && menu.ActualWidth > 0);
                    dismissal.HandlePointerDown(menu.PointToScreen(new Point(menu.ActualWidth / 2, menu.ActualHeight / 2)));
                    owner.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    Check(menu.IsOpen);
                    dismissal.HandlePointerDown(menu.PointToScreen(new Point(-20, -20)));
                    owner.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    Check(!menu.IsOpen);
                }
                owner.Close();
            });
            Run("Real shell shortcut preserves arguments and working directory", () =>
            {
                var workingDirectory = Path.Combine(temporary, "Répertoire de travail");
                Directory.CreateDirectory(workingDirectory);
                var resultPath = Path.Combine(temporary, "launch result.txt");
                var linkPath = Path.Combine(temporary, "Éditeur avec arguments.lnk");
                var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic link = shell.CreateShortcut(linkPath);
                try
                {
                    link.TargetPath = Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe");
                    link.Arguments = $"--record \"{resultPath}\" \"été avec espaces\"";
                    link.WorkingDirectory = workingDirectory;
                    link.WindowStyle = 7;
                    link.Save();
                }
                finally { Marshal.ReleaseComObject(link); Marshal.ReleaseComObject(shell); }
                var tracker = new RunningApplications();
                Check(string.Equals(tracker.ResolveTarget(linkPath), Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe"), StringComparison.OrdinalIgnoreCase));
                Check(tracker.ResolveTarget(workingDirectory) is null);
                var settings = new DockSettings { Entries = [new(Guid.NewGuid(), linkPath)] };
                var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "launch.json")));
                window.Show();
                var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                ((Button)panel.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => File.Exists(resultPath), 10000);
                Check(File.ReadAllText(resultPath) == workingDirectory + "|été avec espaces");
                window.Close();
            });
            Run("WM_CLOSE allows cancellation and closes dedicated minimized windows", () =>
            {
                var first = new Window { Title = "WinDock closure test", Width = 240, Height = 120 };
                var second = new Window { Title = "WinDock minimized test", Width = 240, Height = 120 };
                var requests = 0;
                var cancel = true;
                first.Closing += (_, e) => { requests++; e.Cancel = cancel; };
                first.Show(); second.Show(); second.WindowState = WindowState.Minimized;
                try
                {
                    Check(ApplicationWindows.PostClose(new WindowInteropHelper(first).Handle) == 0);
                    PumpUntil(() => requests == 1);
                    Check(first.IsVisible);
                    Check(ApplicationWindows.PostClose(new WindowInteropHelper(second).Handle) == 0);
                    PumpUntil(() => !second.IsVisible);
                    cancel = false;
                    Check(ApplicationWindows.PostClose(new WindowInteropHelper(first).Handle) == 0);
                    PumpUntil(() => !first.IsVisible);
                    Check(requests == 2);
                }
                finally { cancel = false; first.Close(); second.Close(); }
            });
            Run("Close menu refreshes counts, rejects stale results and dispatches one close request", () =>
            {
                var entry = new DockEntry(Guid.NewGuid(), Path.Combine(temporary, "menu-app.exe"));
                var service = new FakeApplicationWindows();
                var window = new DockWindow(new DockSettings { Entries = [entry] },
                    new SettingsStore(Path.Combine(temporary, "close-menu.json")), new FakeRecycleBin(), service);
                window.Show();
                try
                {
                    var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                    var button = panel.Children.OfType<Button>().Single();
                    var menu = button.ContextMenu;
                    menu.PlacementTarget = button;
                    bool HasClose() => menu.Items.OfType<MenuItem>().Any(i => ((string?)i.Header)?.StartsWith("Fermer") == true);
                    menu.IsOpen = true;
                    PumpUntil(() => service.Pending.Count == 1);
                    menu.IsOpen = false;
                    menu.IsOpen = true;
                    PumpUntil(() => service.Pending.Count == 2);
                    service.Pending[0].SetResult(2);
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    Check(!HasClose());
                    service.Pending[1].SetResult(0);
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    Check(!HasClose());
                    foreach (var count in new[] { 1, 2 })
                    {
                        menu.IsOpen = false; menu.IsOpen = true;
                        service.Pending[^1].SetResult(count);
                        PumpUntil(HasClose);
                        Check(Equals(((MenuItem)menu.Items[0]).Header, count == 1 ? "Fermer l’application" : "Fermer toutes les fenêtres"));
                        Check(Equals(((MenuItem)menu.Items[1]).Header, "Lancer à nouveau"));
                        Check(Equals(((MenuItem)menu.Items[2]).Header, "Modifier / réparer…"));
                    }
                    if (args.Length == 2 && args[0] == "--preview")
                    {
                        menu.UpdateLayout();
                        Directory.CreateDirectory(args[1]);
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth * 2), (int)Math.Ceiling(menu.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        bitmap.Render(menu);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(args[1], "close-menu.png")); encoder.Save(stream);
                    }
                    ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check(!menu.IsOpen && service.Closes == 1);
                }
                finally { window.Close(); }
            });
            Run("Managed shortcuts survive original deletion, preserve parameters and repair atomically", () =>
            {
                var root = Path.Combine(temporary, "managed");
                Directory.CreateDirectory(root);
                var work = Path.Combine(root, "Travail été");
                Directory.CreateDirectory(work);
                var source = Path.Combine(root, "Mon application.lnk");
                var output = Path.Combine(root, "result.txt");
                var executable = Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe");
                var argumentText = "--record \"" + output + "\" \"été \\\"cité\\\" espaces\"";
                ShellShortcut.Write(source, null, new(executable, argumentText, work, true));
                var originalBytes = File.ReadAllBytes(source);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var service = new ManagedShortcuts(store);
                var original = new DockEntry(Guid.NewGuid(), source);
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                var missing = new DockEntry(Guid.NewGuid(), Path.Combine(root, "absent.lnk"));
                var settings = new DockSettings { Entries = [original, separator, missing] };
                store.Save(settings);
                service.Migrate(settings);
                var managed = settings.Entries[0];
                Check(managed.Id == original.Id && managed.SourcePath == source && managed.DisplayName == "Mon application");
                Check(File.ReadAllBytes(managed.Path).SequenceEqual(originalBytes));
                Check(settings.Entries[1] == separator && settings.Entries[2] == missing);
                File.Delete(source);
                var reloaded = store.Load(out var warning);
                Check(warning is null && !service.IsMissing(reloaded.Entries[0]) && service.IsMissing(missing));
                using (var child = Process.Start(new ProcessStartInfo(managed.Path) { UseShellExecute = true }))
                {
                    var watch = Stopwatch.StartNew();
                    while (!File.Exists(output) && watch.ElapsedMilliseconds < 10000) Thread.Sleep(50);
                    Check(File.ReadAllText(output) == work + "|été \"cité\" espaces");
                    child?.WaitForExit(5000);
                }
                var details = ShellShortcut.Read(managed.Path);
                Check(details.Editable && details.Arguments == argumentText && details.WorkingDirectory == work);
                var cleared = service.PrepareEdit(managed, managed.Path, "Sans arguments", details with { Arguments = "", WorkingDirectory = "" });
                Check(ShellShortcut.Read(cleared.Path).Arguments == "" && ShellShortcut.Read(cleared.Path).WorkingDirectory == "");
                service.Commit(settings, [cleared, separator, missing]);
                Check(!File.Exists(managed.Path) && File.Exists(cleared.Path));
                var staged = service.PrepareEdit(cleared, cleared.Path, "Nouvelle version", details);
                using (var locked = new FileStream(Path.Combine(root, "settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var failed = false;
                    try { service.Commit(settings, [staged, separator, missing]); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
                    Check(failed && settings.Entries[0] == cleared && File.Exists(cleared.Path) && !File.Exists(staged.Path));
                }
                var beforeCancel = Directory.GetFiles(Path.Combine(root, "Shortcuts")).Length;
                var editor = new ShortcutEditor(cleared, service, _ => throw new Exception("Cancel must not commit"));
                editor.Show();
                if (args.Length == 2 && args[0] == "--preview")
                {
                    Directory.CreateDirectory(args[1]);
                    editor.UpdateLayout();
                    var content = (FrameworkElement)editor;
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var preview = File.Create(Path.Combine(args[1], "shortcut-editor.png"));
                    encoder.Save(preview);
                }
                editor.Close();
                Check(Directory.GetFiles(Path.Combine(root, "Shortcuts")).Length == beforeCancel);
                var repaired = service.PrepareEdit(missing, executable, "Réparé", new(executable, argumentText, work, true));
                service.Commit(settings, [cleared, separator, repaired]);
                Check(settings.Entries[2].Id == missing.Id && !service.IsMissing(repaired));
                Check(new RunningApplications().ResolveTarget(repaired.Path) == executable);
                var folder = service.PrepareEdit(cleared, work, "Mon dossier", new(work, "", "", false));
                service.Commit(settings, [folder, separator, repaired]);
                Check(settings.Entries[0].Path == work && !File.Exists(cleared.Path));
                // Unreadable links are retained exactly, never reconstructed from empty fields.
                var opaque = Path.Combine(root, "opaque.lnk");
                File.WriteAllBytes(opaque, [1, 2, 3, 4]);
                var opaqueEntry = new DockEntry(Guid.NewGuid(), opaque);
                var preserved = service.PrepareEdit(opaqueEntry, opaque, "Spécial", new(opaque, "", "", false));
                Check(File.ReadAllBytes(preserved.Path).SequenceEqual(File.ReadAllBytes(opaque)));
            });
            Run("Catalogue search and deduplication preserve distinct launch parameters", () =>
            {
                Check(ApplicationCatalog.Matches("Éditeur français", "editeur"));
                Check(ApplicationCatalog.Matches("Éditeur français", " FRANCAIS "));
                Check(!ApplicationCatalog.Matches("Calculatrice", "navigateur"));
                var key = ApplicationCatalog.LaunchKey(new(@"C:\app.exe", "--profile A", "", true));
                var otherKey = ApplicationCatalog.LaunchKey(new(@"C:\app.exe", "--profile a", "", true));
                var a = new CatalogApplication("Application", "app1", null, key);
                var b = new CatalogApplication("Application", "app2", null, otherKey);
                Check(ApplicationCatalog.Deduplicate([a, a with { Name = "Copie" }, b]).Count == 2);
                Check(ApplicationCatalog.Deduplicate([a with { ApplicationId = null }, b with { ApplicationId = null }]).Count == 2);
                Check(ApplicationCatalog.Deduplicate([a, b with { LaunchKey = key }]).Count == 1);
            });
            Run("Catalogue multi-select, existing markers, filtering and cancellation", () =>
            {
                var a = new CatalogApplication("Éditeur", "test.editor", null, "test.editor");
                var b = new CatalogApplication("Calculatrice", "test.calculator", null, "test.calculator");
                var existing = new DockEntry(Guid.NewGuid(), "absent.lnk", ApplicationId: "test.existing");
                var c = new CatalogApplication("Déjà ajouté", "test.existing", null, "test.existing");
                var entries = new List<DockEntry> { existing };
                var added = new List<string>();
                var manual = 0; var folders = 0; var separators = 0;
                var picker = new CatalogWindow(() => entries, app =>
                {
                    added.Add(app.ApplicationId!);
                    entries.Add(new(Guid.NewGuid(), "test.lnk", ApplicationId: app.ApplicationId));
                    return Task.CompletedTask;
                }, _ => manual++, _ => folders++, () => separators++, _ => Task.FromResult(new CatalogResult([a, b, c], null)));
                picker.Show();
                PumpUntil(() => picker.Applications.Items.Count == 3 && picker.RefreshButton.IsEnabled);
                var choices = picker.Applications.Items.Cast<CatalogChoice>().ToArray();
                Check(!choices.Single(choice => choice.Application == c).CanAdd);
                choices.Single(choice => choice.Application == a).IsSelected = true;
                choices.Single(choice => choice.Application == b).IsSelected = true;
                picker.Search.Text = "editeur";
                Check(picker.Applications.Items.Count == 1 && picker.AddButton.IsEnabled);
                picker.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => added.Count == 2 && picker.ManualButton.IsEnabled);
                Check(!choices[0].CanAdd && !choices[1].CanAdd && !picker.AddButton.IsEnabled);
                picker.ManualButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                picker.FolderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                picker.SeparatorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(manual == 1 && folders == 1 && separators == 1);
                picker.Search.Text = "introuvable";
                Check(picker.Applications.Items.Count == 0);
                picker.Close();
                var pending = new TaskCompletionSource<CatalogResult>();
                CancellationToken token = default;
                var closing = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { },
                    ct => { token = ct; return pending.Task; });
                closing.Show();
                PumpUntil(() => token.CanBeCanceled);
                closing.Close();
                Check(token.IsCancellationRequested);
                pending.SetResult(new([a], null));
                var empty = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { },
                    _ => Task.FromResult(new CatalogResult([], "Catalogue indisponible")));
                empty.Show();
                PumpUntil(() => empty.RefreshButton.IsEnabled && empty.Status.Text.Contains("indisponible"));
                Check(empty.ManualButton.IsEnabled && !empty.AddButton.IsEnabled);
                empty.Close();
            });
            Run("Catalogue cache persists icons, shares refresh and survives partial failures", () =>
            {
                var directory = Path.Combine(temporary, "catalog-cache");
                var icon = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 10, 20, 30, 255 }, 4);
                icon.Freeze();
                var app = new CatalogApplication("Éditeur", "test.cached", null, "cached", icon);
                var calls = 0;
                var completion = new TaskCompletionSource<CatalogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var cache = new CatalogCache(directory, _ => { Interlocked.Increment(ref calls); return completion.Task; });
                cache.PreloadAsync().GetAwaiter().GetResult();
                Check(cache.Current is null);
                var first = cache.RefreshAsync();
                Check(ReferenceEquals(first, cache.RefreshAsync()) && calls == 1);
                completion.SetResult(new([app], null));
                first.GetAwaiter().GetResult();
                var restored = new CatalogCache(directory, _ => Task.FromResult(new CatalogResult([], "Lecture partielle")));
                restored.PreloadAsync().GetAwaiter().GetResult();
                Check(restored.Current!.Applications.Single().Icon is BitmapSource { IsFrozen: true });
                Check(restored.RefreshAsync().GetAwaiter().GetResult().Applications.Count == 1);
                Check(restored.Current.Applications.Single().Name == "Éditeur");
                var failing = new CatalogCache(directory, _ => Task.FromException<CatalogResult>(new IOException("indisponible")));
                Check(failing.RefreshAsync().GetAwaiter().GetResult().Applications.Count == 1);
                var empty = new CatalogCache(directory, _ => Task.FromResult(new CatalogResult([], null)));
                Check(empty.RefreshAsync().GetAwaiter().GetResult().Applications.Count == 0);
                var emptyRestored = new CatalogCache(directory);
                emptyRestored.PreloadAsync().GetAwaiter().GetResult();
                Check(emptyRestored.Current!.Applications.Count == 0);
                File.WriteAllText(Path.Combine(directory, "catalog.json"), "{broken");
                var broken = new CatalogCache(directory);
                broken.PreloadAsync().GetAwaiter().GetResult(); Check(broken.Current is null);
                File.WriteAllText(Path.Combine(directory, "catalog.json"), "{\"Version\":99,\"Applications\":[]}");
                var incompatible = new CatalogCache(directory);
                incompatible.PreloadAsync().GetAwaiter().GetResult(); Check(incompatible.Current is null);
                File.WriteAllText(Path.Combine(directory, "catalog.json"), "{\"Version\":1,\"Applications\":[{\"Name\":\"Test\",\"ApplicationId\":\"test\",\"LaunchKey\":\"test\",\"Png\":\"AQID\"}]}");
                var badIcon = new CatalogCache(directory);
                badIcon.PreloadAsync().GetAwaiter().GetResult(); Check(badIcon.Current!.Applications.Single().Icon is null);
                File.WriteAllText(Path.Combine(directory, "catalog.json"), File.ReadAllText(Path.Combine(directory, "catalog.json")).Replace("AQID", "not-base64!"));
                var badEncoding = new CatalogCache(directory);
                badEncoding.PreloadAsync().GetAwaiter().GetResult(); Check(badEncoding.Current!.Applications.Single().Icon is null);
                var blocked = Path.Combine(temporary, "cache-blocked"); File.WriteAllText(blocked, "file");
                var unwritable = new CatalogCache(blocked, _ => Task.FromResult(new CatalogResult([app], null)));
                Check(unwritable.RefreshAsync().GetAwaiter().GetResult().Warning is not null);
                Check(unwritable.Current!.Applications.Count == 1);
            });
            Run("Cached catalogue stays interactive and preserves selection during shared refresh", () =>
            {
                var app = new CatalogApplication("Éditeur", "cached.editor", null, "cached.editor");
                var directory = Path.Combine(temporary, "catalog-ui-cache");
                new CatalogCache(directory, _ => Task.FromResult(new CatalogResult([app], null))).RefreshAsync().GetAwaiter().GetResult();
                var pending = new TaskCompletionSource<CatalogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var calls = 0;
                var cache = new CatalogCache(directory, _ => { Interlocked.Increment(ref calls); return pending.Task; });
                var picker = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { }, cache: cache);
                picker.Show();
                PumpUntil(() => picker.Applications.Items.Count == 1 && calls == 1);
                ((CatalogChoice)picker.Applications.Items[0]).IsSelected = true;
                picker.Search.Text = "editeur";
                Check(picker.AddButton.IsEnabled && !picker.RefreshButton.IsEnabled);
                pending.SetResult(new([app with { Name = "Éditeur actualisé" }], null));
                PumpUntil(() => picker.RefreshButton.IsEnabled);
                Check(picker.Search.Text == "editeur" && ((CatalogChoice)picker.Applications.Items[0]).IsSelected);
                picker.Close();
                var delayed = new TaskCompletionSource<CatalogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken refreshToken = default;
                var shared = new CatalogCache(directory, ct => { refreshToken = ct; return delayed.Task; });
                var closing = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { }, cache: shared);
                closing.Show(); PumpUntil(() => refreshToken.CanBeCanceled); closing.Close();
                Check(!refreshToken.IsCancellationRequested);
                var reopening = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { }, cache: shared);
                reopening.Show(); PumpUntil(() => reopening.Applications.Items.Count == 1);
                delayed.SetResult(new([app], null));
                PumpUntil(() => reopening.RefreshButton.IsEnabled); reopening.Close();
                shared.Stop(); Check(refreshToken.IsCancellationRequested);
            });
            Run("Removal commits before cleanup and preserves original targets", () =>
            {
                var root = Path.Combine(temporary, "removal"); Directory.CreateDirectory(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var manager = new ManagedShortcuts(store);
                var original = Path.Combine(root, "Original.lnk");
                var executable = Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe");
                ShellShortcut.Write(original, null, new(executable, "", root, true));
                var app = manager.Import(new(Guid.NewGuid(), original));
                var folder = new DockEntry(Guid.NewGuid(), root);
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                var settings = new DockSettings { Entries = [app, folder, separator] }; store.Save(settings);
                using (var locked = new FileStream(Path.Combine(root, "settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var failed = false;
                    try { manager.Commit(settings, [folder, separator]); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
                    Check(failed && settings.Entries.Contains(app) && File.Exists(app.Path));
                }
                manager.Commit(settings, [folder, separator]);
                Check(!File.Exists(app.Path) && File.Exists(original) && File.Exists(executable));
                manager.Commit(settings, [separator]); Check(Directory.Exists(root));
                manager.Commit(settings, []); Check(store.Load(out _).Entries.Count == 0);
            });
            Run("Drag removal uses scaled margin and explicit release, never Escape", () =>
            {
                foreach (var scale in new[] { 1.0, 1.5, 2.0 })
                {
                    var dock = new Rect(-1000, 200, 400 * scale, 60 * scale);
                    Check(!DragRemoval.IsArmed(dock, new Point(dock.Left - 23 * scale, dock.Top), scale));
                    Check(DragRemoval.IsArmed(dock, new Point(dock.Left - 25 * scale, dock.Top), scale));
                    Check(DragRemoval.IsArmed(dock, new Point(dock.Right + 25 * scale, dock.Top), scale));
                    Check(DragRemoval.IsArmed(dock, new Point(dock.Left, dock.Top - 25 * scale), scale));
                    Check(DragRemoval.IsArmed(dock, new Point(dock.Left, dock.Bottom + 25 * scale), scale));
                    Check(!DragRemoval.IsArmed(dock, new Point(dock.Left + 1, dock.Top + 1), scale));
                }
                Check(DragRemoval.ShouldRemove(true, false, System.Windows.DragDropKeyStates.None));
                Check(!DragRemoval.ShouldRemove(true, true, System.Windows.DragDropKeyStates.None));
                Check(!DragRemoval.ShouldRemove(false, false, System.Windows.DragDropKeyStates.None));
                Check(!DragRemoval.ShouldRemove(true, false, System.Windows.DragDropKeyStates.LeftMouseButton));
            });
            Run("Missing catalogue apps handle unknown availability and retain identity through rename", () =>
            {
                var root = Path.Combine(temporary, "shell-presence");
                Directory.CreateDirectory(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var service = new ManagedShortcuts(store);
                var link = Path.Combine(root, "opaque.lnk");
                File.WriteAllBytes(link, [1, 2, 3, 4]);
                var entry = new DockEntry(Guid.NewGuid(), link, SourcePath: "shell:AppsFolder\\test.app", ApplicationId: "test.app");
                ApplicationCatalog.RecordPresence(null);
                Check(!service.IsMissing(entry));
                ApplicationCatalog.RecordPresence([]);
                Check(service.IsMissing(entry));
                ApplicationCatalog.RecordPresence(null);
                Check(!service.IsMissing(entry));
                var renamed = service.PrepareEdit(entry, entry.Path, "Nouveau nom", new(entry.Path, "", "", false));
                Check(renamed.ApplicationId == entry.ApplicationId);
                var exe = Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe");
                var repaired = service.PrepareEdit(entry, exe, "Application classique", new(exe, "", "", true));
                Check(repaired.ApplicationId is null);
                var impossible = entry with { ApplicationId = "WinDock.NoSuchApp", Path = exe };
                Check(!new RunningApplications().Snapshot([impossible]).Contains(entry.Id));
            });
            Run("Running indicators follow a real process lifetime and exact executable path", () =>
            {
                var childDirectory = Path.Combine(temporary, "process-test");
                Directory.CreateDirectory(childDirectory);
                foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
                    File.Copy(file, Path.Combine(childDirectory, Path.GetFileName(file)));
                var executable = Path.Combine(childDirectory, "WinDock.Tests.exe");
                var ready = Path.Combine(childDirectory, "ready.txt");
                var entry = new DockEntry(Guid.NewGuid(), executable);
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                var tracker = new RunningApplications();
                Check(tracker.Snapshot([entry, separator]).Count == 0);
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--hold"); start.ArgumentList.Add(ready);
                using var process = Process.Start(start)!;
                try
                {
                    var wait = Stopwatch.StartNew();
                    while (!File.Exists(ready) && !process.HasExited && wait.ElapsedMilliseconds < 10000) Thread.Sleep(50);
                    Check(File.Exists(ready));
                    Check(tracker.Snapshot([entry, separator]).SetEquals([entry.Id]));
                }
                finally { if (!process.HasExited) process.Kill(); process.WaitForExit(); }
                Check(tracker.Snapshot([entry, separator]).Count == 0);
            });
            if (args.Length == 2 && args[0] == "--catalog-smoke")
            {
                Run("Real Windows catalogue, persisted Shell link and Calculator activation", () =>
                {
                    var task = ApplicationCatalog.LoadAsync(CancellationToken.None);
                    PumpUntil(() => task.IsCompleted, 60000);
                    var result = task.GetAwaiter().GetResult();
                    Check(result.Applications.Count > 0 && result.Warning is null);
                    Console.WriteLine($"CATALOG: {result.Applications.Count} apps; {result.Applications.Count(a => a.ShortcutPath is not null)} classic shortcuts; {result.Applications.Count(a => a.ApplicationId?.Contains('!') == true)} packaged apps.");
                    var picker = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { },
                        _ => Task.FromResult(result));
                    picker.Show();
                    PumpUntil(() => picker.RefreshButton.IsEnabled && picker.Applications.Items.Count > 0);
                    picker.UpdateLayout();
                    Directory.CreateDirectory(args[1]);
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(picker.ActualWidth), (int)Math.Ceiling(picker.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(picker);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(Path.Combine(args[1], "catalogue.png"))) encoder.Save(stream);
                    picker.Close();
                    var calculator = result.Applications.First(app => app.ApplicationId?.StartsWith("Microsoft.WindowsCalculator_", StringComparison.OrdinalIgnoreCase) == true);
                    var store = new SettingsStore(Path.Combine(temporary, "catalog-smoke", "settings.json"));
                    var manager = new ManagedShortcuts(store);
                    var settings = new DockSettings();
                    var entry = manager.ImportApplication(calculator);
                    manager.Commit(settings, [entry]);
                    var persisted = store.Load(out var warning).Entries.Single();
                    Check(warning is null && persisted.ApplicationId == calculator.ApplicationId && !manager.IsMissing(persisted));
                    Check(ApplicationCatalog.IsAdded(calculator, ApplicationCatalog.ExistingKeys([persisted])));
                    using var activation = Process.Start(new ProcessStartInfo(persisted.Path) { UseShellExecute = true });
                    var tracker = new RunningApplications();
                    PumpUntil(() => tracker.Snapshot([persisted]).Contains(persisted.Id), 20000);
                    Console.WriteLine("STORE: Calculator launched from persisted .lnk; running indicator recognized its Windows identity.");
                    // Also exercise the catalogue import path with a real, argument-bearing classic shortcut.
                    var source = Path.Combine(temporary, "Classic catalogue test.lnk");
                    var output = Path.Combine(temporary, "classic-catalogue.txt");
                    var executable = Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe");
                    var details = new ShortcutDetails(executable, "--record \"" + output + "\" \"catalogue été\"", temporary, true);
                    ShellShortcut.Write(source, null, details);
                    var classic = manager.ImportApplication(new("Classic test", null, source, ApplicationCatalog.LaunchKey(details)));
                    manager.Commit(settings, [persisted, classic]);
                    File.Delete(source);
                    var classicReloaded = store.Load(out _).Entries[1];
                    using var classicProcess = Process.Start(new ProcessStartInfo(classicReloaded.Path) { UseShellExecute = true });
                    PumpUntil(() => File.Exists(output), 10000);
                    Check(File.ReadAllText(output) == temporary + "|catalogue été");
                    classicProcess?.WaitForExit(5000);
                });
            }
            if (args.Length == 2 && args[0] == "--preview")
            {
                Run("Render native default dock using temporary settings without launching targets", () =>
                {
                    var root = Path.Combine(temporary, "native-default-preview");
                    var store = new SettingsStore(Path.Combine(root, "settings.json"));
                    var preparation = new DefaultDockService(store).PrepareAsync(default);
                    PumpUntil(() => preparation.IsCompleted, 60000);
                    using var prepared = preparation.GetAwaiter().GetResult();
                    var settings = new DockSettings(); prepared.Apply(settings);
                    Check(settings.ShowRecycleBin && settings.Entries.Any(e => e.DisplayName == "Explorateur"));
                    var iconTask = StaWorker.RunAsync(_ => new RecycleBinService().GetIcon(true), default);
                    PumpUntil(() => iconTask.IsCompleted);
                    var fake = new FakeRecycleBin { EmptyState = true, Icon = iconTask.GetAwaiter().GetResult() };
                    var window = new DockWindow(settings, store, fake, new FakeApplicationWindows()); window.Show();
                    try
                    {
                        var refresh = (Task)typeof(DockWindow).GetMethod("RefreshRecycleAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null)!;
                        PumpUntil(() => refresh.IsCompleted); refresh.GetAwaiter().GetResult();
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                        SavePreview(window, Path.Combine(args[1], "default-dock.png"));
                        Console.WriteLine("Default preview entries: " + string.Join(", ", settings.Entries.Where(e => !e.IsSeparator).Select(ManagedShortcuts.Name)));
                        var apps = settings.Entries.Where(e => !e.IsSeparator && ShellShortcut.IsLink(e.Path)).TakeLast(2).ToArray();
                        if (apps.Length == 2)
                        {
                            var groupId = Guid.NewGuid();
                            var grouped = EntryRules.GroupPair(settings.Entries, apps[1].Id, apps[0].Id, groupId)
                                .Select(e => e.Id == groupId ? e with { DisplayName = "Applications" } : e).ToList();
                            new ManagedShortcuts(store).Commit(settings, grouped);
                            typeof(DockWindow).GetMethod("RenderItems", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
                            window.ToggleGroup(settings.Entries.Single(e => e.Id == groupId));
                            var panel = (GroupPanel)typeof(DockWindow).GetField("groupPanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                            SavePreview(panel, Path.Combine(args[1], "group-native.png"));
                        }
                    }
                    finally { window.Close(); }
                });
                Run("Stock recycle bin icons are high resolution, frozen and cached for both states", () =>
                {
                    var task = StaWorker.RunAsync(_ =>
                    {
                        var service = new RecycleBinService();
                        return new[] { true, false }.Select(empty =>
                        {
                            var icon = service.GetIcon(empty);
                            Check(icon is { PixelWidth: 256, PixelHeight: 256, IsFrozen: true });
                            Check(ReferenceEquals(icon, service.GetIcon(empty)));
                            var standard = RecycleBinService.LoadStandardIcon(empty);
                            Check(standard is { IsFrozen: true });
                            return (Empty: empty, Icon: icon!, Standard: standard!);
                        }).ToArray();
                    }, default);
                    PumpUntil(() => task.IsCompleted);
                    Directory.CreateDirectory(args[1]);
                    foreach (var state in task.GetAwaiter().GetResult())
                    foreach (var scale in new[] { 1.0, 1.5, 2.0 })
                    {
                        // Standard icon on the left, jumbo icon on the right, at the actual dock size.
                        var panel = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(27, 30, 39)) };
                        foreach (var source in new[] { state.Standard, state.Icon })
                        {
                            var image = new Image { Source = source, Width = 48, Height = 48, Margin = new Thickness(12), Stretch = Stretch.Uniform };
                            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                            panel.Children.Add(image);
                        }
                        panel.Measure(new Size(144, 72)); panel.Arrange(new Rect(0, 0, 144, 72)); panel.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)(144 * scale), (int)(72 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        bitmap.Render(panel);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(args[1], $"recycle-{(state.Empty ? "empty" : "full")}-{scale * 100:0}.png"));
                        encoder.Save(stream);
                    }
                });
                Run("Render populated and empty WPF docks", () =>
                {
                    Directory.CreateDirectory(args[1]);
                    foreach (var populated in new[] { false, true })
                    {
                        var settings = new DockSettings();
                        if (populated)
                        {
                            settings.ShowRecycleBin = true;
                            settings.BackgroundOpacity = 0.55;
                            settings.Entries.Add(new(Guid.NewGuid(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")));
                            settings.Entries.Add(new(Guid.NewGuid(), Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
                            settings.Entries.Add(new(Guid.NewGuid(), "", true));
                            settings.Entries.Add(new(Guid.NewGuid(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")));
                        }
                        var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "preview.json")));
                        new WindowInteropHelper(window).EnsureHandle();
                        if (populated)
                        {
                            var refresh = window.RefreshRecycleAsync();
                            PumpUntil(() => refresh.IsCompleted);
                            refresh.GetAwaiter().GetResult();
                            var bin = ((StackPanel)((Viewbox)((Border)window.Content).Child).Child).Children.OfType<Button>().Last();
                            Check(bin.Content is Image { Source.IsFrozen: true });
                        }
                        var content = (FrameworkElement)window.Content;
                        content.Measure(new Size(window.Width, window.Height));
                        content.Arrange(new Rect(0, 0, window.Width, window.Height));
                        content.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)window.Width * 2, (int)window.Height * 2, 192, 192, PixelFormats.Pbgra32);
                        bitmap.Render(content);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(args[1], populated ? "dock-populated.png" : "dock-empty.png"));
                        encoder.Save(stream);
                        if (populated)
                        {
                            var settingsItem = window.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Réglages…"));
                            settingsItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                            var settingsWindow = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Réglages — WinDock");
                            try
                            {
                                settingsWindow.UpdateLayout();
                                var settingsPanel = (StackPanel)((ScrollViewer)settingsWindow.Content).Content;
                                Check(settingsWindow.Icon is BitmapSource settingsIcon && settingsIcon.PixelWidth == 256 && settingsIcon.PixelHeight == 256);
                                var version = typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
                                Check(!string.IsNullOrWhiteSpace(version));
                                Check(settingsPanel.Children.OfType<TextBlock>().Any(text => text.Text == $"WinDock — Version {version}"));
                                var settingsBitmap = new RenderTargetBitmap((int)Math.Ceiling(settingsWindow.ActualWidth), (int)Math.Ceiling(settingsWindow.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                                settingsBitmap.Render(settingsWindow);
                                var settingsEncoder = new PngBitmapEncoder();
                                settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));
                                using var settingsStream = File.Create(Path.Combine(args[1], "settings.png"));
                                settingsEncoder.Save(settingsStream);
                            }
                            finally { settingsWindow.Close(); }
                        }
                        window.Close();
                    }
                });
            }
            Console.WriteLine($"PASS: {passed} test groups.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temporary, true); }
    }

    private static DefaultDockSources CreateDefaultSources(string root)
    {
        Directory.CreateDirectory(root);
        var home = Path.Combine(root, "Profil utilisateur");
        var downloads = Path.Combine(root, "Autre emplacement", "Téléchargements déplacés");
        var documents = Path.Combine(root, "OneDrive simulé", "Documents déplacés");
        Directory.CreateDirectory(home); Directory.CreateDirectory(downloads); Directory.CreateDirectory(documents);
        var browser = Path.Combine(root, "TestBrowser.exe"); File.WriteAllText(browser, "not executed");
        var mailExe = Path.Combine(root, "TestMail.exe"); File.WriteAllText(mailExe, "not executed");
        var shortcut = Path.Combine(root, "Messagerie originale.lnk");
        ShellShortcut.Write(shortcut, null, new(mailExe, "", root, true));
        var app = new CatalogApplication("Messagerie test", "test.mail!app", shortcut, ApplicationCatalog.LaunchKey(new(mailExe, "", root, true)));
        return new(Environment.ProcessPath!, new("Navigateur test", browser), new("Messagerie test", mailExe, app), home, downloads, documents);
    }

    private sealed class FakeDefaultDockEnvironment(DefaultDockSources sources) : IDefaultDockEnvironment
    {
        internal int Calls;
        public DefaultDockSources Resolve(CancellationToken token)
        {
            Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA);
            token.ThrowIfCancellationRequested(); Interlocked.Increment(ref Calls); return sources;
        }
    }

    private sealed class FakeDefaultDockService(Func<CancellationToken, Task<PreparedDefaultDock>> prepare) : IDefaultDockService
    {
        internal int Calls;
        public Task<PreparedDefaultDock> PrepareAsync(CancellationToken token) { Calls++; return prepare(token); }
    }

    private static void SavePreview(FrameworkElement element, string path)
    {
        element.UpdateLayout(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * 2), (int)Math.Ceiling(element.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }
    private sealed class FakeApplicationWindows : IApplicationWindows
    {
        internal readonly List<TaskCompletionSource<int>> Pending = [];
        internal int Closes;
        internal readonly List<TaskCompletionSource<IReadOnlyList<ApplicationWindow>>> Lists = [];
        internal readonly List<ApplicationWindow> Activated = [];
        public Task<IReadOnlyList<ApplicationWindow>> ListAsync(DockEntry entry, CancellationToken token)
        {
            var request = new TaskCompletionSource<IReadOnlyList<ApplicationWindow>>(); Lists.Add(request); return request.Task;
        }
        public Task<WindowActivationResult> ActivateAsync(DockEntry entry, ApplicationWindow window, CancellationToken token)
        {
            Activated.Add(window); return Task.FromResult(WindowActivationResult.Activated);
        }
        public Task<int> CountAsync(DockEntry entry, CancellationToken token)
        {
            var request = new TaskCompletionSource<int>(); Pending.Add(request); return request.Task;
        }
        public Task<int> CloseAsync(DockEntry entry, CancellationToken token) { Closes++; return Task.FromResult(0); }
    }
    private sealed class FakeRecycleBin : IRecycleBinService
    {
        internal bool? EmptyState;
        internal bool ThrowOnRead;
        internal ImageSource? Icon;
        internal int Reads, Opens, Empties, MaximumConcurrentReads;
        private int concurrentReads;
        public RecycleBinSnapshot Read()
        {
            var active = Interlocked.Increment(ref concurrentReads);
            MaximumConcurrentReads = Math.Max(MaximumConcurrentReads, active);
            Interlocked.Increment(ref Reads);
            try
            {
                Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA);
                Thread.Sleep(30);
                if (ThrowOnRead) throw new IOException("Simulated unavailable recycle bin");
                return new(EmptyState, Icon);
            }
            finally { Interlocked.Decrement(ref concurrentReads); }
        }
        public void Open() => Interlocked.Increment(ref Opens);
        public void Empty(nint owner) { EmptyState = true; Interlocked.Increment(ref Empties); }
    }
    private static void PumpUntil(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var frame = new DispatcherFrame();
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || watch.ElapsedMilliseconds > timeoutMilliseconds) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Check(condition());
    }
    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
}
