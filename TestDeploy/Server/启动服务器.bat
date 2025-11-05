@echo off
chcp 65001 >nul
title P2P 服务器端
cls
echo ========================================
echo   P2P 服务器端 - 监听端口 8000
echo ========================================
echo.
dotnet P2PServer.dll
pause  
