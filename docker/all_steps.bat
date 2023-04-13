@echo off
set "rootDir=%cd%"

echo Step 1: Dockerizing...
call step1_dockerize.bat
echo Step 1 completed.
cd /d "%rootDir%"

echo Step 2: Creating volumes...
call step2_volumes.bat
echo Step 2 completed.
cd /d "%rootDir%"

echo Step 3: Running...
call step3_run.bat
echo Step 3 completed.
cd /d "%rootDir%"
