using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace TabDirective
{
    public class TabDirectiveParser : IDisposable
    {
        private readonly IWpfTextView _textView;
        private readonly ITextDocument _document;
        private readonly DTE _dte;

        private string _directivePath;
        private FileSystemWatcher _directiveWatcher;

        public int TabSize { get; set; }
        public int IndentSize { get; set; }
        public string IndentStyle { get; set; }
        public bool InsertTabs { get; set; }

        public delegate void ChangedHandler(TabDirectiveParser sender, EventArgs data);
        public event ChangedHandler Change = (s, d) => { };

        private bool _ranOnce = false;

        public TabDirectiveParser(IWpfTextView textView, ITextDocument document, DTE dte)
        {
            this._document = document;
            this._textView = textView;
            this._dte = dte;
            if (this.SetDirectiveFile())
                this.SetWatcher();

        }

        private bool SetDirectiveFile()
        {
            this._directivePath = this.GetTabDirectivesFile();
            return !string.IsNullOrWhiteSpace(this._directivePath);
        }

        private void SetWatcher()
        {
            var info = new FileInfo(this._directivePath);
            if (this._directiveWatcher != null && this._directiveWatcher.Path == info.DirectoryName)
                return;
            if (this._directiveWatcher != null)
            {
                this._directiveWatcher.EnableRaisingEvents = false;
                this._directiveWatcher.Dispose();
                this._directiveWatcher = null;
            }

            this._directiveWatcher = new FileSystemWatcher();
            this._directiveWatcher.Path = info.DirectoryName;
            this._directiveWatcher.Filter = Path.GetFileName(this._directivePath);
            this._directiveWatcher.NotifyFilter =
                NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size;
            this._directiveWatcher.Changed += (s, e) => this.UpdateTabSettings();
            this._directiveWatcher.Deleted += (s, e) => this._directivePath = null;
            this._directiveWatcher.EnableRaisingEvents = true;
        }

        public void UpdateTabSettings()
        {
            if (string.IsNullOrWhiteSpace(this._directivePath))
            {
                if (this.SetDirectiveFile())
                    this.SetWatcher();
            }

            if (string.IsNullOrWhiteSpace(this._directivePath))
                return;


            var directives = this.ParseFileDirectives();
            if (directives == null || !directives.Any())
                return;

            
            var currentIndentSize = this.IndentSize;
            var currentTabSize = this.TabSize;
            var currentInsertTabs = this.InsertTabs;
            var currentIndentStyle = this.IndentStyle;

            var allLanguageProps = this._dte.Properties["TextEditor", "AllLanguages"];

            if (directives.ContainsKey("indentstyle"))
            {
                this.IndentStyle = directives["indentstyle"];
                switch (this.IndentStyle.ToLowerInvariant())
                {
                    case "smart":
                        allLanguageProps.Item("IndentStyle").Value = vsIndentStyle.vsIndentStyleSmart;
                        break;
                    case "none":
                        allLanguageProps.Item("IndentStyle").Value = vsIndentStyle.vsIndentStyleNone;
                        break;
                    default:
                        allLanguageProps.Item("IndentStyle").Value = vsIndentStyle.vsIndentStyleDefault;
                        break;
                }
            }
            if (directives.ContainsKey("tabsize"))
            {
                int tabSize;
                if (int.TryParse(directives["tabsize"], out tabSize))
                {
                    allLanguageProps.Item("TabSize").Value = tabSize;
                    this.TabSize = tabSize;
                }

            }
            if (directives.ContainsKey("indentsize"))
            {
                int indentSize;
                if (int.TryParse(directives["indentsize"], out indentSize))
                {
                    allLanguageProps.Item("IndentSize").Value = indentSize;
                    this.IndentSize = indentSize;
                }
            }
            if (directives.ContainsKey("inserttabs"))
            {
                bool insertTabs;
                if (bool.TryParse(directives["inserttabs"], out insertTabs))
                {
                    allLanguageProps.Item("InsertTabs").Value = insertTabs;
                    this.InsertTabs = insertTabs;
                }
            }


            if (_ranOnce && currentIndentSize != this.IndentSize
                || currentTabSize != this.TabSize
                || currentInsertTabs != this.InsertTabs)
                Change(this, null);
            else Change(this, null);
            
            _ranOnce = true;
        }

        private string GetTabDirectivesFile()
        {
            var filePath = _document.FilePath;

            EnvDTE.ProjectItem projectItem = _dte.Solution.FindProjectItem(_document.FilePath);
            var directivePath = string.Empty;
            while (string.IsNullOrEmpty(directivePath) && projectItem != null)
            {
                var fileName = projectItem.FileNames[0];
                directivePath = this.FindDirectiveFileInFolder(fileName);
                if (projectItem.Collection.Parent is Project)
                {
                    Project p = projectItem.Collection.Parent;
                    directivePath = this.FindDirectiveFileInFolder(p.FullName);
                    projectItem = p.ParentProjectItem;
                    continue;
                }
                projectItem = projectItem.Collection.Parent;
            }
            if (string.IsNullOrWhiteSpace(directivePath))
                directivePath = this.FindDirectiveFileInFolder(_dte.Solution.FullName);
            return directivePath;
        }

        private string FindDirectiveFileInFolder(string fileName)
        {
            var dirInfo = new FileInfo(fileName);
            var dir = dirInfo.DirectoryName;
            var directivesPath = Path.Combine(dir, "tab.directive");

            if (File.Exists(directivesPath))
                return directivesPath;
            return null;
        }

        private Dictionary<string, string> ParseFileDirectives()
        {
            var directives = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(this._directivePath))
                return directives;

            var inputFileContent = this.ReadAll();
            var regex = new Regex(@"\b(?<Key>\w+)\s*:\s*(?<Value>[~\\\/\w\.]+)\b", RegexOptions.ExplicitCapture);
            foreach (Match item in regex.Matches(inputFileContent))
            {
                var key = item.Groups["Key"].Value.ToLowerInvariant();
                var value = item.Groups["Value"].Value;
                directives.Add(key, value);
            }
            return directives;
        }

        private string ReadAll()
        {
            using (var fs = new FileStream(this._directivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var textReader = new StreamReader(fs))
            {
                return textReader.ReadToEnd();
            }
        }


        public void Dispose()
        {
            Dispose(true);
        }
        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this._directiveWatcher != null)
                {
                    this._directiveWatcher.Dispose();
                }
            }
        }       
    }
}
