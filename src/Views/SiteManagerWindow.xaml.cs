using System.Windows;
using System.Windows.Controls;
using ChatHub.ViewModels;

namespace ChatHub.Views;

/// <summary>站点管理窗口：增 / 改 / 查 / 删，改动即时写入 sites.txt。</summary>
public partial class SiteManagerWindow : Window
{
    private readonly SiteManagerViewModel _viewModel;

    public SiteManagerWindow(MainWindowViewModel ownerViewModel)
    {
        ArgumentNullException.ThrowIfNull(ownerViewModel);

        InitializeComponent();

        _viewModel = new SiteManagerViewModel(ownerViewModel.Store, ownerViewModel.Sites)
        {
            ShowEditDialog = ShowEditDialog,
            ConfirmDelete = ConfirmDelete,
            ShowError = message => MessageBox.Show(this, message, "站点管理", MessageBoxButton.OK, MessageBoxImage.Warning),
            RequestClose = Close,
        };

        DataContext = _viewModel;
        _viewModel.UpdateStatus();
    }

    private bool ShowEditDialog(SiteEditViewModel editor)
    {
        var dialog = new SiteEditWindow(editor) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    private bool ConfirmDelete(string siteName) =>
        MessageBox.Show(
            this,
            $"确定要删除站点「{siteName}」吗？\n它的页面会被关闭，此操作不可撤销。",
            "删除站点",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.OnSelectionChanged(SitesGrid.SelectedItem as Models.Site);
    }

    private void OnRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SitesGrid.SelectedItem is Models.Site)
        {
            _viewModel.Edit();
        }
    }
}
