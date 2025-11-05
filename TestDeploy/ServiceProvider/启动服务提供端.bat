@echo off
chcp 65001 >nul
title 服务提供端
cls
echo ========================================
echo   服务提供端 - 提供服务
echo ========================================
echo.
dotnet P2PClient.dll
pause
