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
	curl \
	wget \
    libc6 \
    libgcc1 \
    libgssapi-krb5-2 \
    libicu66 \
    libssl1.1 \
    libstdc++6 \
    zlib1g \
    zlib1g-dev \
    m4 \
	openssh-server \
	unzip \
	sudo \
    make \
	git \
    pkg-config \
    net-tools \
    ca-certificates && \
    apt autoremove
	
# Configure the Liquidsoap user
RUN adduser --disabled-password --gecos '' retro \
  && echo 'retro ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/retro
  
# add user and create necessary directories
RUN mkdir /app \
	mkdir /app/vfs/ && \
	mkdir /app/downloads/ && \
    chown -R retro /app

# retro user!
USER retro

# Application specific folder for vintage hive in this container.
WORKDIR /app

RUN sudo wget -q https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
RUN sudo dpkg -i packages-microsoft-prod.deb
RUN sudo rm packages-microsoft-prod.deb

# Install .NET
RUN sudo apt-get update \
&& sudo apt-get install -y \
    dotnet-sdk-7.0

# HttpProxy Listener (HTTP)
EXPOSE 1990

# FtpProxy Listener (FTP)
EXPOSE 1971

# Secure Proxy (Socks5)
EXPOSE 1996

# OscarServer Listener (AIM/ICQ)
EXPOSE 5190

# HttpProxy Listener (HTTPS)
EXPOSE 9999

# Used with SSH and Visual Studio to allow debugging of running container.
EXPOSE 5000
EXPOSE 5001

# Clone the repository and compile it
#RUN git clone https://github.com/FoxCouncil/VintageHive.git /app/src
RUN git clone https://github.com/Maxwolf/VintageHive.git /app/src

# Change directory to the cloned repository
WORKDIR /app/src

# Restore the dependencies
RUN dotnet restore

# Build the project
RUN dotnet build --nologo --configuration Debug

# Publish the app to a single directory.
RUN dotnet publish -p:PublishProfile=FolderProfile --configuration Debug -o /app/

# Change to publish directory where our app lives.
WORKDIR /app

# Run the compiled application
CMD ["./VintageHive"]