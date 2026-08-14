using ImageMagick;
using System.IO;
using System.Windows;

namespace ConvertToWebP;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ImageMagick.MagickNET.SetTempDirectory(Path.Combine(Path.GetTempPath(), "ConvertToWebP"));
    }
}
