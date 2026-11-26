// Görünüm testleri (GorunumOrtami) tek bir MAUI Application'ı ve stillerin paylaşılan değer nesnelerini (ör. kart gölgesi Shadow) kullanır;
// MAUI iş parçacığı güvenli değildir. Sınıflar paralel koşunca aynı nesne iki iş parçacığından ebeveyne bağlanıp Dictionary
// bozulması ("concurrent update") ile ara sıra düşüyordu. Testler sırayla koşar (paket birkaç saniyede biter).
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
