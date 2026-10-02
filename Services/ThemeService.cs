using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace InFox.Services
{
    public class FoxThemeDefinition : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string AccentHex { get; set; } = "";
        public MediaColor PrimaryAccent { get; set; }
        public MediaColor PrimaryAccentHover { get; set; }
        public MediaColor AccentSubtle { get; set; }
        public MediaColor AccentBorder { get; set; }
        public MediaColor WindowBackground { get; set; }
        public MediaColor HeaderBackground { get; set; }
        public MediaColor CardBackground { get; set; }
        public MediaColor CardBackgroundHover { get; set; }
        public MediaColor CardBorder { get; set; }
        public MediaColor TextPrimary { get; set; } = (MediaColor)MediaColorConverter.ConvertFromString("#F8FAFC");
        public MediaColor TextSecondary { get; set; } = (MediaColor)MediaColorConverter.ConvertFromString("#94A3B8");
        public MediaColor TextMuted { get; set; } = (MediaColor)MediaColorConverter.ConvertFromString("#64748B");
        public MediaColor WindowForeground { get; set; } = (MediaColor)MediaColorConverter.ConvertFromString("#ECEFF4");
        public MediaColor ProgressBarTrack { get; set; } = (MediaColor)MediaColorConverter.ConvertFromString("#1E293B");
        public MediaColor ControlBackground { get; set; } = (MediaColor)MediaColorConverter.ConvertFromString("#141B26");
        public MediaColor ControlBorder { get; set; } = (MediaColor)MediaColorConverter.ConvertFromString("#222C3D");
        public MediaColor ToggleThumbChecked { get; set; } = (MediaColor)MediaColorConverter.ConvertFromString("#FFFFFF");
        public MediaColor PrimaryAccentForeground { get; set; } = (MediaColor)MediaColorConverter.ConvertFromString("#FFFFFF");
        public bool IsLightMode { get; set; }
        public byte DwmCaptionR { get; set; }
        public byte DwmCaptionG { get; set; }
        public byte DwmCaptionB { get; set; }
        public byte DwmBorderR { get; set; }
        public byte DwmBorderG { get; set; }
        public byte DwmBorderB { get; set; }
        public byte DwmTextR { get; set; } = 0xEC;
        public byte DwmTextG { get; set; } = 0xEF;
        public byte DwmTextB { get; set; } = 0xF4;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }
    }

    public static class ThemeService
    {
        public static readonly IReadOnlyList<FoxThemeDefinition> AvailableThemes = new List<FoxThemeDefinition>
        {
            new FoxThemeDefinition
            {
                Id = "RedFox",
                Name = "Red Fox",
                Icon = "🦊",
                Subtitle = "Classic Ember & Copper",
                AccentHex = "#FF6B35",
                PrimaryAccent = (MediaColor)MediaColorConverter.ConvertFromString("#FF6B35"),
                PrimaryAccentHover = (MediaColor)MediaColorConverter.ConvertFromString("#FF8A50"),
                AccentSubtle = (MediaColor)MediaColorConverter.ConvertFromString("#2E1A12"),
                AccentBorder = (MediaColor)MediaColorConverter.ConvertFromString("#7C2D12"),
                WindowBackground = (MediaColor)MediaColorConverter.ConvertFromString("#080B11"),
                HeaderBackground = (MediaColor)MediaColorConverter.ConvertFromString("#0C1017"),
                CardBackground = (MediaColor)MediaColorConverter.ConvertFromString("#0F141C"),
                CardBackgroundHover = (MediaColor)MediaColorConverter.ConvertFromString("#141B26"),
                CardBorder = (MediaColor)MediaColorConverter.ConvertFromString("#1E293B"),
                DwmCaptionR = 0x0C, DwmCaptionG = 0x10, DwmCaptionB = 0x17,
                DwmBorderR = 0x1E, DwmBorderG = 0x29, DwmBorderB = 0x3B
            },
            new FoxThemeDefinition
            {
                Id = "ArcticFox",
                Name = "Arctic Fox",
                Icon = "🦊",
                Subtitle = "Glacial Cyan & Frost",
                AccentHex = "#38BDF8",
                PrimaryAccent = (MediaColor)MediaColorConverter.ConvertFromString("#38BDF8"),
                PrimaryAccentHover = (MediaColor)MediaColorConverter.ConvertFromString("#7DD3FC"),
                AccentSubtle = (MediaColor)MediaColorConverter.ConvertFromString("#0C243B"),
                AccentBorder = (MediaColor)MediaColorConverter.ConvertFromString("#0284C7"),
                WindowBackground = (MediaColor)MediaColorConverter.ConvertFromString("#060B14"),
                HeaderBackground = (MediaColor)MediaColorConverter.ConvertFromString("#0A1120"),
                CardBackground = (MediaColor)MediaColorConverter.ConvertFromString("#0C1527"),
                CardBackgroundHover = (MediaColor)MediaColorConverter.ConvertFromString("#101C33"),
                CardBorder = (MediaColor)MediaColorConverter.ConvertFromString("#1B2A45"),
                DwmCaptionR = 0x0A, DwmCaptionG = 0x11, DwmCaptionB = 0x20,
                DwmBorderR = 0x1B, DwmBorderG = 0x2A, DwmBorderB = 0x45
            },
            new FoxThemeDefinition
            {
                Id = "FennecFox",
                Name = "Fennec Fox",
                Icon = "🦊",
                Subtitle = "Desert Sand & Amber",
                AccentHex = "#F59E0B",
                PrimaryAccent = (MediaColor)MediaColorConverter.ConvertFromString("#F59E0B"),
                PrimaryAccentHover = (MediaColor)MediaColorConverter.ConvertFromString("#FBBF24"),
                AccentSubtle = (MediaColor)MediaColorConverter.ConvertFromString("#2E2210"),
                AccentBorder = (MediaColor)MediaColorConverter.ConvertFromString("#D97706"),
                WindowBackground = (MediaColor)MediaColorConverter.ConvertFromString("#0C0A08"),
                HeaderBackground = (MediaColor)MediaColorConverter.ConvertFromString("#13100C"),
                CardBackground = (MediaColor)MediaColorConverter.ConvertFromString("#17130F"),
                CardBackgroundHover = (MediaColor)MediaColorConverter.ConvertFromString("#1F1A14"),
                CardBorder = (MediaColor)MediaColorConverter.ConvertFromString("#2D251D"),
                DwmCaptionR = 0x13, DwmCaptionG = 0x10, DwmCaptionB = 0x0C,
                DwmBorderR = 0x2D, DwmBorderG = 0x25, DwmBorderB = 0x1D
            },
            new FoxThemeDefinition
            {
                Id = "SilverFox",
                Name = "Silver Fox",
                Icon = "🦊",
                Subtitle = "Stealth Platinum & Slate",
                AccentHex = "#E2E8F0",
                PrimaryAccent = (MediaColor)MediaColorConverter.ConvertFromString("#E2E8F0"),
                PrimaryAccentHover = (MediaColor)MediaColorConverter.ConvertFromString("#FFFFFF"),
                AccentSubtle = (MediaColor)MediaColorConverter.ConvertFromString("#1E2024"),
                AccentBorder = (MediaColor)MediaColorConverter.ConvertFromString("#64748B"),
                WindowBackground = (MediaColor)MediaColorConverter.ConvertFromString("#07080A"),
                HeaderBackground = (MediaColor)MediaColorConverter.ConvertFromString("#0C0D10"),
                CardBackground = (MediaColor)MediaColorConverter.ConvertFromString("#111317"),
                CardBackgroundHover = (MediaColor)MediaColorConverter.ConvertFromString("#181A20"),
                CardBorder = (MediaColor)MediaColorConverter.ConvertFromString("#22252D"),
                ToggleThumbChecked = (MediaColor)MediaColorConverter.ConvertFromString("#07080A"),
                PrimaryAccentForeground = (MediaColor)MediaColorConverter.ConvertFromString("#07080A"),
                DwmCaptionR = 0x0C, DwmCaptionG = 0x0D, DwmCaptionB = 0x10,
                DwmBorderR = 0x22, DwmBorderG = 0x25, DwmBorderB = 0x2D
            }
        };

        public static event Action<FoxThemeDefinition>? ThemeChanged;
        public static FoxThemeDefinition CurrentTheme { get; private set; } = AvailableThemes[0];

        public static void Initialize(string? savedThemeId = null, bool? arcticLightMode = null, bool? silverOledMode = null)
        {
            ApplyTheme(savedThemeId ?? "RedFox", arcticLightMode, silverOledMode);
        }

        public static void ApplyTheme(string? themeId, bool? arcticLightMode = null, bool? silverOledMode = null)
        {
            var baseTheme = AvailableThemes.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase)) ?? AvailableThemes[0];

            bool isArctic = string.Equals(baseTheme.Id, "ArcticFox", StringComparison.OrdinalIgnoreCase);
            bool isLight = isArctic && (arcticLightMode ?? ConfigManager.Instance.Config.ArcticFoxLightMode);

            bool isSilver = string.Equals(baseTheme.Id, "SilverFox", StringComparison.OrdinalIgnoreCase);
            bool isOled = isSilver && (silverOledMode ?? ConfigManager.Instance.Config.SilverFoxOledMode);

            FoxThemeDefinition activeTheme;
            if (isLight)
            {
                activeTheme = new FoxThemeDefinition
                {
                    Id = "ArcticFox",
                    Name = "Arctic Fox (Winter Snow)",
                    Icon = "🦊",
                    Subtitle = "Blizzard Snow & Glacial Ice",
                    AccentHex = "#0284C7",
                    PrimaryAccent = (MediaColor)MediaColorConverter.ConvertFromString("#0284C7"),
                    PrimaryAccentHover = (MediaColor)MediaColorConverter.ConvertFromString("#0369A1"),
                    AccentSubtle = (MediaColor)MediaColorConverter.ConvertFromString("#E0F2FE"),
                    AccentBorder = (MediaColor)MediaColorConverter.ConvertFromString("#BAE6FD"),
                    WindowBackground = (MediaColor)MediaColorConverter.ConvertFromString("#F8FAFC"),
                    HeaderBackground = (MediaColor)MediaColorConverter.ConvertFromString("#FFFFFF"),
                    CardBackground = (MediaColor)MediaColorConverter.ConvertFromString("#FFFFFF"),
                    CardBackgroundHover = (MediaColor)MediaColorConverter.ConvertFromString("#F8FAFC"),
                    CardBorder = (MediaColor)MediaColorConverter.ConvertFromString("#E2E8F0"),
                    TextPrimary = (MediaColor)MediaColorConverter.ConvertFromString("#1E293B"),
                    TextSecondary = (MediaColor)MediaColorConverter.ConvertFromString("#475569"),
                    TextMuted = (MediaColor)MediaColorConverter.ConvertFromString("#64748B"),
                    WindowForeground = (MediaColor)MediaColorConverter.ConvertFromString("#1E293B"),
                    ProgressBarTrack = (MediaColor)MediaColorConverter.ConvertFromString("#E2E8F0"),
                    ControlBackground = (MediaColor)MediaColorConverter.ConvertFromString("#F1F5F9"),
                    ControlBorder = (MediaColor)MediaColorConverter.ConvertFromString("#CBD5E1"),
                    ToggleThumbChecked = (MediaColor)MediaColorConverter.ConvertFromString("#FFFFFF"),
                    PrimaryAccentForeground = (MediaColor)MediaColorConverter.ConvertFromString("#FFFFFF"),
                    IsLightMode = true,
                    DwmCaptionR = 0xFF, DwmCaptionG = 0xFF, DwmCaptionB = 0xFF,
                    DwmBorderR = 0xE2, DwmBorderG = 0xE8, DwmBorderB = 0xF0,
                    DwmTextR = 0x1E, DwmTextG = 0x29, DwmTextB = 0x3B,
                    IsSelected = true
                };
            }
            else if (isOled)
            {
                activeTheme = new FoxThemeDefinition
                {
                    Id = "SilverFox",
                    Name = "Silver Fox (OLED True Black)",
                    Icon = "🦊",
                    Subtitle = "OLED Midnight & True Black",
                    AccentHex = "#FFFFFF",
                    PrimaryAccent = (MediaColor)MediaColorConverter.ConvertFromString("#FFFFFF"),
                    PrimaryAccentHover = (MediaColor)MediaColorConverter.ConvertFromString("#E4E4E7"),
                    AccentSubtle = (MediaColor)MediaColorConverter.ConvertFromString("#18181B"),
                    AccentBorder = (MediaColor)MediaColorConverter.ConvertFromString("#3F3F46"),
                    WindowBackground = (MediaColor)MediaColorConverter.ConvertFromString("#000000"),
                    HeaderBackground = (MediaColor)MediaColorConverter.ConvertFromString("#000000"),
                    CardBackground = (MediaColor)MediaColorConverter.ConvertFromString("#050505"),
                    CardBackgroundHover = (MediaColor)MediaColorConverter.ConvertFromString("#0D0D0D"),
                    CardBorder = (MediaColor)MediaColorConverter.ConvertFromString("#1C1C1E"),
                    TextPrimary = (MediaColor)MediaColorConverter.ConvertFromString("#FFFFFF"),
                    TextSecondary = (MediaColor)MediaColorConverter.ConvertFromString("#A1A1AA"),
                    TextMuted = (MediaColor)MediaColorConverter.ConvertFromString("#71717A"),
                    WindowForeground = (MediaColor)MediaColorConverter.ConvertFromString("#FFFFFF"),
                    ProgressBarTrack = (MediaColor)MediaColorConverter.ConvertFromString("#141414"),
                    ControlBackground = (MediaColor)MediaColorConverter.ConvertFromString("#0A0A0A"),
                    ControlBorder = (MediaColor)MediaColorConverter.ConvertFromString("#222222"),
                    ToggleThumbChecked = (MediaColor)MediaColorConverter.ConvertFromString("#000000"),
                    PrimaryAccentForeground = (MediaColor)MediaColorConverter.ConvertFromString("#000000"),
                    IsLightMode = false,
                    DwmCaptionR = 0x00, DwmCaptionG = 0x00, DwmCaptionB = 0x00,
                    DwmBorderR = 0x1C, DwmBorderG = 0x1C, DwmBorderB = 0x1E,
                    DwmTextR = 0xFF, DwmTextG = 0xFF, DwmTextB = 0xFF,
                    IsSelected = true
                };
            }
            else
            {
                activeTheme = baseTheme;
            }

            CurrentTheme = activeTheme;

            foreach (var t in AvailableThemes)
            {
                t.IsSelected = (t.Id == baseTheme.Id);
            }

            var app = System.Windows.Application.Current;
            if (app == null) return;

            Action updateAction = () =>
            {
                SetBrush(app.Resources, "PrimaryAccentBrush", activeTheme.PrimaryAccent);
                SetBrush(app.Resources, "PrimaryAccentHoverBrush", activeTheme.PrimaryAccentHover);
                SetBrush(app.Resources, "AccentSubtleBrush", activeTheme.AccentSubtle);
                SetBrush(app.Resources, "AccentBorderBrush", activeTheme.AccentBorder);
                SetBrush(app.Resources, "WindowBackgroundBrush", activeTheme.WindowBackground);
                SetBrush(app.Resources, "HeaderBackgroundBrush", activeTheme.HeaderBackground);
                SetBrush(app.Resources, "CardBackgroundBrush", activeTheme.CardBackground);
                SetBrush(app.Resources, "CardBackgroundHoverBrush", activeTheme.CardBackgroundHover);
                SetBrush(app.Resources, "CardBorderBrush", activeTheme.CardBorder);
                SetBrush(app.Resources, "TextPrimaryBrush", activeTheme.TextPrimary);
                SetBrush(app.Resources, "TextSecondaryBrush", activeTheme.TextSecondary);
                SetBrush(app.Resources, "TextMutedBrush", activeTheme.TextMuted);
                SetBrush(app.Resources, "WindowForegroundBrush", activeTheme.WindowForeground);
                SetBrush(app.Resources, "ProgressBarTrackBrush", activeTheme.ProgressBarTrack);
                SetBrush(app.Resources, "ControlBackgroundBrush", activeTheme.ControlBackground);
                SetBrush(app.Resources, "ControlBorderBrush", activeTheme.ControlBorder);
                SetBrush(app.Resources, "ToggleThumbBrush", activeTheme.ToggleThumbChecked);
                SetBrush(app.Resources, "PrimaryAccentForegroundBrush", activeTheme.PrimaryAccentForeground);

                // Dynamic High-Contrast Bluetooth Telemetry Brush (Crisp cobalt blue in light mode, radiant cyan in dark)
                MediaColor bleTextColor = activeTheme.IsLightMode
                    ? (MediaColor)MediaColorConverter.ConvertFromString("#1D4ED8")
                    : (MediaColor)MediaColorConverter.ConvertFromString("#38BDF8");
                SetBrush(app.Resources, "BleTextBrush", bleTextColor);

                // Dynamic System Selection Colors matching active Fox Coat theme
                SetBrush(app.Resources, System.Windows.SystemColors.HighlightBrushKey, activeTheme.AccentSubtle);
                SetBrush(app.Resources, System.Windows.SystemColors.HighlightTextBrushKey, activeTheme.PrimaryAccent);
                SetBrush(app.Resources, System.Windows.SystemColors.InactiveSelectionHighlightBrushKey, activeTheme.ControlBackground);
                SetBrush(app.Resources, System.Windows.SystemColors.InactiveSelectionHighlightTextBrushKey, activeTheme.TextSecondary);

                ThemeChanged?.Invoke(activeTheme);
            };

            if (app.Dispatcher.CheckAccess())
            {
                updateAction();
            }
            else
            {
                app.Dispatcher.Invoke(updateAction);
            }
        }

        private static void SetBrush(ResourceDictionary res, object key, MediaColor color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            res[key] = brush;
        }
    }
}
