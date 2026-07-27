# Installing a user plugin

CUETools loads user-approved plugins from a separate per-user trust zone:

`%AppData%\CUETools2026\plugins`

The WPF and classic release archives include `Install-CUEToolsPlugin.ps1`. Use
that script instead of dropping DLLs into the packaged `plugins` directory. The
packaged directory belongs to the release manifest and must remain an exact set.

## Trust boundary

A plugin runs in-process with the same files, network access, and user privileges
as CUETools. Install only code obtained directly from a publisher you trust.

The installer writes an exact SHA-256 manifest for the bytes you approve. CUETools
checks that manifest before loading. This detects missing, added, or changed DLLs
after enrollment. It is not a publisher signature. A process that can rewrite
both the per-user DLLs and their manifest can approve different code.

## Package layout

Prepare a directory with at least one managed plugin named `CUETools.*.dll`.
Top-level dependency DLLs are allowed. Architecture-specific managed or native
DLLs may live in exactly one of `mono`, `win32`, or `x64`.

```text
MyCodec\
  CUETools.Codecs.MyCodec.dll
  Vendor.ManagedDependency.dll
  x64\
    vendor-native.dll
```

No other files, directories, nested architecture folders, links, junctions, or
reparse points are accepted. The package limit is 128 DLLs, matching the runtime
manifest limit.

## Enroll

Close CUETools, open PowerShell in the extracted release directory, and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\Install-CUEToolsPlugin.ps1 `
  -PackageDirectory C:\Path\To\MyCodec
```

Review the script and plugin provenance before using `-ExecutionPolicy Bypass`.
The installer stages and rehashes the complete set before a same-volume directory
rename publishes it. Restart CUETools after installation.

An existing user plugin set is never merged or overwritten by default. To replace
the complete set:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\Install-CUEToolsPlugin.ps1 `
  -PackageDirectory C:\Path\To\ReplacementSet `
  -Replace
```

Replacement keeps the prior set beside the active directory as
`plugins-backup-<UTC timestamp>-<unique id>`. CUETools does not load those backup
directories.

## Upgrade from loose drop-in plugins

Older builds allowed `CUETools.*.dll` files to be dropped beside the packaged
plugins. Do not overlay a new release on that directory. A current release treats
an extra DLL beside its package manifest as an integrity failure and refuses the
packaged set.

Before upgrading, copy each third-party plugin and its private dependencies into a
separate directory that follows the layout above. Extract the new release into a
clean directory, use its installer to enroll the prepared set, and keep the old
installation until the new build starts and registers the expected codecs. There
is no automatic migration because CUETools cannot infer which old DLLs the user
approved or which native dependencies belong to them.

## Recover the prior set

Close CUETools before recovery. In `%AppData%\CUETools2026`, move the current
`plugins` directory to a separate recovery name, then rename the selected
`plugins-backup-*` directory to `plugins`. Keep the displaced set until CUETools
starts and the expected codecs register.

The installer restores the prior directory automatically if publication of a
replacement fails. It does not claim storage durability, publisher authenticity,
or compatibility with the installed CUETools version.
