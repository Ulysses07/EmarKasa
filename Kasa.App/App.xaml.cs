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
		// Bildirim kaydı başarısız olsa bile uygulama açılmalı (kart şeridi uygulama içinde çalışır).
		try { svc.GetService<Kasa.App.Core.IBildirimServisi>()?.KayitOl(); }
		catch (Exception) { }
		// schtasks süreçleri pencereyi dondurmasın: arka planda.
		_ = Task.Run(() => Platforms.Windows.HatirlatmaKontrol.GoreviGarantile());
#endif
		var shell = svc.GetRequiredService<AppShell>();
		return new Window(shell);
	}
}
