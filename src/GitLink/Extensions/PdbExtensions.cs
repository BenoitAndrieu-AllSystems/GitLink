// --------------------------------------------------------------------------------------------------------------------
// <copyright file="PdbExtensions.cs" company="CatenaLogic">
//   Copyright (c) 2014 - 2014 CatenaLogic. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace GitLink
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Catel;
    using Pdb;
    using System.IO;

    public static class PdbExtensions
    {
        public static Dictionary<string, string> VerifyPdbFiles(this PdbFile pdbFile, IEnumerable<string> files)
        {
            Argument.IsNotNull(() => pdbFile);

            var missing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var actualFileChecksums = files == null ? null : (from x in files
                                                              select new KeyValuePair<string, string>(Hex.Encode(Crypto.GetMd5HashForFiles(new[] { x }).First().Item1), x)).ToDictionary(x => x.Value, x => x.Key);

            foreach (var checksumInfo in pdbFile.GetChecksums())
            {
                var file = checksumInfo.Key;
                var checksum = checksumInfo.Value;

                if (actualFileChecksums == null || !actualFileChecksums.ContainsValue(checksum))
                {
                    if (file.EndsWith(".xaml"))
                    {
                        // #64 ignore xaml files, not important
                        continue;
                    }

                    if (GetProperFilePathCapitalization(file, out string path) == false)
                    {
                        continue;
                    }

                    missing[path] = checksum;
                }
            }

            return missing;
        }

        public static bool GetProperDirectoryCapitalization(DirectoryInfo dirInfo, out string path)
        {
            DirectoryInfo parentDirInfo = dirInfo.Parent;
            if (null == parentDirInfo)
            {
                path = dirInfo.Name;
                if (path.Length == 3 && path[1] == ':')
                {
                    path = path.ToLowerInvariant();
                }

                return true;
            }

            DirectoryInfo[] dirs;
            try
            {
                dirs = parentDirInfo.GetDirectories(dirInfo.Name);
            }
            catch (IOException)
            {
                path = null;
                return false;
            }

            if (GetProperDirectoryCapitalization(parentDirInfo, out string parentPath) == false)
            {
                path = null;
                return false;
            }

            if (dirs.Length == 0)
            {
                path = null;
                return false;
            }

            path = Path.Combine(parentPath, dirs[0].Name);
            return true;
        }

        public static bool GetProperFilePathCapitalization(string filename, out string path)
        {
            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(filename);
            }
            catch (NotSupportedException)
            {
                path = null;
                return false;
            }
            catch (ArgumentException)
            {
                path = null;
                return false;
            }

            DirectoryInfo dirInfo = fileInfo.Directory;
            if (GetProperDirectoryCapitalization(dirInfo, out string dirpath) == false)
            {
                path = null;
                return false;
            }

            var files = dirInfo.GetFiles(fileInfo.Name);
            if (files.Length == 0)
            {
                path = null;
                return false;
            }

            path = Path.Combine(dirpath, files[0].Name);
            return true;
        }

        public static Dictionary<string, string> GetChecksums(this PdbFile pdbFile)
        {
            Argument.IsNotNull(() => pdbFile);

            var checksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in pdbFile.GetFiles())
            {
                var hexed = file.Item2 == null ? null : Hex.Encode(file.Item2);
                string item2;
                if (checksums.TryGetValue(file.Item1, out item2) == true)
                {
                    if (item2 != hexed)
                    {
                        throw new Exception();
                    }

                    continue;
                }

                checksums.Add(file.Item1, hexed);
            }

            return checksums;
        }

        public static IEnumerable<Tuple<string, byte[]>> GetFiles(this PdbFile pdbFile)
        {
            Argument.IsNotNull(() => pdbFile);

            if (pdbFile.SrcToolFiles != null)
            {
                return new List<Tuple<string, byte[]>>(pdbFile.SrcToolFiles);
            }

            var results = new List<Tuple<string, byte[]>>();

            //const int LastInterestingByte = 47;
            const string FileIndicator = "/src/files/";

            var values = pdbFile.Info.NameToPdbName.Values;

            foreach (var value in values)
            {
                if (!value.Name.Contains(FileIndicator))
                {
                    continue;
                }

                var num = value.Stream;
                var name = value.Name.Substring(FileIndicator.Length);

                var bytes = pdbFile.ReadStreamBytes(num);
                if (bytes.Length != 88)
                {
                    continue;
                }

                // Get last 16 bytes for checksum
                var buffer = new byte[16];
                for (int i = 72; i < 88; i++)
                {
                    buffer[i - 72] = bytes[i];
                }

                results.Add(new Tuple<string, byte[]>(name, buffer));
            }

            return results;
        }
    }
}