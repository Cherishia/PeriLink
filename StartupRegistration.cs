using System;
using System.IO;
using Microsoft.Win32;

namespace LinkScope
{
    internal static class StartupRegistration
    {
        private const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name="LinkScope";
        internal const string Argument="--startup";

        private static string CurrentCommand
        {get {return CommandFor(typeof(StartupRegistration).Assembly.Location);}}

        internal static string CommandFor(string executable)
        {return "\"" + Path.GetFullPath(executable) + "\" " + Argument;}

        internal static bool IsEnabled
        {
            get
            {
                try
                {
                    using(var run=Registry.CurrentUser.OpenSubKey(Key))
                        return run!=null&&string.Equals(run.GetValue(Name) as string,CurrentCommand,StringComparison.OrdinalIgnoreCase);
                }
                catch(System.Security.SecurityException) {return false;}
                catch(UnauthorizedAccessException) {return false;}
                catch(IOException) {return false;}
            }
        }

        internal static bool SetEnabled(bool enabled)
        {
            try
            {
                using(var run=enabled?Registry.CurrentUser.CreateSubKey(Key):Registry.CurrentUser.OpenSubKey(Key,true))
                {
                    if(run==null) return !enabled;
                    if(enabled)
                    {
                        var command=CurrentCommand;
                        if(command.Length>260) return false;
                        run.SetValue(Name,command,RegistryValueKind.String);
                    }
                    else if(string.Equals(run.GetValue(Name) as string,CurrentCommand,StringComparison.OrdinalIgnoreCase))
                        run.DeleteValue(Name,false);
                    return true;
                }
            }
            catch(System.Security.SecurityException) {return false;}
            catch(UnauthorizedAccessException) {return false;}
            catch(IOException) {return false;}
        }
    }
}
