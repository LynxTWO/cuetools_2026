@ECHO OFF
REM This script collects built .dll and .pdb files from ThirdParty for debugging CUETools.
REM Wolfgang Stöggl <c72578@yahoo.de>, 2020-2025.

REM The script is located in the subdirectory CUETools
echo %~dp0
pushd %~dp0
SET base_dir=.
SET debug_dir=%base_dir%\bin\Debug\net47

REM use xcopy instead of copy. xcopy creates directories if necessary and outputs the copied file
REM /Y Suppresses prompting to confirm that you want to overwrite an existing destination file.
REM /D xcopy copies all Source files that are newer than existing Destination files

REM xcopy /Y /D %base_dir%\CUETools\user_profiles_enabled %debug_dir%

REM ThirdParty
REM SharpZipLib now ships from the NuGet package (1.4.x) already built into
REM %debug_dir%\plugins; do not overwrite it with the old vendored 0.85.5 copy.
REM (decision D4 / remediation R6)
REM xcopy /Y /D %base_dir%\ThirdParty\ICSharpCode.SharpZipLib.dll %debug_dir%\plugins\

REM ThirdParty\Win32 plugins
xcopy /Y /D %base_dir%\ThirdParty\Win32\hdcd.dll %debug_dir%\plugins\win32\
xcopy /Y /D %base_dir%\ThirdPartyDebug\Win32\libFLAC_dynamic.dll %debug_dir%\plugins\win32\
xcopy /Y /D %base_dir%\ThirdPartyDebug\Win32\libFLAC_dynamic.pdb %debug_dir%\plugins\win32\
xcopy /Y /D %base_dir%\ThirdParty\Win32\libmp3lame.dll %debug_dir%\plugins\win32\
xcopy /Y /D %base_dir%\ThirdPartyDebug\Win32\MACLibDll.dll %debug_dir%\plugins\win32\
xcopy /Y /D %base_dir%\ThirdPartyDebug\Win32\MACLibDll.pdb %debug_dir%\plugins\win32\
xcopy /Y /D %base_dir%\ThirdParty\Win32\unrar.dll %debug_dir%\plugins\win32\
xcopy /Y /D %base_dir%\ThirdPartyDebug\Win32\wavpackdll.dll %debug_dir%\plugins\win32\
xcopy /Y /D %base_dir%\ThirdPartyDebug\Win32\wavpackdll.pdb %debug_dir%\plugins\win32\

REM ThirdParty\x64 plugins
xcopy /Y /D %base_dir%\ThirdParty\x64\hdcd.dll %debug_dir%\plugins\x64\
xcopy /Y /D %base_dir%\ThirdPartyDebug\x64\libFLAC_dynamic.dll %debug_dir%\plugins\x64\
xcopy /Y /D %base_dir%\ThirdPartyDebug\x64\libFLAC_dynamic.pdb %debug_dir%\plugins\x64\
xcopy /Y /D %base_dir%\ThirdParty\x64\libmp3lame.dll %debug_dir%\plugins\x64\
xcopy /Y /D %base_dir%\ThirdPartyDebug\x64\MACLibDll.dll %debug_dir%\plugins\x64\
xcopy /Y /D %base_dir%\ThirdPartyDebug\x64\MACLibDll.pdb %debug_dir%\plugins\x64\
xcopy /Y /D %base_dir%\ThirdParty\x64\Unrar.dll %debug_dir%\plugins\x64\
xcopy /Y /D %base_dir%\ThirdPartyDebug\x64\wavpackdll.dll %debug_dir%\plugins\x64\
xcopy /Y /D %base_dir%\ThirdPartyDebug\x64\wavpackdll.pdb %debug_dir%\plugins\x64\

popd
