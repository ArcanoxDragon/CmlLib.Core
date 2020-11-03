using CmlLib.Core;
using CmlLib.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Downloader;
using CmlLib.Core.Version;

namespace CmlLib.Core
{
    public class CMLauncher
    {
        public CMLauncher(string path)
        {
            this.MinecraftPath = new MinecraftPath(path);
        }

        public CMLauncher(MinecraftPath mc)
        {
            this.MinecraftPath = mc;
        }

        public event DownloadFileChangedHandler FileChanged;
        public event ProgressChangedEventHandler ProgressChanged;
        public event EventHandler<string> LogOutput;

        public MinecraftPath MinecraftPath { get; private set; }
        public MVersionCollection Versions { get; private set; }

        private void fire(MFile kind, string name, int total, int progressed)
        {
            FileChanged?.Invoke(new DownloadFileChangedEventArgs(kind, name, total, progressed));
        }

        private void fire(DownloadFileChangedEventArgs e)
        {
            FileChanged?.Invoke(e);
        }

        private void fire(int progress)
        {
            ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(progress, null));
        }

        public async Task<MVersionCollection> UpdateVersionsAsync(CancellationToken cancellationToken = default)
        {
            Versions = await new MVersionLoader(this.MinecraftPath).GetVersionMetadatasAsync(cancellationToken);
            return Versions;
        }

        public async Task<MVersionCollection> GetAllVersionsAsync(CancellationToken cancellationToken = default)
        {
            if (Versions == null)
                Versions = await this.UpdateVersionsAsync(cancellationToken);

            return Versions;
        }

        public async Task<MVersion> GetVersionAsync(string versionname, CancellationToken cancellationToken = default)
        {
            if (Versions == null)
                await UpdateVersionsAsync(cancellationToken);

            return Versions.GetVersion(versionname);
        }

        public Task<string> CheckJREAsync(CancellationToken cancellationToken = default)
        {
            fire(MFile.Runtime, "java", 1, 0);

            var mjava = new MJava(MinecraftPath.Runtime);
            mjava.ProgressChanged += (sender, e) => fire(e.ProgressPercentage);
            mjava.DownloadCompleted += (sender, e) =>
            {
                fire(MFile.Runtime, "java", 1, 1);
            };

            return mjava.CheckJavaAsync(cancellationToken: cancellationToken);
        }

        public async Task<string> CheckForgeAsync(string mcversion, string forgeversion, string java, CancellationToken cancellationToken = default)
        {
            if (Versions == null)
                await UpdateVersionsAsync(cancellationToken);

            var forgeNameOld = MForge.GetOldForgeName(mcversion, forgeversion);
            var forgeName = MForge.GetForgeName(mcversion, forgeversion);

            var exist = false;
            var name = "";
            foreach (var item in Versions)
            {
                if (item.Name == forgeName)
                {
                    exist = true;
                    name = forgeName;
                    break;
                }
                else if (item.Name == forgeNameOld)
                {
                    exist = true;
                    name = forgeNameOld;
                    break;
                }
            }

            if (!exist)
            {
                var mforge = new MForge(MinecraftPath, java);
                mforge.FileChanged += (e) => fire(e);
                mforge.InstallerOutput += (s, e) => LogOutput?.Invoke(this, e);
                name = await mforge.InstallForgeAsync(mcversion, forgeversion, cancellationToken);

                await this.UpdateVersionsAsync(cancellationToken);
            }

            return name;
        }

        public async Task CheckGameFilesAsync(MVersion version, bool downloadAsset = true, bool checkFileHash = true, CancellationToken cancellationToken = default)
        {
            var downloader = new MDownloader(MinecraftPath, version);
            await downloadGameFilesAsync(downloader, downloadAsset, checkFileHash, cancellationToken);
        }

        public async Task CheckGameFilesParallelAsync(MVersion version, bool downloadAsset = true, bool checkFileHash = true, CancellationToken cancellationToken = default)
        {
            var downloader = new MParallelDownloader(MinecraftPath, version);
            await downloadGameFilesAsync(downloader, downloadAsset, checkFileHash, cancellationToken);
        }

        private async Task downloadGameFilesAsync(MDownloader downloader, bool downloadAsset, bool checkFileHash, CancellationToken cancellationToken = default)
        {
            downloader.CheckHash = checkFileHash;
            downloader.ChangeFile += (e) => fire(e);
            downloader.ChangeProgress += (sender, e) => fire(e.ProgressPercentage);
            
			await downloader.DownloadAllAsync(downloadAsset, cancellationToken);
        }

        public async Task<Process> CreateProcessAsync(string mcversion, string forgeversion, MLaunchOption option, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(option.JavaPath))
                option.JavaPath = await this.CheckJREAsync(cancellationToken);

            await this.CheckGameFilesAsync(await this.GetVersionAsync(mcversion, cancellationToken), false, cancellationToken: cancellationToken);

            var versionName = await CheckForgeAsync(mcversion, forgeversion, option.JavaPath, cancellationToken);
            await this.UpdateVersionsAsync(cancellationToken);

            return await this.CreateProcessAsync(versionName, option, cancellationToken);
        }

        public async Task<Process> CreateProcessAsync(string versionname, MLaunchOption option, CancellationToken cancellationToken = default)
        {
            option.StartVersion = await this.GetVersionAsync(versionname, cancellationToken);
            await CheckGameFilesAsync(option.StartVersion, cancellationToken: cancellationToken);
            return await this.CreateProcessAsync(option, cancellationToken);
        }

        public async Task<Process> CreateProcessAsync(MLaunchOption option, CancellationToken cancellationToken = default)
        {
            if (option.Path == null)
                option.Path = MinecraftPath;

            if (string.IsNullOrEmpty(option.JavaPath))
                option.JavaPath = await this.CheckJREAsync(cancellationToken);

            var launch = new MLaunch(option);
            return launch.GetProcess();
        }
    }
}
