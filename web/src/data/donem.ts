// "2026-06-29" + "2026-06-30" → "29–30 Haz 2026"
const AYLAR = ['Oca', 'Şub', 'Mar', 'Nis', 'May', 'Haz', 'Tem', 'Ağu', 'Eyl', 'Eki', 'Kas', 'Ara']

export function donemEtiket(startIso: string, endIso: string): string {
  const [sy, sm, sd] = startIso.split('-').map(Number)
  const [ey, em, ed] = endIso.split('-').map(Number)
  const gun = (n: number) => String(n)
  if (sy === ey && sm === em) return `${gun(sd)}–${gun(ed)} ${AYLAR[sm - 1]} ${sy}`
  if (sy === ey) return `${gun(sd)} ${AYLAR[sm - 1]} – ${gun(ed)} ${AYLAR[em - 1]} ${sy}`
  return `${gun(sd)} ${AYLAR[sm - 1]} ${sy} – ${gun(ed)} ${AYLAR[em - 1]} ${ey}`
}
