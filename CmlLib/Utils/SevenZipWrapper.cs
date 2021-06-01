using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LzmaDecoder = SevenZip.Compression.LZMA.Decoder;

namespace CmlLib.Utils
{
	internal static class SevenZipWrapper
	{
		public static void DecompressFileLzma(string inFile, string outFile)
		{
			var decoder = new LzmaDecoder();
			var input = new FileStream(inFile, FileMode.Open);
			var output = new FileStream(outFile, FileMode.Create);

			// Read the decoder properties
			var properties = new byte[5];
			input.Read(properties, 0, 5);

			// Read in the decompress file size.
			var fileLengthBytes = new byte[8];
			input.Read(fileLengthBytes, 0, 8);

			var fileLength = BitConverter.ToInt64(fileLengthBytes, 0);

			decoder.SetDecoderProperties(properties);
			decoder.Code(input, output, input.Length, fileLength, null);

			output.Flush();
			output.Close();
		}

		public static async Task DecompressFileLzmaAsync(string inFile, string outFile, CancellationToken cancellationToken = default)
		{
			var decoder = new LzmaDecoder();
			var input = new FileStream(inFile, FileMode.Open);
			var output = new FileStream(outFile, FileMode.Create);

			// Read the decoder properties
			var properties = new byte[5];
			await input.ReadAsync(properties, 0, 5, cancellationToken);

			// Read in the decompress file size.
			var fileLengthBytes = new byte[8];
			await input.ReadAsync(fileLengthBytes, 0, 8, cancellationToken);

			var fileLength = BitConverter.ToInt64(fileLengthBytes, 0);

			decoder.SetDecoderProperties(properties);
			await Task.Run(() => decoder.Code(input, output, input.Length, fileLength, null), cancellationToken);

			cancellationToken.ThrowIfCancellationRequested();

			await output.FlushAsync(cancellationToken);
			output.Close();
		}
	}
}