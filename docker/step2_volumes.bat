@echo off

rem Define info about network share.
set APP_VOLUME=vintagehive-app

:choice
set /P c=Delete existing docker volume?[Y/N]?
if /I "%c%" EQU "Y" goto :del_volumes
if /I "%c%" EQU "N" goto :keep_volumes
goto :choice

:del_volumes

rem Remove existing volumes from docker for this project.
echo Removing existing docker volumes...
docker volume rm %APP_VOLUME%

:keep_volumes

echo Creating VFS volume to persist changes...
mkdir "%cd%\app"
docker volume create ^
 	--name %APP_VOLUME% ^
	--opt type=none ^
	--opt device=%cd%\app ^
	--opt o=bind