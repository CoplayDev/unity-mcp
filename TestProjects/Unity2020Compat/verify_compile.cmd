@echo off
cd /d F:\AIProjects\Unity-shikiMCP\TestProjects\Unity2020Compat
"C:\SoftWare\Unity\2020.3.24f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "F:\AIProjects\Unity-shikiMCP\TestProjects\Unity2020Compat" -logFile "F:\AIProjects\Unity-shikiMCP\TestProjects\Unity2020Compat\compile_check.log"
echo UNITY_EXIT=%ERRORLEVEL%
