// --------------------------------------------------------------------------------------------------------------------
// <copyright file="PdbStrHelper.cs" company="CatenaLogic">
//   Copyright (c) 2014 - 2014 CatenaLogic. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace GitLink
{
    using System.Diagnostics;
    using Catel;
    using Catel.Logging;
    using System.Text;
    using System.Collections.Generic;
    using System;

    public static class SrcToolHelper
    {
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        public static IEnumerable<Tuple<string, byte[]>> Execute(string srcToolFileName, string projectPdbFile)
        {
            Argument.IsNotNullOrWhitespace(() => projectPdbFile);

            var processStartInfo = new ProcessStartInfo(srcToolFileName)
            {
                Arguments = string.Format("\"{0}\" -r", projectPdbFile),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.Default
            };

            var outputStringBuilder = new List<string>();

            var encoding = processStartInfo.StandardOutputEncoding;

            var process = new Process();
            process.StartInfo = processStartInfo;
            process.EnableRaisingEvents = false;
            process.OutputDataReceived +=
                (sender, eventArgs) =>
                {
                    if (eventArgs.Data == null)
                    {
                        return;
                    }

                    byte[] encBytes = encoding.GetBytes(eventArgs.Data);
                    byte[] utf8Bytes = Encoding.Convert(encoding, Encoding.UTF8, encBytes);

                    outputStringBuilder.Add(Encoding.UTF8.GetString(utf8Bytes));
                };

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();

            var processExitCode = process.ExitCode;
            if (processExitCode >= 0)
            {
                var files = new List<Tuple<string, byte[]>>();
                foreach (var it in outputStringBuilder)
                {
                    if (string.IsNullOrEmpty(it) || PdbExtensions.GetProperFilePathCapitalization(it, out string path) == false)
                    {
                        continue;
                    }

                    files.Add(new Tuple<string, byte[]>(path, null));
                }

                return files;
            }

            throw Log.ErrorAndCreateException<GitLinkException>("SrcTool exited with unexpected error code '{0}'", processExitCode);
        }
    }
}