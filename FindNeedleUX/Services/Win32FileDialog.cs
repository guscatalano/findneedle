using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FindNeedleUX.Services;

/// <summary>
/// Native Win32 common file dialogs (<c>IFileOpenDialog</c> / <c>IFileSaveDialog</c>) via COM interop.
/// Used instead of the WinRT <c>Windows.Storage.Pickers</c>, which are brokered out-of-process and
/// fail when the app runs elevated (cross-integrity-level activation is blocked). These run in-process
/// and work in both elevated and normal runs. Must be called on an STA thread (the WinUI UI thread is
/// STA). Returns the selected path, or null if the user cancelled.
///
/// The COM interfaces are declared "flat" (no interface inheritance; every method redeclared in vtable
/// order) — the reliable interop pattern; inheritance + redeclaration corrupts the vtable layout.
/// </summary>
public static class Win32FileDialog
{
    public static string OpenFile(IntPtr owner, IReadOnlyList<(string name, string spec)> filters)
    {
        var dlg = (IFileOpenDialog)new FileOpenDialogRCW();
        try
        {
            dlg.GetOptions(out var opts);
            dlg.SetOptions(opts | FOS.FORCEFILESYSTEM | FOS.FILEMUSTEXIST | FOS.PATHMUSTEXIST);
            SetFilters(filters, dlg.SetFileTypes);
            if (dlg.Show(owner) != 0) return null; // non-zero HRESULT = cancelled / error
            dlg.GetResult(out var item);
            item.GetDisplayName(SIGDN.FILESYSPATH, out var path);
            return path;
        }
        finally { Marshal.FinalReleaseComObject(dlg); }
    }

    public static string PickFolder(IntPtr owner)
    {
        var dlg = (IFileOpenDialog)new FileOpenDialogRCW();
        try
        {
            dlg.GetOptions(out var opts);
            dlg.SetOptions(opts | FOS.FORCEFILESYSTEM | FOS.PICKFOLDERS | FOS.PATHMUSTEXIST);
            if (dlg.Show(owner) != 0) return null;
            dlg.GetResult(out var item);
            item.GetDisplayName(SIGDN.FILESYSPATH, out var path);
            return path;
        }
        finally { Marshal.FinalReleaseComObject(dlg); }
    }

    public static string SaveFile(IntPtr owner, string suggestedName, IReadOnlyList<(string name, string spec)> filters, string defaultExt)
    {
        var dlg = (IFileSaveDialog)new FileSaveDialogRCW();
        try
        {
            dlg.GetOptions(out var opts);
            dlg.SetOptions(opts | FOS.FORCEFILESYSTEM | FOS.OVERWRITEPROMPT);
            if (!string.IsNullOrEmpty(suggestedName)) dlg.SetFileName(suggestedName);
            SetFilters(filters, dlg.SetFileTypes);
            if (!string.IsNullOrEmpty(defaultExt)) dlg.SetDefaultExtension(defaultExt.TrimStart('.'));
            if (dlg.Show(owner) != 0) return null;
            dlg.GetResult(out var item);
            item.GetDisplayName(SIGDN.FILESYSPATH, out var path);
            return path;
        }
        finally { Marshal.FinalReleaseComObject(dlg); }
    }

    private delegate void SetFileTypesFn(uint cFileTypes, [In, MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);

    private static void SetFilters(IReadOnlyList<(string name, string spec)> filters, SetFileTypesFn setter)
    {
        if (filters == null || filters.Count == 0) return;
        var specs = new COMDLG_FILTERSPEC[filters.Count];
        for (int i = 0; i < filters.Count; i++)
            specs[i] = new COMDLG_FILTERSPEC { pszName = filters[i].name, pszSpec = filters[i].spec };
        setter((uint)specs.Length, specs);
    }

    // ----- COM interop definitions (shobjidl), declared flat -----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    [Flags]
    private enum FOS : uint
    {
        OVERWRITEPROMPT = 0x2,
        PICKFOLDERS = 0x20,
        FORCEFILESYSTEM = 0x40,
        PATHMUSTEXIST = 0x800,
        FILEMUSTEXIST = 0x1000,
    }

    private enum SIGDN : uint
    {
        FILESYSPATH = 0x80058000,
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        // IModalWindow
        [PreserveSig] int Show(IntPtr parent);
        // IFileDialog
        void SetFileTypes(uint cFileTypes, [In, MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FOS fos);
        void GetOptions(out FOS pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close([MarshalAs(UnmanagedType.Error)] int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        // IFileOpenDialog
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport, Guid("84bccd23-5fde-4cdb-aea4-af64b83d78ab"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileSaveDialog
    {
        // IModalWindow
        [PreserveSig] int Show(IntPtr parent);
        // IFileDialog
        void SetFileTypes(uint cFileTypes, [In, MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FOS fos);
        void GetOptions(out FOS pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close([MarshalAs(UnmanagedType.Error)] int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        // IFileSaveDialog
        void SetSaveAsItem(IShellItem psi);
        void SetProperties(IntPtr pStore);
        void SetCollectedProperties(IntPtr pList, int fAppendDefault);
        void GetProperties(out IntPtr ppStore);
        void ApplyProperties(IShellItem psi, IntPtr pStore, IntPtr hwnd, IntPtr pSink);
    }

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW { }

    [ComImport, Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
    private class FileSaveDialogRCW { }
}
