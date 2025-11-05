@echo off
chcp 65001 >nul
title 访问客户端
cls
echo ========================================
echo   访问客户端 - 访问服务
echo ========================================
echo.
dotnet P2PClient.dll
pause  
