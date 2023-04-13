@echo off

rem Define info about video network share.
set DATA_VOLUME=retrofox-video
set DOMAIN=workgroup

:choice
set /P c=Delete existing docker volumes?[Y/N]?
if /I "%c%" EQU "Y" goto :del_volumes
if /I "%c%" EQU "N" goto :keep_volumes
goto :choice

:del_volumes

rem Remove existing volumes from docker for this project.
echo Removing existing docker volumes...
docker volume rm retrofox-db
docker volume rm %DATA_VOLUME%

:keep_volumes

rem Mounts the networked share for video as a volume for use in docker.
rem You MUST use the addr option when relying on DNS resolution
echo Creating video network volume...
rem docker volume create ^
rem 	--name %DATA_VOLUME% ^
rem 	--driver local ^
rem 	--opt type=cifs ^
rem 	--opt device=//192.168.1.5/Videos ^
rem 	--opt o=username=guest,password=

docker volume create ^
 	--name %DATA_VOLUME% ^
	--opt type=none ^
	--opt device=%cd%\station ^
	--opt o=bind

echo Creating config volume to persist changes...   
docker volume create ^
	--name retrofox-db ^
	--driver local
