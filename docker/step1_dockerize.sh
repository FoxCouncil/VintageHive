#!/bin/bash
echo Stopping and removing existing container...
sudo docker container stop vintagehive
sudo docker container rm vintagehive

echo Building docker image...
sudo docker build -t foxcouncil/vintagehive .

