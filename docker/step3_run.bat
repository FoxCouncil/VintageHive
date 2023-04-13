@echo off

echo Running docker container...
docker run --name vintagehive ^
	-dp 1990:1990 ^
	-dp 1971:1971 ^
	-dp 1996:1996 ^
	-dp 5190:5190 ^
	-dp 9999:9999 ^
	--restart=always ^
	--volume vintagehive-vfs:/app/vfs ^
	--volume vintagehive-downloads:/app/downloads ^
	--volume vintagehive-db:/app/db ^
	foxcouncil/vintagehive
