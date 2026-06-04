namespace SwiftList.Core
{
    public enum IpcMessageId : byte
    {
        // App -> Hook
        Stop = 1,
        SetAppProcessId = 2,
        SetQuickSearchVisible = 3,
        SetInlineSearchVisible = 4,
        NavigateDialog = 5,
        RestoreDialogFocus = 6,
        ReloadSettings = 7,
        SetHotkeysDisabled = 8,

        // Hook -> App
        Activate = 10,
        ExplorerDeactivated = 11,
        ActiveWindowMoved = 12,
        KeyBackspace = 13,
        KeyEscape = 14,
        KeyEnter = 15,
        KeyUp = 16,
        KeyDown = 17,
        KeyLeft = 18,
        KeyRight = 19,
        KeyChar = 20,
        KeyCtrlNumber = 21,
        MouseClick = 22,
        ExplorerActivated = 23,
        PathCaptured = 24,
        Error = 25
    }

    public struct IpcMessage
    {
        public IpcMessageId Id { get; set; }
        public uint ProcessId { get; set; }
        public bool BoolVal { get; set; }
        public char CharVal { get; set; }
        public int IntVal { get; set; }
        public int MouseX { get; set; }
        public int MouseY { get; set; }
        public long Hwnd { get; set; }
        public string? StringVal1 { get; set; }
        public string? StringVal2 { get; set; }
        public bool IsDesktop { get; set; }
    }
}
