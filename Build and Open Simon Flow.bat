@echo off
title Building Simon Flow
cd /d "%~dp0"
echo.
echo  Building Simon Flow... (this takes a few seconds)
echo.

rem Use the C# compiler that comes with Windows (.NET Framework 4).
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
  echo  Could not find the C# compiler that normally comes with Windows.
  echo  Send a screenshot of this window to Claude.
  pause
  exit /b 1
)

set "GAC=%WINDIR%\Microsoft.NET\assembly\GAC_MSIL"
set "LIBS=/lib:"%FW%" /lib:"%FW%\WPF" /lib:"%GAC%\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35" /lib:"%GAC%\System.Web.Extensions\v4.0_4.0.0.0__31bf3856ad364e35" /lib:"%GAC%\System.Net.Http\v4.0_4.0.0.0__b03f5f7f11d50a3a""

rem Close a running copy so it can be replaced.
taskkill /im SimonFlow.exe /f >nul 2>&1
timeout /t 1 /nobreak >nul

"%FW%\csc.exe" /nologo /nowarn:1668 /codepage:65001 /target:winexe /optimize+ /out:SimonFlow.exe /win32icon:AppIcon.ico %LIBS% ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Speech.dll ^
  SimonFlow.cs
if errorlevel 1 (
  echo.
  echo  Build failed. Send a screenshot of this window to Claude.
  pause
  exit /b 1
)

rem ---- Whisper: high-accuracy speech recognition that runs on this PC (one-time download) ----
set "WD=%LOCALAPPDATA%\SimonFlow\whisper"
if not exist "%WD%" mkdir "%WD%"

if not exist "%WD%\bin\" (
  echo.
  echo  Downloading the Whisper speech engine...
  curl.exe -L --fail --progress-bar -o "%WD%\whisper-bin.zip" "https://github.com/ggml-org/whisper.cpp/releases/latest/download/whisper-bin-x64.zip" || curl.exe -L --fail --progress-bar -o "%WD%\whisper-bin.zip" "https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.2/whisper-bin-x64.zip"
  if exist "%WD%\whisper-bin.zip" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Force -LiteralPath '%WD%\whisper-bin.zip' -DestinationPath '%WD%\bin'"
    del "%WD%\whisper-bin.zip" >nul 2>&1
  )
)

if not exist "%WD%\ggml-base.en.bin" (
  echo.
  echo  Downloading the Whisper voice model, about 150 MB. This can take a few minutes...
  curl.exe -L --fail --progress-bar -o "%WD%\ggml-base.en.bin.part" "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin" && move /y "%WD%\ggml-base.en.bin.part" "%WD%\ggml-base.en.bin" >nul
)

if exist "%WD%\ggml-base.en.bin" (
  echo.
  echo  Whisper is ready.
) else (
  echo.
  echo  Whisper could not be downloaded, so Simon Flow will use Windows' basic speech recognition.
  echo  Check the internet connection and run this file again to retry.
)

rem Desktop shortcut
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Desktop')+'\Simon Flow.lnk'); $s.TargetPath='%~dp0SimonFlow.exe'; $s.WorkingDirectory='%~dp0'; $s.IconLocation='%~dp0SimonFlow.exe,0'; $s.Save()" >nul 2>&1

echo  Done! Opening Simon Flow. There is now a "Simon Flow" shortcut on your desktop.
start "" "%~dp0SimonFlow.exe"
timeout /t 3 >nul
