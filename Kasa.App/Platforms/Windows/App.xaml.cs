using Microsoft.UI.Xaml;
using Kasa.App.Platforms.Windows;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Kasa.App.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	protected override async void OnLaunched(LaunchActivatedEventArgs args)
	{
		var cmd = Environment.GetCommandLineArgs();
		if (cmd.Contains(HatirlatmaKontrol.Arg))
		{
			var app = CreateMauiApp();                 // DI konteyneri (pencere açmadan)
			await HatirlatmaKontrol.CalistirAsync(app.Services);
			Exit();
			return;
		}
		base.OnLaunched(args);
	}
}

