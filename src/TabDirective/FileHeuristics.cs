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
    public class FileHeuristics
    {
        private readonly IWpfTextView _textView;
        private readonly ITextDocument _document;
        private readonly DTE _dte;

        public bool StartsWithSpace { get; private set; }
        public bool StartsWithTabs { get; private set; }
        public int GuessedIndentSize { get; private set; }

        public FileHeuristics(IWpfTextView textView, ITextDocument document, DTE dte)
        {
            this._document = document;
            this._textView = textView;
            this._dte = dte;
        }


        public void Parse()
        {
            ITextSnapshot snapshot = _textView.TextDataModel.DocumentBuffer.CurrentSnapshot;

            int tabSize = _textView.Options.GetOptionValue(DefaultOptions.TabSizeOptionId);

            bool startsWithSpaces = false;
            bool startsWithTabs = false;

            foreach (var line in snapshot.Lines)
            {
                if (line.Length > 0)
                {
                    char firstChar = line.Start.GetChar();
                    if (firstChar == '\t')
                        startsWithTabs = true;
                    else if (firstChar == ' ')
                    {
                        // We need to count to make sure there are enough spaces to go into a tab or a tab that follows the spaces
                        int countOfSpaces = 1;
                        for (int i = line.Start + 1; i < line.End; i++)
                        {
                            char ch = snapshot[i];
                            if (ch == ' ')
                            {
                                countOfSpaces++;
                                if (countOfSpaces >= tabSize)
                                {
                                    startsWithSpaces = true;
                                    break;
                                }
                            }
                            else if (ch == '\t')
                            {
                                startsWithSpaces = true;
                                break;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    if (startsWithSpaces && startsWithTabs)
                        break;
                }
            }
            this.StartsWithSpace = startsWithSpaces;
            this.StartsWithTabs = startsWithTabs;
            this.GuessedIndentSize = this.GuessCurrentIndentSize();
        }
        private int GuessCurrentIndentSize()
        {
            int linesSeen = 0, guessed = 0, previousLineSpaces = 0;
            bool guessNextLine = false;
            var guesses = new List<int>();

            using (ITextEdit edit = _textView.TextBuffer.CreateEdit())
            {
                foreach (var line in edit.Snapshot.Lines)
                {
                    if (linesSeen >= 400 || guessed >= 20)
                    {
                        //we have a big enough sample size so lets break incase someone opened 
                        //a 20.000 loc file :)
                        break;
                    }

                    if (guessNextLine)
                    {
                        if (line.Length <= 2 && line.GetText() == "")
                        {
                            //empty line skip to next line
                            continue;
                        }

                        var currentSpaces = this.SpacesOnLine(edit, line);
                        var guessIndentSize = currentSpaces - previousLineSpaces;
                        guesses.Add(guessIndentSize);
                        guessNextLine = false;
                        guessed++;
                    }
                    else
                    {
                        for (int i = line.End; i > line.Start; i--)
                        {
                            char ch = edit.Snapshot[i];
                            if (ch == '{')
                            {
                                guessNextLine = true;
                                previousLineSpaces = this.SpacesOnLine(edit, line);
                            }
                            else if (!new[] { '\t', ' ', '}', '\n', '\r' }.Contains(ch))
                                break;
                        }
                    }
                    linesSeen++;
                }
            }
            if (!guesses.Any())
                return 0;
            return guesses
                .GroupBy(g => g)
                .OrderBy(g => g.Count())
                .FirstOrDefault()
                .FirstOrDefault();
        }

        private int SpacesOnLine(ITextEdit edit, ITextSnapshotLine line)
        {
            int spaces = 0;
            for (int c = line.Start; c < line.End; c++)
            {
                if (edit.Snapshot[c] == ' ')
                    spaces++;
                else break;
            }
            return spaces;
        }



    }
}
