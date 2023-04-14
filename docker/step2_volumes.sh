#!/bin/bash

# Define info about network share.
APP_VOLUME=vintagehive-app

while true; do
    read -p "Delete existing docker volume? [Y/N] " yn
    case $yn in
        [Yy]* ) 
            # Remove existing volumes from docker for this project.
            echo Removing existing docker volume...
            sudo docker volume rm ${APP_VOLUME}
            break;;
        [Nn]* ) 
            break;;
        * ) echo "Please answer yes or no.";;
    esac
done

echo Creating VFS volume to persist changes...
mkdir "$(pwd)/app"
sudo docker volume create \
    --name ${APP_VOLUME} \
    --opt type=none \
    --opt device="$(pwd)/app" \
    --opt o=bind