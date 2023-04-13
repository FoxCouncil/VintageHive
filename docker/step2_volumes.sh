#!/bin/bash

# Define info about network share.
VFS_VOLUME=vintagehive-vfs
DOWNLOADS_VOLUME=vintagehive-downloads
DB_VOLUME=vintagehive-db

while true; do
    read -p "Delete existing docker volumes? [Y/N] " yn
    case $yn in
        [Yy]* ) 
            # Remove existing volumes from docker for this project.
            echo Removing existing docker volumes...
            sudo docker volume rm ${VFS_VOLUME}
            sudo docker volume rm ${DOWNLOADS_VOLUME}
			sudo docker volume rm ${DB_VOLUME}
            break;;
        [Nn]* ) 
            break;;
        * ) echo "Please answer yes or no.";;
    esac
done

echo Creating VFS volume to persist changes...
mkdir "$(pwd)/vfs"
sudo docker volume create \
    --name ${VFS_VOLUME} \
    --opt type=none \
    --opt device="$(pwd)/vfs" \
    --opt o=bind

echo Creating downloads volume for saved files...
mkdir "$(pwd)/downloads"
sudo docker volume create \
    --name ${DOWNLOADS_VOLUME} \
    --opt type=none \
    --opt device="$(pwd)/downloads" \
    --opt o=bind
	
echo Creating database volume...
mkdir "$(pwd)/db"
sudo docker volume create \
    --name ${DB_VOLUME} \
    --opt type=none \
    --opt device="$(pwd)/db" \
    --opt o=bind