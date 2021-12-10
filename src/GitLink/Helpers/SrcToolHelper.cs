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
    using System.IO;
    using System.Linq;

    public static class SrcToolHelper
    {
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        public static IEnumerable<Tuple<string, byte[]>> Execute(string srcToolFileName, string projectPdbFile, string srcToolMask)
        {
            Argument.IsNotNullOrWhitespace(() => projectPdbFile);

            string arguments = string.Format("\"{0}\" -r", projectPdbFile);
            if (string.IsNullOrWhiteSpace(srcToolMask) == false)
            {
                arguments += string.Format(" \"-l:{0}\"", srcToolMask.Replace(Path.DirectorySeparatorChar.ToString(), Path.DirectorySeparatorChar + Path.DirectorySeparatorChar.ToString()));
            }

            var processStartInfo = new ProcessStartInfo(srcToolFileName)
            {
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default
            };

            var outputStringBuilder = new List<string>();
            var errorStringBuilder = new List<string>();

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
            process.ErrorDataReceived += (sender, eventArgs) =>
                {
                    if (eventArgs.Data == null)
                    {
                        return;
                    }

                    byte[] encBytes = encoding.GetBytes(eventArgs.Data);
                    byte[] utf8Bytes = Encoding.Convert(encoding, Encoding.UTF8, encBytes);

                    errorStringBuilder.Add(Encoding.UTF8.GetString(utf8Bytes));
                };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (errorStringBuilder.Count == 1)
            {
                if (errorStringBuilder.First().ToLowerInvariant().StartsWith("no source information in pdb for "))
                {
                    Log.Warning("no source information in pdb for '{0}'", projectPdbFile);
                    return new List<Tuple<string, byte[]>>();
                }
            }

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