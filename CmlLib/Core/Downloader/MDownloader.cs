using CmlLib.Core.Version;
using CmlLib.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CmlLib.Core.Downloader
{
    public class MDownloader
    {
        public event DownloadFileChangedHandler ChangeFile;
        public event ProgressChangedEventHandler ChangeProgress;

        public bool IgnoreInvalidFiles { get; set; } = true;
        public bool CheckHash { get; set; } = true;

        public MVersion DownloadVersion { get; set; }
        protected MinecraftPath MinecraftPath;

        public MDownloader(MinecraftPath downloadPath)
        {
            MinecraftPath = downloadPath;
        }

        public MDownloader(MinecraftPath downloadPath, MVersion _version)
        {
            DownloadVersion = _version;
            MinecraftPath = downloadPath;
        }

		/// <summary>
		/// Download All files that require to launch
		/// </summary>
		/// <param name="resource"></param>
		/// <param name="cancellationToken"></param>
		public async Task DownloadAllAsync(bool resource = true, CancellationToken cancellationToken = default)
        {
            if (DownloadVersion == null)
                throw new NullReferenceException("DownloadVersion was null");

            await this.DownloadLibrariesAsync(cancellationToken);

            if (resource)
            {
                DownloadIndex();
                await this.DownloadResourceAsync(cancellationToken);
            }

            await this.DownloadMinecraftAsync(cancellationToken);
        }

        public async Task DownloadLibrariesAsync(CancellationToken cancellationToken = default)
        {
            if (DownloadVersion == null)
                throw new NullReferenceException("DownloadVersion was null");

            await this.DownloadLibrariesAsync(DownloadVersion.Libraries, cancellationToken);
        }

        public async Task DownloadLibrariesAsync(MLibrary[] libraries, CancellationToken cancellationToken = default)
        {
            fireDownloadFileChangedEvent(MFile.Library, "", 0, 0);

            var files = from lib in libraries
                        where CheckDownloadRequireLibrary(lib)
                        select new DownloadFile(MFile.Library, lib.Name, Path.Combine(MinecraftPath.Library, lib.Path), lib.Url);

            await this.DownloadFilesAsync(files.Distinct().ToArray(), cancellationToken);
        }

        private bool CheckDownloadRequireLibrary(MLibrary lib)
        {
            return lib.IsRequire
                && !string.IsNullOrEmpty(lib.Path)
                && !string.IsNullOrEmpty(lib.Url)
                && !CheckFileValidation(Path.Combine(MinecraftPath.Library, lib.Path), lib.Hash);
        }

        /// <summary>
        /// Download index file
        /// </summary>
        public void DownloadIndex()
        {
            string path = MinecraftPath.GetIndexFilePath(DownloadVersion.AssetId);

            if (DownloadVersion.AssetUrl != "" && !CheckFileValidation(path, DownloadVersion.AssetHash))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                using (var wc = new WebClient())
                {
                    wc.DownloadFile(DownloadVersion.AssetUrl, path);
                }
            }
        }

        /// <summary>
        /// Download assets and copy to legacy or resources
        /// </summary>
        public async Task DownloadResourceAsync(CancellationToken cancellationToken = default)
        {
            var indexpath = MinecraftPath.GetIndexFilePath(DownloadVersion.AssetId);
            if (!File.Exists(indexpath)) return;

            var json  = File.ReadAllText(indexpath);
            var index = JObject.Parse(json);

            await this.DownloadResourceAsync(index, cancellationToken);
        }

        public async Task DownloadResourceAsync(JObject index, CancellationToken cancellationToken = default)
        {
            fireDownloadFileChangedEvent(MFile.Resource, DownloadVersion.AssetId, 0, 0);

            var isVirtual = checkJsonTrue(index["virtual"]); // check virtual
            var mapResource = checkJsonTrue(index["map_to_resources"]); // check map_to_resources

            var list = (JObject)index["objects"];
            var downloadRequiredFiles = new List<DownloadFile>();
            var copyRequiredFiles = new List<Tuple<string, string>>();

            int total = list.Count;
            int progressed = 0;
            foreach (var item in list)
            {
                JToken job = item.Value;

                // download hash resource
                var hash = job["hash"]?.ToString();
                var hashName = hash.Substring(0, 2) + "/" + hash;
                var hashPath = Path.Combine(MinecraftPath.GetAssetObjectPath(), hashName);

                if (!CheckFileValidation(hashPath, hash))
                {
                    var hashUrl = MojangServer.ResourceDownload + hashName;
                    downloadRequiredFiles.Add(new DownloadFile(MFile.Resource, item.Key, hashPath, hashUrl));
                }

                if (isVirtual)
                {
                    var resPath = Path.Combine(MinecraftPath.GetAssetLegacyPath(), item.Key);
                    copyRequiredFiles.Add(new Tuple<string, string>(hashPath, resPath));
                }

                if (mapResource)
                {
                    var resPath = Path.Combine(MinecraftPath.Resource, item.Key);
                    copyRequiredFiles.Add(new Tuple<string, string>(hashPath, resPath));
                }

                progressed++;
                fireDownloadFileChangedEvent(MFile.Resource, "", total, progressed);
            }

            await this.DownloadFilesAsync(downloadRequiredFiles.Distinct().ToArray(), cancellationToken);

            total = copyRequiredFiles.Count;
            progressed = 0;
            foreach (var item in copyRequiredFiles)
            {
                if (!File.Exists(item.Item2))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(item.Item2));
                    File.Copy(item.Item1, item.Item2, true);
                }

                progressed++;
                fireDownloadFileChangedEvent(MFile.Resource, "", total, progressed);
            }
        }

        bool checkJsonTrue(JToken j)
        {
            var str = j?.ToString()?.ToLower();
            if (str != null && str == "true")
                return true;
            else
                return false;
        }

        /// <summary>
        /// Download client jar
        /// </summary>
        public async Task DownloadMinecraftAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(DownloadVersion.ClientDownloadUrl)) return;

            string id = DownloadVersion.Jar;
            var path = MinecraftPath.GetVersionJarPath(id);

            if (!CheckFileValidation(path, DownloadVersion.ClientHash))
            {
                var file = new DownloadFile(MFile.Minecraft, id, path, DownloadVersion.ClientDownloadUrl);
                await this.DownloadFilesAsync(new DownloadFile[] { file }, cancellationToken);
            }

            fireDownloadFileChangedEvent(MFile.Minecraft, id, 1, 1);
        }

        private bool CheckFileValidation(string path, string hash)
        {
            return File.Exists(path) && CheckSHA1(path, hash);
        }

        private bool CheckFileValidation(string path, string hash, long size)
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length == size && CheckSHA1(path, hash);
        }

        private bool CheckSHA1(string path, string hash)
        {
            if (!CheckHash)
                return true;
            else
                return IOUtil.CheckSHA1(path, hash);
        }

        protected void fireDownloadFileChangedEvent(MFile file, string name, int totalFiles, int progressedFiles)
        {
            var e = new DownloadFileChangedEventArgs(file, name, totalFiles, progressedFiles);
            fireDownloadFileChangedEvent(e);
        }

        protected void fireDownloadFileChangedEvent(DownloadFileChangedEventArgs e)
        {
            ChangeFile?.Invoke(e);
        }

        private void fireDownloadProgressChangedEvent(object sender, ProgressChangedEventArgs e)
        {
            ChangeProgress?.Invoke(this, e);
        }

        public virtual async Task DownloadFilesAsync( DownloadFile[] files, CancellationToken cancellationToken = default )
		{
			using var webClient = new WebClient();

			cancellationToken.Register( webClient.CancelAsync );

			webClient.DownloadProgressChanged += fireDownloadProgressChangedEvent;

            var length = files.Length;
            if (length == 0)
                return;

            fireDownloadFileChangedEvent(files[0].Type, files[0].Name, length, 0);

            for (int i = 0; i < length; i++)
            {
                try
                {
                    var downloadFile = files[i];
                    Directory.CreateDirectory(Path.GetDirectoryName(downloadFile.Path));
                    await webClient.DownloadFileTaskAsync(downloadFile.Url, downloadFile.Path);
                    fireDownloadFileChangedEvent(downloadFile.Type, downloadFile.Name, length, i + 1);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (WebException ex)
                {
                    if (!IgnoreInvalidFiles)
                        throw new MDownloadFileException(ex.Message, ex, files[i]);
                }
            }
        }
    }
}
