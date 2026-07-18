# C DiskGlow (Windows 7/10/11)

Windows maintenance utility rebuilt from the cloned ZyperWin++ WinForms source.

## Final modules

- `C盘深度清理`: system/app/cache/temp categories, dedicated WeChat/QQ scans, large package scans, whitelist, backup and restore
- `软件强力卸载`: desktop/Store categories, search, batch/force uninstall, registry backup and residual cleanup
- `系统智能优化`: directly integrated ZyperWin++ rules plus NVIDIA/AMD/Intel capability detection
- `磁盘文件管理器`: drill-down treemap, extension filtering, batch file operations and seven system-directory migrations
- `CMD 系统修复`: recommended/deep modes for SFC, CHKDSK, DNS, Winsock, DISM, update components and Store cache

The main window is fixed at `1120x700`, has no maximize action, and keeps the
green C DiskGlow visual structure. Long-running work has progress state and
cancellation where the underlying Windows operation supports it.

The executable requests administrator privileges through its application
manifest. Destructive operations require confirmation and are logged. Cleanup
and file deletion backups are stored on a non-system drive when one is available;
otherwise the confirmation dialog explicitly discloses permanent deletion so a
C-drive backup cannot cancel out reclaimed space. System-directory migrations
use a crash-recoverable transaction journal, and ZyperWin++ changes made by this
app are journaled for global restore.

## Downloads

- Windows 10/11: use the `win-x64-self-contained` or `win-x86-self-contained`
  executable. No separate .NET installation is required.
- Windows 7 SP1: use `C-DiskGlow-Windows7-SP1-x86-x64-setup.exe`. The setup
  selects the correct architecture and installs the bundled, Microsoft-signed
  .NET Framework 4.8 runtime automatically when it is missing.
- The smaller `win7-net48` executables are intended only for Windows 7 systems
  that already have .NET Framework 4.8.

Published binaries and SHA-256 checksums are available on the
[GitHub Releases page](https://github.com/lihuan1811/ClearCSharp/releases/latest).

## Build

Build and test both `net8.0-windows` and `net48` with the .NET 8 SDK:

```powershell
dotnet restore ZyperWin++.sln
dotnet build ZyperWin++.sln -c Release --no-restore
dotnet run --project .\CDriveCleaner.Tests\CDriveCleaner.Tests.csproj -c Release -f net8.0-windows --no-build
```

GitHub Actions builds and smoke-tests `net8.0-windows` and `.NET Framework 4.8`
for x64 and x86, validates the embedded administrator manifest, verifies the
Microsoft signature on the bundled framework installer, and publishes all five
executables plus `SHA256SUMS.txt`.
