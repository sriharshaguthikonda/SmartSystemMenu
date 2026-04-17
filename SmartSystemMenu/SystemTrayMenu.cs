using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SmartSystemMenu.Settings;
using SmartSystemMenu.Extensions;
using SmartSystemMenu.Utils;

namespace SmartSystemMenu
{
    class SystemTrayMenu : IDisposable
    {
        private readonly ContextMenuStrip _systemTrayMenu;
        private readonly ToolStripMenuItem _menuItemAutoStart;
        private readonly ToolStripMenuItem _menuItemEnableMenuInjection;
        private readonly ToolStripMenuItem _menuItemHideByTarget;
        private readonly ToolStripMenuItem _menuItemHideForAltTabByTarget;
        private readonly ToolStripMenuItem _menuItemRestore;
        private readonly ToolStripMenuItem _menuItemSettings;
        private readonly ToolStripMenuItem _menuItemAbout;
        private readonly ToolStripMenuItem _menuItemExit;
        private readonly ToolStripSeparator _menuItemSeparator1;
        private readonly ToolStripSeparator _menuItemSeparator2;
        private readonly NotifyIcon _icon;
        private readonly ApplicationSettings _settings;
        private bool _created;

        public event EventHandler MenuItemAutoStartClick;
        public event EventHandler MenuItemEnableMenuInjectionClick;
        public event EventHandler MenuItemHideByTargetClick;
        public event EventHandler MenuItemHideForAltTabByTargetClick;
        public event EventHandler MenuItemSettingsClick;
        public event EventHandler MenuItemAboutClick;
        public event EventHandler MenuItemExitClick;
        public event EventHandler<EventArgs<long>> MenuItemRestoreClick;

        public SystemTrayMenu(ApplicationSettings settings)
        {
            _menuItemAutoStart = new ToolStripMenuItem();
            _menuItemEnableMenuInjection = new ToolStripMenuItem();
            _menuItemHideByTarget = new ToolStripMenuItem();
            _menuItemHideForAltTabByTarget = new ToolStripMenuItem();
            _menuItemRestore = new ToolStripMenuItem();
            _menuItemSettings = new ToolStripMenuItem();
            _menuItemAbout = new ToolStripMenuItem();
            _menuItemSeparator1 = new ToolStripSeparator();
            _menuItemSeparator2 = new ToolStripSeparator();
            _menuItemExit = new ToolStripMenuItem();

            var components = new Container();
            _systemTrayMenu = new ContextMenuStrip(components);
            _icon = new NotifyIcon(components);

            _settings = settings;
            _created = false;
        }

