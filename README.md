# C DiskGlow (.NET Framework 4.8)

Windows maintenance utility rebuilt from the cloned ZyperWin++ WinForms source.

## Modules

- C drive, QQ, and WeChat cleanup with scan-before-delete
- ZyperWin++ system optimization rules with restore operations
- Desktop application and Microsoft Store application uninstall
- Disk usage table, extension summary, largest files, and drill-down treemap
- NVIDIA, AMD, and Intel GPU capability detection
- DISM, SFC, CHKDSK, and network repair entry points
- Persistent operation logs

The executable requests administrator privileges through its application
manifest. Destructive operations require confirmation and are logged.

## Build

Open `ZyperWin++.sln` in Visual Studio 2022 and build `Release | Any CPU`, or run:

```powershell
msbuild ZyperWin++.sln /m /p:Configuration=Release /p:Platform="Any CPU"
```

