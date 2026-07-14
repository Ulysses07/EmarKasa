using Microsoft.Extensions.DependencyInjection;

namespace Kasa.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var shell = Current!.Handler!.MauiContext!.Services.GetRequiredService<AppShell>();
		return new Window(shell);
	}
}