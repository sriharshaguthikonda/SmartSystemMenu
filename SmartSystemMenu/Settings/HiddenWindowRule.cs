using System;

namespace SmartSystemMenu.Settings
{
    public class HiddenWindowRule : ICloneable
    {
        public bool Enabled { get; set; }

        public HiddenWindowAction Action { get; set; }

        public string ProcessPath { get; set; }

        public string ClassName { get; set; }

        public string WindowTitle { get; set; }

        public HiddenWindowRule()
        {
            Enabled = true;
            Action = HiddenWindowAction.Hide;
            ProcessPath = string.Empty;
            ClassName = string.Empty;
            WindowTitle = string.Empty;
        }

        public object Clone() => MemberwiseClone();
    }
}
