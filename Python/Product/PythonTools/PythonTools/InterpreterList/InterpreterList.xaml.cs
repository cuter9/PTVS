﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Commands;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Options;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Repl;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.PythonTools.InterpreterList {
#if INTERACTIVE_WINDOW
    using IReplWindow = IInteractiveWindow;
    using IReplEvaluator = IInteractiveEngine;
#endif
    internal partial class InterpreterList : UserControl {
        readonly AnalyzerStatusListener _listener;
        readonly List<InterpreterView> _interpreters;
        readonly DispatcherTimer _refreshTimer;
        readonly IInterpreterOptionsService _interpService;

        public static readonly RoutedCommand RefreshCommand = new RoutedUICommand("_Refresh", "Refresh", typeof(InterpreterList));
        public static readonly RoutedCommand RegenerateCommand = new RoutedUICommand("Refresh", "Regenerate", typeof(InterpreterList));
        public static readonly RoutedCommand AbortCommand = new RoutedUICommand("Abort", "Abort", typeof(InterpreterList));
        public static readonly RoutedCommand OpenReplCommand = new RoutedUICommand("Interactive Window", "OpenInteractive", typeof(InterpreterList));
        public static readonly RoutedCommand OpenOptionsCommand = new RoutedUICommand("Options", "OpenOptions", typeof(InterpreterList));
        public static readonly RoutedCommand OpenReplOptionsCommand = new RoutedUICommand("Interactive Options", "OpenInteractiveOptions", typeof(InterpreterList));
        public static readonly RoutedCommand OpenPathCommand = new RoutedUICommand("View in File Explorer", "OpenPath", typeof(InterpreterList));
        public static readonly RoutedCommand MakeDefaultCommand = new RoutedUICommand("Make Default", "MakeDefault", typeof(InterpreterList));
        public static readonly RoutedCommand CopyReasonCommand = new RoutedUICommand("Copy", "CopyReason", typeof(InterpreterList));
        public static readonly RoutedCommand WebChooseInterpreterCommand = new RoutedUICommand("Help me choose an interpreter", "WebChooseInterpreter", typeof(InterpreterList));

        public InterpreterList(IInterpreterOptionsService service) {
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(500.0);
            _refreshTimer.Tick += AutoRefresh_Elapsed;
            _interpService = service;

            _interpreters = InterpreterView.GetInterpreters(_interpService).ToList();
            _interpService.InterpretersChanged += InterpretersChanged;
            _interpService.DefaultInterpreterChanged += DefaultInterpreterChanged;

            foreach (var interp in _interpreters) {
                interp.PropertyChanged += View_PropertyChanged;
            }
            Interpreters = new ObservableCollection<InterpreterView>(_interpreters);
            DataContext = this;

            _listener = new AnalyzerStatusListener(Update);
            InitializeComponent();

            _refreshTimer.Start();
        }

        private void View_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            CommandManager.InvalidateRequerySuggested();
        }

        void InterpretersChanged(object sender, EventArgs e) {
            if (Dispatcher.CheckAccess()) {
                lock (_interpreters) {
                    _interpreters.Clear();
                    Interpreters.Clear();
                    foreach (var interp in InterpreterView.GetInterpreters(_interpService)) {
                        _interpreters.Add(interp);
                        Interpreters.Add(interp);
                    }
                }
                // The column only resizes when the value changes. Since it's
                // currently set to NaN, we need to set it to something else
                // first. Setting it to ActualWidth results in no visible
                // effects to the user, since the column is already ActualWidth
                // wide.
                nameColumn.Width = nameColumn.ActualWidth;
                nameColumn.Width = double.NaN;
            } else {
                Dispatcher.BeginInvoke((Action)(() => InterpretersChanged(sender, e)));
            }
        }

        void DefaultInterpreterChanged(object sender, EventArgs e) {
            InterpreterView[] interps;
            lock (_interpreters) {
                interps = _interpreters.ToArray();
            }
            foreach (var interp in interps) {
                interp.DefaultInterpreterUpdate(_interpService.DefaultInterpreter);
            }
        }

        private void AutoRefresh_Elapsed(object sender, EventArgs e) {
            Debug.Assert(Dispatcher.CheckAccess());
            _listener.ThrowPendingExceptions();
            _listener.RequestUpdate();
        }

        public ObservableCollection<InterpreterView> Interpreters {
            get { return (ObservableCollection<InterpreterView>)GetValue(InterpretersProperty); }
            private set { SetValue(InterpretersPropertyKey, value); }
        }

        private static readonly DependencyPropertyKey InterpretersPropertyKey = DependencyProperty.RegisterReadOnly("Interpreters", typeof(ObservableCollection<InterpreterView>), typeof(InterpreterList), new PropertyMetadata());
        public static readonly DependencyProperty InterpretersProperty = InterpretersPropertyKey.DependencyProperty;


        private void Abort_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = false;

            // TODO: Uncomment when analysis can be aborted
            //var interp = e.Parameter as InterpreterView;
            //e.CanExecute = interp != null && interp.IsRunning;
        }

        private void Abort_Executed(object sender, ExecutedRoutedEventArgs e) {
            // TODO: Uncomment when analysis can be aborted
            //((InterpreterView)e.Parameter).Abort();
        }

        private void Refresh_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            try {
                _listener.ThrowPendingExceptions();
                e.CanExecute = true;
            } catch (Exception ex) {
                Debug.WriteLine(ex.ToString());
                e.CanExecute = false;
            }
        }

        private void Refresh_Executed(object sender, ExecutedRoutedEventArgs e) {
            _listener.RequestUpdate();
        }

        internal void Update(Dictionary<string, AnalysisProgress> data) {
            InterpreterView[] interps;
            lock (_interpreters) {
                interps = _interpreters.ToArray();
            }
            foreach (var interp in interps) {
                interp.ProgressUpdate(data);
            }
        }


        private void Regenerate_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var interp = e.Parameter as InterpreterView;
            e.CanExecute = interp != null && interp.CanRefresh && !interp.IsRunning;
        }

        private void Regenerate_Executed(object sender, ExecutedRoutedEventArgs e) {
            ((InterpreterView)e.Parameter).Start();
        }

        private void OpenRepl_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var view = e.Parameter as InterpreterView;
            e.CanExecute = view != null && view.Interpreter != null &&
                !string.IsNullOrEmpty(view.Interpreter.Configuration.InterpreterPath) &&
                File.Exists(view.Interpreter.Configuration.InterpreterPath);
        }

        private void OpenRepl_Executed(object sender, ExecutedRoutedEventArgs e) {
            var interp = ((InterpreterView)e.Parameter).Interpreter;
            var window = ExecuteInReplCommand.EnsureReplWindow(interp) as ToolWindowPane;
            if (window != null) {
                IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
                ErrorHandler.ThrowOnFailure(windowFrame.Show());
                ((IReplWindow)window).Focus();
            }
        }


        private void OpenWindow_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var view = e.Parameter as InterpreterView;
            e.CanExecute = e.Parameter == null || (view != null && view.Interpreter != null);
        }

        private void OpenWindow_Executed(object sender, ExecutedRoutedEventArgs e) {
            IPythonInterpreterFactory interp = null;
            if (e.Parameter != null) {
                interp = ((InterpreterView)e.Parameter).Interpreter;
            }
            if (e.Command == OpenOptionsCommand) {
                PythonToolsPackage.Instance.ShowOptionPage(typeof(PythonInterpreterOptionsPage), interp);

            } else if (e.Command == OpenReplOptionsCommand) {
                PythonToolsPackage.Instance.ShowOptionPage(typeof(PythonInteractiveOptionsPage), interp);
            }
        }

        private void MakeDefault_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var view = e.Parameter as InterpreterView;
            e.CanExecute = view != null && view.Interpreter != null && !view.IsDefault;
        }

        private void MakeDefault_Executed(object sender, ExecutedRoutedEventArgs e) {
            var view = (InterpreterView)e.Parameter;
            _interpService.DefaultInterpreter = view.Interpreter;
        }

        private void CopyReason_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var view = e.Parameter as InterpreterView;
            e.CanExecute = view != null && view.Interpreter is IInterpreterWithCompletionDatabase && !view.IsCurrent;
        }

        private void CopyReason_Executed(object sender, ExecutedRoutedEventArgs e) {
            var withDb = (IInterpreterWithCompletionDatabase)((InterpreterView)e.Parameter).Interpreter;
            Clipboard.SetText(withDb.GetIsCurrentReason(CultureInfo.CurrentUICulture));
        }

        private void OpenPath_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var view = e.Parameter as InterpreterView;
            e.CanExecute = view != null && view.Interpreter != null && !string.IsNullOrEmpty(view.Interpreter.Configuration.InterpreterPath) &&
                File.Exists(view.Interpreter.Configuration.InterpreterPath);
        }

        private void OpenPath_Executed(object sender, ExecutedRoutedEventArgs e) {
            var path = Path.GetDirectoryName(((InterpreterView)e.Parameter).Interpreter.Configuration.InterpreterPath);
            Process.Start(new ProcessStartInfo {
                FileName = path,
                Verb = "open",
                UseShellExecute = true
            });
        }

        private void WebChooseInterpreter_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = true;
        }

        private void WebChooseInterpreter_Executed(object sender, ExecutedRoutedEventArgs e) {
            PythonToolsPackage.OpenWebBrowser(PythonToolsPackage.InterpreterHelpUrl);
        }
    }
}