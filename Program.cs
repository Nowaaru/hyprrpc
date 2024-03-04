using System.Collections.Immutable;
using System.Reflection;

public class HyprRPCli
{
    public static readonly string Version =
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

    public static string ConfigDirectory
    {
        get
        {
            string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hyprrpc");

            if (!Directory.Exists(configDir))

                Directory.CreateDirectory(configDir).Create();

            return configDir;
        }

    }

    public static string LogDirectory
    {
        get
        {
            string logsDir = Path.Combine(ConfigDirectory, "logs").ToString();

            if (!Directory.Exists(logsDir))
                Directory.CreateDirectory(logsDir).Create();

            return logsDir;
        }
    }


    public static void Main()
    {

        HyprRPC.Logging.Logger Logger = HyprRPC.RPCInterfacer.Logger = new(LogDirectory);
        Logger.Log("initializing Manager...");
        Logger.DisableLogToTty = true;


        HyprRPC.RPCManager Manager = new();

        Task<List<HyprRPC.Application.App>> initialAppListTask = Manager.DoApplicationScanTask();
        initialAppListTask.Wait();

        ImmutableList<HyprRPC.Application.App> initialAppList = initialAppListTask.Result.ToImmutableList();

        Task<int> initializedManagerTask = Manager.Initialize();
        HyprRPC.Terminal.TUIManager tuiManager = new(initialAppList, Logger, Manager);

        bool didCleanup = false;
        Action Cleanup = () =>
        {
            if (didCleanup)

                return;

            didCleanup = true;
            HyprRPC.RPCInterfacer.Cleanup();
            Manager.Cleanup();
            Logger.Kill();
            tuiManager.CursorEnabled = true;
        };

        ImmutableList<HyprRPC.Application.App> lastAppList = initialAppList.ToImmutableList();

        Action<ImmutableList<HyprRPC.Application.App>> lowUpdateRenderer = (ImmutableList<HyprRPC.Application.App> apps) =>
        {
            tuiManager.RenderedApps = apps;
            lastAppList = tuiManager.RenderedApps;
            HyprRPC.RPCInterfacer.Update();

        };

        Action<ImmutableList<HyprRPC.Application.App>> lowUpdateApps = (ImmutableList<HyprRPC.Application.App> apps) =>
        {
            Logger.Log("gaming yea");

            apps.ForEach((e) =>
            {
                Logger.Log($"rendered app: {e.Name} ({e.ProcessId})");
                HyprRPC.RPCTimeKeep.Record(e);
            });

            Logger.Log($"------");
        };

        Action UpdateApps = () =>
        {
            lowUpdateApps(Manager.GetRegisteredApps());
            lowUpdateRenderer(Manager.GetRegisteredApps());
        };
        Action UpdateRenderer = () =>
        {
            lowUpdateRenderer(Manager.GetRegisteredApps());
        };

        Manager.ConnectEvent(HyprRPC.Events.RPCEventType.APPLICATION_IN, UpdateApps);
        Manager.ConnectEvent(HyprRPC.Events.RPCEventType.APPLICATION_OUT, UpdateApps);
        Manager.ConnectEvent(HyprRPC.Events.RPCEventType.ENDANGERED, UpdateRenderer);
        Manager.ConnectEvent(HyprRPC.Events.RPCEventType.UPDATE, () =>
                {
                    ImmutableList<HyprRPC.Application.App> apps = Manager.GetRegisteredApps();
                    tuiManager.ApplicationCursorPosition = tuiManager.ApplicationCursorPosition >= apps.Count ? apps.Count - 1 : tuiManager.ApplicationCursorPosition;
                    UpdateRenderer();
                });

        tuiManager.ConnectEvent(HyprRPC.Terminal.TUIEventType.KEY_PRESSED, () =>
        {
            if (tuiManager.LastPressedKey is null)

                throw new NullReferenceException("tuiManager.LastPressedKey is null");

            ImmutableList<HyprRPC.Application.App> appList = Manager.GetRegisteredApps();
            ConsoleKeyInfo lastPressedKey = tuiManager.LastPressedKey.Value;
            switch (lastPressedKey.Key)
            {
                case ConsoleKey.UpArrow:
                    tuiManager.ApplicationCursorPosition -= 1;
                    break;

                case ConsoleKey.DownArrow:
                    tuiManager.ApplicationCursorPosition += 1;
                    break;

                case ConsoleKey.LeftArrow:
                    tuiManager.ApplicationCursorPosition = (int)Math.Floor((decimal)(tuiManager.ApplicationCursorPosition / 2));
                    break;

                case ConsoleKey.RightArrow:
                    tuiManager.ApplicationCursorPosition += (int)Math.Ceiling((decimal)(((appList.Count) - tuiManager.ApplicationCursorPosition) / 2));
                    break;

                case ConsoleKey.PageUp:
                    tuiManager.OptionCursorPosition -= 1;
                    break;

                case ConsoleKey.PageDown:
                    tuiManager.OptionCursorPosition += 1;
                    break;

                case ConsoleKey.Enter:
                    {
                        foreach (HyprRPC.Application.App i in appList)

                            if (appList.IndexOf(i) == tuiManager.ApplicationCursorPosition)
                            {
                                Logger.Log($"selecting app {appList.IndexOf(i)} - {i.Name} ({i.ProcessId})");
                                tuiManager.SelectedApp = i;
                                HyprRPC.RPCInterfacer.SelectedApp = i;

                                break;
                            }

                        break;
                    }


                case ConsoleKey.Spacebar:
                    {
                        tuiManager.ToggleOption();
                        tuiManager.Redraw();

                        break;
                    }

                case ConsoleKey.Q:
                    {
                        if ((lastPressedKey.Modifiers & ConsoleModifiers.Shift) != 0)

                            Cleanup();

                        break;
                    }

                case ConsoleKey.Backspace:
                    {
                        break;
                    }
            }
        });

        Console.CancelKeyPress += delegate
        {
            Cleanup();
        };

        initializedManagerTask.Wait();
        Cleanup();
    }
}
