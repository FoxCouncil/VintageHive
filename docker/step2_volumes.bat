@echo off

rem Define info about network share.
set VFS_VOLUME=vintagehive-vfs
set DOWNLOADS_VOLUME=vintagehive-downloads

:choice
set /P c=Delete existing docker volumes?[Y/N]?
if /I "%c%" EQU "Y" goto :del_volumes
if /I "%c%" EQU "N" goto :keep_volumes
goto :choice

:del_volumes

rem Remove existing volumes from docker for this project.
echo Removing existing docker volumes...
docker volume rm %VFS_VOLUME%
docker volume rm %DOWNLOADS_VOLUME%

:keep_volumes

echo Creating VFS volume to persist changes...
mkdir "%cd%/vfs"
docker volume create ^
 	--name %VFS_VOLUME% ^
	--opt type=none ^
	--opt device=%cd%\vfs ^
	--opt o=bind

echo Creating downloads volume for saved files...
mkdir "%cd%\downloads"
docker volume create ^
 	--name %DOWNLOADS_VOLUME% ^
	--opt type=none ^
	--opt device=%cd%\downloads ^
	--opt o=bind
