using AqiClock.Application.Abstractions;
using AqiClock.App.Services;
using System.Windows;
using Wpf.Ui.Controls;

namespace AqiClock.App.Views;

public partial class RoleChoiceWindow : FluentWindow
{
    private readonly IWindowService _windows;
    public RoleChoiceWindow(IWindowService windows)
    {
        InitializeComponent();
        _windows = windows;
        FitToWorkArea(SystemParameters.WorkArea);
    }

    internal void FitToWorkArea(Rect workArea)
    {
        WindowPlacement fitted = WindowPlacements.Clamp(
            new WindowPlacement(workArea.Left, workArea.Top, Width, Height),
            workArea.Left, workArea.Top, workArea.Width, workArea.Height,
            MinWidth, MinHeight);
        Width = fitted.Width;
        Height = fitted.Height;
    }
    private void OnTeacher(object sender, System.Windows.RoutedEventArgs e) { Hide(); _windows.ShowTeacherSignInWindow(); Close(); }
    private void OnStudent(object sender, System.Windows.RoutedEventArgs e) { Hide(); _windows.ShowStudentClassPickerWindow(); Close(); }
}
