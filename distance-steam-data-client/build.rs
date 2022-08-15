fn main() -> Result<(), Box<dyn std::error::Error>> {
    tonic_build::configure().build_server(false).compile(
        &["../DistanceSteamDataServer/Protos/steam.proto"],
        &["../DistanceSteamDataServer/Protos"],
    )?;

    Ok(())
}
