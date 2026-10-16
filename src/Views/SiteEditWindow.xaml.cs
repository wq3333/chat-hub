using System.Windows;
using ChatHub.ViewModels;

namespace ChatHub.Views;

/// <summary>新增 / 修改站点的对话框：校验不通过时禁用「保存」。</summary>
public partial class SiteEditWindow : Window
{
    private readonly SiteEditViewModel _viewModel;

    public SiteEditWindow(SiteEditViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();
        DataContext = _viewModel;

        // 命令的初始状态在构造时已算好，这里直接同步按钮可用性，并在校验变化时保持同步。
        OkButton.IsEnabled = _viewModel.OkCommand.CanExecute(null);
        _viewModel.OkCommand.CanExecuteChanged += (_, _) =>
            OkButton.IsEnabled = _viewModel.OkCommand.CanExecute(null);

        Loaded += (_, _) =>
        {
            if (_viewModel.IsNew)
            {
                NameBox.Focus();
            }
            else
            {
                UrlBox.Focus();
                UrlBox.SelectAll();
            }
        };
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.OkCommand.CanExecute(null))
        {
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
