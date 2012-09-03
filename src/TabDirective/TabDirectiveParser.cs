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
    public class TabDirectiveParser
    {
        private readonly IWpfTextView _textView;
        private readonly ITextDocument _document;
        private readonly DTE _dte;

        public int TabSize { get; set; }
        public int IndentSize { get; set; }
        public string IndentStyle { get; set; }
        public bool InsertTabs { get; set; }

        public TabDirectiveParser(IWpfTextView textView, ITextDocument document, DTE dte)
        {
            this._document = document;
            this._textView = textView;
            this._dte = dte;
        }

        public void UpdateTabSettings()
        {
            var directivesFile = this.GetTabDirectivesFile();
            if (string.IsNullOrWhiteSpace(directivesFile))
                return;
            var directives = this.ParseFileDirectives(directivesFile);
            if (directives == null || !directives.Any())
                return;

            if (directives.ContainsKey("indentstyle"))
            {
                this.IndentStyle = directives["indentstyle"];
                switch (this.IndentStyle.ToLowerInvariant())
                {
                    case "smart":
                        this._dte.Properties["TextEditor", "AllLanguages"].Item("TabSize").Value = vsIndentStyle.vsIndentStyleSmart;
                        break;
                    case "none":
                        this._dte.Properties["TextEditor", "AllLanguages"].Item("TabSize").Value = vsIndentStyle.vsIndentStyleNone;
                        break;
                    default:
                        this._dte.Properties["TextEditor", "AllLanguages"].Item("TabSize").Value = vsIndentStyle.vsIndentStyleDefault;
                        break;
                }
            }
            if (directives.ContainsKey("tabsize"))
            {
                int tabSize;
                if (int.TryParse(directives["tabsize"], out tabSize))
                {
                    this._dte.Properties["TextEditor", "AllLanguages"].Item("TabSize").Value = tabSize;
                    this.TabSize = tabSize;
                }

            }
            if (directives.ContainsKey("indentsize"))
            {
                int indentSize;
                if (int.TryParse(directives["indentsize"], out indentSize))
                {
                    this._dte.Properties["TextEditor", "AllLanguages"].Item("IndentSize").Value = indentSize;
                    this.IndentSize = indentSize;
                }
            }
            if (directives.ContainsKey("inserttabs"))
            {
                bool insertTabs;
                if (bool.TryParse(directives["inserttabs"], out insertTabs))
                {
                    this._dte.Properties["TextEditor", "AllLanguages"].Item("InsertTabs").Value = insertTabs;
                    this.InsertTabs = insertTabs;
                }
            }
            _textView.VisualElement.UpdateLayout();
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

        private Dictionary<string, string> ParseFileDirectives(string filePath)
        {
            var directives = new Dictionary<string, string>();
            var inputFileContent = File.ReadAllText(filePath);
            var regex = new Regex(@"\b(?<Key>\w+)\s*:\s*(?<Value>[~\\\/\w\.]+)\b", RegexOptions.ExplicitCapture);
            foreach (Match item in regex.Matches(inputFileContent))
            {
                var key = item.Groups["Key"].Value.ToLowerInvariant();
                var value = item.Groups["Value"].Value;
                directives.Add(key, value);
            }
            return directives;
        }


    }
}
