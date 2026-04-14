using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using SmartSystemMenu.Extensions;
using SmartSystemMenu.Settings;
using SmartSystemMenu.Utils;
using static SmartSystemMenu.Native.User32;
using WinPoint = SmartSystemMenu.Native.Structs.Point;

namespace SmartSystemMenu.Forms
{
    class WindowTargetPickerForm : Form
    {
        private readonly LanguageSettings _language;
        private readonly Func<IntPtr, bool> _isInvalidTarget;
        private readonly string _titleText;
        private readonly string _instructionText;
        private readonly string _dropHintText;

        private Label _lblInstruction;
        private Label _lblPreview;
        private Panel _pnlTarget;
        private Button _btnCancel;

        private bool _isDragging;
        private IntPtr _currentHandle;
        private Rectangle _currentHighlightRect;

        public IntPtr SelectedHandle { get; private set; }

        public WindowTargetPickerForm(LanguageSettings language, Func<IntPtr, bool> isInvalidTarget, string titleText = null, string instructionText = null, string dropHintText = null)
        {
            _language = language;
            _isInvalidTarget = isInvalidTarget ?? (_ => false);
            _titleText = titleText;
            _instructionText = instructionText;
            _dropHintText = dropHintText;
            _currentHandle = IntPtr.Zero;
            _currentHighlightRect = Rectangle.Empty;

            InitializeControls();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ClearHighlight();
            base.OnFormClosing(e);
        }

        private void InitializeControls()
        {
            Text = string.IsNullOrWhiteSpace(_titleText) ? GetText("mi_hide_by_target", "Hide...") : _titleText;
            ClientSize = new Size(520, 180);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            TopMost = true;
            KeyDown += FormKeyDown;

            _lblInstruction = new Label
            {
                AutoSize = false,
                Left = 12,
                Top = 12,
                Width = 496,
                Height = 36,
                Text = string.IsNullOrWhiteSpace(_instructionText) ? GetText("window_picker_instruction", "Drag the target symbol onto a window to hide it immediately.") : _instructionText
            };

            _pnlTarget = new Panel
            {
                Left = 12,
                Top = 56,
                Width = 64,
                Height = 64,
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Cross
            };
            _pnlTarget.Paint += TargetPanelPaint;
            _pnlTarget.MouseDown += TargetPanelMouseDown;
            _pnlTarget.MouseMove += TargetPanelMouseMove;
            _pnlTarget.MouseUp += TargetPanelMouseUp;

            _lblPreview = new Label
            {
                AutoSize = false,
                Left = 92,
                Top = 56,
                Width = 416,
                Height = 84,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(6),
                Text = GetDropHintText()
            };

            _btnCancel = new Button
            {
                Left = 433,
                Top = 148,
                Width = 75,
                Height = 23,
                Text = GetText("settings_btn_cancel", "Cancel")
            };
            _btnCancel.Click += ButtonCancelClick;

            Controls.Add(_lblInstruction);
            Controls.Add(_pnlTarget);
            Controls.Add(_lblPreview);
            Controls.Add(_btnCancel);
        }

        private void TargetPanelPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(0, 120, 215), 2);
            var centerX = _pnlTarget.Width / 2;
            var centerY = _pnlTarget.Height / 2;
            e.Graphics.DrawEllipse(pen, 6, 6, _pnlTarget.Width - 13, _pnlTarget.Height - 13);
            e.Graphics.DrawEllipse(pen, 20, 20, _pnlTarget.Width - 41, _pnlTarget.Height - 41);
            e.Graphics.DrawLine(pen, centerX, 3, centerX, _pnlTarget.Height - 4);
            e.Graphics.DrawLine(pen, 3, centerY, _pnlTarget.Width - 4, centerY);
        }

        private void TargetPanelMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _isDragging = true;
            _pnlTarget.Capture = true;
            UpdateTargetFromCursor();
        }

        private void TargetPanelMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            UpdateTargetFromCursor();
        }

        private void TargetPanelMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _isDragging = false;
            _pnlTarget.Capture = false;
            ClearHighlight();

            if (_currentHandle == IntPtr.Zero)
            {
                _lblPreview.Text = GetDropHintText();
                return;
            }

            SelectedHandle = _currentHandle;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void FormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyValue == 27)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void ButtonCancelClick(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void UpdateTargetFromCursor()
        {
            var cursorPosition = Cursor.Position;
            var handle = WindowFromPoint(new WinPoint { x = cursorPosition.X, y = cursorPosition.Y });
            handle = WindowUtils.GetParentWindow(handle);
            if (_isInvalidTarget(handle))
            {
                _currentHandle = IntPtr.Zero;
                ClearHighlight();
                _lblPreview.Text = GetText("window_picker_invalid", "No valid window under cursor.");
                return;
            }

            _currentHandle = handle;
            UpdateHighlight(handle);
            UpdatePreview(handle);
        }

        private void UpdateHighlight(IntPtr handle)
        {
            if (!GetWindowRect(handle, out var rect))
            {
                ClearHighlight();
                return;
            }

            var highlightRect = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (highlightRect.Width <= 0 || highlightRect.Height <= 0)
            {
                ClearHighlight();
                return;
            }

            if (_currentHighlightRect == highlightRect)
            {
                return;
            }

            ClearHighlight();
            _currentHighlightRect = highlightRect;
            ControlPaint.DrawReversibleFrame(_currentHighlightRect, Color.Red, FrameStyle.Thick);
        }

        private void ClearHighlight()
        {
            if (_currentHighlightRect == Rectangle.Empty)
            {
                return;
            }

            ControlPaint.DrawReversibleFrame(_currentHighlightRect, Color.Red, FrameStyle.Thick);
            _currentHighlightRect = Rectangle.Empty;
        }

        private void UpdatePreview(IntPtr handle)
        {
            var title = WindowUtils.GetWindowText(handle);
            title = string.IsNullOrWhiteSpace(title) ? "<No title>" : title;

            var className = WindowUtils.GetClassName(handle);
            className = string.IsNullOrWhiteSpace(className) ? "<Unknown>" : className;

            var processName = "<Unknown>";
            try
            {
                var processId = WindowUtils.GetProcessId(handle);
                var process = SystemUtils.GetProcessByIdSafely(processId);
                if (process != null)
                {
                    var processPath = process.GetMainModuleFileName();
                    processName = string.IsNullOrWhiteSpace(processPath) ? process.ProcessName : Path.GetFileName(processPath);
                }
            }
            catch
            {
            }

            _lblPreview.Text = $"Title: {title}{Environment.NewLine}Process: {processName}{Environment.NewLine}Class: {className}";
        }

        private string GetText(string key, string fallback)
        {
            var value = _language?.GetValue(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private string GetDropHintText()
        {
            return string.IsNullOrWhiteSpace(_dropHintText) ? GetText("window_picker_drop_hint", "Drop on a window to hide it.") : _dropHintText;
        }
    }
}
