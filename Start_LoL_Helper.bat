@echo off
title LoL Helper App
cd /d "%~dp0"
if exist "%~dp0LoLHelper.PCClient\bin\Debug\net9.0-windows\LoLHelper.PCClient.exe" (
    start "" "%~dp0LoLHelper.PCClient\bin\Debug\net9.0-windows\LoLHelper.PCClient.exe"
) else (
    "C:\Program Files\dotnet\dotnet.exe" run --project "%~dp0LoLHelper.PCClient"
)
