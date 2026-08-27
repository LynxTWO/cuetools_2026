# Captures the artwork browser and the codec picker on screen at each Windows display scale, in
# both themes, for the release evidence archive. The windows are shown by the gated
# SelectorCaptureTests in CUETools.Wpf.Tests, which only runs when CUETOOLS_SELECTOR_CAPTURE_DIR
# is set; this script sets it, drives the display scale, and restores the machine afterwards.
#
# The display scale is a live, system-wide setting. Every exit path restores the original index
# and removes the PerMonitorSettings registry key the override creates when it did not exist
# beforehand, so the workstation is left exactly as found. Requires the test project to be built
# already (Release) - the sweep runs with --no-build so each scale sees identical binaries.
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\..\TestResults\SelectorSweep"),
    [int[]]$ScalePercents = @(100, 125, 150, 175, 200)
)
$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$testProject = Join-Path $repoRoot "CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj"
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class DpiCtl {
  [DllImport("user32.dll", SetLastError=true)] public static extern bool SystemParametersInfo(uint a, uint p, IntPtr pv, uint w);
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
  [DllImport("Shcore.dll")] public static extern int GetDpiForMonitor(IntPtr h, int t, out uint x, out uint y);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static bool Aware() { return SetProcessDpiAwarenessContext((IntPtr)(-4)); }
  public static uint Read() { var p=new POINT{X=1,Y=1}; uint x,y; GetDpiForMonitor(MonitorFromPoint(p,2),0,out x,out y); return x; }
  // SPI_SETLOGICALDPIOVERRIDE: relative index from the recommended scale.
  public static bool Set(int rel) { return SystemParametersInfo(0x009F,(uint)rel,IntPtr.Zero,1); }
}
"@
[void][DpiCtl]::Aware()

$registryKey = 'HKCU:\Control Panel\Desktop\PerMonitorSettings'
$keyExistedBefore = Test-Path $registryKey
$originalDpi = [DpiCtl]::Read()
"original display scale: $originalDpi dpi"
if ($originalDpi -ne 96) {
    throw "This sweep assumes the workstation starts at the recommended 100 percent (96 dpi); found $originalDpi. Refusing to run."
}

$scaleIndex = @{ 100 = 0; 125 = 1; 150 = 2; 175 = 3; 200 = 4 }
try {
    foreach ($pct in $ScalePercents) {
        [void][DpiCtl]::Set($scaleIndex[$pct])
        Start-Sleep -Milliseconds 2500
        $got = [DpiCtl]::Read()
        $want = [int][math]::Round(96 * $pct / 100)
        if ($got -ne $want) { throw "Scale $pct percent did not apply (wanted $want dpi, got $got)." }
        "=== $pct percent ($got dpi) ==="
        $env:CUETOOLS_SELECTOR_CAPTURE_DIR = $OutDir
        try {
            & dotnet test $testProject -c Release --nologo -v quiet --no-build `
                --filter "FullyQualifiedName~SelectorCaptureTests" 2>&1 |
                Select-String -Pattern 'Passed!|Failed!|error' | ForEach-Object { "  " + $_.ToString().Trim() }
            if ($LASTEXITCODE -ne 0) { throw "Capture test failed at $pct percent." }
        } finally {
            Remove-Item Env:CUETOOLS_SELECTOR_CAPTURE_DIR -ErrorAction SilentlyContinue
        }
    }
}
finally {
    [void][DpiCtl]::Set(0)
    Start-Sleep -Milliseconds 2500
    $back = [DpiCtl]::Read()
    "RESTORED display scale: $back dpi"
    if ($back -ne 96) { Write-Warning "DISPLAY SCALE NOT RESTORED - expected 96 dpi, got $back" }
    if (-not $keyExistedBefore -and (Test-Path $registryKey)) {
        Remove-Item -Path $registryKey -Recurse -Force -Confirm:$false
        "removed the PerMonitorSettings key the override created"
    }
}
"captures: $((Get-ChildItem $OutDir -Filter *.png).Count) files in $OutDir"
