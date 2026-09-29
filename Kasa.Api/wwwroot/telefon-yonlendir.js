// Telefonları /m/ telefon arayüzüne yönlendirir. "Masaüstü görünümü" seçildiyse burada kalır
// ve küçük ekranda telefon görünümüne dönüş düğmesi gösterir. Bildirim adresindeki # kısmı korunur.
(function () {
  var ANAHTAR = 'kasa.gorunum';
  var telefon = false;
  try {
    telefon = window.matchMedia('(pointer: coarse)').matches && Math.min(screen.width, screen.height) < 600;
  } catch (e) {}
  if (!telefon) return;
  var masaustu = false;
  try {
    masaustu = localStorage.getItem(ANAHTAR) === 'masaustu';
  } catch (e) {}
  // Tarayıcı kaydı tutamıyorsa (gizli sekme, kapalı depolama) telefon arayüzü bu işaretle gelir.
  if (/[?&]gorunum=masaustu(&|$)/.test(location.search)) masaustu = true;
  if (!masaustu) {
    location.replace('/m/' + location.hash);
    return;
  }
  function telefonaDon() {
    try {
      localStorage.removeItem(ANAHTAR);
    } catch (e) {}
    location.href = '/m/' + location.hash;
  }
  function dugme(sinif) {
    var b = document.createElement('button');
    b.type = 'button';
    b.className = sinif;
    b.textContent = 'Telefon görünümü';
    b.addEventListener('click', telefonaDon);
    return b;
  }
  document.addEventListener('DOMContentLoaded', function () {
    var alt = document.querySelector('.sidebar-foot');
    var cikis = document.getElementById('logout');
    if (alt && cikis) alt.insertBefore(dugme('logout'), cikis);
    var kurtar = document.getElementById('recover-open');
    if (kurtar && kurtar.parentNode) kurtar.parentNode.insertBefore(dugme('link-button'), kurtar.nextSibling);
  });
})();
