# C DiskGlow (.NET Framework 4.8)

Windows maintenance utility rebuilt from the cloned ZyperWin++ WinForms source.

## Final modules

- `C盘深度清理`: five cleanup categories, scan-only high-risk rules, whitelist, backup and restore
- `软件强力卸载`: desktop/Store categories, search, batch/force uninstall, registry backup and residual cleanup
- `系统智能优化`: directly integrated ZyperWin++ rules plus NVIDIA/AMD/Intel capability detection
- `磁盘文件管理器`: drill-down treemap, extension filtering, batch file operations and seven system-directory migrations
- `CMD 系统修复`: recommended/deep modes for SFC, CHKDSK, DNS, Winsock, DISM, update components and Store cache

The main window is fixed at `1120x700`, has no maximize action, and keeps the
green C DiskGlow visual structure. Long-running work has progress state and
cancellation where the underlying Windows operation supports it.

The executable requests administrator privileges through its application
manifest. Destructive operations require confirmation and are logged. Cleanup
deletes are backed up, system-directory migrations keep rollback records, and
ZyperWin++ changes made by this app are journaled for global restore.

## Build

Open `ZyperWin++.sln` in Visual Studio 2022 and build `Release | Any CPU`, or run:

```powershell
msbuild ZyperWin++.sln /m /p:Configuration=Release /p:Platform="Any CPU"
```
