@echo off
setlocal

set ROOT=%~dp0
set SOLUTION=%ROOT%AuditoriaExtend.sln
set WEBPROJECT=%ROOT%src\AuditoriaExtend.Web\AuditoriaExtend.Web.csproj
set PUBLISHDIR=%ROOT%Publish\BPOContasSaude
set DESTINO=\\cab-srv-350\wwwroot$\BPOContasSaude
set LOGFILE=%ROOT%LogCopiaBPOContasSaudeDesenv_350.txt

echo.
echo ... Restore
dotnet restore "%SOLUTION%"
if errorlevel 1 goto :erro

echo.
echo ... Build
dotnet build "%SOLUTION%" -c Release --no-restore
if errorlevel 1 goto :erro

echo.
echo ... Publish
dotnet publish "%WEBPROJECT%" -c Release -o "%PUBLISHDIR%" --no-build
if errorlevel 1 goto :erro

echo.
echo ... Copiando para o servidor
robocopy "%PUBLISHDIR%" "%DESTINO%" /MIR /xf *.config /xd Log /LOG+:"%LOGFILE%"
set RC=%ERRORLEVEL%

REM Robocopy considera 0 a 7 como sucesso
if %RC% GEQ 8 goto :erro_robo

echo.
echo Publicacao concluida com sucesso.
goto :fim

:erro_robo
echo.
echo Erro no robocopy. Codigo: %RC%
pause
exit /b %RC%

:erro
echo.
echo Erro durante o processo.
pause
exit /b 1

:fim
echo.
if /I not "%1"=="/np" (
  pause
)