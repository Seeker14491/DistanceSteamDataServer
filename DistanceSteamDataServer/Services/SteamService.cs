using Grpc.Core;
using SteamKit2;

namespace DistanceSteamDataServer.Services;

public class SteamService : Steam.SteamBase
{
    public override async Task<LeaderboardResponse> GetLeaderboardEntries(LeaderboardRequest request,
        ServerCallContext context)
    {
        var steamUserStats = Program.Steam.SteamClient.GetHandler<SteamUserStats>()!;
        var leaderboard = await steamUserStats.FindLeaderboard(SteamKit.DistanceAppId, request.LeaderboardName);

        // This succeeds under normal circumstances, even for non-existing leaderboards.
        if (leaderboard.Result != EResult.OK)
        {
            throw new Exception($"Error finding leaderboard '{request.LeaderboardName}'");
        }

        var numEntriesRequested = request.EndIndex - request.StartIndex + 1;
        if ((request.StartIndex < 1 && request.EndIndex < 1) || (numEntriesRequested < 1))
        {
            return new LeaderboardResponse
            {
                TotalEntries = leaderboard.EntryCount
            };
        }

        var entries = await steamUserStats.GetLeaderboardEntries(SteamKit.DistanceAppId, leaderboard.ID,
            request.StartIndex,
            request.EndIndex, ELeaderboardDataRequest.Global);
        if (entries.Result != EResult.OK)
        {
            // Leaderboard doesn't exist
            return new LeaderboardResponse();
        }

        var entries2 = entries.Entries.Select(entry => new LeaderboardEntry
        {
            SteamId = entry.SteamID,
            GlobalRank = entry.GlobalRank,
            Score = entry.Score,
            HasReplay = entry.UGCId != 0xFFFF_FFFF_FFFF_FFFF
        });

        var response = new LeaderboardResponse
        {
            TotalEntries = leaderboard.EntryCount,
        };
        response.Entries.AddRange(entries2);

        return response;
    }

    public override async Task<PersonaResponse> GetPersonaNames(PersonaRequest request, ServerCallContext context)
    {
        if (request.SteamIds.Count == 0)
        {
            return new PersonaResponse();
        }

        var personaNames = await Program.Steam.GetPersonaNames(request.SteamIds.Select(id => new SteamID(id)));
        var response = new PersonaResponse();
        response.PersonaNames.AddRange(personaNames);

        return response;
    }

    private readonly ILogger<SteamService> _logger;

    public SteamService(ILogger<SteamService> logger)
    {
        _logger = logger;
    }
}