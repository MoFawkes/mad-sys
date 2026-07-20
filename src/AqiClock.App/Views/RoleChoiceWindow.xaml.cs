using AqiClock.Application.Abstractions;
using Wpf.Ui.Controls;

namespace AqiClock.App.Views;

public partial class RoleChoiceWindow : FluentWindow
{
    private readonly IWindowService _windows;
    public RoleChoiceWindow(IWindowService windows) { InitializeComponent(); _windows = windows; }
    private void OnTeacher(object sender, System.Windows.RoutedEventArgs e) { Hide(); _windows.ShowTeacherSignInWindow(); Close(); }
    private void OnStudent(object sender, System.Windows.RoutedEventArgs e) { Hide(); _windows.ShowStudentClassPickerWindow(); Close(); }
}
