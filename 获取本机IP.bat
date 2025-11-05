@echo off
chcp 65001 > nul
echo ========================================
echo 获取本机 IP 地址
echo ========================================
echo.
echo 你的本机 IP 地址：
echo.
ipconfig | findstr /C:"IPv4" | findstr /V "127.0.0.1"
echo.
echo ========================================
echo 请将上面显示的 IP 地址填入配置文件
echo 例如：192.168.1.100
echo.
echo 修改以下文件中的 "127.0.0.1" 为你的实际 IP：
echo   - server_config_DEBUG.json （不需要改，保持 0.0.0.0 或留空）
echo   - client_config_访问端_DEBUG.json 中的 Servers
echo   - client_config_服务端_DEBUG.json 中的 Servers
echo ========================================
pause
