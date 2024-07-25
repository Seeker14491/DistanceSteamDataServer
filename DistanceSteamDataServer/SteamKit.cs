using SteamKit2;

namespace DistanceSteamDataServer;

public class SteamKit
{
    public const uint DistanceAppId = 233610;

    public readonly SteamClient SteamClient;
    private readonly CallbackManager _callbackManager;

    private readonly string _steamUsername;
    private readonly string _steamPassword;

    private readonly TaskCompletionSource _onAccountInfoTcs = new();

    private readonly TaskCompletionSource _shutdown = new();

    private SteamKit(SteamClient client, CallbackManager callbackManager, string steamUsername, string steamPassword)
    {
        SteamClient = client;
        _callbackManager = callbackManager;
        _steamUsername = steamUsername;
        _steamPassword = steamPassword;
    }

    public static SteamKit Connect(string user, string pass)
    {
        DebugLog.AddListener((category, msg) => Console.WriteLine("{0}: {1}", category, msg));
        DebugLog.Enabled = true;

        var client = new SteamClient();
        var manager = new CallbackManager(client);

        var steamKit = new SteamKit(client, manager, user, pass);

        manager.Subscribe<SteamClient.ConnectedCallback>(steamKit.OnConnected);
        manager.Subscribe<SteamClient.DisconnectedCallback>(steamKit.OnDisconnected);
        manager.Subscribe<SteamUser.LoggedOnCallback>(steamKit.OnLoggedOn);
        manager.Subscribe<SteamUser.LoggedOffCallback>(steamKit.OnLoggedOff);
        manager.Subscribe<SteamUser.AccountInfoCallback>(steamKit.OnAccountInfo);

        manager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);

        Console.WriteLine("Connecting to Steam...");
        client.Connect();

        return steamKit;
    }

    public async Task<Task> RunAsync()
    {
        var waitForAccountInfoTask = _onAccountInfoTcs.Task;
        var runCallbacksTask = Task.Factory.StartNew(() =>
        {
            var shutdown = _shutdown.Task;
            while (!shutdown.IsCompleted)
            {
                _callbackManager.RunWaitAllCallbacks(TimeSpan.FromMilliseconds(50));
            }
        }, TaskCreationOptions.LongRunning);

        var shutdown = _shutdown.Task;
        await Task.WhenAny(waitForAccountInfoTask, shutdown);
        if (shutdown.IsCompleted)
        {
            throw new OperationCanceledException();
        }

        await waitForAccountInfoTask;
        Console.WriteLine("Steam account info received");

        return runCallbacksTask;
    }

    public void Disconnect()
    {
        SteamClient.GetHandler<SteamUser>()!.LogOff();
    }

    private void OnConnected(SteamClient.ConnectedCallback callback)
    {
        Console.WriteLine("Connected to Steam! Logging in '{0}'...", _steamUsername);

        SteamClient.GetHandler<SteamUser>()!.LogOn(new SteamUser.LogOnDetails
        {
            Username = _steamUsername,
            Password = _steamPassword,
        });
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback callback)
    {
        Console.WriteLine("Disconnected from Steam");

        _shutdown.SetResult();
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result != EResult.OK)
        {
            if (callback.Result == EResult.AccountLogonDenied)
            {
                Console.WriteLine("Unable to logon to Steam: This account is SteamGuard protected.");

                _shutdown.SetResult();
                return;
            }

            Console.WriteLine("Unable to logon to Steam: {0} / {1}", callback.Result, callback.ExtendedResult);

            _shutdown.SetResult();
            return;
        }

        Console.WriteLine("Successfully logged on!");
    }

    private void OnAccountInfo(SteamUser.AccountInfoCallback callback)
    {
        _onAccountInfoTcs.TrySetResult();
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback callback)
    {
        Console.WriteLine("Logged off of Steam: {0}", callback.Result);

        _shutdown.SetResult();
    }

    private class GetPersonaNamesJob(HashSet<SteamID> pendingIds)
    {
        public HashSet<SteamID> PendingIds { get; set; } = pendingIds;
        public TaskCompletionSource Tcs { get; set; } = new TaskCompletionSource();
        public CancellationTokenSource CancelTimeout { get; set; } = new CancellationTokenSource();
        public bool IsDead { get; set; }
    }

    private static readonly List<GetPersonaNamesJob> GetPersonaJobs = [];

    public async Task<IEnumerable<string>> GetPersonaNames(IEnumerable<SteamID> steamIds)
    {
        var steamIdList = steamIds as SteamID[] ?? steamIds.ToArray();
        var filteredSteamIds = steamIdList.Where(IsSteamIdValid).ToArray();
        var steamFriends = SteamClient.GetHandler<SteamFriends>()!;
        if (filteredSteamIds.Length <= 0)
        {
            return steamIdList.Select(_ => "");
        }

        var pendingIds = new HashSet<SteamID>(filteredSteamIds);
        var job = new GetPersonaNamesJob(pendingIds);
        ResetGetPersonaNamesTimeout(job);

        lock (GetPersonaJobs)
        {
            GetPersonaJobs.Add(job);
        }

        steamFriends.RequestFriendInfo(filteredSteamIds, EClientPersonaStateFlag.PlayerName);
        await job.Tcs.Task;

        return steamIdList.Select(id => steamFriends.GetFriendPersonaName(id) ?? "");
    }

    private static void ResetGetPersonaNamesTimeout(GetPersonaNamesJob job)
    {
        job.CancelTimeout.Cancel();
        job.CancelTimeout = new CancellationTokenSource();
        var timeout = Task.Delay(60 * 1000, job.CancelTimeout.Token);
        _ = Task.Run(async () =>
        {
            await timeout;
            job.Tcs.TrySetResult();
            job.IsDead = true;
            if (job.PendingIds.Count > 0)
            {
                Console.WriteLine($"GetPersonaNames request timed out. Skipping {job.PendingIds.Count} players.");
            }
        });
    }

    // Returns true when the instance of the account, type of account, and universe are all '1'.
    // See https://developer.valvesoftware.com/wiki/SteamID.
    private static bool IsSteamIdValid(SteamID id)
    {
        return id >> 32 == 0x0110_0001;
    }

    private static DateTime _onPersonaStateHeartbeat = DateTime.Now;

    private static void OnPersonaState(SteamFriends.PersonaStateCallback callback)
    {
        lock (GetPersonaJobs)
        {
            var i = 0;
            while (i < GetPersonaJobs.Count)
            {
                var job = GetPersonaJobs[i];
                job.PendingIds.Remove(callback.FriendID);
                if (job.PendingIds.Count == 0 || job.IsDead)
                {
                    job.Tcs.TrySetResult();
                    GetPersonaJobs.RemoveAt(i);
                }
                else
                {
                    i += 1;
                }
            }

            if (DateTime.Now - _onPersonaStateHeartbeat <= TimeSpan.FromSeconds(5)) return;

            foreach (var job in GetPersonaJobs)
            {
                ResetGetPersonaNamesTimeout(job);
            }

            _onPersonaStateHeartbeat = DateTime.Now;
        }
    }
}