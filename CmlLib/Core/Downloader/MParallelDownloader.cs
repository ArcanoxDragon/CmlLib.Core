using CmlLib.Core.Version;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CmlLib.Core.Downloader
{
    public class MParallelDownloader : MDownloader
    {
        public MParallelDownloader(MinecraftPath path, MVersion mVersion) : this(path, mVersion, 10, true)
        {

        }

        public MParallelDownloader(MinecraftPath path, MVersion mVersion, int maxThread, bool setConnectionLimit) : base(path, mVersion)
        {
            MaxThread = maxThread;

            if (setConnectionLimit)
                ServicePointManager.DefaultConnectionLimit = maxThread + 5;
        }

        public int MaxThread { get; private set; }

        public override async Task DownloadFilesAsync( DownloadFile[] files, CancellationToken cancellationToken = default )
		{
			await DownloadParallelAsync( files, MaxThread, cancellationToken );
		}

        public async Task DownloadParallelAsync(DownloadFile[] files, int parallelDegree, CancellationToken cancellationToken = default)
        {
			var filetype = MFile.Library;
			
			if (files.Length > 0)
				filetype = files[0].Type;

            var semaphore = new SemaphoreSlim(parallelDegree, parallelDegree);
            var progressed = 0;

            async Task doDownload(string path, string url)
			{
				await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

				using var webClient = new WebClient();

				cancellationToken.Register( webClient.CancelAsync );

                try
				{
					await webClient.DownloadFileTaskAsync( url, path );
				}
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
                finally
                {
                    Interlocked.Increment(ref progressed);

					fireDownloadFileChangedEvent(filetype, "", files.Length, progressed);

                    semaphore.Release();
                }
            }

            await Task.WhenAll(files.Select(file => doDownload( file.Path, file.Url )  ));
        }
    }
}
