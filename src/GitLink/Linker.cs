// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Linker.cs" company="CatenaLogic">
//   Copyright (c) 2014 - 2014 CatenaLogic. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace GitLink
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using Catel;
    using Catel.Logging;
    using GitTools;
    using Microsoft.Build.Evaluation;
    using Pdb;
    using Providers;

    /// <summary>
    /// Class Linker.
    /// </summary>
    public static class Linker
    {
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        public static int Link(Context context)
        {
            int? exitCode = null;

            var stopWatch = new Stopwatch();
            stopWatch.Start();

            context.ValidateContext();

            if (!string.IsNullOrEmpty(context.LogFile))
            {
                var fileLogListener = new FileLogListener(context.LogFile, 25 * 1024);
                fileLogListener.IsDebugEnabled = context.IsDebug;

                fileLogListener.IgnoreCatelLogging = true;
                LogManager.AddListener(fileLogListener);
            }

            using (var temporaryFilesContext = new TemporaryFilesContext())
            {
                Log.Info("Extracting embedded pdbstr.exe");

                var pdbStrFile = temporaryFilesContext.GetFile("pdbstr.exe");
                ResourceHelper.ExtractEmbeddedResource("GitLink.Resources.Files.pdbstr.exe", pdbStrFile);

                Log.Info("Extracting embedded srctool.exe");

                var srctoolFile = temporaryFilesContext.GetFile("srctool.exe");
                ResourceHelper.ExtractEmbeddedResource("GitLink.Resources.Files.srctool.exe", srctoolFile);

                try
                {
                    if (context.NoSolutionFile == true)
                    {
                        if (string.IsNullOrEmpty(context.PdbFilesDirectory) == true)
                        {
                            Log.Error("PdbFilesDirectory not defined");
                            return -1;
                        }

                        var provider = context.Provider;
                        if (provider == null)
                        {
                            throw Log.ErrorAndCreateException<GitLinkException>("Cannot find a matching provider for '{0}'", context.TargetUrl);
                        }

                        Log.Info("Using provider '{0}'", provider.GetType().Name);

                        var shaHash = context.Provider.GetShaHashOfCurrentBranch(context, temporaryFilesContext);

                        Log.Info("Using commit sha '{0}' as version stamp", shaHash);

                        var pdbFiles = Directory.GetFiles(context.PdbFilesDirectory, "*.pdb", SearchOption.AllDirectories).ToList();

                        var failedPdbs = new HashSet<string>();
                        foreach (var pdbFile in pdbFiles)
                        {
                            try
                            {
                                if (!LinkPdb(pdbFile, context, pdbStrFile, srctoolFile, shaHash, context.PdbFilesDirectory))
                                {
                                    failedPdbs.Add(pdbFile);
                                }
                            }
                            catch
                            {
                                failedPdbs.Add(pdbFile);
                            }
                        }

                        Log.Info("All pdbs are done. {0} of {1} succeeded", pdbFiles.Count - failedPdbs.Count, pdbFiles.Count);

                        if (failedPdbs.Count > 0)
                        {
                            Log.Info(string.Empty);
                            Log.Info("The following pdbs have failed:");
                            Log.Indent();

                            foreach (var failedPdb in failedPdbs)
                            {
                                Log.Info("* {0}", context.GetRelativePath(failedPdb));
                            }

                            Log.Unindent();
                        }

                        exitCode = (failedPdbs.Count == 0) ? 0 : -1;
                    }
                    else
                    {
                        var projects = new List<Project>();
                        string[] solutionFiles;
                        if (string.IsNullOrEmpty(context.SolutionFile))
                        {
                            solutionFiles = Directory.GetFiles(context.SolutionDirectory, "*.sln", SearchOption.AllDirectories);
                        }
                        else
                        {
                            var pathToSolutionFile = Path.Combine(context.SolutionDirectory, context.SolutionFile);
                            if (!File.Exists(pathToSolutionFile))
                            {
                                Log.Error("Could not find solution file: {0}", pathToSolutionFile);
                                return -1;
                            }

                            solutionFiles = new[] { pathToSolutionFile };
                        }

                        foreach (var solutionFile in solutionFiles)
                        {
                            var solutionProjects = ProjectHelper.GetProjects(solutionFile, context.ConfigurationName, context.PlatformName);
                            projects.AddRange(solutionProjects);
                        }

                        var provider = context.Provider;
                        if (provider == null)
                        {
                            throw Log.ErrorAndCreateException<GitLinkException>("Cannot find a matching provider for '{0}'", context.TargetUrl);
                        }

                        Log.Info("Using provider '{0}'", provider.GetType().Name);

                        var shaHash = context.Provider.GetShaHashOfCurrentBranch(context, temporaryFilesContext);

                        Log.Info("Using commit sha '{0}' as version stamp", shaHash);

                        var projectCount = projects.Count();
                        var failedProjects = new List<Project>();
                        Log.Info("Found '{0}' project(s)", projectCount);
                        Log.Info(string.Empty);

                        foreach (var project in projects)
                        {
                            try
                            {
                                var projectName = project.GetProjectName();
                                if (ProjectHelper.ShouldBeIgnored(projectName, context.IncludedProjects, context.IgnoredProjects))
                                {
                                    Log.Info("Ignoring '{0}'", project.GetProjectName());
                                    Log.Info(string.Empty);
                                    continue;
                                }

                                if (context.IsDebug)
                                {
                                    project.DumpProperties();
                                }

                                if (!LinkProject(context, project, pdbStrFile, shaHash, context.PdbFilesDirectory))
                                {
                                    failedProjects.Add(project);
                                }
                            }
                            catch (Exception)
                            {
                                failedProjects.Add(project);
                            }
                        }

                        Log.Info("All projects are done. {0} of {1} succeeded", projectCount - failedProjects.Count, projectCount);

                        if (failedProjects.Count > 0)
                        {
                            Log.Info(string.Empty);
                            Log.Info("The following projects have failed:");
                            Log.Indent();

                            foreach (var failedProject in failedProjects)
                            {
                                Log.Info("* {0}", context.GetRelativePath(failedProject.GetProjectName()));
                            }

                            Log.Unindent();
                        }

                        exitCode = (failedProjects.Count == 0) ? 0 : -1;
                    }
                }
                catch (GitLinkException ex)
                {
                    Log.Error(ex, "An error occurred");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "An unexpected error occurred");
                }

                stopWatch.Stop();
            }

            Log.Info(string.Empty);
            Log.Info("Completed in '{0}'", stopWatch.Elapsed);

            exitCode = exitCode ?? -1;

            if (context.ErrorsAsWarnings && exitCode != 0)
            {
                Log.Info("One or more errors occurred, but treating it as warning instead");

                exitCode = 0;
            }

            return exitCode.Value;
        }

        private static bool LinkPdb(string projectPdbFile, Context context, string pdbStrFile, string srctoolFile, string shaHash, string pdbFilesDirectory)
        {
            Argument.IsNotNull(() => context);

            try
            {
                Log.Info("Handling pdb '{0}'", projectPdbFile);

                var projectSrcSrvFile = projectPdbFile + ".srcsrv";

                var srcSrvContext = new SrcSrvContext
                {
                    Revision = shaHash,
                    RawUrl = context.Provider.RawGitUrl,
                    DownloadWithPowershell = context.DownloadWithPowershell
                };

                var missingFiles = ProjectExtensions.VerifyPdbFiles(null, projectPdbFile, srctoolFile);

                if (!srcSrvContext.RawUrl.Contains("%var2%") && !srcSrvContext.RawUrl.Contains("{0}"))
                {
                    srcSrvContext.RawUrl = string.Format("{0}/{{0}}/%var2%", srcSrvContext.RawUrl);
                }

                foreach (var compilable in missingFiles)
                {
                    string relative = compilable.Key.Replace(context.SolutionDirectory, string.Empty);
                    var relativePathForUrl = ReplaceSlashes(context.Provider, relative);
                    while (relativePathForUrl.StartsWith("/"))
                    {
                        relativePathForUrl = relativePathForUrl.Substring(1, relativePathForUrl.Length - 1);
                    }

                    srcSrvContext.Paths.Add(new Tuple<string, string>(compilable.Key, relativePathForUrl));
                }

                // When using the VisualStudioTeamServicesProvider, add extra infomration to dictionary with VSTS-specific data
                if (context.Provider.GetType().Name.EqualsIgnoreCase("VisualStudioTeamServicesProvider"))
                {
                    srcSrvContext.VstsData["TFS_COLLECTION"] = context.Provider.CompanyUrl;
                    srcSrvContext.VstsData["TFS_TEAM_PROJECT"] = context.Provider.ProjectName;
                    srcSrvContext.VstsData["TFS_REPO"] = context.Provider.ProjectUrl;
                }

                ProjectExtensions.CreateSrcSrv(projectSrcSrvFile, srcSrvContext);

                Log.Debug("Created source server link file, updating pdb file '{0}'", context.GetRelativePath(projectPdbFile));

                PdbStrHelper.Execute(pdbStrFile, projectPdbFile, projectSrcSrvFile);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "An error occurred while processing pdb '{0}'", projectPdbFile);
                throw;
            }
            finally
            {
                Log.Unindent();
                Log.Info(string.Empty);
            }

            return true;
        }

        private static bool LinkProject(Context context, Project project, string pdbStrFile, string shaHash, string pathPdbDirectory = null)
        {
            Argument.IsNotNull(() => context);
            Argument.IsNotNull(() => project);

            try
            {
                var projectName = project.GetProjectName();

                Log.Info("Handling project '{0}'", projectName);

                Log.Indent();

                var compilables = project.GetCompilableItems().Select(x => x.GetFullFileName()).ToList();

                var outputPdbFile = project.GetOutputPdbFile();
                var projectPdbFile = pathPdbDirectory != null ? Path.Combine(pathPdbDirectory, Path.GetFileName(outputPdbFile)) : Path.GetFullPath(outputPdbFile);
                var projectSrcSrvFile = projectPdbFile + ".srcsrv";

                var srcSrvContext = new SrcSrvContext
                {
                    Revision = shaHash,
                    RawUrl = context.Provider.RawGitUrl,
                    DownloadWithPowershell = context.DownloadWithPowershell
                };

                if (!File.Exists(projectPdbFile))
                {
                    Log.Warning("No pdb file found for '{0}', is project built in '{1}' mode with pdb files enabled? Expected file is '{2}'", projectName, context.ConfigurationName, projectPdbFile);
                    return false;
                }

                if (!context.SkipVerify)
                {
                    Log.Info("Verifying pdb file");

                    var missingFiles = ProjectExtensions.VerifyPdbFiles(compilables, projectPdbFile, null);
                    foreach (var missingFile in missingFiles)
                    {
                        Log.Warning("Missing file '{0}' or checksum '{1}' did not match", missingFile.Key, missingFile.Value);
                    }
                }

                if (!srcSrvContext.RawUrl.Contains("%var2%") && !srcSrvContext.RawUrl.Contains("{0}"))
                {
                    srcSrvContext.RawUrl = string.Format("{0}/{{0}}/%var2%", srcSrvContext.RawUrl);
                }

                foreach (var compilable in compilables)
                {
                    var relativePathForUrl = ReplaceSlashes(context.Provider, compilable.Replace(context.SolutionDirectory, string.Empty));
                    while (relativePathForUrl.StartsWith("/"))
                    {
                        relativePathForUrl = relativePathForUrl.Substring(1, relativePathForUrl.Length - 1);
                    }

                    srcSrvContext.Paths.Add(new Tuple<string, string>(compilable, relativePathForUrl));
                }

                // When using the VisualStudioTeamServicesProvider, add extra infomration to dictionary with VSTS-specific data
                if (context.Provider.GetType().Name.EqualsIgnoreCase("VisualStudioTeamServicesProvider"))
                {
                    srcSrvContext.VstsData["TFS_COLLECTION"] = context.Provider.CompanyUrl;
                    srcSrvContext.VstsData["TFS_TEAM_PROJECT"] = context.Provider.ProjectName;
                    srcSrvContext.VstsData["TFS_REPO"] = context.Provider.ProjectUrl;
                }

                ProjectExtensions.CreateSrcSrv(projectSrcSrvFile, srcSrvContext);

                Log.Debug("Created source server link file, updating pdb file '{0}'", context.GetRelativePath(projectPdbFile));

                PdbStrHelper.Execute(pdbStrFile, projectPdbFile, projectSrcSrvFile);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "An error occurred while processing project '{0}'", project.GetProjectName());
                throw;
            }
            finally
            {
                Log.Unindent();
                Log.Info(string.Empty);
            }

            return true;
        }

        private static string ReplaceSlashes(IProvider provider, string relativePathForUrl)
        {
            bool isBackSlashSupported = false;

            // Check if provider is capable of determining whether to use back slashes or forward slashes.
            var backSlashSupport = provider as IBackSlashSupport;
            if (backSlashSupport != null)
            {
                isBackSlashSupported = backSlashSupport.IsBackSlashSupported;
            }

            if (isBackSlashSupported)
            {
                relativePathForUrl = relativePathForUrl.Replace("/", "\\");
            }
            else
            {
                relativePathForUrl = relativePathForUrl.Replace("\\", "/");
            }

            return relativePathForUrl;
        }
    }
}