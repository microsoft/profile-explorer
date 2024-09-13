// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace ProfileExplorer.UI;

static class NativeMethods {
  // C++ function name demangling
  [Flags]
  public enum UnDecorateFlags {
    UNDNAME_COMPLETE = 0x0000, // Enable full undecoration
    UNDNAME_NO_LEADING_UNDERSCORES = 0x0001, // Remove leading underscores from MS extended keywords
    UNDNAME_NO_MS_KEYWORDS = 0x0002, // Disable expansion of MS extended keywords
    UNDNAME_NO_FUNCTION_RETURNS = 0x0004, // Disable expansion of return type for primary declaration
    UNDNAME_NO_ALLOCATION_MODEL = 0x0008, // Disable expansion of the declaration model
    UNDNAME_NO_ALLOCATION_LANGUAGE = 0x0010, // Disable expansion of the declaration language specifier
    UNDNAME_NO_MS_THISTYPE = 0x0020, // NYI Disable expansion of MS keywords on the 'this' type for primary declaration
    UNDNAME_NO_CV_THISTYPE = 0x0040, // NYI Disable expansion of CV modifiers on the 'this' type for primary declaration
    UNDNAME_NO_THISTYPE = 0x0060, // Disable all modifiers on the 'this' type
    UNDNAME_NO_ACCESS_SPECIFIERS = 0x0080, // Disable expansion of access specifiers for members
    UNDNAME_NO_THROW_SIGNATURES =
      0x0100, // Disable expansion of 'throw-signatures' for functions and pointers to functions
    UNDNAME_NO_MEMBER_TYPE = 0x0200, // Disable expansion of 'static' or 'virtual'ness of members
    UNDNAME_NO_RETURN_UDT_MODEL = 0x0400, // Disable expansion of MS model for UDT returns
    UNDNAME_32_BIT_DECODE = 0x0800, // Undecorate 32-bit decorated names
    UNDNAME_NAME_ONLY = 0x1000, // Crack only the name for primary declaration;
    // return just [scope::]name.  Does expand template params
    UNDNAME_NO_ARGUMENTS = 0x2000, // Don't undecorate arguments to function
    UNDNAME_NO_SPECIAL_SYMS = 0x4000 // Don't undecorate special names (v-table, vcall, vector xxx, metatype, etc)
  }

  public const uint TOPMOST_FLAGS =
    SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSIZE | SWP_NOMOVE | SWP_NOREDRAW | SWP_NOSENDCHANGING;
  public const uint SYMOPT_DEBUG = 0x80000000;
  public const uint SYMOPT_EXACT_SYMBOLS = 0x00000400;
  private const int MAX_PATH = 260;
  private const uint SWP_NOSIZE = 0x0001;
  private const uint SWP_NOMOVE = 0x0002;
  private const uint SWP_NOZORDER = 0x0004;
  private const uint SWP_NOREDRAW = 0x0008;
  private const uint SWP_NOACTIVATE = 0x0010;
  private const uint SWP_FRAMECHANGED = 0x0020; /* The frame changed: send WM_NCCALCSIZE */
  private const uint SWP_SHOWWINDOW = 0x0040;
  private const uint SWP_HIDEWINDOW = 0x0080;
  private const uint SWP_NOCOPYBITS = 0x0100;
  private const uint SWP_NOOWNERZORDER = 0x0200; /* Don’t do owner Z ordering */
  private const uint SWP_NOSENDCHANGING = 0x0400; /* Don’t send WM_WINDOWPOSCHANGING */
  public const int WM_MOUSEHWHEEL = 0x020E;
  public static readonly IntPtr HWND_TOPMOST = new(-1);
  public static readonly IntPtr HWND_NOTOPMOST = new(-2);
  public static readonly IntPtr HWND_TOP = new(0);
  public static readonly IntPtr HWND_BOTTOM = new(1);

  [SuppressUnmanagedCodeSecurity]
  [DllImport("user32.dll")]
  public static extern uint GetDoubleClickTime();

  [SuppressUnmanagedCodeSecurity]
  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

  [SuppressUnmanagedCodeSecurity]
  [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
  public static extern bool PathFindOnPath([In][Out] StringBuilder pszFile,
                                           [In] string[] ppszOtherDirs);

  [SuppressUnmanagedCodeSecurity]
  [DllImport("user32.dll")]
  public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X,
                                         int Y, int cx, int cy, uint uFlags);

  [SuppressUnmanagedCodeSecurity]
  [DllImport("dbghelp.dll", SetLastError = true, PreserveSig = true)]
  public static extern int UnDecorateSymbolName(
    [In][MarshalAs(UnmanagedType.LPStr)] string DecoratedName,
    [Out] StringBuilder UnDecoratedName,
    [In][MarshalAs(UnmanagedType.U4)] int UndecoratedLength,
    [In][MarshalAs(UnmanagedType.U4)] UnDecorateFlags Flags);

  public static string GetFullPathFromWindows(string exeName) {
    var sb = new StringBuilder(exeName, MAX_PATH);
    return PathFindOnPath(sb, null) ? sb.ToString() : null;
  }

  public static int HIWORD(IntPtr ptr) {
    unchecked {
      if (Environment.Is64BitOperatingSystem) {
        long val64 = ptr.ToInt64();
        return (short)(val64 >> 16 & 0xFFFF);
      }

      int val32 = ptr.ToInt32();
      return (short)(val32 >> 16 & 0xFFFF);
    }
  }

  [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
  private static extern int GetSystemMetrics(int nIndex);

  public static bool IsRemoteDesktopSession() {
    return (GetSystemMetrics(0x1000) & 1) != 0;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct RECT {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }
}