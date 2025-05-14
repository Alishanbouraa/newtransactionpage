using System;
using System.Windows;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class BulkProcessingStatusWindow : Window
    {
        public BulkProcessingStatusWindow()
        {
            InitializeComponent();

            this.Loaded += BulkProcessingStatusWindow_Loaded;
        }

        private void BulkProcessingStatusWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is BulkProcessingStatusViewModel viewModel)
            {
                viewModel.CloseRequested += (s, args) =>
                {
                    this.DialogResult = args.DialogResult;
                    this.Close();
                };
            }
        }
    }
}