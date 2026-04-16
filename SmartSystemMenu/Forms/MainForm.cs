using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SmartSystemMenu.Extensions;
using SmartSystemMenu.Utils;
using SmartSystemMenu.Hooks;
using SmartSystemMenu.HotKeys;
using SmartSystemMenu.Settings;
using SmartSystemMenu.Native.Structs;
using static SmartSystemMenu.Native.User32;
using static SmartSystemMenu.Native.Constants;

namespace SmartSystemMenu.Forms
{
    partial class MainForm : Form
    {
        private const string SHELL_WINDOW_NAME = "Program Manager";
        private Dictionary<IntPtr, Window> _windows;
        private CallWndProcHook _callWndProcHook;
        private GetMsgHook _getMsgHook;
        private ShellHook _shellHook;
        private CBTHook _cbtHook;
        private HotKeyHook _hotKeyHook;
        private AboutForm _aboutForm;
        private ApplicationSettingsForm _settingsForm;
        private readonly List<DimForm> _dimForms;
        private ApplicationSettings _settings;
        private readonly WindowSettings _windowSettings;
        private readonly IntPtr _parentHandle;
        private readonly int _currentProcessId;
        private IntPtr _childHandle;
        private IntPtr _dimHandle;

#if WIN32
        private SystemTrayMenu _systemTrayMenu;
        private MouseHook _hotKeyMouseHook;
        private Process _64BitProcess;
        private bool _64BitProcessExtracted;
#endif

        public MainForm(ApplicationSettings settings, WindowSettings windowSettings, IntPtr parentHandle)
        {
            InitializeComponent();

            _settings = settings;
            _windowSettings = windowSettings;
            _parentHandle = parentHandle;
            _currentProcessId = Process.GetCurrentProcess().Id;
            _childHandle = IntPtr.Zero;
            _dimHandle = IntPtr.Zero;
            _dimForms = new List<DimForm>();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            using var process = Process.GetCurrentProcess();
            using var mainModule = process.MainModule;

#if WIN32
            if (Environment.Is64BitOperatingSystem)
            {
                var resourceName = "SmartSystemMenu.SmartSystemMenu64.exe";
                var fileName = "SmartSystemMenu64.exe";
                var directoryName = Path.GetDirectoryName(AssemblyUtils.AssemblyLocation);
                var filePath = Path.Combine(directoryName, fileName);
                try
                {
                    if (!File.Exists(filePath))
                    {
                        AssemblyUtils.ExtractFileFromAssembly(resourceName, filePath);
                        _64BitProcessExtracted = true;
                    }
                    _64BitProcess = Process.Start(filePath, $"--parentHandle {Handle.ToInt64()}");
                }
                catch
                {
                    string message = $"Failed to load {fileName} process!";
                    MessageBox.Show(message, AssemblyUtils.AssemblyTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                    return;
                }
            }

            if (_settings.ShowSystemTrayIcon)
            {
                _systemTrayMenu = new SystemTrayMenu(_settings);
                _systemTrayMenu.MenuItemAutoStartClick += MenuItemAutoStartClick;
                _systemTrayMenu.MenuItemHideByTargetClick += MenuItemHideByTargetClick;
                _systemTrayMenu.MenuItemHideForAltTabByTargetClick += MenuItemHideForAltTabByTargetClick;
                _systemTrayMenu.MenuItemSettingsClick += MenuItemSettingsClick;
                _systemTrayMenu.MenuItemAboutClick += MenuItemAboutClick;
                _systemTrayMenu.MenuItemExitClick += MenuItemExitClick;
                _systemTrayMenu.MenuItemRestoreClick += MenuItemRestoreClick;
                _systemTrayMenu.Create();
                _systemTrayMenu.CheckMenuItemAutoStart(AutoStarter.IsAutoStartByRegisterEnabled(AssemblyUtils.AssemblyProductName, AssemblyUtils.AssemblyLocation));
            }

            ThemeUtils.ApplyTheme(this, _settings.ThemeMode);
            ThemeUtils.ApplyThemeToOpenForms(_settings.ThemeMode);
#if WIN32
            _systemTrayMenu?.ApplyTheme();
#endif

            _hotKeyMouseHook = new MouseHook();
            _hotKeyMouseHook.Hooked += HotKeyMouseHooked;
            if (_settings.Closer.MouseButton != MouseButton.None)
            {
                _hotKeyMouseHook.Start(mainModule.ModuleName, _settings.Closer.Key1, _settings.Closer.Key2, _settings.Closer.MouseButton);
            }
#endif

            if (_parentHandle != IntPtr.Zero)
            {
                var ptrCopyData = SystemUtils.BuildWmCopyDataPointer(SEND_CHILD_HANDLE, Handle.ToInt64().ToString());
                if (ptrCopyData != IntPtr.Zero)
                {
                    SendMessage(_parentHandle, WM_COPYDATA, Handle, ptrCopyData);
                }
            }

            _windows = EnumWindows.EnumAllWindows(_settings, _windowSettings, new string[] { SHELL_WINDOW_NAME }).ToDictionary(x => x.Handle, y => y);

            foreach (var window in _windows.Values)
            {
                var processPath = window.Process?.GetMainModuleFileName() ?? string.Empty;
                var fileName = Path.GetFileName(processPath);
                if (!string.IsNullOrEmpty(fileName) && _settings.ExcludedProcessItems.Select(x => x.Name).Contains(fileName.ToLower()) || _settings.InitEventProcessNames.Contains(fileName.ToLower()))
                {
                    continue;
                }

                var isAdded = window.Menu.Create();
                if (isAdded)
                {
                    if (_settings.Sizer.ResizableByDefault)
                    {
                        window.MakeResizable(true);
                        window.Menu.CheckMenuItem(MenuItemId.SC_RESIZABLE, true);
                    }

                    window.CheckDefaultMenuItems();
                    window.NoRestoreMenu = !string.IsNullOrEmpty(fileName) && _settings.NoRestoreMenuProcessNames.Contains(fileName.ToLower());

                    var windowClassName = window.GetClassName();
                    var states = _windowSettings.Find(windowClassName, processPath);
                    if (states.Any())
                    {
                        window.ApplyState(states[0], _settings.SaveSelectedItems, _settings.MenuItems.WindowSizeItems);
                        window.Menu.CheckMenuItem(MenuItemId.SC_SAVE_SELECTED_ITEMS, true);
                    }

                    ApplyHiddenWindowRules(window, processPath);
                }
            }

            _callWndProcHook = new CallWndProcHook(Handle);
            _callWndProcHook.SysCommand += SysCommand;
            _callWndProcHook.InitMenu += InitMenu;
            _callWndProcHook.Start();

            _getMsgHook = new GetMsgHook(Handle);
            _getMsgHook.SysCommand += SysCommand;
            _getMsgHook.InitMenu += InitMenu;
            _getMsgHook.Start();

            _shellHook = new ShellHook(Handle);
            _shellHook.WindowCreated += WindowCreated;
            _shellHook.WindowDestroyed += WindowDestroyed;
            _shellHook.Start();

            _cbtHook = new CBTHook(Handle);
            _cbtHook.WindowCreated += WindowCreated;
            _cbtHook.WindowDestroyed += WindowDestroyed;
            _cbtHook.MoveSize += WindowMoveSize;
            _cbtHook.MinMax += WindowMinMax;
            _cbtHook.Activate += WindowActivate;
            _cbtHook.Start();

            _hotKeyHook = new HotKeyHook();
            _hotKeyHook.MenuItemHooked += HotKeyHooked;
            _hotKeyHook.MoveToHooked += MoveToHooked;
            _hotKeyHook.Start(_settings, mainModule.ModuleName);

            SystemEvents.UserPreferenceChanged += SystemEventsUserPreferenceChanged;

            Hide();
        }

        private void HotKeyMouseHooked(object sender, EventArgs<SmartSystemMenu.Native.Structs.Point> e)
        {
            if (_settings.Closer.Type == WindowCloserType.CloseForegroundWindow)
            {
                var handle = GetForegroundWindow();
                PostMessage(handle, WM_CLOSE, 0, 0);
            }
            else if (_settings.Closer.Type == WindowCloserType.CloseWindowUnderCursor)
            {
                var handle = WindowFromPoint(e.Entity);
                handle = WindowUtils.GetParentWindow(handle);
                PostMessage(handle, WM_CLOSE, 0, 0);
            }
            else if (_settings.Closer.Type == WindowCloserType.KillProcessWithForegroundWindow)
            {
                var handle = GetForegroundWindow();
                var processId = WindowUtils.GetProcessId(handle);
                SystemUtils.TerminateProcess(processId, 0);
            }
            else if (_settings.Closer.Type == WindowCloserType.KillProcessWithWindowUnderCursor)
            {
                var handle = WindowFromPoint(e.Entity);
                handle = WindowUtils.GetParentWindow(handle);
                var processId = WindowUtils.GetProcessId(handle);
                SystemUtils.TerminateProcess(processId, 0);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Unhook while the message loop is still alive so injected processes
            // receive the WM_NULL broadcast and unload the hook DLL before we exit.
            _callWndProcHook?.Stop();
            _getMsgHook?.Stop();
            _shellHook?.Stop();
            _cbtHook?.Stop();

            PostMessage((IntPtr)HWND_BROADCAST, WM_NULL, 0, 0);
            SendNotifyMessage((IntPtr)HWND_BROADCAST, WM_NULL, 0, 0);

            // Pump the message queue briefly so injected processes can process the
            // broadcast and unload the DLL before we terminate.
            var deadline = Environment.TickCount + 300;
            while (Environment.TickCount < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }

            base.OnFormClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= SystemEventsUserPreferenceChanged;
            // Hooks already stopped in OnFormClosing; guard against double-stop.
            _callWndProcHook?.Stop();
            _getMsgHook?.Stop();
            _shellHook?.Stop();
            _cbtHook?.Stop();

            HideDimWindows();

            if (_windows != null)
            {
                foreach (Window window in _windows.Values)
                {
                    window.Dispose();
                }
            }

#if WIN32
            _systemTrayMenu?.Dispose();
            _hotKeyHook?.Dispose();

            if (Environment.Is64BitOperatingSystem && _64BitProcess != null && !_64BitProcess.HasExited)
            {
                foreach (var handle in _64BitProcess.GetWindowHandles())
                {
                    PostMessage(handle, WM_CLOSE, 0, 0);
                }

                if (!_64BitProcess.WaitForExit(5000))
                {
                    _64BitProcess.Kill();
                }

                if (_64BitProcessExtracted)
                {
                    try
                    {
                        File.Delete(_64BitProcess.StartInfo.FileName);
                    }
                    catch
                    {
                    }
                }
            }
#endif
            base.OnClosed(e);
        }

        private void SystemEventsUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_settings == null || _settings.ThemeMode != ThemeMode.System)
            {
                return;
            }

            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            BeginInvoke((MethodInvoker)(() =>
            {
                ThemeUtils.ApplyTheme(this, _settings.ThemeMode);
                ThemeUtils.ApplyThemeToOpenForms(_settings.ThemeMode);
#if WIN32
                _systemTrayMenu?.ApplyTheme();
#endif
            }));
        }

