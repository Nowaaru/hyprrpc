using System.Text;
using Spectre.Console;

/*
 * TODO: tbh i like the idea of a logging system
 * that has:
 *   * scopes (warning/info/error/silly)
 *   * severities (minor/major/critical)
 *   * groups (combat/logger/transport)
 * and a logviewer that sorts in groups the order of 
 * (scopes/severities/groups) to keep
 * everything sorted in order.
 *  
 * maybe revisit this in a diff lang later?
 */

namespace HyprRPC
{
    namespace Logging
    {
        public enum Severity
        {
            CRITICAL, // uh-oh!
            MAJOR,
            MINOR,


            UNIMPORTANT,
        };

        enum Scope
        {
            ERROR,
            WARNING,
            INFO,

            SILLY,
        }

        enum LogType
        {
            LATEST,
            OLD
        }

        public class Logger
        {
            public static string ERROR_COLOR = "#E46A78";
            public static string WARNING_COLOR = "#E5C283";
            public static string INFO_COLOR = "#FEFEFE";
            public static string SILLY_COLOR = "#7EA2";

            public bool DisableLogToTty = false;

            private string GenerateLogName(string? version, DateTime? time)
            {
                //hyprRPC-v0.00.0-latest.log
                if (time is not null)

                    return $"hyprRPC-v{version ?? HyprRPCli.Version}-{time.Value.ToString("s")}";

                return $"hyprRPC-v{version ?? HyprRPCli.Version}-latest.log";
            }

            public readonly FileStream CurrentLogFile;

            public FileStream MakeLatestLogFile(string location)
            {
                string pathToLatest = Path.Combine(location, this.GenerateLogName(null, null));
                if (File.Exists(pathToLatest))
                {
                    // TODO: condense after everything works
                    string oldFile = Path.GetFileNameWithoutExtension(pathToLatest);
                    string[] oldFileNameSplit = oldFile.Split("-");
                    string oldVersionString = oldFileNameSplit[1].Substring(1);
                    string targetDir = Path.Combine(location, $"{this.GenerateLogName(oldVersionString, File.GetCreationTime(pathToLatest))}.log");

                    this.Write(Scope.INFO, Severity.MINOR, $"found old log file in {pathToLatest}. moving to {targetDir}...", false);
                    File.Move(pathToLatest, targetDir);
                }

                return File.Create(pathToLatest);
            }

            public Logger(string logDirectory)
            {
                this.CurrentLogFile = this.MakeLatestLogFile(logDirectory);
            }

            private string Tag(string tag, string str)
                => $"[{tag}]{str}[/]";

            private string Colourize(string str, string color)
                => this.Tag(color, str);

            private void Write(Scope scope, Severity severity, string content, bool forwardToOutFile = true)
            {
                List<string> outList = new();

                switch (scope)
                {
                    case (Scope.ERROR):
                        outList.Add($"[[{this.Tag("underline", this.Colourize("WARNING", Logger.WARNING_COLOR))}]]");
                        break;

                    case (Scope.WARNING):
                        outList.Add($"[[{this.Tag("underline", this.Colourize("WARNING", Logger.WARNING_COLOR))}]]");
                        break;

                    case (Scope.SILLY):
                        outList.Add($"[[{this.Tag("underline", this.Colourize("SILLY", Logger.SILLY_COLOR))}]]");
                        break;

                    default:
                        outList.Add($"[[{this.Tag("underline", this.Colourize("INFO", Logger.INFO_COLOR))}]]");
                        break;
                }

                switch (severity)
                {
                    case (Severity.CRITICAL):
                        outList.Add($"[[!!!]]");
                        break;
                    case (Severity.MAJOR):
                        outList.Add($"[[!!]]");
                        break;
                    case (Severity.MINOR):
                        outList.Add($"[[!]]");
                        break;
                    default:
                        break;
                }

                string joinedContent = $"{String.Join(" ", outList.ToArray())} {content}\n";
                if (forwardToOutFile)
                {
                    byte[] stringAsBytes = new UTF8Encoding(true).GetBytes(Markup.Remove(joinedContent));
                    CurrentLogFile.Write(stringAsBytes);
                }

                if (!this.DisableLogToTty)

                    AnsiConsole.Markup(joinedContent);
            }

            public void Log(string data, Severity severity = Severity.MINOR)
            {
                this.Write(Scope.INFO, severity, data);
            }

            public void Warn(string data, Severity severity = Severity.MINOR)
            {
                this.Write(Scope.WARNING, severity, data);
            }

            public void Error(string data, Severity severity = Severity.MAJOR)
            {
                this.Write(Scope.ERROR, severity, data);
            }

            public void Kill()
            {
                this.CurrentLogFile.Close();
            }

            ~Logger()
            {
                this.Log("killing logger.");
                this.Kill();
            }
        }
    }
}

