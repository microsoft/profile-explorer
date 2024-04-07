// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace IRExplorerUI;

static class NativeMethods {
  [DllImport("user32.dll")]
  public static extern uint GetDoubleClickTime();

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

  [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
  public static extern bool PathFindOnPath([In][Out] StringBuilder pszFile,
                                           [In] string[] ppszOtherDirs);

  [DllImport("User32.dll")]
  public static extern bool SetForegroundWindow(IntPtr handle);

  [DllImport("user32.dll")]
  public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X,
                                         int Y, int cx, int cy, uint uFlags);

  [DllImport("dbghelp.dll", SetLastError = true, PreserveSig = true)]
  public static extern int UnDecorateSymbolName(
    [In][MarshalAs(UnmanagedType.LPStr)] string DecoratedName,
    [Out] StringBuilder UnDecoratedName,
    [In][MarshalAs(UnmanagedType.U4)] int UndecoratedLength,
    [In][MarshalAs(UnmanagedType.U4)] UnDecorateFlags Flags);

  [DllImport("dbghelp.dll", CharSet = CharSet.Unicode)]
  public static extern bool SymFindFileInPath(IntPtr hProcess,
                                              [MarshalAs(UnmanagedType.LPWStr)] string SearchPath,
                                              [MarshalAs(UnmanagedType.LPWStr)] string FileName,
                                              IntPtr id,
                                              int two,
                                              int three,
                                              int flags,
                                              [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder filePath,
                                              IntPtr callback,
                                              IntPtr context);

  [DllImport("dbghelp.dll")]
  public static extern bool SymCleanup(IntPtr hProcess);

  [DllImport("dbghelp.dll", CharSet = CharSet.Unicode)]
  public static extern bool SymInitialize(
    IntPtr hProcess,
    [MarshalAs(UnmanagedType.LPWStr)] string UserSearchPath,
    bool fInvadeProcess);

  [DllImport("dbghelp.dll", CharSet = CharSet.Unicode)]
  public static extern uint SymSetOptions(uint options);

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
  public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
  public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
  public static readonly IntPtr HWND_TOP = new IntPtr(0);
  public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

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

  public static string GetFullPathFromWindows(string exeName) {
    var sb = new StringBuilder(exeName, MAX_PATH);
    return PathFindOnPath(sb, null) ? sb.ToString() : null;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct RECT {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }
}