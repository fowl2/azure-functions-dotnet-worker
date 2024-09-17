﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Transactions;
using FunctionsNetHost.Prelaunch;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using Newtonsoft.Json.Linq;

namespace FunctionsNetHost
{
    internal class Program
    {
        private const string WindowsExecutableName = "func.exe";
        private const string LinuxExecutableName = "func";
        private const string InProc8DirectoryName = "in-proc8";
        private const string InProc6DirectoryName = "in-proc6";
        static async Task Main(string[] args)
        {
            try
            {
                Logger.Log("Starting FunctionsNetHost");

                PreLauncher.Run();

                using var appLoader = new AppLoader();

                var workerRuntime = EnvironmentUtils.GetValue(EnvironmentVariables.FunctionsWorkerRuntime)!;
                if (string.IsNullOrEmpty(workerRuntime))
                {
                    Logger.Log($"Environment variable '{EnvironmentVariables.FunctionsWorkerRuntime}' is not set.");
                    return;
                }
                if (workerRuntime == "dotnet")
                {
                    // Start child process for .NET 8 in proc host
                    if (string.Equals("1", EnvironmentUtils.GetValue(EnvironmentVariables.FunctionsInProcNet8Enabled)))
                    {
                        await LoadHostAssembly(isOutOfProc: false, isNet8InProc: true);
                    }
                    else
                    {
                        // Start child process for .NET 6 in proc host
                        await LoadHostAssembly(isOutOfProc: false, isNet8InProc: false);
                    }

                }
                else if (workerRuntime == "dotnet-isolated")
                {
                    // Start process for oop host
                    await LoadHostAssembly(isOutOfProc: true, isNet8InProc: false);
                }
            }
            catch (Exception exception)
            {
                Logger.Log($"An error occurred while running FunctionsNetHost.{exception}");
            }
        }

        private static Task LoadHostAssembly(bool isOutOfProc, bool isNet8InProc)
        {
            var commandLineArguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
            var tcs = new TaskCompletionSource();

            var rootDirectory = GetFunctionAppRootDirectory(Environment.CurrentDirectory, new[] { "Azure.Functions.Cli" });
            var coreToolsDirectory = Path.Combine(rootDirectory, "Azure.Functions.Cli");

            var executableName = LinuxExecutableName;

            var fileName = isNet8InProc ? Path.Combine(coreToolsDirectory, InProc8DirectoryName, executableName): Path.Combine(coreToolsDirectory, InProc8DirectoryName, executableName);

            if (isOutOfProc)
            {
                fileName = Path.Combine(coreToolsDirectory, executableName);
            }
            Assembly assembly = Assembly.LoadFrom(fileName);
            Logger.Log("Loaded Assembly: " + assembly.FullName);

            return tcs.Task;
        }

        private static string GetFunctionAppRootDirectory(string startingDirectory, IEnumerable<string> searchDirectories)
        {
            if (searchDirectories.Any(file => Directory.Exists(Path.Combine(startingDirectory, file))))
            {
                return startingDirectory;
            }

            var parent = Path.GetDirectoryName(startingDirectory);

            if (parent == null)
            {
                var files = searchDirectories.Aggregate((accum, file) => $"{accum}, {file}");
                throw new ($"Unable to find project root. Expecting to find one of {files} in project root.");
            }
            else
            {
                return GetFunctionAppRootDirectory(parent, searchDirectories);
            }
        }
    }
}
