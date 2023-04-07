# syntax=docker/dockerfile:1
#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0-bullseye-slim AS base
WORKDIR /app

# Copy self signed SSL certificate
COPY development-certificates/certs/marxact.marxact.pfx .
# Install marXact CA certificates, root and signing
COPY development-certificates/ca/signing-ca.crt /usr/share/ca-certificates/marxact/
COPY development-certificates/ca/root-ca.crt /usr/share/ca-certificates/marxact/
RUN echo "marxact/signing-ca.crt" >> /etc/ca-certificates.conf
RUN echo "marxact/root-ca.crt" >> /etc/ca-certificates.conf
RUN update-ca-certificates

FROM mcr.microsoft.com/dotnet/sdk:6.0-bullseye-slim AS build
WORKDIR /src
COPY ["DownloadService/KDownloadService.csproj", "DownloadService/"]
COPY ["surveycontracts/SurveyContracts/SurveyContracts.shproj", "surveycontracts/SurveyContracts/"]
RUN dotnet restore "DownloadService/DownloadService.csproj"
COPY . .
WORKDIR "/src/DownloadService"
RUN dotnet build "DownloadService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DownloadService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DownloadService.dll"]
