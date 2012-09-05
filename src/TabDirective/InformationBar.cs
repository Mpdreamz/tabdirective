using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using EnvDTE;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace TabDirective
{
    sealed class InformationBarMargin : ContentControl, IWpfTextViewMargin
    {
        public const string MarginName = "InformationBar";
        private IWpfTextView _textView;
        private ITextDocument _document;
        private IEditorOperations _operations;
        private ITextUndoHistory _undoHistory;
        private DTE _dte;

        private bool _isDisposed = false;

        bool _dontShowAgain = false;

        private readonly TabDirectiveParser _tabDirectiveParser;
        private readonly FileHeuristics _fileHeuristics;
        private readonly InformationBarControl _informationBarControl;

        public InformationBarMargin(IWpfTextView textView, ITextDocument document, IEditorOperations editorOperations, ITextUndoHistory undoHistory, DTE dte)
        {
            _textView = textView;
            _document = document;
            _operations = editorOperations;
            _undoHistory = undoHistory;
            _dte = dte;

            _informationBarControl = new InformationBarControl();
            _informationBarControl.Hide.Click += Hide;
            _informationBarControl.DontShowAgain.Click += DontShowAgain;
            var format = new Action(() => this.FormatDocument());
            _informationBarControl.Tabify.Click += (s, e) => this.Dispatcher.Invoke(format);

            this.Height = 0;
            this.Content = _informationBarControl;
            this.Name = MarginName;

            document.FileActionOccurred += FileActionOccurred;
            textView.Closed += TextViewClosed;

            // Delay the initial check until the view gets focus
            textView.GotAggregateFocus += GotAggregateFocus;

            this._tabDirectiveParser = new TabDirectiveParser(textView, document, dte);
            this._fileHeuristics = new FileHeuristics(textView, document, dte);

            var fix = new Action(() => this.FixFile());
            this._tabDirectiveParser.Change += (s, e) => this.Dispatcher.Invoke(fix);
        }

        void DisableInformationBar()
        {
            _dontShowAgain = true;
            this.CloseInformationBar();

            if (_document != null)
            {
                _document.FileActionOccurred -= FileActionOccurred;
                _document = null;
            }

            if (_textView != null)
            {
                _textView.GotAggregateFocus -= GotAggregateFocus;
                _textView.Closed -= TextViewClosed;
                _textView = null;
            }
        }


        void FixFile()
        {
            var message = string.Empty;
            var fh = this._fileHeuristics;
            var tp = this._tabDirectiveParser;
            this._fileHeuristics.Parse();
            if (fh.StartsWithSpace && fh.StartsWithTabs)
                message = "This file contains mixed tabs and spaces";
            else if (fh.StartsWithSpace && tp.InsertTabs)
                message = "This file seems to be using spaces while the tabdirective mandates tabs";
            else if (fh.StartsWithTabs && !tp.InsertTabs)
                message = "This file seems to be using tabs while the tabdirective mandates spaces of indentsize: " + tp.IndentSize;
            else if (fh.StartsWithSpace && fh.GuessedIndentSize != tp.IndentSize)
                message = string.Format("This file seems to be using indentsize: {0} while the tabdirective mandates: {1}",
                    fh.GuessedIndentSize, tp.IndentSize);

            if (string.IsNullOrWhiteSpace(message))
                return;

            _informationBarControl.Message.Text = message;
            this.ShowInformationBar();
        }
        void FormatDocument()
        {
            PerformActionInUndo(() =>
            {
                _textView.VisualElement.Focus();
                _dte.ExecuteCommand("Edit.FormatDocument");
                this.CloseInformationBar();
                return true;
            });
        }

        #region Event Handlers

        void FileActionOccurred(object sender, TextDocumentFileActionEventArgs e)
        {
            if (_dontShowAgain)
                return;

            if ((e.FileActionType & FileActionTypes.ContentLoadedFromDisk) != 0 ||
                (e.FileActionType & FileActionTypes.ContentSavedToDisk) != 0)
            {
                this._tabDirectiveParser.UpdateTabSettings();
            }
        }

        void GotAggregateFocus(object sender, EventArgs e)
        {
            _textView.GotAggregateFocus -= GotAggregateFocus;

            this._tabDirectiveParser.UpdateTabSettings();
        }

        void TextViewClosed(object sender, EventArgs e)
        {
            DisableInformationBar();
        }

        #endregion

        #region Hiding and showing the information bar

        void Hide(object sender, RoutedEventArgs e)
        {
            this.CloseInformationBar();
        }

        void DontShowAgain(object sender, RoutedEventArgs e)
        {
            this.DisableInformationBar();
        }

        void CloseInformationBar()
        {
            if (this.Height == 0 || _dontShowAgain)
                return;

            // Since we're going to be closing, make sure focus is back in the editor
            _textView.VisualElement.Focus();

            ChangeHeightTo(0);
        }

        void ShowInformationBar()
        {
            if (this.Height > 0 || _dontShowAgain)
                return;

            ChangeHeightTo(27);
        }

        void ChangeHeightTo(double newHeight)
        {
            if (_dontShowAgain)
                return;

            if (_textView.Options.GetOptionValue(DefaultWpfViewOptions.EnableSimpleGraphicsId))
            {
                this.Height = newHeight;
            }
            else
            {
                DoubleAnimation animation = new DoubleAnimation(this.Height, newHeight, new Duration(TimeSpan.FromMilliseconds(175)));
                Storyboard.SetTarget(animation, this);
                Storyboard.SetTargetProperty(animation, new PropertyPath(StackPanel.HeightProperty));

                Storyboard storyboard = new Storyboard();
                storyboard.Children.Add(animation);

                storyboard.Begin(this);
            }
        }

        #endregion

     
        #region Performing Tabify and Untabify

        void PerformActionInUndo(Func<bool> action)
        {
            ITrackingPoint anchor = _textView.TextSnapshot.CreateTrackingPoint(_textView.Selection.AnchorPoint.Position, PointTrackingMode.Positive);
            ITrackingPoint active = _textView.TextSnapshot.CreateTrackingPoint(_textView.Selection.ActivePoint.Position, PointTrackingMode.Positive);
            bool empty = _textView.Selection.IsEmpty;
            TextSelectionMode mode = _textView.Selection.Mode;

            using (var undo = _undoHistory.CreateTransaction("Untabify"))
            {
                _operations.AddBeforeTextBufferChangePrimitive();

                if (!action())
                {
                    undo.Cancel();
                    return;
                }

                ITextSnapshot after = _textView.TextSnapshot;

                _operations.SelectAndMoveCaret(new VirtualSnapshotPoint(anchor.GetPoint(after)),
                                               new VirtualSnapshotPoint(active.GetPoint(after)),
                                               mode,
                                               EnsureSpanVisibleOptions.ShowStart);

                _operations.AddAfterTextBufferChangePrimitive();

                undo.Complete();
            }

        }

        #endregion

        #region IWpfTextViewMargin Members

        public FrameworkElement VisualElement
        {
            get
            {
                return this;
            }
        }

        #endregion

        #region ITextViewMargin Members

        public double MarginSize
        {
            get
            {
                return this.ActualHeight;
            }
        }

        public bool Enabled
        {
            get
            {
                return !_dontShowAgain;
            }
        }

        public ITextViewMargin GetTextViewMargin(string marginName)
        {
            return (marginName == InformationBarMargin.MarginName) ? (IWpfTextViewMargin)this : null;
        }

        public void Dispose()
        {
            this._tabDirectiveParser.Dispose();
            this.DisableInformationBar();
        }

        #endregion
    }
}
