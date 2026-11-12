using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using TrafficLimiter.Core;

namespace TrafficLimiter.App;

/// <summary>Zweisprachige Texte (Deutsch/Englisch), live umschaltbar. In XAML: {local:T key}.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    /// <summary>Wirksame Sprache: "de" oder "en".</summary>
    public string Lang { get; private set; } = "en";

    static readonly Dictionary<string, (string De, string En)> Table = new()
    {
        ["status_on"] = ("Traffic-Kontrolle aktiv", "Traffic control active"),
        ["status_off"] = ("Traffic-Kontrolle inaktiv", "Traffic control inactive"),
        ["status_noadmin"] = ("Inaktiv – Administratorrechte nötig", "Inactive – administrator rights required"),
        ["btn_start"] = ("Traffic-Kontrolle starten", "Start traffic control"),
        ["btn_stop"] = ("Traffic-Kontrolle stoppen", "Stop traffic control"),
        ["btn_pause"] = ("Limits pausieren", "Pause limits"),
        ["btn_settings"] = ("⚙  Einstellungen", "⚙  Settings"),
        ["search_ph"] = ("Anwendung suchen …", "Search applications …"),
        ["global_limit"] = ("Gesamtlimit", "Total limit"),
        ["tip_global_down"] = ("Globales Download-Limit in KB/s (∞ = aus)", "Global download limit in KB/s (∞ = off)"),
        ["tip_global_up"] = ("Globales Upload-Limit in KB/s (∞ = aus)", "Global upload limit in KB/s (∞ = off)"),
        ["ctx_reset"] = ("Regel zurücksetzen", "Reset rule"),
        ["col_app"] = ("ANWENDUNG", "APPLICATION"),
        ["col_prio"] = ("PRIORITÄT", "PRIORITY"),
        ["col_total_down"] = ("↓ GESAMT", "↓ TOTAL"),
        ["col_total_up"] = ("↑ GESAMT", "↑ TOTAL"),
        ["block"] = ("Blockieren", "Block"),
        ["info_hint"] = ("Wähle eine Anwendung, um Details zu sehen.", "Select an application to see details."),
        ["info_peak"] = ("Spitze ↓ / ↑", "Peak ↓ / ↑"),
        ["info_total_down"] = ("Gesamt ↓", "Total ↓"),
        ["info_total_up"] = ("Gesamt ↑", "Total ↑"),
        ["info_prio"] = ("Priorität", "Priority"),
        ["prio_Low"] = ("Niedrig", "Low"),
        ["prio_Normal"] = ("Normal", "Normal"),
        ["prio_High"] = ("Hoch", "High"),
        ["prio_Critical"] = ("Kritisch", "Critical"),
        ["msg_dll"] = ("WinDivert konnte nicht geladen werden. Möglicherweise blockiert ein Virenscanner die Dateien in %LocalAppData%\\TrafficLimiter\\native.",
                       "WinDivert could not be loaded. An antivirus may be blocking the files in %LocalAppData%\\TrafficLimiter\\native."),
        ["msg_admin"] = ("Der Paketfilter braucht Administratorrechte. Jetzt als Administrator neu starten?",
                         "The packet filter needs administrator rights. Restart as administrator now?"),
        ["msg_fail"] = ("WinDivert konnte nicht starten", "WinDivert could not start"),
        ["msg_autostart_failed"] = ("Der Autostart konnte nicht geändert werden (Administrator-Abfrage abgelehnt?).",
                                    "Autostart could not be changed (administrator prompt declined?)."),
        ["tray_open"] = ("Öffnen", "Open"),
        ["tray_exit"] = ("Beenden", "Exit"),
        ["set_title"] = ("Einstellungen", "Settings"),
        ["set_language"] = ("Sprache", "Language"),
        ["lang_auto"] = ("Automatisch (Windows-Sprache)", "Automatic (Windows language)"),
        ["set_autostart"] = ("Mit Windows starten", "Start with Windows"),
        ["set_autostart_desc"] = ("Startet TrafficLimiter beim Anmelden mit Administratorrechten – ohne UAC-Abfrage.",
                                  "Starts TrafficLimiter at sign-in with administrator rights – no UAC prompt."),
        ["set_minimized"] = ("Immer minimiert im Tray starten", "Always start minimized to tray"),
        ["set_minimized_desc"] = ("Das Fenster bleibt beim Start geschlossen. Öffnen per Doppelklick auf das Tray-Symbol.",
                                  "The window stays hidden on launch. Open it by double-clicking the tray icon."),
        ["set_engine_desc"] = ("Die Traffic-Kontrolle startet mit dem zuletzt gewählten Zustand (Standard: aktiv).",
                               "Traffic control starts in the state you last chose (default: active)."),
        ["tip_chart"] = ("Diagramm und Details ein-/ausblenden", "Show/hide chart and details"),
        ["chart_show_total"] = ("Gesamt anzeigen", "Show total"),
        ["sec_rules"] = ("Regeln", "Rules"),
        ["sec_stats"] = ("Statistik", "Statistics"),
        ["lbl_firewall"] = ("Firewall", "Firewall"),
        ["lbl_limit_down"] = ("Limit ↓", "Limit ↓"),
        ["lbl_limit_up"] = ("Limit ↑", "Limit ↑"),
        ["col_rules"] = ("Regeln", "Rules"),
        ["total_hint"] = ("Gesamtübersicht. Wähle links eine Anwendung, um Regeln zu setzen.", "Overview. Select an application on the left to set rules."),
        ["chart_now"] = ("jetzt", "now"),
        ["filter_active"] = ("Nur aktive", "Active only"),
        ["profile_label"] = ("Profil", "Profile"),
        ["profile_none"] = ("Keines", "None"),
        ["profile_save"] = ("Aktuelle Einstellungen als Profil speichern …", "Save current settings as profile …"),
        ["profile_delete"] = ("Aktives Profil löschen", "Delete active profile"),
        ["profile_name_title"] = ("Profil speichern", "Save profile"),
        ["profile_name_prompt"] = ("Name des Profils", "Profile name"),
        ["btn_ok"] = ("OK", "OK"),
        ["btn_cancel"] = ("Abbrechen", "Cancel"),
        ["tray_profiles"] = ("Profile", "Profiles"),
        ["btn_close"] = ("Schließen", "Close"),
    };

    public string this[string key] => Table.TryGetValue(key, out var v) ? (Lang == "de" ? v.De : v.En) : key;

    public static string T(string key) => Instance[key];

    /// <summary>setting: "auto" | "de" | "en".</summary>
    public void Set(string setting)
    {
        Lang = setting switch
        {
            "de" => "de",
            "en" => "en",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en"
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
        Changed?.Invoke();
    }
}

/// <summary>{local:T key} – bindet einen Text live an die aktuelle Sprache.</summary>
public class TExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider sp) =>
        new Binding($"[{key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(sp);
}

/// <summary>Priority + Sprache -> lokalisierter Name (die Sprache als 2. Wert löst das Neuzeichnen aus).</summary>
public class PriorityNameConverter : IMultiValueConverter
{
    public object Convert(object[] v, Type t, object p, CultureInfo c) => Loc.T("prio_" + v[0]);
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}
