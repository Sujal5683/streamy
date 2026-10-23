# Multi-stage Dockerfile for EnterpriseRemote.ControlPlane
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY src/EnterpriseRemote.Common/EnterpriseRemote.Common.csproj src/EnterpriseRemote.Common/
COPY src/EnterpriseRemote.ControlPlane/EnterpriseRemote.ControlPlane.csproj src/EnterpriseRemote.ControlPlane/

# Restore dependencies
RUN dotnet restore src/EnterpriseRemote.ControlPlane/EnterpriseRemote.ControlPlane.csproj

# Copy full source and build Release
COPY src/EnterpriseRemote.Common/ src/EnterpriseRemote.Common/
COPY src/EnterpriseRemote.ControlPlane/ src/EnterpriseRemote.ControlPlane/

WORKDIR /src/src/EnterpriseRemote.ControlPlane
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:5293
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5293

ENTRYPOINT ["dotnet", "EnterpriseRemote.ControlPlane.dll"]
