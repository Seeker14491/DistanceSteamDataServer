# DistanceSteamData

A [gRPC](https://grpc.io/) server providing access to Steam leaderboards and related data for the game [Distance](http://survivethedistance.com/).

The way the server works is pretty simple: you run the server and provide it with a Steam username and password of an account that owns Distance (Steam Guard needs to be disabled for this account). The server connects to the Steam network using those credentials, and is then able to query the Steam network. The server exposes this Steam-querying functionality through a gRPC interface, allowing client applications access to the Steam data.

Currently, the server allows querying

- leaderboard entries, given a leaderboard name
- player names, given Steam IDs

See [`steam.proto`](./DistanceSteamDataServer/Protos/steam.proto) for the exact details of the API. Notably, querying the Steam Workshop is not supported due to the SteamKit library used not supporting it.

Also in this repo is a Rust client library for consuming the API.

## Building and running the server with Docker

```bash
docker build -t distance-steam-data-server .
docker run --rm -p 80:8080 -e STEAM_USERNAME=john -e STEAM_PASSWORD=12345 distance-steam-data-server
```

## Using the Rust client library

Add the library to `Cargo.toml`:

```toml
[dependencies]
distance-steam-data-client = { git = "https://github.com/Seeker14491/DistanceSteamDataServer.git" }
```
