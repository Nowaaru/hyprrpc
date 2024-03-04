using System.Collections.Immutable;
using System.Diagnostics;
using Spectre.Console;
using HyprRPC.Events;

// TODO: add Options sub-window next to
// Status that allows you to toggle
// the kind of data that you show
// on HyprRPC

namespace HyprRPC
{
    namespace Terminal
    {
        public enum TUIEventType
        {
            KEY_PRESSED
        };

        public class TUIEvent : Event<TUIEventType>
        {
            public TUIEvent(TUIEventType eventType, Action action) : base(eventType, action)
            { }
        }

        public enum SortingMethod
        {
            NAME,
            UPTIME,
            PROCESS_ID,
        }

        public class TUIManager : EventFactory<TUIEventType>
        {


            private readonly IAnsiConsole AnsiConsole;

            public static int TERMINAL_REDRAW_FPS = 60;

            private readonly Layout layout = new Layout("Root")
                        .SplitColumns(
                            new Layout("Applications"),
                            new Layout("Miscellaneous")
                                .SplitRows(new Layout("Status"), new Layout("Options"))
                        );

            Dictionary<string, int> redrawCache = new() {
                {"Width" , -1},
                {"Height" , -1}
            };

            public ConsoleKeyInfo? LastPressedKey;

            private int _AppCursorPos = -1;
            private Application.App? _App;
            public Application.App? SelectedApp
            {
                get
                    => this._App;

                set
                {
                    try
                    {


                        Logger.Log($"requesting app change {value?.Name} ({value?.ProcessId})");
                        this._App = value;
                        this.Redraw();
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"smn wrong happened lol {e.ToString()}");
                    }
                }
            }

            public int ApplicationCursorPosition
            {
                get =>
                    this._AppCursorPos;

                set
                {
                    this._AppCursorPos = Math.Clamp(value, 0, this.RenderedApps.Count - 1);
                    this.Redraw();
                }
            }

            private int _OptsCursorPos = 0;
            public int OptionCursorPosition
            {
                get =>
                    this._OptsCursorPos;

                set
                {
                    this._OptsCursorPos = Math.Clamp(value, 0, this.CurrentOptions.Count - 1);
                    this.Redraw();
                }
            }

            Logging.Logger Logger;
            RPCManager Manager;

            Task ConsoleRedrawManager;
            Task ConsoleInputManager;
            Task ConsoleStatusManager;
            public TUIManager(ImmutableList<Application.App>? initialApps, Logging.Logger Logger, RPCManager manager)
            {
                AnsiConsoleSettings Settings = new AnsiConsoleSettings();
                Settings.Interactive = InteractionSupport.Yes;

                this.Logger = Logger;
                this.Manager = manager;
                this.AnsiConsole = Spectre.Console.AnsiConsole.Create(Settings);

                this.ConsoleRedrawManager =
                    new Task(() =>
                    {

                        while (true)
                        {
                            if (redrawCache["Width"] != Console.WindowWidth || redrawCache["Height"] != Console.BufferHeight)
                            {
                                redrawCache["Width"] = Console.WindowWidth;
                                redrawCache["Height"] = Console.WindowHeight;
                                this.Redraw();
                            }

                            RPCTimeKeep.WaitFrames(1, TUIManager.TERMINAL_REDRAW_FPS);
                        }
                    });

                this.ConsoleInputManager = new Task(async () =>
                        {
                            while (true)
                            {
                                ConsoleKeyInfo pressedKey = Console.ReadKey(true);
                                this.LastPressedKey = pressedKey;

                                await this.FireEvent(TUIEventType.KEY_PRESSED);
                            }
                        });

                this.ConsoleStatusManager = new Task(() =>
                        {
                            while (true)
                            {
                                RPCTimeKeep.WaitFrames(TUIManager.TERMINAL_REDRAW_FPS, TUIManager.TERMINAL_REDRAW_FPS);
                                this.RedrawStatusPanel();
                                this.Redraw(true);
                            }
                        });

                layout["Applications"].Ratio(2);
                this.ConsoleRedrawManager.Start();
                this.ConsoleStatusManager.Start();
                this.ConsoleInputManager.Start();

                this.RedrawApplicationPanel(initialApps);
                this.RedrawOptionsPanel();
                this.RedrawStatusPanel();
                AnsiConsole.Cursor.Hide();
            }