        public void Create()
        {
            if (!_created)
            {
                _menuItemAutoStart.Name = "miAutoStart";
                _menuItemAutoStart.Size = new Size(175, 22);
                _menuItemAutoStart.Text = _settings.Language.GetValue("mi_auto_start");
                _menuItemAutoStart.Click += ItemAutoStartClick;

                _menuItemEnableMenuInjection.Name = "miEnableMenuInjection";
                _menuItemEnableMenuInjection.Size = new Size(175, 22);
                _menuItemEnableMenuInjection.Text = "Enable Menu Injection";
                _menuItemEnableMenuInjection.Checked = _settings.EnableMenuInjection;
                _menuItemEnableMenuInjection.Click += ItemEnableMenuInjectionClick;

                _menuItemSettings.Name = "miSettings";
                _menuItemSettings.Size = new Size(175, 22);
                _menuItemSettings.Font = new Font(_menuItemSettings.Font.Name, _menuItemSettings.Font.Size, FontStyle.Bold);
                _menuItemSettings.Text = _settings.Language.GetValue("mi_settings");
                _menuItemSettings.Click += ItemSettingsClick;

                _menuItemAbout.Name = "miAbout";
                _menuItemAbout.Size = new Size(175, 22);
                _menuItemAbout.Text = _settings.Language.GetValue("mi_about");
                _menuItemAbout.Click += ItemAboutClick;

                _menuItemSeparator1.Name = "miSeparator1";
                _menuItemSeparator1.Size = new Size(172, 6);

                _menuItemSeparator2.Name = "miSeparator2";
                _menuItemSeparator2.Size = new Size(172, 6);

                _menuItemExit.Name = "miExit";
                _menuItemExit.Size = new Size(175, 22);
                _menuItemExit.Text = _settings.Language.GetValue("mi_exit");
                _menuItemExit.Click += ItemExitClick;

                var hideItemName = MenuItemId.GetName(MenuItemId.SC_HIDE);
                var hideForAltTabItemName = MenuItemId.GetName(MenuItemId.SC_HIDE_FOR_ALT_TAB);
                var clickThroughItemName = MenuItemId.GetName(MenuItemId.SC_CLICK_THROUGH);
                var transparencyItemName = MenuItemId.GetName(MenuItemId.SC_TRANS);
                var dimmerItemName = MenuItemId.GetName(MenuItemId.SC_DIMMER);
                var menuItems = _settings.MenuItems.Items.Flatten(x => x.Items);
                var hideAny = menuItems.Any(x => x.Type == MenuItemType.Item && x.Name == hideItemName && x.Show);
                var hideForAltTabAny = menuItems.Any(x => x.Type == MenuItemType.Item && x.Name == hideForAltTabItemName && x.Show);
                var clickThroughAny = menuItems.Any(x => x.Type == MenuItemType.Item && x.Name == clickThroughItemName && x.Show);
                var transparencyAny = menuItems.Any(x => x.Type == MenuItemType.Group && x.Name == transparencyItemName && x.Show);
                var dimmerAny = menuItems.Any(x => x.Type == MenuItemType.Group && x.Name == dimmerItemName && x.Show);
                var hideByTargetText = _settings.Language.GetValue("mi_hide_by_target");
                hideByTargetText = string.IsNullOrWhiteSpace(hideByTargetText) ? _settings.Language.GetValue("hide") + "..." : hideByTargetText;
                var hideForAltTabText = _settings.Language.GetValue("hide_for_alt_tab");
                hideForAltTabText = string.IsNullOrWhiteSpace(hideForAltTabText) ? "Hide For Alt+Tab" : hideForAltTabText;
                var hideForAltTabByTargetText = _settings.Language.GetValue("mi_hide_for_alt_tab_by_target");
                hideForAltTabByTargetText = string.IsNullOrWhiteSpace(hideForAltTabByTargetText) ? hideForAltTabText + "..." : hideForAltTabByTargetText;

                if (hideAny)
                {
                    _menuItemHideByTarget.Name = "miHideByTarget";
                    _menuItemHideByTarget.Size = new Size(175, 22);
                    _menuItemHideByTarget.Text = hideByTargetText;
                    _menuItemHideByTarget.Click += ItemHideByTargetClick;
                }

                if (hideForAltTabAny)
                {
                    _menuItemHideForAltTabByTarget.Name = "miHideForAltTabByTarget";
                    _menuItemHideForAltTabByTarget.Size = new Size(175, 22);
                    _menuItemHideForAltTabByTarget.Text = hideForAltTabByTargetText;
                    _menuItemHideForAltTabByTarget.Click += ItemHideForAltTabByTargetClick;
                }

                var restoreAny = hideAny || clickThroughAny || transparencyAny || dimmerAny;
                if (restoreAny)
                {
                    _menuItemRestore.Name = "miRestore";
                    _menuItemRestore.Size = new Size(175, 22);
                    _menuItemRestore.Text = _settings.Language.GetValue("mi_restore_windows");

                    if (hideAny)
                    {
                        var subMenuItem = new ToolStripMenuItem();
                        subMenuItem.Name = "miHide";
                        subMenuItem.Size = new Size(175, 22);
                        subMenuItem.Text = _settings.Language.GetValue("hide");
                        subMenuItem.Click += ItemRestoreClick;
                        _menuItemRestore.DropDownItems.Add(subMenuItem);
                    }

                    if (clickThroughAny)
                    {
                        var subMenuItem = new ToolStripMenuItem();
                        subMenuItem.Name = "miClickThrough";
                        subMenuItem.Size = new Size(175, 22);
                        subMenuItem.Text = _settings.Language.GetValue("click_through");
                        subMenuItem.Click += ItemRestoreClick;
                        _menuItemRestore.DropDownItems.Add(subMenuItem);
                    }

                    if (transparencyAny)
                    {
                        var subMenuItem = new ToolStripMenuItem();
                        subMenuItem.Name = "miTransparency";
                        subMenuItem.Size = new Size(175, 22);
                        subMenuItem.Text = _settings.Language.GetValue("transparency");
                        subMenuItem.Click += ItemRestoreClick;
                        _menuItemRestore.DropDownItems.Add(subMenuItem);
                    }

                    if (dimmerAny)
                    {
                        var subMenuItem = new ToolStripMenuItem();
                        subMenuItem.Name = "miDimmer";
                        subMenuItem.Size = new Size(175, 22);
                        subMenuItem.Text = _settings.Language.GetValue("dimmer");
                        subMenuItem.Click += ItemRestoreClick;
                        _menuItemRestore.DropDownItems.Add(subMenuItem);
                    }
                }

                var trayItems = new List<ToolStripItem> { _menuItemAutoStart, _menuItemEnableMenuInjection, _menuItemSeparator1 };
                if (hideAny)
                {
                    trayItems.Add(_menuItemHideByTarget);
                }
                if (hideForAltTabAny)
                {
                    trayItems.Add(_menuItemHideForAltTabByTarget);
                }
                if (restoreAny)
                {
                    trayItems.Add(_menuItemRestore);
                }
                trayItems.Add(_menuItemSettings);
                trayItems.Add(_menuItemAbout);
                trayItems.Add(_menuItemSeparator2);
                trayItems.Add(_menuItemExit);
                _systemTrayMenu.Items.AddRange(trayItems.ToArray());

                _systemTrayMenu.Name = "systemTrayMenu";
                _systemTrayMenu.Size = new Size(176, 80);
                ThemeUtils.ApplyTheme(_systemTrayMenu, _settings.ThemeMode);

                _icon.ContextMenuStrip = _systemTrayMenu;
                _icon.Icon = Properties.Resources.SmartSystemMenu;
                _icon.Text = AssemblyUtils.AssemblyTitle;
                _icon.Visible = true;
                _icon.DoubleClick += ItemSettingsClick;

                _created = true;
            }
        }

