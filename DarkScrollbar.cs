using System;
using System.Runtime.InteropServices;

namespace SaabDidTool
{
    internal static class DarkScrollbar
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        public static void Apply(IntPtr hwnd)
        {
            try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
