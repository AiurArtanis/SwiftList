using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.CoreExtensions.Shell
{
    public static class ShellContextMenu
    {
        // Keep public structures and constants used by other parts of the solution (like ShellMenuPresenter.cs) to avoid breaking compatibility
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public static int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm)
        {
            return ShellContextMenuNativeMethods.TrackPopupMenuEx(hmenu, fuFlags, x, y, hwnd, lptpm);
        }

        public static bool GetCursorPos(out POINT lpPoint)
        {
            var p = new ShellContextMenuNativeMethods.POINT();
            bool result = ShellContextMenuNativeMethods.GetCursorPos(out p);
            lpPoint = new POINT { X = p.X, Y = p.Y };
            return result;
        }

        public static bool DestroyMenu(IntPtr hMenu)
        {
            return ShellContextMenuNativeMethods.DestroyMenu(hMenu);
        }
    }

    // ==========================================
    // Shell menu item data (no WPF dependency)
    // ==========================================
    public class ShellMenuItem
    {
        public string Text { get; set; } = "";
        public uint CommandId { get; set; }
        public bool IsSeparator { get; set; }
        public bool HasSubMenu { get; set; }
        public IntPtr SubMenuHandle { get; set; }
        public bool IsDisabled { get; set; }
        public IntPtr HBitmapItem { get; set; }
    }

    // ==========================================
    // Managed shell menu session for enumeration
    // ==========================================
    public class ShellMenuSession : IDisposable
    {
        private IntPtr _pidl;
        private IntPtr _parentFolderPtr;
        private IntPtr _contextMenuPtr;
        private IntPtr _hMenu;
        private ShellContextMenuNativeMethods.IContextMenu? _contextMenu;
        private bool _disposed;

        public const uint CMD_FIRST = 1;
        public const uint CMD_LAST = 30000;

        private ShellMenuSession() { }

        public static ShellMenuSession? Create(string path)
        {
            var session = new ShellMenuSession();
            try
            {
                int hr = ShellContextMenuNativeMethods.SHParseDisplayName(path, IntPtr.Zero, out session._pidl, 0, out _);
                if (hr < 0)
                {
                    Logger.Log($"[ShellMenuSession] SHParseDisplayName failed for: {path} (HR: {hr})", LogLevel.Error);
                    session.Dispose();
                    return null;
                }

                Guid iidIShellFolder = new Guid("000214E6-0000-0000-C000-000000000046");
                IntPtr relativePidl = IntPtr.Zero;
                hr = ShellContextMenuNativeMethods.SHBindToParent(session._pidl, ref iidIShellFolder, out session._parentFolderPtr, ref relativePidl);
                if (hr < 0)
                {
                    Logger.Log($"[ShellMenuSession] SHBindToParent failed (HR: {hr})", LogLevel.Error);
                    session.Dispose();
                    return null;
                }

                var parentFolder = (ShellContextMenuNativeMethods.IShellFolder)Marshal.GetObjectForIUnknown(session._parentFolderPtr);

                Guid iidIContextMenu = new Guid("000214E4-0000-0000-C000-000000000046");
                uint reserved = 0;
                parentFolder.GetUIObjectOf(IntPtr.Zero, 1, new IntPtr[] { relativePidl }, ref iidIContextMenu, ref reserved, out session._contextMenuPtr);
                session._contextMenu = (ShellContextMenuNativeMethods.IContextMenu)Marshal.GetObjectForIUnknown(session._contextMenuPtr);

                session._hMenu = ShellContextMenuNativeMethods.CreatePopupMenu();
                if (session._hMenu == IntPtr.Zero)
                {
                    session.Dispose();
                    return null;
                }

                session._contextMenu.QueryContextMenu(session._hMenu, 0, CMD_FIRST, CMD_LAST, 0x00000000);

                return session;
            }
            catch (Exception ex)
            {
                Logger.Log($"[ShellMenuSession] Create failed: {ex.Message}", LogLevel.Error);
                session.Dispose();
                return null;
            }
        }

        public List<ShellMenuItem> EnumerateItems(IntPtr hMenu = default)
        {
            if (hMenu == IntPtr.Zero)
                hMenu = _hMenu;

            var items = new List<ShellMenuItem>();
            int count = ShellContextMenuNativeMethods.GetMenuItemCount(hMenu);
            if (count <= 0) return items;

            for (uint i = 0; i < (uint)count; i++)
            {
                var mii = new ShellContextMenuNativeMethods.MENUITEMINFOW();
                mii.cbSize = (uint)Marshal.SizeOf<ShellContextMenuNativeMethods.MENUITEMINFOW>();
                mii.fMask = ShellContextMenuNativeMethods.MIIM_FTYPE | ShellContextMenuNativeMethods.MIIM_ID
                          | ShellContextMenuNativeMethods.MIIM_SUBMENU | ShellContextMenuNativeMethods.MIIM_STATE
                          | ShellContextMenuNativeMethods.MIIM_BITMAP;
                mii.dwTypeData = IntPtr.Zero;
                mii.cch = 0;

                if (!ShellContextMenuNativeMethods.GetMenuItemInfoW(hMenu, i, true, ref mii))
                    continue;

                if ((mii.fType & ShellContextMenuNativeMethods.MFT_SEPARATOR) != 0)
                {
                    items.Add(new ShellMenuItem { IsSeparator = true });
                    continue;
                }

                var mii2 = new ShellContextMenuNativeMethods.MENUITEMINFOW();
                mii2.cbSize = (uint)Marshal.SizeOf<ShellContextMenuNativeMethods.MENUITEMINFOW>();
                mii2.fMask = ShellContextMenuNativeMethods.MIIM_STRING;
                mii2.cch = 256;
                mii2.dwTypeData = Marshal.AllocHGlobal(512);

                try
                {
                    if (!ShellContextMenuNativeMethods.GetMenuItemInfoW(hMenu, i, true, ref mii2))
                        continue;

                    string text = Marshal.PtrToStringUni(mii2.dwTypeData) ?? "";
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    items.Add(new ShellMenuItem
                    {
                        Text = text,
                        CommandId = mii.wID,
                        HasSubMenu = mii.hSubMenu != IntPtr.Zero && ShellContextMenuNativeMethods.GetMenuItemCount(mii.hSubMenu) > 0,
                        SubMenuHandle = mii.hSubMenu,
                        IsDisabled = (mii.fState & ShellContextMenuNativeMethods.MFS_DISABLED) != 0,
                        HBitmapItem = mii.hbmpItem
                    });
                }
                finally
                {
                    Marshal.FreeHGlobal(mii2.dwTypeData);
                }
            }

            return items;
        }

        public void InvokeCommand(uint commandId, IntPtr ownerHwnd)
        {
            if (_contextMenu == null) return;

            try
            {
                var info = new ShellContextMenuNativeMethods.CMINVOKECOMMANDINFO();
                info.cbSize = Marshal.SizeOf(info);
                info.hwnd = ownerHwnd;
                info.lpVerb = (IntPtr)(commandId - CMD_FIRST);
                info.nShow = 1;

                _contextMenu.InvokeCommand(ref info);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ShellMenuSession] InvokeCommand failed (id={commandId}): {ex.Message}", LogLevel.Error);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_hMenu != IntPtr.Zero)
            {
                ShellContextMenuNativeMethods.DestroyMenu(_hMenu);
                _hMenu = IntPtr.Zero;
            }
            if (_contextMenuPtr != IntPtr.Zero)
            {
                Marshal.Release(_contextMenuPtr);
                _contextMenuPtr = IntPtr.Zero;
            }
            if (_parentFolderPtr != IntPtr.Zero)
            {
                Marshal.Release(_parentFolderPtr);
                _parentFolderPtr = IntPtr.Zero;
            }
            if (_pidl != IntPtr.Zero)
            {
                ShellContextMenuNativeMethods.CoTaskMemFree(_pidl);
                _pidl = IntPtr.Zero;
            }
        }
    }
}
