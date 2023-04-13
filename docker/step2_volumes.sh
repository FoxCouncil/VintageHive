#!/bin/bash

# Define info about video network share.
DATA_VOLUME=retrofox-video
DOMAIN=workgroup

while true; do
    read -p "Delete existing docker volumes? [Y/N] " yn
    case $yn in
        [Yy]* ) 
            # Remove existing volumes from docker for this project.
            echo Removing existing docker volumes...
            sudo docker volume rm retrofox-db
            sudo docker volume rm ${DATA_VOLUME}
            break;;
        [Nn]* ) 
            break;;
        * ) echo "Please answer yes or no.";;
    esac
done

# Mounts the networked share for video as a volume for use in docker.
# You MUST use the addr option when relying on DNS resolution
echo Creating video network volume...
# sudo docker volume create \
#     --name ${DATA_VOLUME} \
#     --driver local \
#     --opt type=cifs \
#     --opt device=//192.168.1.5/Videos \
#     --opt o=username=guest,password=

sudo docker volume create \
    --name ${DATA_VOLUME} \
    --opt type=none \
    --opt device="$(pwd)/station" \
    --opt o=bind

echo Creating config volume to persist changes...   
sudo docker volume create \
    --name retrofox-db \
    --driver local

