namespace GitLink.Providers
{
    using GitTools.Git;
    using System.Text.RegularExpressions;

    public sealed class GitDaemonProvider : ProviderBase
    {
        private const string FileNamePlaceHolder = "{filename}";
        private const string RevisionPlaceHolder = "{revision}";
        private readonly Regex _regexUrl = new Regex(@"git://.+");

        private string _rawUrl;

        public GitDaemonProvider()
            : base(new GitPreparer())
        {
        }

        public override string RawGitUrl
        {
            get
            {
                return _rawUrl;
            }
        }

        public override bool Initialize(string url)
        {
            if (string.IsNullOrEmpty(url) || !_regexUrl.IsMatch(url) ||
               (!url.Contains(FileNamePlaceHolder) && !url.Contains(RevisionPlaceHolder)))
            {
                return false;
            }

            if (url.Contains(FileNamePlaceHolder))
            {
                _rawUrl = url.Replace(FileNamePlaceHolder, "%var2%");
            }

            if (url.Contains(RevisionPlaceHolder))
            {
                _rawUrl = _rawUrl.Replace(RevisionPlaceHolder, "{0}");
            }

            return true;
        }
    }
}