        protected override void WndProc(ref Message m)
        {
            _shellHook?.ProcessWindowMessage(ref m);
            _cbtHook?.ProcessWindowMessage(ref m);
            _callWndProcHook?.ProcessWindowMessage(ref m);
            _getMsgHook?.ProcessWindowMessage(ref m);

            if (m.Msg == WM_COPYDATA)
            {
                HandleCopyDataMessage(m);
            }

            base.WndProc(ref m);
        }

        private void HandleCopyDataMessage(Message message)
        {
            if (!TryReadCopyDataStruct(message.LParam, out var copyData))
            {
                return;
            }

            var identifier = copyData.dwData.ToInt64();
            if (!IsSupportedCopyDataIdentifier(identifier))
            {
                return;
            }

            var senderHandle = message.WParam;
            if (!IsTrustedCopyDataSender(senderHandle, identifier))
            {
                return;
            }

            if (identifier == SEND_CHILD_HANDLE)
            {
                if (!TryReadCopyDataString(copyData, 64, out var handleString))
                {
                    return;
                }

                if (!long.TryParse(handleString, out var handleValue))
                {
                    return;
                }

                var childHandle = new IntPtr(handleValue);
                if (childHandle == IntPtr.Zero || childHandle != senderHandle || childHandle == Handle || childHandle == _parentHandle)
                {
                    return;
                }

                _childHandle = childHandle;
                return;
            }

            if (identifier == MenuItemId.SC_HIDE || identifier == MenuItemId.SC_TRANS_DEFAULT || identifier == MenuItemId.SC_CLICK_THROUGH || identifier == MenuItemId.SC_DIMMER_OFF)
            {
                MenuItemRestoreClick(this, new EventArgs<long>(identifier));
            }

            if (identifier == MenuItemId.SC_DIMMER_ON || identifier == MenuItemId.SC_DIMMER_OFF)
            {
                HideDimWindows();
            }
        }

        private bool IsSupportedCopyDataIdentifier(long identifier)
        {
            return identifier == SEND_CHILD_HANDLE ||
                   identifier == MenuItemId.SC_HIDE ||
                   identifier == MenuItemId.SC_TRANS_DEFAULT ||
                   identifier == MenuItemId.SC_CLICK_THROUGH ||
                   identifier == MenuItemId.SC_DIMMER_ON ||
                   identifier == MenuItemId.SC_DIMMER_OFF;
        }

        private bool IsTrustedCopyDataSender(IntPtr senderHandle, long identifier)
        {
            if (senderHandle == IntPtr.Zero)
            {
                return false;
            }

            if (!IsExpectedPeerSenderHandle(senderHandle, identifier))
            {
                return false;
            }

            if (GetWindowThreadProcessId(senderHandle, out int senderProcessId) == 0 || senderProcessId <= 0 || senderProcessId == _currentProcessId)
            {
                return false;
            }

            var senderProcess = SystemUtils.GetProcessByIdSafely(senderProcessId);
            var senderPath = senderProcess?.GetMainModuleFileName();
            return IsTrustedPeerExecutablePath(senderPath);
        }

        private bool IsExpectedPeerSenderHandle(IntPtr senderHandle, long identifier)
        {
            if (identifier == SEND_CHILD_HANDLE)
            {
                if (_parentHandle != IntPtr.Zero)
                {
                    return senderHandle == _parentHandle;
                }

                if (_childHandle != IntPtr.Zero)
                {
                    return senderHandle == _childHandle;
                }

                return true;
            }

            if (_parentHandle != IntPtr.Zero)
            {
                return senderHandle == _parentHandle;
            }

            return _childHandle != IntPtr.Zero && senderHandle == _childHandle;
        }

