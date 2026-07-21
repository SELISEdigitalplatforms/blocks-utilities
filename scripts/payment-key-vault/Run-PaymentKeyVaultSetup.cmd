@echo off
setlocal
title Payment Key Vault Setup

where pwsh.exe >nul 2>&1
if "%ERRORLEVEL%"=="0" (
    pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-PaymentKeyVaultSecrets.ps1"
) else (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-PaymentKeyVaultSecrets.ps1"
)
set "PAYMENT_SETUP_EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%PAYMENT_SETUP_EXIT_CODE%"=="0" (
    echo Setup failed. Review the error above.
) else (
    echo Setup finished successfully.
)

echo.
pause
exit /b %PAYMENT_SETUP_EXIT_CODE%
