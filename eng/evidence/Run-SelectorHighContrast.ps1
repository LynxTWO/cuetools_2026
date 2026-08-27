# Captures the artwork browser and the codec picker on screen with Windows high contrast ON, in
# both app themes, for the release evidence archive. Same gated SelectorCaptureTests as the DPI
# sweep; this script drives the high-contrast setting instead of the display scale.
#
# High contrast is a live, system-wide setting that repaints every window on the desktop. The
# original flags and scheme are read first and restored on every exit path, so the workstation is
# left exactly as found. Requires the test project to be built already (Release).
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\..\TestResults\SelectorHighContrast"),
    [string]$Scheme = "High Contrast Black"
)
$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$testProject = Join-Path $repoRoot "CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj"
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class HcCtl {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public struct HIGHCONTRAST { public uint cbSize; public uint dwFlags; public IntPtr lpszDefaultScheme; }
  [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  public static extern bool SystemParametersInfo(uint a, uint p, ref HIGHCONTRAST pv, uint w);
  public const uint Get = 0x0042, Set = 0x0043, On = 0x1, UpdateAndNotify = 0x3;
  public static uint ReadFlags(out string scheme) {
    var hc = new HIGHCONTRAST(); hc.cbSize = (uint)Marshal.SizeOf(typeof(HIGHCONTRAST));
    if (!SystemParametersInfo(Get, hc.cbSize, ref hc, 0)) throw new InvalidOperationException("SPI_GETHIGHCONTRAST failed");
    scheme = hc.lpszDefaultScheme == IntPtr.Zero ? "" : Marshal.PtrToStringUni(hc.lpszDefaultScheme);
    return hc.dwFlags;
  }
  public static bool Apply(uint flags, string scheme) {
    var hc = new HIGHCONTRAST(); hc.cbSize = (uint)Marshal.SizeOf(typeof(HIGHCONTRAST));
    hc.dwFlags = flags;
    hc.lpszDefaultScheme = Marshal.StringToHGlobalUni(scheme);
    try { return SystemParametersInfo(Set, hc.cbSize, ref hc, UpdateAndNotify); }
    finally { Marshal.FreeHGlobal(hc.lpszDefaultScheme); }
  }
}
"@

$originalScheme = ""
$originalFlags = [HcCtl]::ReadFlags([ref]$originalScheme)
"original high contrast: flags=0x{0:X} on={1} scheme='{2}'" -f $originalFlags, (($originalFlags -band 1) -ne 0), $originalScheme
if (($originalFlags -band 1) -ne 0) {
    throw "High contrast is already on; this sweep expects to start from off. Refusing to run."
}

try {
    "=== enabling high contrast ($Scheme) ==="
    if (-not [HcCtl]::Apply(($originalFlags -bor [HcCtl]::On), $Scheme)) { throw "SPI_SETHIGHCONTRAST (on) failed" }
    # The theme switch is asynchronous and repaints the whole session; give it time to settle.
    Start-Sleep -Seconds 8
    $s = ""; $f = [HcCtl]::ReadFlags([ref]$s)
    if (($f -band 1) -eq 0) { throw "High contrast did not turn on (flags=0x$($f.ToString('X')))." }
    "high contrast is on: scheme='$s'"

    $env:CUETOOLS_SELECTOR_CAPTURE_DIR = $OutDir
    try {
        & dotnet test $testProject -c Release --nologo -v quiet --no-build `
            --filter "FullyQualifiedName~SelectorCaptureTests" 2>&1 |
            Select-String -Pattern 'Passed!|Failed!|error' | ForEach-Object { "  " + $_.ToString().Trim() }
        if ($LASTEXITCODE -ne 0) { throw "Capture test failed under high contrast." }
    } finally {
        Remove-Item Env:CUETOOLS_SELECTOR_CAPTURE_DIR -ErrorAction SilentlyContinue
    }
}
finally {
    "=== restoring high contrast to original ==="
    [void][HcCtl]::Apply($originalFlags, $originalScheme)
    Start-Sleep -Seconds 8
    $s2 = ""; $f2 = [HcCtl]::ReadFlags([ref]$s2)
    "RESTORED: flags=0x{0:X} on={1} scheme='{2}'" -f $f2, (($f2 -band 1) -ne 0), $s2
    if (($f2 -band 1) -ne 0) { Write-Warning "HIGH CONTRAST NOT RESTORED - still on" }
}
"captures: $((Get-ChildItem $OutDir -Filter *.png).Count) files in $OutDir"
