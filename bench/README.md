# Friday ölçüm düzeneği

Friday'in "zekasını" ve hızını sürümden sürüme aynı sorularla ölçer. `FridayAssistant`'ın gerçek cevap hattı (hızlı
komutlar, hafıza, soruya eklenen bağlam, araç seçimi, dil modeli) yazıyla çalıştırılır; mikrofon, ses, ekran ve pano
kullanılmaz.

- **Kurgusal kullanıcı:** Deniz, kızı Ela, eşi Mert. Hafızası, hatırlatmaları, notları, işleri ve harcamaları her
  çalıştırmada sıfırdan kurulur (`BenchWorld.cs`). Gerçek veriye (`D:\Friday\data`) dokunulmaz.
- **Araçlar:** Yalnızca ölçümün kendi kayıtlarını okuyup yazanlar gerçekten çalışır. Bilgisayarda iz bırakanlar (uygulama
  açma, ses, pano, ekran, e-posta penceresi) prova edilir: çalıştırılmaz, "yapıldı" döner.
- **Sorular:** 62 gerçekçi soru, 11 grup (`Cases.cs`). Gruplar: hafıza, uydurmama, kayıtlar, gündem, iş yaptırma, akıl
  yürütme, bağlam, çok turlu, zor, uzun üretim, sohbet. Her soruda cevapta olması gereken, çağrılması gereken araç ya da
  olmaması gereken şey kontrol edilir.

## Çalıştırma

Friday'i kapat: açıkken boşta modeli GPU'dan boşaltır ve ölçümü bozar. Ollama ve modeller Friday'in kurulumundan
(`FRIDAY_HOME`, yoksa `D:\Friday`) gelir; hiçbir şey indirilmez.

```powershell
dotnet run --project bench/Friday.Bench -c Release                         # bütün sorular, Akıllı model
dotnet run --project bench/Friday.Bench -c Release -- --model friday-text  # Hızlı model
dotnet run --project bench/Friday.Bench -c Release -- --groups akil,gundem # yalnızca bazı gruplar
dotnet run --project bench/Friday.Bench -c Release -- --ask "yarın neler var"  # tek soru, adım adım
```

Her çalıştırmanın ayrıntısı `bench/results/` altına yazılır (repoya girmez). Bütün soruların çalıştırıldığı her ölçüm
`bench/history.csv`'ye bir satır ekler: sürüm, commit, model, doğru sayısı, ilk sözün medyanı ve p90'ı, ortalama süre,
modelin boş dönüp yinelendiği tur sayısı ve üretim sınırına takılan cevaplar. Sürüm çıkarmadan önce çalıştırılıp bu
dosya commit edilir; bir gerileme satırlar karşılaştırılarak görülür.

Sorular bugünün tarihine göre değerlendirilir ("yarın akşam planım var mı" çarşamba öncesinde "yüzme" bekler). Dil modeli
her seferinde birebir aynı cevabı vermez; 62 sorudan 1-2'sinin turdan tura değişmesi normaldir.

## Neyi ölçmez

Ses (Whisper, Piper), uyandırma kelimesi, ekran ve pano okuyan özellikler, internetteki araçların doğruluğu. Bunlar için
`tests/Friday.UnitTests` ve elle deneme gerekir.
