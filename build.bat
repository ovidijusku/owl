@echo off
rem Rebuilds dist\BouncingCircle.exe on Windows using the C# compiler that
rem ships with the .NET Framework (no installation needed).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ /out:dist\BouncingCircle.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll src\BouncingCircle.cs
if errorlevel 1 (echo Build FAILED & pause & exit /b 1)
echo Built dist\BouncingCircle.exe
pause
