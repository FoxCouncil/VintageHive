# pull base image
FROM ubuntu:focal

LABEL maintainer="Fox Council <onefox@gmail.com>" \
      github="https://github.com/FoxCouncil/VintageHive"

ENV DEBIAN_FRONTEND=noninteractive

# remove apt lists that are probably outdated
RUN rm -fr /var/lib/apt/lists/*

# update and clean packages
RUN : \
    && apt-get update \
    && rm -rf /var/cache/apk/* \
    && apt-get clean \
    && :

# install liquidsoap dependencies and system packages
RUN apt update && apt upgrade -y && \
	apt install -y \
    gcc \
	wget \
	curl \
    m4 \
	unzip \
	sudo \
    make \
	git \
	dotnet-sdk-7.0 \
	aspnetcore-runtime-7.0 \
    pkg-config \
    zlib1g-dev && \
    apt autoremove && apt clean && \
    rm -rf /var/lib/apt/lists/*
	
# Configure the Liquidsoap user
RUN adduser --disabled-password --gecos '' retro \
  && echo 'retro ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/retro
  
# add user and create necessary directories
RUN mkdir /app /mnt/v && \
	mkdir /app/config && \
	mkdir /app/tv /app/guide/ && \
    chown -R retro /app

USER retro

# open needed ports
EXPOSE 1990
EXPOSE 1971
EXPOSE 1996

# Application specific folder for vintage hive in this container.
WORKDIR /app

# Clone the repositoiry and compile it
RUN git clone https://github.com/FoxCouncil/VintageHive.git

# Change directory to the cloned repository
WORKDIR /app/VintageHive

# Restore the dependencies
RUN dotnet restore

# Build the project
RUN dotnet build -c Release

# Run the compiled application
CMD ["dotnet", "run"]