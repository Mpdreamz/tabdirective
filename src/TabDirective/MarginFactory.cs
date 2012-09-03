﻿using System.ComponentModel.Composition;
using System.Diagnostics;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace TabDirective
{
    #region InformationBar Factory
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(InformationBarMargin.MarginName)]
    [MarginContainer(PredefinedMarginNames.Top)]
    [ContentType("any")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class MarginFactory : IWpfTextViewMarginProvider
    {
        [Import]
        ITextDocumentFactoryService TextDocumentFactoryService = null;

        [Import]
        IEditorOperationsFactoryService OperationsFactory = null;

        [Import]
        ITextUndoHistoryRegistry UndoHistoryRegistry = null;

        [Import]
        internal SVsServiceProvider serviceProvider = null;

        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost textViewHost, IWpfTextViewMargin containerMargin)
        {
            IWpfTextView view = textViewHost.TextView;

            ITextDocument document;
            if (!TextDocumentFactoryService.TryGetTextDocument(view.TextDataModel.DocumentBuffer, out document))
                return null;


            ITextUndoHistory history;
            if (!UndoHistoryRegistry.TryGetHistory(view.TextBuffer, out history))
            {
                Debug.Fail("Unexpected: couldn't get an undo history for the given text buffer");
                return null;
            }
            var dte = (DTE)serviceProvider.GetService(typeof(DTE));
            return new InformationBarMargin(view, document, OperationsFactory.GetEditorOperations(view), history, dte);
        }
    }
    #endregion
}
