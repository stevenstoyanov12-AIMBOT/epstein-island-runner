@echo off
setlocal
cd /d "%~dp0"
pushd "..\Build\WebGPU" || (echo Build folder not found & pause & exit /b 1)
set "ROOT=%CD%"
popd
echo Uploading everything in %ROOT%
for /r "%ROOT%" %%F in (*) do call :up "%%F"
echo.
echo FINISHED - check above for any "error" lines
pause
exit /b
:up
setlocal enabledelayedexpansion
set "FULL=%~1"
set "REL=!FULL:%ROOT%\=!"
set "REL=!REL:\=/!"
call npx wrangler r2 object put "museum-game/!REL!" --file "!FULL!" --remote
endlocal
goto :eof
