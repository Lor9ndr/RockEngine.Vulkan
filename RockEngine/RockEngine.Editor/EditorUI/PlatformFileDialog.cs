using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RockEngine.Editor.EditorUI
{
    public static class PlatformFileDialog
    {
        public static string OpenFile(string filter, string title)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return WindowsNativeDialog.OpenFile(filter, title);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return LinuxNativeDialog.OpenFile(filter, title);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return MacNativeDialog.OpenFile(filter, title);
            }

            throw new PlatformNotSupportedException("Platform not supported");
        }

        public static string OpenFolder(string title)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return WindowsNativeDialog.OpenFolder(title);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return LinuxNativeDialog.OpenFolder(title);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return MacNativeDialog.OpenFolder(title);
            }

            throw new PlatformNotSupportedException("Platform not supported");
        }

        // Windows implementation using native COM dialogs
        private static class WindowsNativeDialog
        {
            [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            private static extern bool GetOpenFileName(ref OpenFileName ofn);

            [DllImport("shell32.dll", CharSet = CharSet.Auto)]
            private static extern nint SHBrowseForFolder(ref BROWSEINFO lpbi);

            [DllImport("shell32.dll", CharSet = CharSet.Auto)]
            private static extern bool SHGetPathFromIDList(nint pidl, string pszPath);

            [DllImport("ole32.dll")]
            private static extern int OleInitialize(nint pvReserved);

            [DllImport("ole32.dll")]
            private static extern void OleUninitialize();

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            private struct OpenFileName
            {
                public int lStructSize;
                public nint hwndOwner;
                public nint hInstance;
                public string lpstrFilter;
                public string lpstrCustomFilter;
                public int nMaxCustFilter;
                public int nFilterIndex;
                public string lpstrFile;
                public int nMaxFile;
                public string lpstrFileTitle;
                public int nMaxFileTitle;
                public string lpstrInitialDir;
                public string lpstrTitle;
                public int Flags;
                public short nFileOffset;
                public short nFileExtension;
                public string lpstrDefExt;
                public nint lCustData;
                public nint lpfnHook;
                public string lpTemplateName;
                public nint pvReserved;
                public int dwReserved;
                public int flagsEx;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            private struct BROWSEINFO
            {
                public nint hwndOwner;
                public nint pidlRoot;
                public string pszDisplayName;
                public string lpszTitle;
                public uint ulFlags;
                public nint lpfn;
                public nint lParam;
                public int iImage;
            }

            public static string OpenFile(string filter, string title)
            {
                try
                {
                    OleInitialize(nint.Zero);

                    const int MAX_FILE_LENGTH = 4096;
                    OpenFileName ofn = new OpenFileName();
                    ofn.lStructSize = Marshal.SizeOf(ofn);
                    ofn.lpstrFilter = filter.Replace('|', '\0') + "\0";
                    ofn.lpstrFile = new string(new char[MAX_FILE_LENGTH]);
                    ofn.nMaxFile = ofn.lpstrFile.Length;
                    ofn.lpstrFileTitle = new string(new char[MAX_FILE_LENGTH]);
                    ofn.nMaxFileTitle = ofn.lpstrFileTitle.Length;
                    ofn.lpstrTitle = title;
                    ofn.Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008; // OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR

                    if (GetOpenFileName(ref ofn))
                    {
                        return ofn.lpstrFile;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error using native file dialog: {ex.Message}");
                    // Fallback to console input
                    return ConsoleFileDialog.OpenFile(title);
                }
                finally
                {
                    OleUninitialize();
                }

                return null;
            }

            public static string OpenFolder(string title)
            {
                try
                {
                    OleInitialize(nint.Zero);

                    BROWSEINFO bi = new BROWSEINFO();
                    bi.lpszTitle = title;
                    bi.ulFlags = 0x00000001; // BIF_RETURNONLYFSDIRS

                    nint pidl = SHBrowseForFolder(ref bi);
                    if (pidl != nint.Zero)
                    {
                        string path = new string('\0', 260);
                        if (SHGetPathFromIDList(pidl, path))
                        {
                            // Trim null characters from the end
                            return path.Substring(0, path.IndexOf('\0'));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error using native folder dialog: {ex.Message}");
                    // Fallback to console input
                    return ConsoleFileDialog.OpenFolder(title);
                }
                finally
                {
                    OleUninitialize();
                }

                return null;
            }
        }

        // Linux implementation using Zenity
        private static class LinuxNativeDialog
        {
            public static string OpenFile(string filter, string title)
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "zenity",
                            Arguments = $"--file-selection --title=\"{title}\" --file-filter=\"{filter}\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    string result = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    return process.ExitCode == 0 ? result : null;
                }
                catch
                {
                    // Fallback to console input
                    return ConsoleFileDialog.OpenFile(title);
                }
            }

            public static string OpenFolder(string title)
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "zenity",
                            Arguments = $"--file-selection --directory --title=\"{title}\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    string result = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    return process.ExitCode == 0 ? result : null;
                }
                catch
                {
                    // Fallback to console input
                    return ConsoleFileDialog.OpenFolder(title);
                }
            }
        }

        // macOS implementation using osascript
        private static class MacNativeDialog
        {
            public static string OpenFile(string filter, string title)
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "osascript",
                            Arguments = $"-e 'choose file name with prompt \"{title}\"'",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    string result = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    return process.ExitCode == 0 ? result : null;
                }
                catch
                {
                    // Fallback to console input
                    return ConsoleFileDialog.OpenFile(title);
                }
            }

            public static string OpenFolder(string title)
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "osascript",
                            Arguments = $"-e 'choose folder with prompt \"{title}\"'",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    string result = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    return process.ExitCode == 0 ? result : null;
                }
                catch
                {
                    // Fallback to console input
                    return ConsoleFileDialog.OpenFolder(title);
                }
            }
        }

        // Fallback implementation using console input
        private static class ConsoleFileDialog
        {
            public static string OpenFile(string title)
            {
                Console.WriteLine($"{title}:");
                Console.Write("Enter file path: ");
                return Console.ReadLine();
            }

            public static string OpenFolder(string title)
            {
                Console.WriteLine($"{title}:");
                Console.Write("Enter folder path: ");
                return Console.ReadLine();
            }
        }
    }

    internal static class Win32Interop
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetActiveWindow();
    }
}