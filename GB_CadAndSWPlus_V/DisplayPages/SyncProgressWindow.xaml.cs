using GB_CadAndSWPlus_V.FunctionalMethod;
using System;
using System.Windows;

namespace GB_CadAndSWPlus_V
{
    public partial class SyncProgressWindow : Window
    {
        public SyncProgressWindow()
        {
            InitializeComponent();
        }

        public event EventHandler? CancelRequested
        {
            add => SyncProgressView.CancelRequested += value;
            remove => SyncProgressView.CancelRequested -= value;
        }

        public void UpdateProgress(SyncProgressInfo progress)
        {
            SyncProgressView.UpdateProgress(progress);
        }
    }
}
