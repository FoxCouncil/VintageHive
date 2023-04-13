@echo off

echo Running docker container...
docker run --name vintagehive ^
	-dp 1999:1999 --restart=always ^
	--volume retrofox-video:/mnt/v ^
	--volume retrofox-db:/app/config ^
	foxcouncil/vintagehive
