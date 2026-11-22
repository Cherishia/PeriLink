using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace LinkScope
{
    internal sealed class AppUpdate
    {
        internal readonly Version Version;
        internal readonly Uri Download;
        internal readonly string Digest;
        internal readonly int Size;

        internal AppUpdate(Version version, Uri download, string digest, int size)
        { Version=version;Download=download;Digest=digest;Size=size; }
    }

    internal static class AppUpdates
    {
        private const string StagingName="PeriLink.update";
        private const string WorkerName="updater.exe";
        private const string PayloadName="next.exe";
        private const string BackupName="previous.exe";
        private const int DownloadLimit=16*1024*1024;
        private const string ReadyPrefix=@"Local\PeriLink.UpdateReady.";
        private static readonly HttpClient Client=CreateClient();
        internal static readonly Version Current=typeof(AppUpdates).Assembly.GetName().Version;

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var client=new HttpClient { Timeout=TimeSpan.FromSeconds(45),MaxResponseContentBufferSize=DownloadLimit };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PeriLink");
            return client;
        }

        internal static async Task<AppUpdate> Check()
        {
            string json=await Client.GetStringAsync("https://api.github.com/repos/Cherishia/PeriLink/releases/latest").ConfigureAwait(false);
            return ReadRelease(json,Current);
        }

        internal static AppUpdate ReadRelease(string json,Version installed)
        {
            var release=new JavaScriptSerializer().Deserialize<Release>(json);
            Version version;
            if(release==null || release.draft || release.prerelease ||
                !Regex.IsMatch(release.tag_name??"",@"^v\d+\.\d+\.\d+$") ||
                !Version.TryParse(release.tag_name.Substring(1)+".0",out version) || version<=installed) return null;
            string url=Dashboard.RepositoryUrl+"/releases/download/"+release.tag_name+"/PeriLink.exe";
            var asset=(release.assets??new Asset[0]).SingleOrDefault(a=>a.name=="PeriLink.exe");
            if(asset==null || asset.state!="uploaded" || asset.browser_download_url!=url ||
                asset.size<=0 || asset.size>DownloadLimit || !Regex.IsMatch(asset.digest??"",@"^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("发布文件或校验值不完整。");
            return new AppUpdate(version,new Uri(url),asset.digest.Substring(7),asset.size);
        }

        internal static async Task Stage(AppUpdate update)
        {
            string target=Assembly.GetEntryAssembly().Location;
            string staging=Path.Combine(Path.GetDirectoryName(target),StagingName);
            byte[] payload=await Client.GetByteArrayAsync(update.Download).ConfigureAwait(false);
            if(payload.Length!=update.Size) throw new InvalidDataException("下载大小不正确。");
            Directory.CreateDirectory(staging);
            string staged=Path.Combine(staging,PayloadName);
            File.WriteAllBytes(staged,payload);
            Verify(staged,update.Version,update.Digest);
            File.Copy(target,Path.Combine(staging,WorkerName),true);
        }

        internal static void StartWorker(AppUpdate update,bool trayOnly)
        {
            string target=Assembly.GetEntryAssembly().Location;
            string staging=Path.Combine(Path.GetDirectoryName(target),StagingName);
            var worker=new ProcessStartInfo(Path.Combine(staging,WorkerName))
            {
                Arguments="--apply-update "+Process.GetCurrentProcess().Id+" \""+Path.GetFileName(target)+"\" "+update.Version+" "+update.Digest+
                    (trayOnly?" --startup":""),
                UseShellExecute=false, CreateNoWindow=true, WorkingDirectory=staging
            };
            using(var process=Process.Start(worker))
                if(process==null) throw new InvalidOperationException("无法启动更新程序。");
        }

        internal static void Apply(string[] args)
        {
            string staging=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            int pid;
            Version version;
            if(args.Length<5 || args.Length>6 || !int.TryParse(args[1],out pid) || pid<=0 ||
                args[2]!=Path.GetFileName(args[2]) || !args[2].EndsWith(".exe",StringComparison.OrdinalIgnoreCase) ||
                !Version.TryParse(args[3],out version) || version<=Current ||
                !Regex.IsMatch(args[4],@"^[a-fA-F0-9]{64}$") ||
                !string.Equals(Path.GetFileName(staging),StagingName,StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(Assembly.GetEntryAssembly().Location)!=WorkerName) return;
            string target=Path.Combine(Path.GetDirectoryName(staging),args[2]);
            string staged=Path.Combine(staging,PayloadName);
            string backup=Path.Combine(staging,BackupName);
            bool trayOnly=args.Length==6 && args[5]==StartupRegistration.Argument;
            bool replaced=false;
            try
            {
                WaitForCurrent(pid,target);
                Verify(staged,version,args[4]);
                File.Replace(staged,target,backup);
                replaced=true;
                StartUpdated(target,trayOnly);
            }
            catch(Exception e)
            {
                string message="更新未完成。"+e.Message;
                try
                {
                    if(replaced) File.Replace(backup,target,null);
                    using(var process=Start(target,trayOnly,"")) {}
                }
                catch(Exception restartError) { message+="\n"+restartError.Message; }
                MessageBox.Show(message,"PeriLink · 更新",MessageBoxButton.OK,MessageBoxImage.Warning);
            }
        }

        private static void WaitForCurrent(int pid,string target)
        {
            Process running;
            try { running=Process.GetProcessById(pid); }
            catch(ArgumentException) { return; }
            using(running)
            {
                if(running.HasExited) return;
                string executable;
                try { executable=running.MainModule.FileName; }
                catch(System.ComponentModel.Win32Exception) { if(running.HasExited) return; throw; }
                if(!string.Equals(executable,target,StringComparison.OrdinalIgnoreCase) || !running.WaitForExit(20000))
                    throw new InvalidOperationException("正在运行的 PeriLink 尚未退出。");
            }
        }

        private static void Verify(string path,Version expected,string digest)
        {
            string actual;
            using(var stream=File.OpenRead(path))
            using(var hash=SHA256.Create())
                actual=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");
            if(!string.Equals(actual,digest,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("新版文件校验失败。");
            var file=AssemblyName.GetAssemblyName(path);
            if(file.Name!="PeriLink" || file.Version!=expected)
                throw new InvalidDataException("新版文件与发布版本不一致。");
        }

        private static Process Start(string target,bool trayOnly,string extra)
        {
            var start=new ProcessStartInfo(target)
            {
                Arguments=(trayOnly?StartupRegistration.Argument+" ":"")+extra,
                UseShellExecute=false, WorkingDirectory=Path.GetDirectoryName(target)
            };
            return Process.Start(start);
        }

        private static void StartUpdated(string target,bool trayOnly)
        {
            string token=Guid.NewGuid().ToString("N");
            using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,ReadyPrefix+token))
            using(var process=Start(target,trayOnly,"--update-ready "+token))
            {
                if(process==null) throw new InvalidOperationException("无法启动新版 PeriLink。");
                var timeout=Stopwatch.StartNew();
                while(timeout.ElapsedMilliseconds<15000)
                {
                    if(ready.WaitOne(100)) return;
                    if(process.HasExited) break;
                }
                if(!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }
                throw new InvalidOperationException("新版 PeriLink 未能启动，已尝试恢复上一版本。");
            }
        }

        internal static void SignalReady(string[] args)
        {
            int flag=Array.IndexOf(args,"--update-ready");
            Guid token;
            if(flag<0 || flag+1>=args.Length || !Guid.TryParseExact(args[flag+1],"N",out token)) return;
            try
            {
                using(var ready=EventWaitHandle.OpenExisting(ReadyPrefix+token.ToString("N"))) ready.Set();
            }
            catch(WaitHandleCannotBeOpenedException) {}
        }

        private sealed class Release
        {
            public string tag_name {get;set;}
            public bool draft {get;set;}
            public bool prerelease {get;set;}
            public Asset[] assets {get;set;}
        }

        private sealed class Asset
        {
            public string name {get;set;}
            public string state {get;set;}
            public string browser_download_url {get;set;}
            public string digest {get;set;}
            public int size {get;set;}
        }
    }
}