            private ImmutableList<Application.App> _apps = new List<Application.App>().ToImmutableList();
            public ImmutableList<Application.App> RenderedApps
            {
                get => this._apps;
                set
                {
                    this.SilentUpdateRenderedApps(value);
                    this.Redraw();
                }

            }

            private void SilentUpdateRenderedApps(ImmutableList<Application.App> value)
            {
                if (this._AppCursorPos != -1)
                {
                    Application.App cursorItem = this._apps[this.ApplicationCursorPosition];
                    this._AppCursorPos = this._apps.FindIndex((e) => e.Address == cursorItem.Address);
                }

                this._apps = value;
            }

            private IEnumerable<Application.App> SortRenderedApps(SortingMethod method)
            {
                return this.SortRenderedApps(method, this.RenderedApps);
            }


            public ImmutableList<Application.App> SortRenderedApps(SortingMethod method, IEnumerable<Application.App> apps)
            {
                switch (method)
                {
                    /* NOTE: should be fine to index 
                     * RPCTimeKeep without a guard because 
                     * if it's being rendered it first has to go through
                     * the timekeep
                     */

                    case SortingMethod.UPTIME:
                        return this.RenderedApps.OrderBy((app) => RPCTimeKeep.GetTimeSince(app.Address)).ToImmutableList();

                    case SortingMethod.PROCESS_ID:
                        return this.RenderedApps.OrderBy((app) => app.ProcessId).ToImmutableList();

                    default:
                        return this.RenderedApps.OrderBy((app) => app.Name).ToImmutableList();

                }
            }


            private void RedrawApplicationPanel()
            {
                this.RedrawApplicationPanel(null);
            }


            private void RedrawApplicationPanel(ImmutableList<Application.App>? apps)
            {

                if (apps is not null)
                {
                    this.RenderedApps = apps;
                    foreach (Application.App app in apps)

                        RPCTimeKeep.Record(app);
                    // TODO: add cleanup system for timekeep after pid closes

                }

                Panel panelMain = new Panel(
                        new Rows(this.RenderedApps.ConvertAll((e) =>
                        {
                            bool isHovered = this.RenderedApps.IndexOf(e) == this._AppCursorPos;
                            bool isSelected = this.SelectedApp?.Address == e.Address;
                            bool isEndangered = this.Manager.IsEndangered(e);

                            string node = isSelected ? "✔" : (isHovered ? (isEndangered ? "✘" : "•") : " ");

                            string markupString = $"{(isHovered ? $"[bold]" : "")} [{(isSelected ? "green" : "#E4BF7A")}]{node}[/] {(isSelected ? "[blue bold]" : "")}{e.Name}{(isSelected ? "[/]" : "")} ({e.ProcessId}) | {e.Title}{(isHovered ? "[/]" : "")}";

                            if (this.Manager.IsEndangered(e))
                            
                                return new Markup($"[bold red]{Markup.Remove(markupString)}[/]").Ellipsis();

                            return new Markup(markupString);
                        })));

                panelMain.Header = new PanelHeader("Applications");
                panelMain.Border = BoxBorder.Double;
                panelMain.Expand = true;

                if (this.RenderedApps.Count <= 0)

                    this.layout["Applications"].Update(new Text("No applications available."));

                else this.layout["Applications"].Update(panelMain);
            }

            private string IfOptionEnabled(string optionName, string _out)
                => this.CurrentOptions[optionName] ? _out : "";

