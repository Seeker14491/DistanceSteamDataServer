# https://hub.docker.com/_/microsoft-dotnet
FROM mcr.microsoft.com/dotnet/sdk:10.0.301 AS restore
WORKDIR /src

# copy project metadata and restore as a dedicated layer
COPY DistanceSteamDataServer/DistanceSteamDataServer.csproj DistanceSteamDataServer/
RUN dotnet restore DistanceSteamDataServer/DistanceSteamDataServer.csproj

FROM restore AS publish

# copy source and publish the app
COPY DistanceSteamDataServer/ DistanceSteamDataServer/
RUN dotnet publish DistanceSteamDataServer/DistanceSteamDataServer.csproj -c Release -o /app/publish --no-restore

# final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DistanceSteamDataServer.dll"]