        private bool IsTrustedPeerExecutablePath(string executablePath)
        {
            if (!SystemUtils.TryResolveExecutablePath(executablePath, out var resolvedPath, out _))
            {
                return false;
            }

            var assemblyDirectory = Path.GetFullPath(AssemblyUtils.AssemblyDirectory);
            var executableDirectory = Path.GetDirectoryName(resolvedPath);
            if (!string.Equals(executableDirectory, assemblyDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var fileName = Path.GetFileName(resolvedPath);
            return string.Equals(fileName, "SmartSystemMenu.exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "SmartSystemMenu64.exe", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryReadCopyDataStruct(IntPtr lParam, out CopyDataStruct copyData)
        {
            copyData = default;
            if (lParam == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                copyData = (CopyDataStruct)Marshal.PtrToStructure(lParam, typeof(CopyDataStruct));
                if (copyData.cbData < 0)
                {
                    copyData = default;
                    return false;
                }

                if (copyData.cbData > 0 && copyData.lpData == IntPtr.Zero)
                {
                    copyData = default;
                    return false;
                }

                return true;
            }
            catch
            {
                copyData = default;
                return false;
            }
        }

        private bool TryReadCopyDataString(CopyDataStruct copyData, int maxLength, out string data)
        {
            data = null;
            if (copyData.lpData == IntPtr.Zero || copyData.cbData <= 0 || copyData.cbData > maxLength)
            {
                return false;
            }

            try
            {
                data = Marshal.PtrToStringAnsi(copyData.lpData, copyData.cbData)?.TrimEnd('\0');
                return !string.IsNullOrWhiteSpace(data);
            }
            catch
            {
                data = null;
                return false;
            }
        }

        private void MenuItemAutoStartClick(object sender, EventArgs e)
        {
            var keyName = AssemblyUtils.AssemblyProductName;
            var assemblyLocation = AssemblyUtils.AssemblyLocation;
            var autoStartEnabled = AutoStarter.IsAutoStartByRegisterEnabled(keyName, assemblyLocation);
            var configureScheduler = Environment.OSVersion.Version.Major >= 6;
            bool success;
            string reason;

            if (autoStartEnabled)
            {
                success = AutoStarter.TryDisableAutoStart(keyName, configureScheduler, out reason);
            }
            else
            {
                success = AutoStarter.TryEnableAutoStart(keyName, assemblyLocation, configureScheduler, out reason);
            }

            if (!success)
            {
                var message = string.IsNullOrWhiteSpace(reason) ? "Autostart setup failed." : reason;
                MessageBox.Show(message, "Security Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (sender is ToolStripMenuItem menuItem)
            {
                menuItem.Checked = !autoStartEnabled;
            }
        }

        private void MenuItemHideByTargetClick(object sender, EventArgs e)
        {
            using var pickerForm = CreateWindowTargetPicker();
            var result = pickerForm.ShowDialog();
            if (result == DialogResult.OK)
            {
                if (HideWindowByHandle(pickerForm.SelectedHandle))
                {
                    RememberHiddenWindowRule(pickerForm.SelectedHandle, HiddenWindowAction.Hide);
                }
            }
        }

        private void MenuItemHideForAltTabByTargetClick(object sender, EventArgs e)
        {
            var hideForAltTabText = GetWindowPickerText("hide_for_alt_tab", "Hide For Alt+Tab");
            var title = GetWindowPickerText("mi_hide_for_alt_tab_by_target", hideForAltTabText + "...");
            var instruction = GetWindowPickerText("window_picker_instruction_hide_for_alt_tab", "Drag the target symbol onto a window to hide it from Alt+Tab while keeping it visible on the desktop.");
            var dropHint = GetWindowPickerText("window_picker_drop_hint_hide_for_alt_tab", "Drop on a window to hide it from Alt+Tab.");

            using var pickerForm = CreateWindowTargetPicker(title, instruction, dropHint);
            var result = pickerForm.ShowDialog();
            if (result == DialogResult.OK)
            {
                if (HideWindowForAltTabByHandle(pickerForm.SelectedHandle))
                {
                    RememberHiddenWindowRule(pickerForm.SelectedHandle, HiddenWindowAction.HideForAltTab);
                }
            }
        }

        private void MenuItemAboutClick(object sender, EventArgs e)
        {
            if (_aboutForm == null || _aboutForm.IsDisposed || !_aboutForm.IsHandleCreated)
            {
                _aboutForm = new AboutForm(_settings.Language);
            }
            ThemeUtils.ApplyTheme(_aboutForm, _settings.ThemeMode);
            _aboutForm.Show();
            _aboutForm.Activate();
        }

        private bool IsInvalidTargetHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return true;
            }

            handle = WindowUtils.GetParentWindow(handle);
            if (handle == IntPtr.Zero)
            {
                return true;
            }

            if (handle == Handle || handle == _parentHandle || handle == _childHandle)
            {
                return true;
            }

            if (WindowUtils.IsDesktopWindow(handle))
            {
                return true;
            }

            var className = WindowUtils.GetClassName(handle);
            if (className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd")
            {
                return true;
            }

            var processId = WindowUtils.GetProcessId(handle);
            if (processId == _currentProcessId)
            {
                return true;
            }

            return false;
        }

        private bool HideWindowByHandle(IntPtr handle)
        {
            var window = GetOrCreateTargetWindow(handle);
            if (window == null || window.IsHidden)
            {
                return false;
            }

            window.Hide();
            return true;
        }

        private bool HideWindowForAltTabByHandle(IntPtr handle)
        {
            var window = GetOrCreateTargetWindow(handle);
            if (window == null)
            {
                return false;
            }

            if (!window.IsExToolWindow)
            {
                window.Menu.CheckMenuItem(MenuItemId.SC_HIDE_FOR_ALT_TAB, true);
                window.HideForAltTab(true);
            }

            return true;
        }

        private Window GetOrCreateTargetWindow(IntPtr handle)
        {
            if (_windows == null || handle == IntPtr.Zero)
            {
                return null;
            }

            handle = WindowUtils.GetParentWindow(handle);
            if (IsInvalidTargetHandle(handle))
            {
                return null;
            }

            var window = _windows.TryGetValue(handle, out var existingWindow) ? existingWindow : null;
            if (window != null)
            {
                return window;
            }

            GetWindowThreadProcessId(handle, out int processId);
            var process = SystemUtils.GetProcessByIdSafely(processId);
            var processPath = process?.GetMainModuleFileName() ?? string.Empty;

            window = new Window(handle, _settings.MenuItems, _settings.Language);
            CreateMenu(window, processId, processPath);
            if (!_windows.TryGetValue(handle, out var trackedWindow))
            {
                _windows[handle] = window;
                return window;
            }

            return trackedWindow;
        }

        private WindowTargetPickerForm CreateWindowTargetPicker(string title = null, string instruction = null, string dropHint = null)
        {
            var picker = new WindowTargetPickerForm(_settings.Language, IsInvalidTargetHandle, title, instruction, dropHint);
            ThemeUtils.ApplyTheme(picker, _settings.ThemeMode);
            return picker;
        }

        private string GetWindowPickerText(string key, string fallback)
        {
            var value = _settings.Language.GetValue(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private void ApplyThemeToForm(Form form)
        {
            ThemeUtils.ApplyTheme(form, _settings.ThemeMode);
        }

        private void MenuItemSettingsClick(object sender, EventArgs e)
        {
            if (_settingsForm == null || _settingsForm.IsDisposed || !_settingsForm.IsHandleCreated)
            {
                _settingsForm = new ApplicationSettingsForm(_settings);
                _settingsForm.OkClick += (object s, EventArgs<ApplicationSettings> ea) =>
                {
                    _settings = ea.Entity;
                    ApplyHiddenWindowRulesToAllWindows();
                    ThemeUtils.ApplyTheme(this, _settings.ThemeMode);
                    ThemeUtils.ApplyThemeToOpenForms(_settings.ThemeMode);
#if WIN32
                    _systemTrayMenu?.ApplyTheme();
#endif
                };
                _settingsForm.HideByTargetClick += MenuItemHideByTargetClick;
                _settingsForm.HideForAltTabByTargetClick += MenuItemHideForAltTabByTargetClick;
            }

            ThemeUtils.ApplyTheme(_settingsForm, _settings.ThemeMode);
            _settingsForm.Show();
            _settingsForm.Activate();
        }

        private void MenuItemExitClick(object sender, EventArgs e) => Close();

        private void MenuItemRestoreClick(object sender, EventArgs<long> e)
        {
            switch (e.Entity)
            {
                case MenuItemId.SC_HIDE:
                    {
                        foreach (var window in _windows.Values)
                        {
                            if (window.IsHidden)
                            {
                                window.Show();
                            }
                        }
                    }
                    break;

                case MenuItemId.SC_TRANS_DEFAULT:
                    {
                        foreach (var window in _windows.Values)
                        {
                            window.Menu.UncheckTransparencyMenu();
                            window.Menu.CheckMenuItem(MenuItemId.SC_TRANS_DEFAULT, true);
                            window.RestoreTransparency();
                        }
                    }
                    break;

                case MenuItemId.SC_CLICK_THROUGH:
                    {
                        foreach (var window in _windows.Values)
                        {
                            if (window.IsClickThrough)
                            {
                                var isChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_CLICK_THROUGH);
                                if (isChecked)
                                {
                                    window.Menu.CheckMenuItem(MenuItemId.SC_CLICK_THROUGH, !isChecked);
                                    window.ClickThrough(!isChecked);
                                }
                            }
                        }
                    }
                    break;

                case MenuItemId.SC_DIMMER_ON:
                case MenuItemId.SC_DIMMER_OFF:
                    {
                        HideDimWindows();
                    }
                    break;
            }

            if ((e.Entity == MenuItemId.SC_HIDE || e.Entity == MenuItemId.SC_TRANS_DEFAULT || e.Entity == MenuItemId.SC_CLICK_THROUGH || e.Entity == MenuItemId.SC_DIMMER_OFF || e.Entity == MenuItemId.SC_DIMMER_ON) && _childHandle != IntPtr.Zero)
            {
                var ptrCopyData = SystemUtils.BuildWmCopyDataPointer(e.Entity);
                if (ptrCopyData != IntPtr.Zero)
                {
                    SendMessage(_childHandle, WM_COPYDATA, Handle, ptrCopyData);
                }
            }
        }

        private void WindowCreated(object sender, WindowEventArgs e)
        {
            if (e.Handle != IntPtr.Zero && !_windows.ContainsKey(e.Handle))
            {
                GetWindowThreadProcessId(e.Handle, out int processId);
                var process = SystemUtils.GetProcessByIdSafely(processId);
                var processPath = process?.GetMainModuleFileName() ?? string.Empty;
                var fileName = Path.GetFileName(processPath);
                if (!string.IsNullOrEmpty(fileName) &&
                    _settings.ExcludedProcessItems.Select(x => x.Name).Contains(fileName.ToLower()) || _settings.InitEventProcessNames.Contains(fileName.ToLower()))
                {
                    return;
                }

                var window = new Window(e.Handle, _settings.MenuItems, _settings.Language);
                CreateMenu(window, processId, processPath);
            }
        }

        private void InitMenu(object sender, SysCommandEventArgs e)
        {
            if (e.WParam != IntPtr.Zero && IsWindowVisible(e.WParam))
            {
                GetWindowThreadProcessId(e.WParam, out int processId);
                var process = SystemUtils.GetProcessByIdSafely(processId);
                var processPath = process?.GetMainModuleFileName() ?? string.Empty;
                var fileName = Path.GetFileName(processPath);
                if (!string.IsNullOrEmpty(fileName) &&
                    _settings.ExcludedProcessItems.Select(x => x.Name).Contains(fileName.ToLower()) || !_settings.InitEventProcessNames.Contains(fileName.ToLower()))
                {
                    return;
                }

                var window = _windows.TryGetValue(e.WParam, out var win) ? win : null;
                if (window == null)
                {
                    window = new Window(e.WParam, _settings.MenuItems, _settings.Language);
                    CreateMenu(window, processId, processPath);
                }
                else
                {
                    var systemMenuHandle = window.Menu.MenuHandle;
                    if (systemMenuHandle != IntPtr.Zero && !window.Menu.IsMenuItem(systemMenuHandle, MenuItemId.SC_SEPARATOR_BOTTOM))
                    {
                        CreateMenu(window, processId, processPath, false);
                    }
                }
            }
        }

        private void CreateMenu(Window window, int processId, string processPath, bool add = true)
        {
            bool isWriteProcess;
#if WIN32
            isWriteProcess = !Environment.Is64BitOperatingSystem || SystemUtils.IsWow64Process(processId);
#else
            isWriteProcess = Environment.Is64BitOperatingSystem && !SystemUtils.IsWow64Process(processId);
#endif
            var filterTitles = new string[] { SHELL_WINDOW_NAME };
            if (isWriteProcess && !filterTitles.Any(s => window.GetWindowText() == s))
            {
                var isAdded = window.Menu.Create();
                if (isAdded)
                {
                    if (_settings.Sizer.ResizableByDefault)
                    {
                        window.MakeResizable(true);
                        window.Menu.CheckMenuItem(MenuItemId.SC_RESIZABLE, true);
                    }

                    window.CheckDefaultMenuItems();
                    
                    var fileName = Path.GetFileName(processPath);
                    window.NoRestoreMenu = !string.IsNullOrEmpty(fileName) && _settings.NoRestoreMenuProcessNames.Contains(fileName.ToLower());

                    if (add)
                    {
                        _windows.Add(window.Handle, window);
                    }

                    var windowClassName = window.GetClassName();
                    var isConsoleClassName = string.Compare(windowClassName, Window.ConsoleClassName, StringComparison.CurrentCulture) == 0;
                    var states = isConsoleClassName ? _windowSettings.Find(windowClassName) : _windowSettings.Find(windowClassName, processPath);
                    if (states.Any())
                    {
                        window.ApplyState(states[0], _settings.SaveSelectedItems, _settings.MenuItems.WindowSizeItems);
                        window.Menu.CheckMenuItem(MenuItemId.SC_SAVE_SELECTED_ITEMS, true);
                    }

                    ApplyHiddenWindowRules(window, processPath);
                }
            }
        }

        private void ApplyHiddenWindowRulesToAllWindows()
        {
            if (_windows == null || _settings == null || !_settings.RememberHiddenTargets || _settings.HiddenWindowRules == null || _settings.HiddenWindowRules.Count == 0)
            {
                return;
            }

            foreach (var window in _windows.Values)
            {
                ApplyHiddenWindowRules(window);
            }
        }

        private void ApplyHiddenWindowRules(Window window, string processPath = null)
        {
            if (window == null || _settings == null || !_settings.RememberHiddenTargets || _settings.HiddenWindowRules == null || _settings.HiddenWindowRules.Count == 0)
            {
                return;
            }

            processPath = processPath ?? window.Process?.GetMainModuleFileName() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return;
            }

            var className = WindowUtils.NormalizeClassName(window.GetClassName());
            var title = window.GetWindowText() ?? string.Empty;

            foreach (var rule in _settings.HiddenWindowRules)
            {
                if (!IsHiddenWindowRuleMatch(rule, processPath, className, title))
                {
                    continue;
                }

                if (rule.Action == HiddenWindowAction.Hide)
                {
                    if (!window.IsHidden)
                    {
                        window.Hide();
                    }
                }
                else if (rule.Action == HiddenWindowAction.HideForAltTab && !window.IsExToolWindow)
                {
                    window.Menu.CheckMenuItem(MenuItemId.SC_HIDE_FOR_ALT_TAB, true);
                    window.HideForAltTab(true);
                }
            }
        }

        private bool IsHiddenWindowRuleMatch(HiddenWindowRule rule, string processPath, string className, string title)
        {
            if (rule == null || !rule.Enabled)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(rule.ProcessPath) || string.IsNullOrWhiteSpace(processPath))
            {
                return false;
            }

            if (!string.Equals(rule.ProcessPath, processPath, StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(rule.ClassName) && !string.Equals(rule.ClassName, className, StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(rule.WindowTitle) && !string.Equals(rule.WindowTitle, title, StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private void RememberHiddenWindowRule(IntPtr handle, HiddenWindowAction action)
        {
            if (_settings == null || !_settings.RememberHiddenTargets)
            {
                return;
            }

            var normalizedHandle = WindowUtils.GetParentWindow(handle);
            var window = GetOrCreateTargetWindow(normalizedHandle);
            if (window == null)
            {
                return;
            }

            var processPath = window.Process?.GetMainModuleFileName() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return;
            }

            var rule = new HiddenWindowRule
            {
                Enabled = true,
                Action = action,
                ProcessPath = processPath,
                ClassName = WindowUtils.NormalizeClassName(window.GetClassName()),
                WindowTitle = window.GetWindowText() ?? string.Empty
            };

            var existingRule = _settings.HiddenWindowRules.FirstOrDefault(x =>
                x.Action == rule.Action &&
                string.Equals(x.ProcessPath, rule.ProcessPath, StringComparison.CurrentCultureIgnoreCase) &&
                string.Equals(x.ClassName, rule.ClassName, StringComparison.CurrentCulture) &&
                string.Equals(x.WindowTitle, rule.WindowTitle, StringComparison.CurrentCultureIgnoreCase));
            if (existingRule != null)
            {
                existingRule.Enabled = true;
            }
            else
            {
                _settings.HiddenWindowRules.Add(rule);
            }

            SaveApplicationSettings();
        }

        private void SaveApplicationSettings()
        {
            if (_settings == null)
            {
                return;
            }

            try
            {
                var settingsFileName = Path.Combine(AssemblyUtils.AssemblyDirectory, "SmartSystemMenu.xml");
                ApplicationSettingsFile.Save(settingsFileName, _settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save hidden target settings." + Environment.NewLine + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WindowDestroyed(object sender, WindowEventArgs e)
        {
            var window = _windows.TryGetValue(e.Handle, out var win) ? win : null;
            if (window != null && !window.IsHidden)
            {
                if (window.Handle == _dimHandle)
                {
                    HideDimWindows();
                }

                if (!window.ExistSystemTrayIcon)
                {
                    window.Dispose();
                    _windows.Remove(window.Handle);
                }
            }
        }

        private void WindowMinMax(object sender, SysCommandEventArgs e)
        {
            var window = _windows.TryGetValue(e.WParam, out var win) ? win : null;
            if (window != null)
            {
                if (e.LParam.ToInt64() == SW_MAXIMIZE)
                {
                    window.Menu.UncheckSizeMenu();
                }
                
                if (e.LParam.ToInt64() == SW_MINIMIZE)
                {
                    if (window.Handle == _dimHandle)
                    {
                        foreach (var dimForm in _dimForms)
                        {
                            dimForm.Hide();
                        }
                    }

                    if (window.Menu.IsMenuItemChecked(MenuItemId.SC_MINIMIZE_ALWAYS_TO_SYSTEMTRAY))
                    {
                        window.MoveToSystemTray();
                    }
                }
            }
        }

        private void WindowMoveSize(object sender, WindowEventArgs e)
        {
            var window = _windows.TryGetValue(e.Handle, out var win) ? win : null;
            window?.SaveDefaultSizePosition();
        }

        private void WindowKeyboardEvent(object sender, BasicHookEventArgs e)
        {
            var wParam = e.WParam.ToInt64();
            if (wParam == VK_DOWN)
            {
                var controlState = GetAsyncKeyState(VK_CONTROL) & 0x8000;
                var shiftState = GetAsyncKeyState(VK_SHIFT) & 0x8000;
                var controlKey = Convert.ToBoolean(controlState);
                var shiftKey = Convert.ToBoolean(shiftState);
                if (controlKey && shiftKey)
                {
                    var handle = GetForegroundWindow();
                    var window = _windows.TryGetValue(handle, out var win) ? win : null;
                    window?.MinimizeToSystemTray();
                }
            }
        }

        private void WindowActivate(object sender, WindowEventArgs e)
        {
            if (_dimHandle == e.Handle)
            {
                UpdateDimming(_dimHandle);
            }
        }

        private void SysCommand(object sender, SysCommandEventArgs e)
        {
            var message = e.Message.ToInt64();
            if (message == WM_SYSCOMMAND)
            {
                var window = _windows.TryGetValue(e.Handle, out var win) ? win : null;
                if (window != null)
                {
                    var lowOrder = e.WParam.ToInt64() & 0x0000FFFF;
                    switch (lowOrder)
                    {
                        case MenuItemId.SC_MAXIMIZE:
                            {
                                window.Menu.UncheckSizeMenu();
                            }
                            break;

                        case MenuItemId.SC_MINIMIZE_TO_SYSTEMTRAY:
                            {
                                window.MinimizeToSystemTray();
                            }
                            break;

                        case MenuItemId.SC_MINIMIZE_ALWAYS_TO_SYSTEMTRAY:
                            {
                                bool r = window.Menu.IsMenuItemChecked(MenuItemId.SC_MINIMIZE_ALWAYS_TO_SYSTEMTRAY);
                                window.Menu.CheckMenuItem(MenuItemId.SC_MINIMIZE_ALWAYS_TO_SYSTEMTRAY, !r);
                                window.SetStateMinimizeToTrayAlways(!r);
                            }
                            break;

                        case MenuItemId.SC_SUSPEND_TO_SYSTEMTRAY:
                            {
                                window.MinimizeToSystemTray();
                                Thread.Sleep(100);
                                window.Suspend();
                            }
                            break;

                        case MenuItemId.SC_INFORMATION:
                            {
                                var infoForm = new InformationForm(window.GetWindowInfo(), _settings.Language);
                                ApplyThemeToForm(infoForm);
                                infoForm.Show(window.Win32Window);
                            }
                            break;

                        case MenuItemId.SC_SAVE_SCREEN_SHOT:
                            {
                                var result = WindowUtils.PrintWindow(window.Handle, out var bitmap);
                                if (!result || !WindowUtils.IsCorrectScreenshot(window.Handle, bitmap))
                                {
                                    Thread.Sleep(1000);
                                    WindowUtils.CaptureWindow(window.Handle, false, out bitmap);
                                }

                                var dialog = new SaveFileDialog
                                {
                                    OverwritePrompt = true,
                                    ValidateNames = true,
                                    Title = _settings.Language.GetValue("save_screenshot_title"),
                                    FileName = _settings.Language.GetValue("save_screenshot_filename"),
                                    DefaultExt = _settings.Language.GetValue("save_screenshot_default_ext"),
                                    RestoreDirectory = false,
                                    Filter = _settings.Language.GetValue("save_screenshot_filter")
                                };
                                if (dialog.ShowDialog(window.Win32Window) == DialogResult.OK)
                                {
                                    var fileExtension = Path.GetExtension(dialog.FileName).ToLower();
                                    var imageFormat = fileExtension == ".bmp" ? ImageFormat.Bmp :
                                        fileExtension == ".gif" ? ImageFormat.Gif :
                                        fileExtension == ".jpeg" ? ImageFormat.Jpeg :
                                        fileExtension == ".png" ? ImageFormat.Png :
                                        fileExtension == ".tiff" ? ImageFormat.Tiff : ImageFormat.Wmf;
                                    bitmap.Save(dialog.FileName, imageFormat);
                                }
                            }
                            break;

                        case MenuItemId.SC_COPY_SCREEN_SHOT:
                            {
                                var result = WindowUtils.PrintWindow(window.Handle, out var bitmap);
                                if (!result || !WindowUtils.IsCorrectScreenshot(window.Handle, bitmap))
                                {
                                    Thread.Sleep(1000);
                                    WindowUtils.CaptureWindow(window.Handle, false, out bitmap);
                                }

                                Clipboard.SetImage(bitmap);
                            }
                            break;

                        case MenuItemId.SC_COPY_WINDOW_TEXT:
                            {
                                var text = window.ExtractText();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    Clipboard.SetText(text);
                                }
                            }
                            break;

                        case MenuItemId.SC_COPY_WINDOW_TITLE:
                            {
                                var text = window.GetWindowText();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    Clipboard.SetText(text);
                                }
                            }
                            break;

                        case MenuItemId.SC_COPY_FULL_PROCESS_PATH:
                            {
                                var path = window.Process?.GetMainModuleFileName();
                                if (!string.IsNullOrEmpty(path))
                                {
                                    Clipboard.SetText(path);
                                }
                            }
                            break;

                        case MenuItemId.SC_CLEAR_CLIPBOARD:
                            {
                                Clipboard.Clear();
                            }
                            break;

                        case MenuItemId.SC_DRAG_BY_MOUSE:
                            {
                                var isChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_DRAG_BY_MOUSE);
                                window.Menu.CheckMenuItem(MenuItemId.SC_DRAG_BY_MOUSE, !isChecked);
                            }
                            break;

                        case MenuItemId.SC_OPEN_FILE_IN_EXPLORER:
                            {
                                try
                                {
                                    var explorerPath = SystemUtils.GetAbsoluteSystemExecutablePath("explorer.exe");
                                    SystemUtils.RunAs(explorerPath, "/select, " + window.Process.GetMainModuleFileName(), true, UserType.Normal);
                                }
                                catch
                                {
                                }
                            }
                            break;

                        case MenuItemId.SC_MINIMIZE_OTHER_WINDOWS:
                        case MenuItemId.SC_CLOSE_OTHER_WINDOWS:
                            {
                                EnumWindows((IntPtr handle, int lParam) =>
                                {
                                    if (handle != IntPtr.Zero && handle != Handle && handle != window.Handle && WindowUtils.IsAltTabWindow(handle))
                                    {
                                        if (lowOrder == MenuItemId.SC_CLOSE_OTHER_WINDOWS)
                                        {
                                            PostMessage(handle, WM_CLOSE, 0, 0);
                                        }
                                        else
                                        {
                                            PostMessage(handle, WM_SYSCOMMAND, MenuItemId.SC_MINIMIZE, 0);
                                        }
                                    }
                                    return true;
                                }, 0);
                            }
                            break;

                        case MenuItemId.SC_TOPMOST:
                            {
                                var isChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_TOPMOST);
                                window.Menu.CheckMenuItem(MenuItemId.SC_TOPMOST, !isChecked);
                                window.MakeTopMost(!isChecked);
                            }
                            break;

                        case MenuItemId.SC_HIDE_FOR_ALT_TAB:
                            {
                                var isChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_HIDE_FOR_ALT_TAB);
                                window.Menu.CheckMenuItem(MenuItemId.SC_HIDE_FOR_ALT_TAB, !isChecked);
                                window.HideForAltTab(!isChecked);
                            }
                            break;

                        case MenuItemId.SC_CLICK_THROUGH:
                            {
                                var isChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_CLICK_THROUGH);
                                window.Menu.CheckMenuItem(MenuItemId.SC_CLICK_THROUGH, !isChecked);
                                window.ClickThrough(!isChecked);
                            }
                            break;

                        case MenuItemId.SC_SEND_TO_BOTTOM:
                            {
                                window.SendToBottom();
                            }
                            break;

                        case MenuItemId.SC_AERO_GLASS:
                            {
                                var isChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_AERO_GLASS);
                                window.AeroGlass(!isChecked);
                                window.Menu.CheckMenuItem(MenuItemId.SC_AERO_GLASS, !isChecked);
                            }
                            break;

                        case MenuItemId.SC_ROLLUP:
                            {
                                var isChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_ROLLUP);
                                window.Menu.CheckMenuItem(MenuItemId.SC_ROLLUP, !isChecked);
                                if (!isChecked)
                                {
                                    window.RollUp();
                                    window.Menu.UncheckSizeMenu();
                                }
                                else
                                {
                                    window.UnRollUp();
                                }
                                InvalidateRect(window.Handle, IntPtr.Zero, true);
                            }
                            break;

                        case MenuItemId.SC_HIDE:
                            {
                                window.Hide();
                            }
                            break;

                        case MenuItemId.SC_SIZE_DEFAULT:
                            {
                                window.Menu.UncheckSizeMenu();
                                window.Menu.CheckMenuItem(MenuItemId.SC_SIZE_DEFAULT, true);
                                window.ShowNormal();
                                window.RestoreSize();
                                window.Menu.UncheckMenuItems(MenuItemId.SC_ROLLUP);
                            }
                            break;

                        case MenuItemId.SC_SIZE_CUSTOM:
                            {
                                var sizeForm = new SizeForm(window, _settings);
                                ApplyThemeToForm(sizeForm);
                                var result = sizeForm.ShowDialog(window.Win32Window);
                                if (result == DialogResult.OK)
                                {
                                    window.ShowNormal();

                                    if (_settings.Sizer.SizerType == WindowSizerType.WindowWithMargins)
                                    {
                                        window.SetSize(sizeForm.WindowWidth, sizeForm.WindowHeight, sizeForm.WindowLeft, sizeForm.WindowTop);
                                    }
                                    else if (_settings.Sizer.SizerType == WindowSizerType.WindowWithoutMargins)
                                    {
                                        var margins = window.GetSystemMargins();
                                        window.SetSize(sizeForm.WindowWidth == null ? null : (sizeForm.WindowWidth + margins.Left + margins.Right),
                                                       sizeForm.WindowHeight == null ? null : (sizeForm.WindowHeight + margins.Top + margins.Bottom),
                                                       sizeForm.WindowLeft,
                                                       sizeForm.WindowTop);
                                    }
                                    else
                                    {
                                        window.SetSize(sizeForm.WindowWidth == null ? null : (sizeForm.WindowWidth + (window.Size.Width - window.ClientSize.Width)),
                                                       sizeForm.WindowHeight == null ? null : (sizeForm.WindowHeight + (window.Size.Height - window.ClientSize.Height)),
                                                       sizeForm.WindowLeft,
                                                       sizeForm.WindowTop);
                                    }

                                    window.Menu.UncheckSizeMenu();
                                    window.Menu.CheckMenuItem(MenuItemId.SC_SIZE_CUSTOM, true);
                                    window.Menu.UncheckMenuItems(MenuItemId.SC_ROLLUP);
                                }
                            }
                            break;

                        case MenuItemId.SC_RESIZABLE:
                            {
                                var isChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_RESIZABLE);
                                window.Menu.CheckMenuItem(MenuItemId.SC_RESIZABLE, !isChecked);
                                window.MakeResizable(!isChecked);
                                var size = window.Size;
                                MoveWindow(window.Handle, size.Left, size.Top, size.Width, size.Height, true);
                                InvalidateRect(window.Handle, IntPtr.Zero, true);
                            }
                            break;

                        case MenuItemId.SC_TRANS_DEFAULT:
                            {
                                window.Menu.UncheckTransparencyMenu();
                                window.Menu.CheckMenuItem(MenuItemId.SC_TRANS_DEFAULT, true);
                                window.RestoreTransparency();
                            }
                            break;

                        case MenuItemId.SC_TRANS_CUSTOM:
                            {
                                var opacityForm = new TransparencyForm(window, _settings);
                                ApplyThemeToForm(opacityForm);
                                var result = opacityForm.ShowDialog(window.Win32Window);
                                if (result == DialogResult.OK)
                                {
                                    window.Menu.UncheckTransparencyMenu();
                                    window.Menu.CheckMenuItem(MenuItemId.SC_TRANS_CUSTOM, true);
                                }
                            }
                            break;

                        case MenuItemId.SC_ALIGN_DEFAULT:
                            {
                                window.Menu.UncheckAlignmentMenu();
                                window.Menu.CheckMenuItem(MenuItemId.SC_ALIGN_DEFAULT, true);
                                window.RestorePosition();
                            }
                            break;

                        case MenuItemId.SC_ALIGN_CUSTOM:
                            {
                                var positionForm = new PositionForm(window, _settings.Language);
                                ApplyThemeToForm(positionForm);
                                var result = positionForm.ShowDialog(window.Win32Window);

                                if (result == DialogResult.OK)
                                {
                                    window.ShowNormal();
                                    window.SetPosition(positionForm.WindowLeft, positionForm.WindowTop);
                                    window.Menu.UncheckAlignmentMenu();
                                    window.Menu.CheckMenuItem(MenuItemId.SC_ALIGN_CUSTOM, true);
                                }
                            }
                            break;

                        case MenuItemId.SC_CHANGE_ICON:
                            {
                                try
                                {
                                    var dialog = new OpenFileDialog
                                    {
                                        ValidateNames = true,
                                        DefaultExt = _settings.Language.GetValue("icon_default_ext"),
                                        RestoreDirectory = false,
                                        Filter = _settings.Language.GetValue("icon_filter")
                                    };

                                    if (dialog.ShowDialog(window.Win32Window) == DialogResult.OK)
                                    {
                                        window.ChangeIcon(dialog.FileName);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show(ex.Message);
                                }
                            }
                            break;


                        case MenuItemId.SC_CHANGE_TITLE:
                            {
                                var titleForm = new TitleForm(_settings.Language);
                                ApplyThemeToForm(titleForm);
                                titleForm.Title = window.GetWindowText();
                                var result = titleForm.ShowDialog(window.Win32Window);

                                if (result == DialogResult.OK)
                                {
                                    window.SetWindowText(titleForm.Title);
                                }
                            }
                            break;

                        case MenuItemId.SC_DIMMER_ON:
                            {
                                _dimHandle = e.Handle;
                                ShowDimWindows();
                                UpdateDimming(e.Handle);

                                var ptrCopyData = SystemUtils.BuildWmCopyDataPointer(MenuItemId.SC_DIMMER_ON);
                                if (ptrCopyData != IntPtr.Zero)
                                {
                                    if (_childHandle != IntPtr.Zero)
                                    {
                                        SendMessage(_childHandle, WM_COPYDATA, Handle, ptrCopyData);
                                    }

                                    if (_parentHandle != IntPtr.Zero)
                                    {
                                        SendMessage(_parentHandle, WM_COPYDATA, Handle, ptrCopyData);
                                    }
                                }
                            }
                            break;

                        case MenuItemId.SC_DIMMER_OFF:
                            {
                                HideDimWindows();

                                var ptrCopyData = SystemUtils.BuildWmCopyDataPointer(MenuItemId.SC_DIMMER_OFF);
                                if (ptrCopyData != IntPtr.Zero)
                                {
                                    if (_childHandle != IntPtr.Zero)
                                    {
                                        SendMessage(_childHandle, WM_COPYDATA, Handle, ptrCopyData);
                                    }

                                    if (_parentHandle != IntPtr.Zero)
                                    {
                                        SendMessage(_parentHandle, WM_COPYDATA, Handle, ptrCopyData);
                                    }
                                }

                                SetForegroundWindow(e.Handle);
                            }
                            break;

                        case MenuItemId.SC_DISABLE_MINIMIZE_BUTTON:
                            {
                                var isChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_DISABLE_MINIMIZE_BUTTON);
                                window.Menu.CheckMenuItem(MenuItemId.SC_DISABLE_MINIMIZE_BUTTON, !isChecked);
                                window.DisableMinimizeButton(!isChecked);
                            }
                            break;

                        case MenuItemId.SC_DISABLE_MAXIMIZE_BUTTON:
                            {
                                var isChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_DISABLE_MAXIMIZE_BUTTON);
                                window.Menu.CheckMenuItem(MenuItemId.SC_DISABLE_MAXIMIZE_BUTTON, !isChecked);
                                window.DisableMaximizeButton(!isChecked);
                            }
                            break;

                        case MenuItemId.SC_DISABLE_CLOSE_BUTTON:
                            {
                                var isChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_DISABLE_CLOSE_BUTTON);
                                window.Menu.CheckMenuItem(MenuItemId.SC_DISABLE_CLOSE_BUTTON, !isChecked);
                                window.DisableCloseButton(!isChecked);
                            }
                            break;

                        case MenuItemId.SC_SETTINGS:
                            {
                                MenuItemSettingsClick(this, EventArgs.Empty);
                            }
                            break;

                        case MenuItemId.SC_TRANS_100:
                        case MenuItemId.SC_TRANS_90:
                        case MenuItemId.SC_TRANS_80:
                        case MenuItemId.SC_TRANS_70:
                        case MenuItemId.SC_TRANS_60:
                        case MenuItemId.SC_TRANS_50:
                        case MenuItemId.SC_TRANS_40:
                        case MenuItemId.SC_TRANS_30:
                        case MenuItemId.SC_TRANS_20:
                        case MenuItemId.SC_TRANS_10:
                        case MenuItemId.SC_TRANS_00:
                            SetTransparencyMenuItem(window, (int)lowOrder);
                            break;

                        case MenuItemId.SC_PRIORITY_REAL_TIME:
                        case MenuItemId.SC_PRIORITY_HIGH:
                        case MenuItemId.SC_PRIORITY_ABOVE_NORMAL:
                        case MenuItemId.SC_PRIORITY_NORMAL:
                        case MenuItemId.SC_PRIORITY_BELOW_NORMAL:
                        case MenuItemId.SC_PRIORITY_IDLE: 
                            SetPriorityMenuItem(window, (int)lowOrder);
                            break;

                        case MenuItemId.SC_ALIGN_TOP_LEFT:
                        case MenuItemId.SC_ALIGN_TOP_CENTER:
                        case MenuItemId.SC_ALIGN_TOP_RIGHT:
                        case MenuItemId.SC_ALIGN_MIDDLE_LEFT:
                        case MenuItemId.SC_ALIGN_MIDDLE_CENTER:
                        case MenuItemId.SC_ALIGN_MIDDLE_RIGHT:
                        case MenuItemId.SC_ALIGN_BOTTOM_LEFT:
                        case MenuItemId.SC_ALIGN_BOTTOM_CENTER:
                        case MenuItemId.SC_ALIGN_BOTTOM_RIGHT:
                        case MenuItemId.SC_ALIGN_CENTER_HORIZONTALLY:
                        case MenuItemId.SC_ALIGN_CENTER_VERTICALLY:
                            SetAlignmentMenuItem(window, (int)lowOrder);
                            break;
                    }

                    var moveToSubMenuItem = (int)lowOrder - MenuItemId.SC_MOVE_TO;
                    if (window.Menu.MoveToMenuItems.ContainsKey(moveToSubMenuItem))
                    {
                        var monitorHandle = window.Menu.MoveToMenuItems[moveToSubMenuItem];
                        window.MoveToMonitor(monitorHandle);
                    }

                    var windowSizeItem = _settings.MenuItems.WindowSizeItems.FirstOrDefault(x => x.Id == lowOrder);
                    if (windowSizeItem != null)
                    {
                        SetSizeMenuItem(window, (int)lowOrder, windowSizeItem);
                    }

                    for (int i = 0; i < _settings.MenuItems.StartProgramItems.Count; i++)
                    {
                        if (lowOrder - MenuItemId.SC_START_PROGRAM == i)
                        {
                            try
                            {
                                var item = _settings.MenuItems.StartProgramItems[i];
                                var arguments = item.Arguments;
                                var argumentParameters = arguments.GetParams(item.BeginParameter, item.EndParameter);
                                var allParametersInputed = true;
                                var processPath = window.Process?.GetMainModuleFileName() ?? string.Empty;
                                foreach (var parameter in argumentParameters)
                                {
                                    var parameterName = parameter.TrimStart(item.BeginParameter).TrimEnd(item.EndParameter);
                                    if (string.Compare(parameterName, StartProgramMenuItem.PARAMETER_PROCESS_ID, true) == 0)
                                    {
                                        arguments = arguments.Replace(parameter, window.Process?.Id.ToString() ?? string.Empty);
                                        continue;
                                    }

                                    if (string.Compare(parameterName, StartProgramMenuItem.PARAMETER_PROCESS_NAME, true) == 0)
                                    {
                                        arguments = arguments.Replace(parameter, Path.GetFileName(processPath));
                                        continue;
                                    }

                                    if (string.Compare(parameterName, StartProgramMenuItem.PARAMETER_WINDOW_TITLE, true) == 0)
                                    {
                                        arguments = arguments.Replace(parameter, window.GetWindowText());
                                        continue;
                                    }

                                    var parameterForm = new ParameterForm(parameterName, _settings.Language);
                                    ApplyThemeToForm(parameterForm);
                                    var result = parameterForm.ShowDialog(window.Win32Window);

                                    if (result == DialogResult.OK)
                                    {
                                        arguments = arguments.Replace(parameter, parameterForm.ParameterValue);
                                    }
                                    else
                                    {
                                        allParametersInputed = false;
                                        break;
                                    }
                                }
                                
                                if (allParametersInputed)
                                {
                                    if (!SystemUtils.TryResolveExecutablePath(item.FileName, out _, out var executablePathError))
                                    {
                                        throw new InvalidOperationException(executablePathError);
                                    }

                                    SystemUtils.RunAs(item.FileName, arguments, item.ShowWindow, item.RunAs, item.UseWindowWorkingDirectory ? Path.GetDirectoryName(processPath) : null);
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                            break;
                        }
                    }

                    var isSaveItemChecked = window.Menu.IsMenuItemChecked(MenuItemId.SC_SAVE_SELECTED_ITEMS);
                    if (lowOrder == MenuItemId.SC_SAVE_SELECTED_ITEMS)
                    {
                        isSaveItemChecked = !isSaveItemChecked;
                        window.Menu.CheckMenuItem(MenuItemId.SC_SAVE_SELECTED_ITEMS, isSaveItemChecked);
                        window.RefreshState();
                        var windowClassName = window.GetClassName();
                        var processPath = window.Process?.GetMainModuleFileName() ?? string.Empty;
                        if (!string.IsNullOrEmpty(windowClassName) && !string.IsNullOrEmpty(processPath))
                        {
                            var states = _windowSettings.Find(windowClassName, processPath);
                            if (states.Any())
                            {
                                _windowSettings.Remove(windowClassName, processPath);
                            }

                            if (isSaveItemChecked)
                            {
                                _windowSettings.Items.Add((WindowState)window.State.Clone());
                            }

#if WIN32
                            var fileName = Path.Combine(AssemblyUtils.AssemblyDirectory, "Window.xml");
#else
                            var fileName = Path.Combine(AssemblyUtils.AssemblyDirectory, "Window64.xml");
#endif
                            WindowSettings.Save(fileName, _windowSettings, _settings);
                        }
                    }
                    else if (isSaveItemChecked &&
                             lowOrder != MenuItemId.SC_MOVE &&
                             lowOrder != MenuItemId.SC_MINIMIZE &&
                             lowOrder != MenuItemId.SC_MAXIMIZE && 
                             lowOrder != MenuItemId.SC_RESTORE && 
                             lowOrder != MenuItemId.SC_RESIZE)
                    {
                        window.RefreshState();
                        var windowClassName = window.GetClassName();
                        var processPath = window.Process?.GetMainModuleFileName() ?? string.Empty;
                        if (!string.IsNullOrEmpty(windowClassName) && !string.IsNullOrEmpty(processPath))
                        {
                            var states = _windowSettings.Find(windowClassName, processPath);
                            if (states.Any())
                            {
                                _windowSettings.Remove(windowClassName, processPath);
                            }

                            _windowSettings.Items.Add((WindowState)window.State.Clone());
#if WIN32
                            var fileName = Path.Combine(AssemblyUtils.AssemblyDirectory, "Window.xml");
#else
                            var fileName = Path.Combine(AssemblyUtils.AssemblyDirectory, "Window64.xml");
#endif
                            WindowSettings.Save(fileName, _windowSettings, _settings);
                        }
                    }
                }
            }
        }

        private void HotKeyHooked(object sender, HotKeyEventArgs e)
        {
            var handle = GetForegroundWindow();
            var systemMenuHandle = GetSystemMenu(handle, false);

            if (handle != null && handle != IntPtr.Zero && systemMenuHandle != null && systemMenuHandle != IntPtr.Zero)
            {
                var processName = "";

                try
                {
                    GetWindowThreadProcessId(handle, out int processId);
                    var process = SystemUtils.GetProcessByIdSafely(processId);
                    processName = Path.GetFileName(process.GetMainModuleFileName());
                }
                catch
                {
                }

                if (!_settings.ExcludedProcessItems.Select(x => x.Name).Contains(processName.ToLower()))
                {
                    PostMessage(handle, WM_SYSCOMMAND, (uint)e.MenuItemId, 0);
                    e.Succeeded = true;
                }
            }
        }

        private void MoveToHooked(object sender, HotKeyEventArgs e)
        {
            var monitorHandles = SystemUtils.GetMonitors();
            if (monitorHandles.Count < 2)
            {
                return;
            }

            var handle = GetForegroundWindow();
            var window = _windows.TryGetValue(handle, out var win) ? win : null;
            if (window == null)
            {
                handle = WindowUtils.GetParentWindow(handle);
                if (handle == IntPtr.Zero || WindowUtils.IsDesktopWindow(handle))
                {
                    return;
                }

                window = _windows.TryGetValue(handle, out var parentWin) ? parentWin : null;
                if (window == null)
                {
                    return;
                }
            }

            var monitorHandle = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
            var monitor = monitorHandles.Select((x, i) => new { Handle = x, Index = i }).Where(x => x.Handle == monitorHandle).FirstOrDefault();
            if (monitor == null)
            {
                return;
            }

            var monitorIndex = monitor.Index;
            if (e.NextMonitor)
            {
                monitorIndex = monitorIndex == (monitorHandles.Count - 1) ? 0 : monitorIndex + 1;
            }

            if (e.PreviousMonitor)
            {
                monitorIndex = monitorIndex == 0 ? monitorHandles.Count - 1 : monitorIndex - 1;
            }

            monitorHandle = monitorHandles[monitorIndex];
            window.MoveToMonitor(monitorHandle);
            e.Succeeded = true;
        }

        private void SetPriorityMenuItem(Window window, int itemId)
        {
            window.Menu.UncheckPriorityMenu();
            window.Menu.CheckMenuItem(itemId, true);
            window.SetPriority(EnumUtils.GetPriority(itemId));
        }

        private void SetAlignmentMenuItem(Window window, int itemId)
        {
            window.Menu.UncheckAlignmentMenu();
            window.Menu.CheckMenuItem(itemId, true);
            window.ShowNormal();
            window.SetAlignment(EnumUtils.GetWindowAlignment(itemId));
        }

        private void SetSizeMenuItem(Window window, int itemId, WindowSizeMenuItem item)
        {
            window.Menu.UncheckSizeMenu();
            window.Menu.CheckMenuItem(itemId, true);
            window.ShowNormal();
            if (_settings.Sizer.SizerType == WindowSizerType.WindowWithMargins)
            {
                window.SetSize(item.Width, item.Height, item.Left, item.Top);
            }
            else if (_settings.Sizer.SizerType == WindowSizerType.WindowWithoutMargins)
            {
                var margins = window.GetSystemMargins();
                window.SetSize(item.Width == null ? null : (item.Width + margins.Left + margins.Right),
                               item.Height == null ? null : (item.Height + margins.Top + margins.Bottom), 
                               item.Left, 
                               item.Top);
            }
            else
            {
                window.SetSize(item.Width == null ? null : (item.Width + (window.Size.Width - window.ClientSize.Width)),
                               item.Height == null ? null : (item.Height + (window.Size.Height - window.ClientSize.Height)), 
                               item.Left,
                               item.Top);
            }
            window.Menu.UncheckMenuItems(MenuItemId.SC_ROLLUP);
        }

        private void SetTransparencyMenuItem(Window window, int itemId)
        {
            window.Menu.UncheckTransparencyMenu();
            window.Menu.CheckMenuItem(itemId, true);
            window.SetTransparency(EnumUtils.GetTransparency(itemId));
        }

        private void ShowDimWindows()
        {
            if (!_dimForms.Any())
            {
                var opacity = WindowUtils.TransparencyToOpacity(_settings.Dimmer.Transparency);
                var color = ColorTranslator.FromHtml(_settings.Dimmer.Color);
                foreach (var screen in Screen.AllScreens)
                {
                    var dimForm = new DimForm(color, opacity)
                    {
                        Left = screen.Bounds.Left,
                        Top = screen.Bounds.Top
                    };
                    dimForm.Click += DimFormClick;
                    dimForm.DoubleClick += DimFormClick;
                    dimForm.MouseClick += DimFormClick;
                    dimForm.MouseDoubleClick += DimFormClick;
                    dimForm.Show();
                    _dimForms.Add(dimForm);
                }
            }
        }

        private void DimFormClick(object sender, EventArgs e)
        {
            if (_dimHandle != IntPtr.Zero)
            {
                UpdateDimming(_dimHandle);
            }
        }

        private void HideDimWindows()
        {
            _dimForms.ForEach(w => w.Close());
            _dimForms.Clear();
            _dimHandle = IntPtr.Zero;
        }

        private void UpdateDimming(IntPtr hwnd)
        {
            foreach (var dimForm in _dimForms)
            {
                dimForm.Show();
                PostMessage(dimForm.Handle, WM_SYSCOMMAND, MenuItemId.SC_MAXIMIZE, 0);
                SetWindowPos(dimForm.Handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                SetWindowPos(dimForm.Handle, hwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }
}
