﻿using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

using Insight.Shared.Model;
using Insight.Shared.VersionControl;
using Insight.ViewModels;

using Visualization.Controls;
using Visualization.Controls.Data;

namespace Insight
{
    /// <summary>
    /// Shows the various analysis results as tabs inside the main window.
    /// </summary>
    internal sealed class TabBuilder
    {
        private readonly MainViewModel _mainViewModel;

        public TabBuilder(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
        }

        public void ShowChangeCoupling(List<Coupling> data)
        {
            // Context menu to show couplings in chord diagram
            var commands = new DataGridViewUserCommands<Coupling>();
            commands.Register(Strings.Visualize, args => _mainViewModel.OnShowChangeCouplingChord(args));

            var descr = new TableViewModel();
            descr.Commands = commands;
            descr.Data = data;
            descr.Title = "Change Couplings";
            ShowTab(descr, true);
        }

        /// <summary>
        /// Show a selection of the data grid as chord
        /// </summary>
        public void ShowChangeCoupling(List<EdgeData> data)
        {
            var descr = new ChordViewModel();
            descr.Data = data;
            descr.Title = "Change Couplings (Chord)";
            ShowTab(descr, true);
        }

        public void ShowHierarchicalData(HierarchicalData data, string title)
        {
            if (data == null)
            {
                return;
            }

            var cp = new CirclePackingViewModel();
            var commands = new HierarchicalDataCommands();
            commands.Register("Trend", _mainViewModel.OnShowTrend);
            commands.Register("Work", _mainViewModel.OnShowWork);
            cp.Data = data.Clone();
            cp.Title = title + " (Circle)";
            cp.Commands = commands;
            ShowTab(cp, true);

            var tm = new TreeMapViewModel();
            commands = new HierarchicalDataCommands();
            commands.Register("Trend", _mainViewModel.OnShowTrend);
            commands.Register("Work", _mainViewModel.OnShowWork);
            tm.Data = data.Clone();
            tm.Title = title + " (Treemap)";
            tm.Commands = commands;
            ShowTab(tm, false);
        }

        public void ShowImage(BitmapImage bitmapImage)
        {
            var descr = new ImageViewModel();
            descr.Data = bitmapImage;
            descr.Title = "Image Viewer";
            ShowTab(descr, true);
        }

        public void ShowSummary(List<DataGridFriendlyArtifact> data)
        {
            var descr = new TableViewModel();
            descr.Commands = null;
            descr.Data = data;
            descr.Title = "Summary";
            ShowTab(descr, true);
        }

        public void ShowWarnings(List<WarningMessage> data)
        {
            var title = "Warnings";
            if (data == null || !data.Any())
            {
                var vm = _mainViewModel.Tabs.FirstOrDefault(x => x.Title == title);
                if (vm != null)
                {
                    _mainViewModel.Tabs.Remove(vm);
                }

                return;
            }

            var descr = new TableViewModel();
            descr.Commands = null;
            descr.Data = data;
            descr.Title = title;
            ShowTab(descr, true);
        }

        private void ShowTab(TabContentViewModel info, bool toForeground)
        {
            var oldInfo = _mainViewModel.Tabs.FirstOrDefault(d => d.Title == info.Title);
            int index;
            if (oldInfo != null)
            {
                index = _mainViewModel.Tabs.IndexOf(oldInfo);
                _mainViewModel.Tabs.RemoveAt(index);
                _mainViewModel.Tabs.Insert(index, info);
            }
            else
            {
                _mainViewModel.Tabs.Add(info);
                index = _mainViewModel.Tabs.Count - 1;
            }

            if (toForeground || _mainViewModel.Tabs.Count == 1)
            {
                _mainViewModel.SelectedIndex = index;
            }
        }
    }
}