        public void ApplyTheme()
        {
            ThemeUtils.ApplyTheme(_systemTrayMenu, _settings.ThemeMode);
        }

        public void CheckMenuItemAutoStart(bool check)
        {
            _menuItemAutoStart.Checked = check;
        }

        public void CheckMenuItemEnableMenuInjection(bool check)
        {
            _menuItemEnableMenuInjection.Checked = check;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _menuItemAutoStart?.Dispose();
                _menuItemEnableMenuInjection?.Dispose();
                _menuItemHideByTarget?.Dispose();
                _menuItemHideForAltTabByTarget?.Dispose();
                _menuItemRestore?.Dispose();
                _menuItemSettings?.Dispose();
                _menuItemAbout?.Dispose();
                _menuItemExit?.Dispose();
                _menuItemSeparator1?.Dispose();
                _menuItemSeparator2?.Dispose();
                _systemTrayMenu?.Dispose();
                _icon.Visible = false;
                _icon.Dispose();
            }
        }

        ~SystemTrayMenu()
        {
            Dispose(false);
        }

        private void ItemAutoStartClick(object sender, EventArgs e)
        {
            var handler = MenuItemAutoStartClick;
            handler?.Invoke(sender, e);
        }

        private void ItemEnableMenuInjectionClick(object sender, EventArgs e)
        {
            var handler = MenuItemEnableMenuInjectionClick;
            handler?.Invoke(sender, e);
        }

        private void ItemRestoreClick(object sender, EventArgs e)
        {
            var handler = MenuItemRestoreClick;
            if (handler != null && sender is ToolStripMenuItem menuItem)
            {
                var menuItemId = menuItem.Name == "miHide" ? MenuItemId.SC_HIDE : menuItem.Name == "miClickThrough" ? MenuItemId.SC_CLICK_THROUGH : menuItem.Name == "miTransparency" ? MenuItemId.SC_TRANS_DEFAULT : MenuItemId.SC_DIMMER_OFF;
                handler.Invoke(sender, new EventArgs<long>(menuItemId));
            }
        }

        private void ItemHideByTargetClick(object sender, EventArgs e)
        {
            var handler = MenuItemHideByTargetClick;
            handler?.Invoke(sender, e);
        }

        private void ItemHideForAltTabByTargetClick(object sender, EventArgs e)
        {
            var handler = MenuItemHideForAltTabByTargetClick;
            handler?.Invoke(sender, e);
        }

        private void ItemSettingsClick(object sender, EventArgs e)
        {
            var handler = MenuItemSettingsClick;
            handler?.Invoke(sender, e);
        }

        private void ItemAboutClick(object sender, EventArgs e)
        {
            var handler = MenuItemAboutClick;
            handler?.Invoke(sender, e);
        }

        private void ItemExitClick(object sender, EventArgs e)
        {
            var handler = MenuItemExitClick;
            handler?.Invoke(sender, e);
        }
    }
}
