using CmlLib.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CmlLib.Core.Downloader
{
    public class MJava
    {
        public static string DefaultRuntimeDirectory = Path.Combine(MinecraftPath.GetOSDefaultPath(), "runtime");

        public event ProgressChangedEventHandler ProgressChanged;
        public event EventHandler DownloadCompleted;
        public string RuntimeDirectory { get; private set; }

        public MJava() : this(DefaultRuntimeDirectory) { }

        public MJava(string runtimePath)
        {
            RuntimeDirectory = runtimePath;
        }

        public Task<string> CheckJavaAsync(CancellationToken cancellationToken = default)
        {
            var binaryName = "java";
            if (MRule.OSName == MRule.Windows)
                binaryName = "javaw.exe";

            return CheckJavaAsync(binaryName, cancellationToken);
        }

        public async Task<string> CheckJavaAsync(string binaryName, CancellationToken cancellationToken = default)
        {
            var javapath = Path.Combine(RuntimeDirectory, "bin", binaryName);

            if (!File.Exists(javapath))
            {
                string json = "";

                var       javaUrl = "";
				using var wc      = new WebClient();

				cancellationToken.Register(wc.CancelAsync);

                json = await wc.DownloadStringTaskAsync(MojangServer.LauncherMeta);
                cancellationToken.ThrowIfCancellationRequested();

                var job = JObject.Parse(json)[MRule.OSName];
                javaUrl = job[MRule.Arch]?["jre"]?["url"]?.ToString();

                if (string.IsNullOrEmpty(javaUrl))
                    throw new PlatformNotSupportedException("Downloading JRE on current OS is not supported. Set JavaPath manually.");

                Directory.CreateDirectory(RuntimeDirectory);

                var lzmapath = Path.Combine(Path.GetTempPath(), "jre.lzma");
                var zippath = Path.Combine(Path.GetTempPath(), "jre.zip");

                await wc.DownloadFileTaskAsync(new Uri(javaUrl), lzmapath);
				cancellationToken.ThrowIfCancellationRequested();

                DownloadCompleted?.Invoke(this, new EventArgs());

				await SevenZipWrapper.DecompressFileLzmaAsync( lzmapath, zippath, cancellationToken );

                await Task.Run(() => {
                    cancellationToken.ThrowIfCancellationRequested();

	                var z = new SharpZip(zippath);
	                z.ProgressEvent += Z_ProgressEvent;
	                z.Unzip(RuntimeDirectory);

                    cancellationToken.ThrowIfCancellationRequested();

	                if (!File.Exists(javapath))
	                    throw new Exception("Failed Download");

	                if (MRule.OSName != MRule.Windows)
	                    IOUtil.Chmod(javapath, IOUtil.Chmod755);
				}, cancellationToken );
            }

            return javapath;
        }

        private void Z_ProgressEvent(object sender, int e)
        {
            ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(e, null));
        }

        private void Szip_ProgressChange(object sender, ProgressChangedEventArgs e)
        {
            ProgressChanged?.Invoke(this, e);
        }

        private void Downloader_DownloadProgressChangedEvent(object sender, ProgressChangedEventArgs e)
        {
            ProgressChanged?.Invoke(this, e);
        }
    }
}
