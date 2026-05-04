FROM mcr.microsoft.com/devcontainers/python:3.13-bullseye

# Remove Yarn apt repo entirely to avoid expired GPG key errors during feature installs
USER root
RUN rm -f /etc/apt/sources.list.d/yarn.list \
    && rm -f /usr/share/keyrings/yarn.gpg
