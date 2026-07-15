// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT
// Portions derived from DoseConverter (https://github.com/NickChng/DoseConverter),
// Copyright (c) 2021 Denis Brojan, MIT License.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ReRT
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool DragSelected = false;

        private ScrollViewer _leftStructureScroll;
        private ScrollViewer _rightDiscountBodyScroll;
        private ScrollViewer _rightDiscountHeaderScroll;
        private ScrollViewer _preConstraintScroll;
        private bool _syncingScroll;

        // Track last-known selected label per structure to swallow virtualization-induced no-op SelectionChanged events.
        private readonly Dictionary<string, string> _lastSelectedLabel = new Dictionary<string, string>();

        public MainWindow(ViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(HookUpScrollSync), DispatcherPriority.Loaded);
        }

        // Under Citrix/RDP, WPF binds to the session's virtual-GPU render path when the
        // process starts. If Eclipse launches before that path has settled, this window
        // can render dark/blank until the user re-logs Eclipse (a fresh process binds to
        // the now-stable device). Forcing software rendering for this window in a remote
        // session sidesteps the unreliable hardware path entirely; local sessions keep
        // hardware acceleration.
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                if (GetSystemMetrics(SM_REMOTESESSION) != 0
                    && PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource src
                    && src.CompositionTarget != null)
                {
                    src.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                }
            }
            catch { /* a rendering hint only — never block the window from opening */ }
        }

        private const int SM_REMOTESESSION = 0x1000;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        // ---------- Scroll sync across the structure table columns: left structure
        // list, pre-plan dmax constraint column, and right discount panel ----------

        private void HookUpScrollSync()
        {
            _leftStructureScroll = FindScrollViewer(StructureListView);
            _rightDiscountBodyScroll = StructureDiscountScrollViewer;
            _rightDiscountHeaderScroll = StructureDiscountHeaderScrollViewer;
            _preConstraintScroll = PreConstraintScrollViewer;

            if (_leftStructureScroll != null)
            {
                _leftStructureScroll.ScrollChanged -= LeftStructureScroll_ScrollChanged;
                _leftStructureScroll.ScrollChanged += LeftStructureScroll_ScrollChanged;
            }
            if (_rightDiscountBodyScroll != null)
            {
                _rightDiscountBodyScroll.ScrollChanged -= RightDiscountBody_ScrollChanged;
                _rightDiscountBodyScroll.ScrollChanged += RightDiscountBody_ScrollChanged;
            }
            // The pre-plan dmax constraint column can be wheel-scrolled on its
            // own (its scrollbar is Hidden, not Disabled), so it must push its
            // offset back to the other columns too — otherwise it desyncs.
            if (_preConstraintScroll != null)
            {
                _preConstraintScroll.ScrollChanged -= PreConstraintScroll_ScrollChanged;
                _preConstraintScroll.ScrollChanged += PreConstraintScroll_ScrollChanged;
            }
        }

        private void LeftStructureScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_syncingScroll || e.VerticalChange == 0) return;
            _syncingScroll = true;
            try
            {
                if (_rightDiscountBodyScroll != null)
                    _rightDiscountBodyScroll.ScrollToVerticalOffset(_leftStructureScroll.VerticalOffset);
                if (_preConstraintScroll != null)
                    _preConstraintScroll.ScrollToVerticalOffset(_leftStructureScroll.VerticalOffset);
            }
            finally { _syncingScroll = false; }
        }

        private void RightDiscountBody_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.HorizontalChange != 0 && _rightDiscountHeaderScroll != null)
                _rightDiscountHeaderScroll.ScrollToHorizontalOffset(_rightDiscountBodyScroll.HorizontalOffset);

            if (_syncingScroll || e.VerticalChange == 0) return;
            _syncingScroll = true;
            try
            {
                if (_leftStructureScroll != null)
                    _leftStructureScroll.ScrollToVerticalOffset(_rightDiscountBodyScroll.VerticalOffset);
                if (_preConstraintScroll != null)
                    _preConstraintScroll.ScrollToVerticalOffset(_rightDiscountBodyScroll.VerticalOffset);
            }
            finally { _syncingScroll = false; }
        }

        private void PreConstraintScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_syncingScroll || e.VerticalChange == 0) return;
            _syncingScroll = true;
            try
            {
                if (_leftStructureScroll != null)
                    _leftStructureScroll.ScrollToVerticalOffset(_preConstraintScroll.VerticalOffset);
                if (_rightDiscountBodyScroll != null)
                    _rightDiscountBodyScroll.ScrollToVerticalOffset(_preConstraintScroll.VerticalOffset);
            }
            finally { _syncingScroll = false; }
        }

        private static ScrollViewer FindScrollViewer(DependencyObject d)
        {
            if (d == null) return null;
            if (d is ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        // ---------- Drag-and-drop reorder of structures ----------

        delegate Point GetPositionDelegate(IInputElement element);

        private void DragConstraint_MouseDown(object sender, MouseButtonEventArgs e)
        {
            StructureListView.AllowDrop = true;
            DragSelected = true;
        }

        private void DragConstraint_ListView_DragOver(object sender, DragEventArgs e)
        {
            var VM = DataContext as ViewModel;
            var SelectedIndex = DragConstraint_GetCurrentIndex(e.GetPosition);
            var DropIndex = VM.SelectedIndex;
            if (SelectedIndex < 0 || DropIndex < 0 || SelectedIndex == DropIndex) return;
            if (!DragSelected) return;

            int inc = SelectedIndex < DropIndex ? -1 : 1;
            int CurrentIndex = DropIndex;
            while (CurrentIndex != SelectedIndex)
            {
                VM.StructureDefinitions.Move(CurrentIndex + inc, CurrentIndex);
                CurrentIndex += inc;
            }
        }

        private void DragConstraint_Drop(object sender, DragEventArgs e)
        {
            var LV = sender as ListView;
            if (LV != null && LV.AllowDrop && Mouse.LeftButton == MouseButtonState.Released)
            {
                LV.AllowDrop = false;
                DragSelected = false;
            }
        }

        private int DragConstraint_GetCurrentIndex(GetPositionDelegate getPosition)
        {
            for (int i = 0; i < StructureListView.Items.Count; ++i)
            {
                var item = StructureListView.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (item != null && IsMouseOverTarget(item, getPosition))
                    return i;
            }
            return -1;
        }

        private static bool IsMouseOverTarget(Visual target, GetPositionDelegate getPosition)
        {
            Rect bounds = VisualTreeHelper.GetDescendantBounds(target);
            Point mousePos = getPosition((IInputElement)target);
            return bounds.Contains(mousePos);
        }

        // ---------- Toolbar / footer click handlers ----------

        private void FetchDiscount(object sender, RoutedEventArgs e)
        {
            (DataContext as ViewModel)?.FetchAllDiscount();
        }

        private void ZeroDiscount(object sender, RoutedEventArgs e)
        {
            (DataContext as ViewModel)?.ZeroDiscount();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // ---------- Type combo ----------

        private void Label_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var VM = DataContext as ViewModel;
            var comboBox = sender as ComboBox;
            var structure = comboBox?.DataContext as StructureViewModel;
            if (structure == null) return;

            string newLabel = null;
            if (comboBox?.SelectedValue != null)
                newLabel = comboBox.SelectedValue.ToString();
            else if (e.AddedItems != null && e.AddedItems.Count > 0 && e.AddedItems[0] != null)
                newLabel = e.AddedItems[0].ToString();

            _lastSelectedLabel.TryGetValue(structure.StructureId, out string lastLabel);
            if (string.Equals(lastLabel ?? string.Empty, newLabel ?? string.Empty, StringComparison.Ordinal))
                return;

            _lastSelectedLabel[structure.StructureId] = newLabel;

            if (VM != null)
            {
                VM.FetchDiscount(structure);
                VM.LoadConstraintsFromConfig(structure);
            }
        }
    }
}
