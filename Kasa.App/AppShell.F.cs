namespace Kasa.App;

/// <summary>Paket F menüleri: Fatura Takibi, POS ve ERP12 Karşılaştırma. Her iki rol görür; yazma editörde.</summary>
public partial class AppShell
{
    private void PaketFMenusu(bool gorunur)
    {
        FaturaTakibiItem.IsVisible = gorunur;
        PosItem.IsVisible = gorunur;
        Erp12Item.IsVisible = gorunur;
    }
}
