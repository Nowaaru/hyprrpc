using System.Collections.Immutable;
using HyprRPC.Application;
using HyprRPC.Events;

using DiscordRPC;

namespace HyprRPC
{
    public static class RPCTimeKeep
    {
        public static void WaitFrames(int frames = 1, int perSecond = 60)
            => Thread.Sleep(frames / perSecond * 1000);


        public static double CurrentTimeMS
        {
            get => DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalMilliseconds;
            set => throw new NotImplementedException();
        }

        public static readonly double TimeInitialized;
        static RPCTimeKeep()
        {
            TimeInitialized = CurrentTimeMS;
        }

        public static double GetTimeSince(string key)
        {
            return CurrentTimeMS - GetTime(key);
        }

        public static double GetTime(string key)
        {
            if (!Recordbook.ContainsKey(key))

                throw new NullReferenceException("attempt to index null record {key}");

            return Recordbook[key];
        }

        private static Dictionary<string, double> Recordbook = new();
        public static void Record(App app)
        {

            string key = app.Address.ToString();
            Record(key);
        }
        public static void Record(string key)
        {
            if (Recordbook.ContainsKey(key))

                return;

            Recordbook.Add(key, CurrentTimeMS);
        }
    }

    public static class RPCInterfacer
    {
        public static HyprRPC.Logging.Logger? Logger;

        private static readonly DiscordRpcClient RPC = new("1213247614204510298");

        private static Dictionary<string, bool> _opts = new() {
                {"Name", true},
                {"Title", false},
                {"Image", true},
                {"TimeElapsed", true},

                {"LargeTooltipEnabled", true},
                {"SmallTooltipEnabled", true},

                {"RandomizedTooltipsEnabled", true},
        };

        public static bool IsOptionEnabled(string what)

            => _opts[what];


        public static Dictionary<string, bool> Options
        {
            get =>
                _opts;

            set
            {
                _opts = value;
                UpdatePresence();
            }
        }


        private static App? _app;

        private static RichPresence _presence;
        public static RichPresence Presence
        {
            get => _presence;
            set
            {
                _presence = value;
                if (!RPC.IsInitialized)

                    return;

                RPC.SetPresence(_presence);
            }
        }

        public static App? SelectedApp
        {
            get =>
                _app;

            set
            {
                _app = value;
                if (_app is null)
                {
                    RPCInterfacer.Disable();
                    return;
                }

                Logger?.Log($"selected app {_app.Name} ({_app.ProcessId})");
                UpdatePresence();
            }
        }

        public static void UpdatePresence()
        {
            if (_app is null)
            {
                Logger?.Log("app is null.");
                return;
            }

            string trimmedTitle = _app.Title.Trim();
            string trimmedName = _app.Name.Trim();
            Logger?.Log("trying update");
            Timestamps stamp = new Timestamps(
                        DateTimeOffset.FromUnixTimeMilliseconds(
                            (long)RPCTimeKeep.GetTime(_app.Address.ToString())).DateTime);

            Assets assets = new Assets()
            {
                LargeImageKey = "hyprland-large",
                LargeImageText = IsOptionEnabled("LargeTooltipEnabled") ? "The Animated Tiling Compositor" : null,
                SmallImageKey = "yellow",
                SmallImageText = IsOptionEnabled("SmallTooltipEnabled") ? "Never gonna give you up..." : null,
            };

            RPCInterfacer.Presence = new RichPresence()
            {
                Details = IsOptionEnabled("Title") ? trimmedTitle : null,
                State = IsOptionEnabled("Name") && trimmedTitle != trimmedName ? trimmedName : null,
                Timestamps = IsOptionEnabled("TimeElapsed") ? stamp : null,
                Assets = IsOptionEnabled("Image") ? assets : null,
            };

            Logger?.Log($"updated: {Presence.ToString()}");
        }

        public static bool IsInitialized { get => RPC.IsInitialized; }

        public static void Update()
        {
            RPC.SynchronizeState();
        }

        public static void Cleanup()
        {
            RPC.ClearPresence();
            RPC.Dispose();
        }

        public static void Start()
        {
            if (!RPC.IsInitialized)

                RPC.Initialize();
        }

        public static void Disable()
        {
            if (RPC.IsInitialized)

                RPC.Deinitialize();
        }

