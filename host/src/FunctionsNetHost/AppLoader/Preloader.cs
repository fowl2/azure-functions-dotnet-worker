﻿namespace FunctionsNetHost
{
    internal class Preloader
    {
        internal static void Preload()
        {
            const string basePath = @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\6.0.14\";

            string[] arr = new[]
            {
                $@"{basePath}Microsoft.Extensions.DependencyInjection.dll",
                $@"{basePath}Microsoft.Extensions.Logging.dll",
                $@"{basePath}System.Collections.Concurrent.dll",
                $@"{basePath}coreclr.dll",
                $@"{basePath}System.Collections.dll",
                $@"{basePath}System.Collections.Immutable.dll",
                $@"{basePath}System.Diagnostics.Tracing.dll",
                $@"{basePath}System.Linq.dll",
                $@"{basePath}System.Net.Http.dll",
                $@"{basePath}System.Private.CoreLib.dll",
                $@"{basePath}System.Runtime.dll",
                $@"{basePath}System.Text.Json.dll",
                $@"{basePath}System.Threading.Channels.dll",
                $@"{basePath}System.Threading.dll",
                $@"{basePath}System.Threading.Thread.dll"
            };
            ReadRuntimeAssemblyFiles(arr);
        }
        private static void ReadRuntimeAssemblyFiles(string[] allFiles)
        {
            try
            {
                // Read File content in 4K chunks
                int maxBuffer = 4 * 1024;
                byte[] chunk = new byte[maxBuffer];
                Random random = new Random();
                foreach (string file in allFiles)
                {
                    // Read file content to avoid disk reads during specialization. This is only to page-in bytes.
                    ReadFileInChunks(file, chunk, maxBuffer, random);
                }
                Logger.Log($"Preloader.ReadRuntimeAssemblyFiles Total files read:{allFiles.Length}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error in Preloader.ReadRuntimeAssemblyFiles:{ex}");
            }
        }

        private static void ReadFileInChunks(string file, byte[] chunk, int maxBuffer, Random random)
        {
            var fileExist = File.Exists(file);
            Logger.Log($"Reading {file}. file exist:{fileExist}");

            if (!fileExist)
            {
                return;
            }

            try
            {
                using (FileStream fileStream = new FileStream(file, FileMode.Open, FileAccess.Read))
                {
                    int bytesRead;
                    while ((bytesRead = fileStream.Read(chunk, 0, maxBuffer)) != 0)
                    {
                        // Read one random byte for every 4K bytes - 4K is default OS page size. This will help avoid disk read during specialization
                        // see for details on OS page buffering in Windows - https://docs.microsoft.com/en-us/windows/win32/fileio/file-buffering
                        var randomByte = Convert.ToInt32(chunk[random.Next(0, bytesRead - 1)]);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to read file {file}. {ex}");
            }
        }
    }
}
