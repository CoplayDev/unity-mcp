@echo off
setlocal

git checkout main
if errorlevel 1 exit /b 1

git fetch -ap upstream
if errorlevel 1 exit /b 1

git fetch -ap
if errorlevel 1 exit /b 1

git rebase upstream/main
if errorlevel 1 exit /b 1

git push
if errorlevel 1 exit /b 1