        static RPCInterfacer()
        {
            _presence = new RichPresence();
            RPC.SkipIdenticalPresence = true;
            RPC.Initialize();
        }
    }

    public class RPCManager : EventFactory<RPCEventType>
    {
        private static readonly int INC_SPLICE = 5;

        public static readonly int MAX_RPC_TIMEOUT = 3;
        public static readonly int RPC_PERIODIC_SCAN_MS = 1000;

        private readonly Dictionary<App, int> EndangeredBinaries = new();
        private readonly List<RPCEvent> RPCEvents = new();
        private readonly ApplicationCache Cache = new();
        private readonly ApplicationList List = new();

        public readonly int APP_UPDATER_INTERVAL = 5;
        public Task? _Updater;

        public async Task<List<App>> DoApplicationScanTask()
        {
            List<App> scanResult = App.ParseApplicationsFromClient(await this.List.ReadApplications());
            scanResult.RemoveAll((res) => res.BinaryPath == "");

            return this.Cache.Checkout(scanResult);
        }

        public async Task<bool> RegisterApp(App app)
        {
            if (this.Cache.pushApplication(app))
            {
                await this.FireEvent(RPCEventType.APPLICATION_IN);

                return true;
            }

            return false;
        }

        public bool IsEndangered(App app)
            => this.EndangeredBinaries.ToList().Exists((e) => e.Key.Address == app.Address);

        public ImmutableList<App> GetRegisteredApps()
            => this.Cache.CachedApplications.ToImmutableList();

        public async Task<int> DoApplicationTimeoutTask()
        {
            int purgedApps = 0;
            List<App> cachedApps = new(Cache.CachedApplications);
            List<App> badApps = new();
            foreach (App i in cachedApps)

                if (!(await i.ExistsStrict()))

                    badApps.Add(i);

            foreach (App nonExistentApp in badApps)
            {
                if (!EndangeredBinaries.ContainsKey(nonExistentApp))
                {
                    EndangeredBinaries.Add(nonExistentApp, RPCManager.MAX_RPC_TIMEOUT);
                    await this.FireEvent(RPCEventType.ENDANGERED);
                }
                else EndangeredBinaries[nonExistentApp] -= 1;

                if (EndangeredBinaries[nonExistentApp] < 0)
                {
                    this.EndangeredBinaries.Remove(nonExistentApp);
                    this.Cache.purgeApplication(nonExistentApp);
                    purgedApps++;
                }
            }

            if (purgedApps > 0)

                await this.FireEvent(RPCEventType.APPLICATION_OUT);

            return purgedApps;
        }

        public async Task Update()
        {
            foreach (App e in this.Cache.CachedApplications)

                await e.Update();

            await this.FireEvent(RPCEventType.UPDATE);
        }

        CancellationTokenSource tokenSource = new();
        private bool firstRun = true;
        public async Task<int> Initialize()
        {
            List<App> processedApps = await DoApplicationScanTask();
            foreach (App app in processedApps)

                this.Cache.pushApplication(app);

            this._Updater = Task.Run(async () =>
                {
                    while (true)
                    {
                        if (tokenSource is null)

                            throw new NullReferenceException("token source is null");

                        else if (tokenSource.IsCancellationRequested)
                        {
                            break;
                        }

                        Thread.Sleep(this.APP_UPDATER_INTERVAL * 1000);
                        await this.Update();
                    }
                });

            return await Task.Run(async () =>
                    {
                        while (true)
                        {
                            if (tokenSource is null)

                                throw new NullReferenceException("token source is null");

                            else if (tokenSource.IsCancellationRequested)
                            {
                                break;
                            }


                            if (!firstRun)
                            {
                                await DoApplicationTimeoutTask();
                                ImmutableList<App> processedApps = (await this.DoApplicationScanTask()).ToImmutableList();
                                foreach (App app in processedApps)
                                {
                                    await this.RegisterApp(app);
                                }
                            }

                            for (double i = 0; i < INC_SPLICE; i++)
                            {
                                if (tokenSource.IsCancellationRequested)

                                    return 1;

                                Thread.Sleep(RPCManager.RPC_PERIODIC_SCAN_MS / INC_SPLICE);
                            }

                            if (firstRun)
                                firstRun = false;
                        }

                        return 0;
                    });
        }

        public void Cleanup()
        {
            if (tokenSource is null)

                throw new NullReferenceException("token source is null");

            tokenSource.Cancel();
        }
    }
}
