// See https://aka.ms/new-console-template for more information
using System.Collections.Immutable;
using System.Diagnostics;
using Newtonsoft.Json;

namespace HyprRPC
{
    namespace Application
    {
        public class IHyprWorkspace
        {
            public int id { get; set; }
            public string name { get; set; } = "Unnamed Workspace";
        }

        public interface IHyprClients
        {
        }

        [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
        public class IHyprClient
        {
            [JsonProperty("address")]
            public string Address { get; set; } = null!;

            [JsonProperty("mapped")]
            public bool IsMapped { get; set; }

            [JsonProperty("hidden")]
            public bool Hidden { get; set; }

            // [JsonProperty("at")]
            // int[] Position { get; set; }

            // [JsonProperty("size")]
            // int[] Size { get; set; }

            [JsonProperty("floating")]
            public bool IsFloating { get; set; }

            [JsonProperty("monitor")]
            public int Monitor { get; set; }

            [JsonProperty("workspace")]
            public IHyprWorkspace Workspace { get; set; } = null!;

            [JsonProperty("title")]
            public string Title { get; set; } = null!;

            [JsonProperty("class")]
            public string Class { get; set; } = null!;

            [JsonProperty("initialTitle")]
            public string InitialTitle { get; set; } = null!;

            [JsonProperty("initialClass")]
            public string InitialClass { get; set; } = null!;
            [JsonProperty("pid")]
            public int ProcessId { get; set; }

            [JsonProperty("xwayland")]
            public bool UsingXWayland { get; set; }

            [JsonProperty("pinned")]
            public bool IsPinned { get; set; }

            [JsonProperty("fullscreen")]
            public bool IsFullscreen { get; set; }

            [JsonProperty("fakeFullscreen")]
            public bool IsBorderlessFullscreen { get; set; }

            [JsonProperty("swallowing")]
            public string IsSwallowing { get; set; } = null!;

            [JsonProperty("focusHistoryId")]
            public int FocusHistoryId { get; set; }
        }

        public class ApplicationCache
        {
            private List<App> applicationList = new List<App>();

            public bool hasBinary(string binary) =>
                 applicationList.Exists(item => item.BinaryPath == binary);

            public bool hasApplication(App application) =>
                applicationList.Exists(item => item.Address == application.Address);

            public bool pushApplication(App application)
            {
                if (this.hasApplication(application))

                    return false;

                this.applicationList.Add(application);
                return true;
            }

            public bool purgeApplication(App application)
            {
                if (!this.hasApplication(application))
                    return false;

                this.applicationList.Remove(this.applicationList.Find((e) => e.Address == application.Address)!);
                return true;
            }

            public List<App> Checkout(List<App> appList)
            {
                return appList.ConvertAll((e) =>
                        this.CachedApplications.Find((f) => e.Address == f.Address) ?? e);
            }

            public ImmutableList<App> CachedApplications
            {
                get 
                {
                    ImmutableList<App> filteredList = this.applicationList.ToImmutableList();
                    return filteredList.RemoveAll((e) => e.Title == "" || e.ProcessId == -1); 
                    // remove uninitialized or seemingly bugged apps?
                }

                set => throw new NotImplementedException();
            }
        };

        /*
         * Gets all currently-open applications and then
         * lists them. Essentially just spits out
         * `hyprctl clients.`
         */
        public class ApplicationList
        {

            private Process? _childProcess;
            private readonly ProcessStartInfo _startInfo = new ProcessStartInfo()
            {
                FileName = "hyprctl",
                Arguments = "-j clients",
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            private string[] ParseApplicationString(string applicationString)
            {
                return applicationString.Split("\n\n");
            }

            public async Task<bool> ApplicationExists(App app)
            {
                return (await this.ReadApplications()).Exists((e) => e.Address == app.Address);
            }

            // public async Task<string[]> ReadApplications()
            // {
            //     this._childProcess = Process.Start(_startInfo);
            //     ArgumentNullException.ThrowIfNull(this._childProcess); // TODO: google why _childProcess can be null ever..?
            //
            //     string standardOutput = this._childProcess.StandardOutput.ReadToEnd();
            //
            //     await this._childProcess.WaitForExitAsync();
            //
            //     List<string> AppStr = new List<string>(ParseApplicationString(standardOutput));
            //     AppStr.RemoveAll((v) => v.Trim() == "");
            //
            //     return AppStr.ToArray();
            // }

            public List<IHyprClient> ParseClientCall(string clients)
                => JsonConvert.DeserializeObject<List<IHyprClient>>(clients)!;

            public async Task<List<IHyprClient>> ReadApplications()
            {
                this._childProcess = Process.Start(_startInfo);
                if (_childProcess is null)

                    throw new NullReferenceException("child process is null");

                string standardOutput = await this._childProcess.StandardOutput.ReadToEndAsync();
                await this._childProcess.WaitForExitAsync();

                return this.ParseClientCall(standardOutput);
            }
        };

        public class App
        {
            public static List<App> ParseApplicationsFromClient(List<IHyprClient> clients)
            {
                return clients.ConvertAll((e) => new App(e));
            }

            private readonly Process? CurrentProcess;

            private IHyprClient RawAppData;

            public readonly int ApplicationRegisteredAt = (int)DateTime.Today.ToOADate();
            public string Name { get => this.RawAppData.InitialTitle; }
            public string Title { get => this.RawAppData.Title; }
            public string InitialClass { get => this.RawAppData.InitialClass; }
            public string Address { get => this.RawAppData.Address; }
            public int ProcessId { get => this.RawAppData.ProcessId; }

            public readonly string BinaryPath;

            public async Task Update()
            {
                ApplicationList list = new();
                await this.Update(list);
            }

            public async Task Update(ApplicationList fromList)
            {
                IHyprClient? newRawAppData = (await fromList.ReadApplications()).Find((e) => e.Address == this.RawAppData.Address);
                if (newRawAppData is null)

                    throw new NullReferenceException($"newRawApp data for PID {this.RawAppData.ProcessId} is null");

                this.RawAppData = newRawAppData;
            }

            // public string ExistsOrUnknown(string what)
            // {
            //     if (RawAppData.ContainsKey(what))
            //     {
            //         if (RawAppData[what].Trim().Length != 0)
            //
            //             return RawAppData[what];
            //     }
            //
            //     return "unknown";
            // }

            public async Task<bool> ExistsStrict()

                => await (new ApplicationList().ApplicationExists(this));



            public bool Exists
            {
                get =>
                    Process.GetProcesses().ToList().Exists((e) => e.Id == this.RawAppData.ProcessId);
                set =>
                    throw new NotImplementedException();
            }

            // public App(FrozenDictionary<string, string> applicationData)
            public App(IHyprClient applicationData)
            {
                this.RawAppData = applicationData;
                this.CurrentProcess = Process.GetProcessById(applicationData.ProcessId);
                this.BinaryPath = this.CurrentProcess.MainModule?.FileName ?? "";
            }
        };
    }
}