            private readonly FigletFont figletFont = FigletFont.Load("./figlet/alligator.flf");
            public Spectre.Console.Rendering.IRenderable GetStatusPanelInterior()
            {
                Application.App? selectedApp = this.SelectedApp;
                Process? appProcess = selectedApp is not null ? Process.GetProcessById(selectedApp.ProcessId) : null;

                List<Spectre.Console.Rendering.IRenderable> StatusPanelContent = new();
                if (selectedApp is null)
                {
                    StatusPanelContent.AddRange(
                        new List<Spectre.Console.Rendering.IRenderable>() { // ??
                            new FigletText(this.figletFont, "HYPR").Centered(),
                            new FigletText(this.figletFont, "RPC").Centered()
                        }
                    );

                    return new Rows(StatusPanelContent);
                }


                if (appProcess is null)
                {
                    StatusPanelContent.AddRange(
                        new List<Spectre.Console.Rendering.IRenderable>() {
                            new Markup($"[bold red]✘[/] Application is [bold red]not open[/].")
                        }
                    );
                }
                else
                {
                    /* TODO: make some kind of RPCRenderable that has a Value getter and setter
                     *       where the getter is the formatted result and the setter requires
                     *       the unformatted result
                     */

                    double diffTime = Math.Abs(RPCTimeKeep.GetTimeSince(selectedApp.Address.ToString()));
                    List<string> stringList = new() {
                        IfOptionEnabled("Name", $"Name: [bold green]{selectedApp.Name}[/]"),
                        IfOptionEnabled("TimeElapsed", $"Time Elapsed: [bold green]{TimeSpan.FromMilliseconds(diffTime).ToString("hh\\:mm\\:ss")}[/]"),
                        this.CurrentOptions["Name"] || this.CurrentOptions["TimeElapsed"] ? "\0" : "",
                        IfOptionEnabled("LargeTooltipEnabled", $"Main Image Tooltip: [bold blue]Hyprland[/]"),
                        IfOptionEnabled("SmallTooltipEnabled", $"State Image Tooltip: [bold #E4BF7A]The Animated Tiling Compositor[/]")
                    };

                    stringList.RemoveAll((e) => e == "");
                    List<Spectre.Console.Rendering.IRenderable> renderableList = stringList.ConvertAll<Spectre.Console.Rendering.IRenderable>
                        ((e) => new Markup(e));

                    StatusPanelContent.AddRange(
                        renderableList
                    );
                }

                return new Rows(StatusPanelContent);
            }

            public void RedrawStatusPanel()
            {
                Panel panelSub = new Panel(this.GetStatusPanelInterior());
                panelSub.Header = new PanelHeader("Status");
                panelSub.Border = BoxBorder.Heavy;
                panelSub.Expand = true;

                this.layout["Miscellaneous"]["Status"].Update(panelSub);
                this.layout["Miscellaneous"].Update(this.layout["Miscellaneous"]["Status"]);
            }

            private readonly Dictionary<string, bool> CurrentOptions = RPCInterfacer.Options;
            public void ToggleOption(string optionId)
            {
                this.CurrentOptions[optionId] = !this.CurrentOptions[optionId];
            }

            public void ToggleOption()
            {
                List<KeyValuePair<string, bool>> ListOptions = CurrentOptions.ToList();
                string key = ListOptions[this.OptionCursorPosition].Key;
                this.CurrentOptions[key] = !this.CurrentOptions[key];
                RPCInterfacer.Options = this.CurrentOptions;
            }

            private void RedrawOptionsPanel()
            {
                List<KeyValuePair<string, bool>> ListOptions = CurrentOptions.ToList();
                List<Markup> FormattedOptionList = ListOptions.ConvertAll((e) =>
                    {
                        bool isHovered = this.OptionCursorPosition == ListOptions.IndexOf(e);
                        return new Markup($"{(isHovered ? "[#E4BF7A bold]•[/]" : " ")} {(e.Value ? $"[bold]{e.Key}[/]" : e.Key)} ({(e.Value ? "[bold green]✔[/]" : "[bold red]✘[/]")})");
                    });

                Panel panelSub = new Panel(
                    new Rows(FormattedOptionList)
                );

                panelSub.Header = new PanelHeader("Options");
                panelSub.Border = BoxBorder.Heavy;
                panelSub.Expand = true;

                this.layout["Miscellaneous"]["Options"].Update(panelSub);
            }

            public void Redraw(bool noGlobalRedraw = false)
            {
                if (!noGlobalRedraw)
                {
                    this.RedrawApplicationPanel();
                    this.RedrawOptionsPanel();
                    this.RedrawStatusPanel();
                }

                AnsiConsole.Clear();
                AnsiConsole.Write(this.layout);

                RPCTimeKeep.WaitFrames(1, TUIManager.TERMINAL_REDRAW_FPS);
            }

            public bool CursorEnabled
            {
                set
                {
                    if (value)

                        AnsiConsole.Cursor.Show();

                    else AnsiConsole.Cursor.Hide();
                }
            }

            ~TUIManager()
            {
                this.CursorEnabled = true;
            }
        }
    }
}
