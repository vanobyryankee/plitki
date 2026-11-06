using System.IO;
using System.Windows;
using Tiles.Core;

namespace Tiles.App;

/// <summary>Окно приложения, ярлыки «Плитки» и общие переключатели.</summary>
public partial class App
{
    private const string HubEventName = "Tiles.DesktopFolders.OpenHub";
    private HubWindow? _hub;

    internal IReadOnlyList<TileWindow> Tiles => _tiles;

    /// <summary>
    /// Часовой пояс: .NET запоминает его при запуске, а Windows меняет сам (автоопределение по сети, VPN) —
    /// часы на столе убегали на пару часов. Сбрасываем запомненный пояс, когда Windows сообщает о смене
    /// времени, и на всякий случай раз в 5 минут.
    /// </summary>
    private void WatchTimeZone()
    {
        Microsoft.Win32.SystemEvents.TimeChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            TimeZoneInfo.ClearCachedData();
            Log($"time changed: zone {TimeZoneInfo.Local.Id}");
            RestartWidgets();
        });
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        string zone = TimeZoneInfo.Local.Id;
        t.Tick += (_, _) =>
        {
            TimeZoneInfo.ClearCachedData();
            if (TimeZoneInfo.Local.Id == zone) return;
            zone = TimeZoneInfo.Local.Id;
            Log($"time zone now {zone}");
            RestartWidgets();
        };
        t.Start();
    }

    /// <summary>«Обновить» в трее: заново время, обои под стеклом, папки и все виджеты.</summary>
    internal void RefreshEverything()
    {
        Log("refresh all");
        TimeZoneInfo.ClearCachedData();
        System.Globalization.CultureInfo.CurrentCulture.ClearCachedData();
        Sync();
        RefreshAll();
        RestartWidgets();
        AdoptScreens(); // и под текущий расклад экранов, и расцепить слипшиеся
    }

    /// <summary>Все включённые виджеты собираем заново (на месте, с теми же настройками).</summary>
    private void RestartWidgets()
    {
        foreach (var info in Config.Widgets.Where(w => w.Enabled).ToList())
        {
            if (!_widgets.Remove(info.Kind, out var win)) continue;
            _selection.Remove(win.Id);
            win.View.Stop();
            win.Close();
            OpenWidget(info);
        }
        WidgetsChanged?.Invoke();
    }

    /// <summary>Сменить язык ("" — как в Windows): запоминаем и перерисовываем всё сразу, без перезапуска.</summary>
    internal void SetLanguage(string code)
    {
        Config.Language = code;
        SaveConfig();
        Loc.Set(code);
    }

    /// <summary>Язык сменился: виджеты собираем заново на новом языке, окно приложения открываем там же.</summary>
    private void OnLanguageChanged()
    {
        Log($"language {Loc.Lang}");
        RestartWidgets();
        if (_hub is { } hub)
        {
            var page = hub.CurrentPage;
            var bounds = new Rect(hub.Left, hub.Top, hub.Width, hub.Height);
            hub.Close();
            OpenHub(page);
            if (_hub != null) { _hub.Left = bounds.Left; _hub.Top = bounds.Top; _hub.Width = bounds.Width; _hub.Height = bounds.Height; }
        }
        RebuildTray();
    }

    internal void OpenHub(string page)
    {
        _panel?.Close();
        if (_hub == null)
        {
            _hub = new HubWindow();
            _hub.Closed += (_, _) => { _hub = null; GC.Collect(); }; // окно большое — отдаём память сразу
            _hub.ShowPage(page, animate: false);
            _hub.Show();
        }
        else
        {
            if (_hub.WindowState == WindowState.Minimized) _hub.WindowState = WindowState.Normal;
            _hub.ShowPage(page);
        }
        _hub.Activate();
        Log($"hub {page}");
    }

    /// <summary>Повторный запуск (ярлык в Пуске) будит эту копию: показать окно.</summary>
    private void ListenForHub()
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, HubEventName);
        var t = new Thread(() =>
        {
            while (ev.WaitOne()) Dispatcher.BeginInvoke(() => OpenHub("workshop"));
        }) { IsBackground = true, Name = "Плитки: ждём окно" }; // lang-ok
        t.Start();
    }

    /// <summary>Ярлыки «Плитки» в Пуске и на столе — один раз; удалённый со стола не возвращаем.</summary>
    private void EnsureShortcuts()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            var start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Плитки.lnk"); // lang-ok
            MakeShortcut(start, exe); // в Пуске — всегда свежий (путь мог поменяться)
            if (!Config.ShortcutsMade)
            {
                MakeShortcut(Path.Combine(Store.Desktop, "Плитки.lnk"), exe); // lang-ok
                Config.ShortcutsMade = true;
                SaveConfig();
            }
        }
        catch (Exception ex) { Log("shortcuts failed: " + ex.Message); }
    }

    private static void MakeShortcut(string path, string exe)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic lnk = shell.CreateShortcut(path);
        lnk.TargetPath = exe;
        lnk.Arguments = "--hub";
        lnk.WorkingDirectory = Path.GetDirectoryName(exe);
        lnk.IconLocation = exe + ",0";
        lnk.Description = Loc.T("Плитки — стеклянный рабочий стол");
        lnk.Save();
    }

    internal void SetEco(bool on)
    {
        Config.EcoMode = on;
        if (on) _wallpaperCheck.Stop(); else _wallpaperCheck.Start();
        SaveConfig();
        Log($"eco {on}");
    }

    internal void SetAutostart(bool on)
    {
        Config.Autostart = on;
        SaveConfig();
        ApplyAutostart();
    }

    internal void ApplyDefaultsToAllTiles()
    {
        Config.ApplyDefaultsToAll();
        foreach (var w in _tiles) Restyle(w);
        RestyleWidgets();
        SaveConfig();
        Log("defaults applied to all");
    }
}
