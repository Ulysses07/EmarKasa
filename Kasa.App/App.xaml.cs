using Microsoft.Extensions.DependencyInjection;

namespace Kasa.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var svc = Current!.Handler!.MauiContext!.Services;
		var shell = svc.GetRequiredService<AppShell>();
		return new Window(shell);
	}
}