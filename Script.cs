// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT
// Portions derived from DoseConverter (https://github.com/NickChng/DoseConverter),
// Copyright (c) 2021 Denis Brojan, MIT License.

using ReRT;
using ESAPIScript;
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using VMS.TPS.Common.Model.API;

[assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    class Script
    {
        private void RunOnNewStaThread(Action a)
        {
            Thread thread = new Thread(() => a());
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        private void InitializeAndStartMainWindow(EsapiWorker esapiWorker)
        {
            var viewModel = new ViewModel(esapiWorker);
            var mainWindow = new MainWindow(viewModel);
            mainWindow.ShowDialog();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext scriptcontext)
        {
            if (scriptcontext.PlanSumsInScope.Count() == 0)
            {
                MessageBox.Show("No plan sums found.", "Error");
                return;
            }

            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

            Helpers.SeriLog.Initialize(scriptcontext.CurrentUser.Id);
            // EsapiWorker must be created on the Eclipse main thread so it captures the right Dispatcher.
            var esapiWorker = new EsapiWorker(scriptcontext.Patient, scriptcontext.PlanSumsInScope);

            // Park the Eclipse thread on a dispatcher frame until the WPF window closes.
            DispatcherFrame frame = new DispatcherFrame();

            RunOnNewStaThread(() =>
            {
                // This method won't return until the window is closed

                InitializeAndStartMainWindow(esapiWorker);

                // End the queue so that the script can exit
                frame.Continue = false;
            });

            // Start the new queue, waiting until the window is closed
            Dispatcher.PushFrame(frame);
        }
    }
}
