# DistanceSteamData

## Building and running the server with Docker

```bash
cd DistanceSteamDataServer
docker build -t distance-steam-data-server .
docker run --rm -p 80:80 -e STEAM_USERNAME=john -e STEAM_PASSWORD=12345 distance-steam-data-server
```
