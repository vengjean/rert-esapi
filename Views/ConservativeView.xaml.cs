// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System.Windows.Controls;

namespace ReRT
{
    /// <summary>
    /// Interaction logic for ConservativeView.xaml — the conservative maximum
    /// dose accumulation view (Paradis et al.). Hosted in-place inside
    /// MainWindow; its DataContext is a <see cref="ConservativeViewModel"/>.
    /// Returning to the rigid-registration view is driven by the shared
    /// dose-accumulation toggle (bound to the host window's ViewModel).
    /// </summary>
    public partial class ConservativeView : UserControl
    {
        public ConservativeView()
        {
            InitializeComponent();
        }
    }
}
