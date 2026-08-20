using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace VSAgent.Services.I18n
{
    /// <summary>
    /// Supported UI languages. The first entry is the fallback (English) and is
    /// always used when a key is missing in the active language.
    /// </summary>
    public enum UiLanguage
    {
        Auto = 0,
        En = 1,
        De = 2
    }

    /// <summary>
    /// Lightweight resource lookup that exposes the same keys across every
    /// supported language. The service is intentionally tiny — the translation
    /// tables live in <see cref="LocalizedStrings"/> so the resource surface is
    /// visible in the binary and easy to extend.
    ///
    /// Strings are looked up by stable keys (e.g. <c>"tab.chat"</c>). When a key
    /// is missing in the active language the service falls back to English and
    /// finally to the key itself so the UI never renders empty placeholders.
    ///
    /// Switching language fires <see cref="LanguageChanged"/>; views subscribe
    /// to that and refresh their visible labels.
    /// </summary>
    public sealed class LocalizationService
    {
        public static readonly LocalizationService Current = new LocalizationService();

        private UiLanguage currentLanguage = UiLanguage.Auto;
        private CultureInfo? currentCulture;

        public event EventHandler? LanguageChanged;

        public UiLanguage Language
        {
            get => currentLanguage;
            set
            {
                var resolved = ResolveCulture(value);
                if (Equals(resolved, currentCulture)) return;
                currentLanguage = value;
                currentCulture = resolved;
                ApplyCulture(resolved);
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public CultureInfo Culture => currentCulture ??= ResolveCulture(currentLanguage);

        public IReadOnlyList<UiLanguage> AvailableLanguages { get; } = new[]
        {
            UiLanguage.Auto, UiLanguage.En, UiLanguage.De
        };

        public string this[string key] => Lookup(key);

        public string Get(string key, params (string Placeholder, string Value)[] arguments)
        {
            var template = Lookup(key);
            if (arguments == null || arguments.Length == 0) return template;
            foreach (var (placeholder, value) in arguments)
                template = template.Replace("{" + placeholder + "}", value ?? string.Empty);
            return template;
        }

        public string GetLanguageDisplayName(UiLanguage language)
        {
            return language switch
            {
                UiLanguage.Auto => "Auto",
                UiLanguage.En => "English",
                UiLanguage.De => "Deutsch",
                _ => language.ToString()
            };
        }

        private string Lookup(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            var tables = LocalizedStrings.Tables;
            if (tables.TryGetValue(Culture.Name, out var table) && table.TryGetValue(key, out var localized))
                return localized;
            if (tables.TryGetValue("en-US", out var english) && english.TryGetValue(key, out var fallback))
                return fallback;
            if (tables.TryGetValue("en", out var englishShort) && englishShort.TryGetValue(key, out fallback))
                return fallback;
            return key;
        }

        private static CultureInfo ResolveCulture(UiLanguage language)
        {
            if (language == UiLanguage.Auto)
            {
                var current = CultureInfo.CurrentUICulture;
                if (LocalizedStrings.Tables.ContainsKey(current.Name)) return current;
                if (current.TwoLetterISOLanguageName == "de") return new CultureInfo("de-DE");
                return new CultureInfo("en-US");
            }
            return language switch
            {
                UiLanguage.De => new CultureInfo("de-DE"),
                UiLanguage.En => new CultureInfo("en-US"),
                _ => new CultureInfo("en-US")
            };
        }

        private static void ApplyCulture(CultureInfo culture)
        {
            try
            {
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
            catch
            {
                // Some hosts (e.g. VS Experimental Instance) restrict culture
                // mutation; failing silently preserves the active UI language.
            }
        }
    }
}
