using SteamKit2;

namespace DistanceSteamDataServer;

public class SteamKit
{
    public const uint DistanceAppId = 233610;

    public readonly SteamClient SteamClient;
    private readonly CallbackManager _callbackManager;

    private readonly string _steamUsername;
    private readonly string _steamPassword;

    private bool _isRunning;
    private readonly object _isRunningLock = new();

    private readonly TaskCompletionSource _onAccountInfoTcs = new();

    private bool IsRunning
    {
        get
        {
            lock (_isRunningLock)
            {
                return _isRunning;
            }
        }
        set
        {
            lock (_isRunningLock)
            {
                _isRunning = value;
            }
        }
    }

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
        IsRunning = true;

        var waitForAccountInfoTask = _onAccountInfoTcs.Task;
        var runCallbacksTask = Task.Factory.StartNew(() =>
        {
            while (IsRunning)
            {
                _callbackManager.RunWaitAllCallbacks(TimeSpan.FromMilliseconds(50));
            }
        }, TaskCreationOptions.LongRunning);

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

        IsRunning = false;
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result != EResult.OK)
        {
            if (callback.Result == EResult.AccountLogonDenied)
            {
                Console.WriteLine("Unable to logon to Steam: This account is SteamGuard protected.");

                IsRunning = false;
                return;
            }

            Console.WriteLine("Unable to logon to Steam: {0} / {1}", callback.Result, callback.ExtendedResult);

            IsRunning = false;
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

        IsRunning = false;
    }

    private static readonly List<(HashSet<SteamID> idSet, TaskCompletionSource tcs)> GetPersonaJobs = new();

    public async Task<IEnumerable<string>> GetPersonaNames(IEnumerable<SteamID> steamIds)
    {
        var steamIdList = steamIds as SteamID[] ?? steamIds.ToArray();
        var filteredSteamIds = steamIdList.Where(IsSteamIdValid).ToArray();
        var steamFriends = SteamClient.GetHandler<SteamFriends>()!;
        if (filteredSteamIds.Length <= 0)
        {
            return steamIdList.Select(_ => "");
        }

        var idSet = new HashSet<SteamID>(filteredSteamIds);
        var tcs = new TaskCompletionSource();
        lock (GetPersonaJobs)
        {
            GetPersonaJobs.Add((idSet, tcs));
        }

        steamFriends.RequestFriendInfo(filteredSteamIds, EClientPersonaStateFlag.PlayerName);

        await tcs.Task;

        return steamIdList.Select(id => steamFriends.GetFriendPersonaName(id) ?? "");
    }

    // Returns true when the instance of the account, type of account, and universe are all '1'.
    // See https://developer.valvesoftware.com/wiki/SteamID.
    private static bool IsSteamIdValid(SteamID id)
    {
        return id >> 32 == 0x0110_0001;
    }

    private static void OnPersonaState(SteamFriends.PersonaStateCallback callback)
    {
        Console.WriteLine($"Received player name for {callback.FriendID}: {callback.Name}");

        lock (GetPersonaJobs)
        {
            var i = 0;
            while (i < GetPersonaJobs.Count)
            {
                var (set, tcs) = GetPersonaJobs[i];
                set.Remove(callback.FriendID);
                if (set.Count == 0)
                {
                    tcs.SetResult();
                    GetPersonaJobs.RemoveAt(i);
                }
                else
                {
                    i += 1;
                }
            }
        }
    }
}