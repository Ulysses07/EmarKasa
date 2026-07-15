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
#if WINDOWS
		svc.GetService<Kasa.App.Core.IBildirimServisi>()?.KayitOl();
		Platforms.Windows.HatirlatmaKontrol.GoreviGarantile();
#endif
		var shell = svc.GetRequiredService<AppShell>();
		return new Window(shell);
	}
}