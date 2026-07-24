@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\Tools\VsDevCmd.bat" >nul
if errorlevel 1 ( echo VsDevCmd failed & exit /b 1 )
msbuild VSAgent.sln /restore /p:Configuration=Release /verbosity:minimal /nologo
exit /b %ERRORLEVEL%
