using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using SmartSystemMenu.Settings;

namespace SmartSystemMenu.Utils
{
    static class ThemeUtils
    {
        private sealed class ThemePalette
        {
            public Color FormBackColor { get; set; }
            public Color SurfaceBackColor { get; set; }
            public Color InputBackColor { get; set; }
            public Color BorderColor { get; set; }
            public Color ForeColor { get; set; }
            public Color SecondaryForeColor { get; set; }
            public Color SelectionBackColor { get; set; }
            public Color SelectionForeColor { get; set; }
            public Color HeaderBackColor { get; set; }
        }

        private static readonly ThemePalette LightPalette = new ThemePalette
        {
            FormBackColor = Color.White,
            SurfaceBackColor = Color.FromArgb(246, 246, 246),
            InputBackColor = Color.White,
            BorderColor = Color.FromArgb(206, 206, 206),
            ForeColor = Color.FromArgb(24, 24, 24),
            SecondaryForeColor = Color.FromArgb(90, 90, 90),
            SelectionBackColor = Color.FromArgb(225, 238, 255),
            SelectionForeColor = Color.FromArgb(24, 24, 24),
            HeaderBackColor = Color.FromArgb(238, 238, 238)
        };

        private static readonly ThemePalette DarkPalette = new ThemePalette
        {
            FormBackColor = Color.FromArgb(30, 30, 30),
            SurfaceBackColor = Color.FromArgb(37, 37, 38),
            InputBackColor = Color.FromArgb(45, 45, 48),
            BorderColor = Color.FromArgb(62, 62, 66),
            ForeColor = Color.FromArgb(241, 241, 241),
            SecondaryForeColor = Color.FromArgb(176, 176, 176),
            SelectionBackColor = Color.FromArgb(62, 88, 131),
            SelectionForeColor = Color.White,
            HeaderBackColor = Color.FromArgb(51, 51, 55)
        };

        public static ThemeMode ResolveThemeMode(ThemeMode themeMode)
        {
            if (themeMode != ThemeMode.System)
            {
                return themeMode;
            }

            return IsSystemDarkThemeEnabled() ? ThemeMode.Dark : ThemeMode.Light;
        }

        public static bool IsSystemDarkThemeEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int intValue)
                {
                    return intValue == 0;
                }

                if (value is long longValue)
                {
                    return longValue == 0;
                }

                if (value is byte byteValue)
                {
                    return byteValue == 0;
                }
            }
            catch
            {
            }

            return false;
        }

        public static void ApplyTheme(Form form, ThemeMode themeMode)
        {
            if (form == null)
            {
                return;
            }

            var resolved = ResolveThemeMode(themeMode);
            var palette = resolved == ThemeMode.Dark ? DarkPalette : LightPalette;
            ApplyControlTheme(form, palette);
        }

        public static void ApplyThemeToOpenForms(ThemeMode themeMode)
        {
            foreach (Form form in Application.OpenForms)
            {
                ApplyTheme(form, themeMode);
            }
        }

        public static void ApplyTheme(ContextMenuStrip contextMenu, ThemeMode themeMode)
        {
            if (contextMenu == null)
            {
                return;
            }

            var resolved = ResolveThemeMode(themeMode);
            var palette = resolved == ThemeMode.Dark ? DarkPalette : LightPalette;

            contextMenu.BackColor = palette.SurfaceBackColor;
            contextMenu.ForeColor = palette.ForeColor;
            contextMenu.ShowCheckMargin = true;
            contextMenu.ShowImageMargin = false;

            foreach (ToolStripItem item in contextMenu.Items)
            {
                ApplyToolStripItemTheme(item, palette);
            }
        }

        private static void ApplyToolStripItemTheme(ToolStripItem item, ThemePalette palette)
        {
            if (item == null)
            {
                return;
            }

            item.BackColor = palette.SurfaceBackColor;
            item.ForeColor = palette.ForeColor;

            if (item is ToolStripMenuItem menuItem)
            {
                foreach (ToolStripItem dropDownItem in menuItem.DropDownItems)
                {
                    ApplyToolStripItemTheme(dropDownItem, palette);
                }
            }
        }

        private static void ApplyControlTheme(Control control, ThemePalette palette)
        {
            if (control == null)
            {
                return;
            }

            if (control is Form)
            {
                control.BackColor = palette.FormBackColor;
                control.ForeColor = palette.ForeColor;
            }
            else if (control is TabControl)
            {
                control.BackColor = palette.FormBackColor;
                control.ForeColor = palette.ForeColor;
            }
            else if (control is TabPage)
            {
                control.BackColor = palette.FormBackColor;
                control.ForeColor = palette.ForeColor;
            }
            else if (control is GroupBox)
            {
                control.BackColor = palette.FormBackColor;
                control.ForeColor = palette.ForeColor;
            }
            else if (control is Label)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = palette.ForeColor;
            }
            else if (control is CheckBox || control is RadioButton)
            {
                control.BackColor = palette.FormBackColor;
                control.ForeColor = palette.ForeColor;
            }
            else if (control is TextBox || control is ComboBox)
            {
                control.BackColor = palette.InputBackColor;
                control.ForeColor = palette.ForeColor;
            }
            else if (control is TrackBar)
            {
                control.BackColor = palette.FormBackColor;
                control.ForeColor = palette.ForeColor;
            }
            else if (control is Button button)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = palette.BorderColor;
                button.BackColor = palette.SurfaceBackColor;
                button.ForeColor = palette.ForeColor;
            }
            else if (control is DataGridView grid)
            {
                ApplyDataGridViewTheme(grid, palette);
            }
            else if (!(control is Panel))
            {
                control.BackColor = palette.FormBackColor;
                control.ForeColor = palette.ForeColor;
            }

            foreach (Control child in control.Controls.Cast<Control>())
            {
                ApplyControlTheme(child, palette);
            }
        }

        private static void ApplyDataGridViewTheme(DataGridView grid, ThemePalette palette)
        {
            grid.BackgroundColor = palette.InputBackColor;
            grid.GridColor = palette.BorderColor;
            grid.ForeColor = palette.ForeColor;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.EnableHeadersVisualStyles = false;

            var headerStyle = new DataGridViewCellStyle
            {
                BackColor = palette.HeaderBackColor,
                ForeColor = palette.ForeColor,
                SelectionBackColor = palette.SelectionBackColor,
                SelectionForeColor = palette.SelectionForeColor
            };

            var rowStyle = new DataGridViewCellStyle
            {
                BackColor = palette.InputBackColor,
                ForeColor = palette.ForeColor,
                SelectionBackColor = palette.SelectionBackColor,
                SelectionForeColor = palette.SelectionForeColor
            };

            grid.ColumnHeadersDefaultCellStyle = headerStyle;
            grid.DefaultCellStyle = rowStyle;
            grid.RowHeadersDefaultCellStyle = headerStyle;
            grid.AlternatingRowsDefaultCellStyle = rowStyle;
        }
    }
}
