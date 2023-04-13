@echo off

echo Stopping and removing existing container...
docker container stop vintagehive
docker container rm vintagehive

echo Building docker image...
docker build -t foxcouncil/vintagehive .
