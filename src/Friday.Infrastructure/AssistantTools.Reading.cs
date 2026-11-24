using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Okuduklarım (bkz. <see cref="ReadingLog"/>): kullanıcının açıp yarım dakikadan uzun baktığı belgeler hatırlanır; dil
/// modeli boştayken her birine Türkçe bir özet ve iki dilli anahtar sözcükler yazar. "Geçen hafta okuduğum sözleşmede
/// depozito ne kadardı", "bu hafta neler okudum", "dün baktığım raporu aç", "bunun öncekinden farkı ne".
/// </summary>
public sealed partial class AssistantTools
{
    private const long ReadingMaxFileBytes = 30L * 1024 * 1024;

    private const string DigestInstruction =
        "Aşağıda kullanıcının bilgisayarında okuduğu bir belgenin adı ve başı var. Yalnızca şu iki satırı yaz, başka hiçbir şey yazma:\n" +
        "ÖZET: <belgenin ne olduğu ve ana konusu; Türkçe, en fazla 2 cümle; taraflar, tarihler, tutarlar gibi ayırt edici bilgileri koru>\n" +
        "ANAHTAR: <virgülle ayrılmış 10-15 anahtar sözcük: belgenin türü, konusu ve önemli adlar; hem Türkçe hem belgenin kendi dilinde>";

    private string? _readingName;
    private int _readingSeconds;
    private bool _readingCounted;
    private string? _readingPath;
    private int _readingBusy;

    /// <summary>Okuduklarım (App verir; ayar kapalıysa kullanılmaz).</summary>
    public ReadingLog? Reading { get; set; }

    /// <summary>
    /// Etkinlik izleyicisi 15 sn'de bir öndeki pencerenin başlığını verir. Başlıkta bir belge varsa ve yarım dakikadan uzun
    /// önde kalırsa dosyası bulunur, metni saklanır (dosya değiştiyse önceki sürümü de).
    /// </summary>
    public void NoticeReading(string title, int seconds)
    {
        if (Reading is not { } log || !Settings.ReadingMemory)
        {
            return;
        }

        if (FrontDocument.FileName(title) is not { } name)
        {
            _readingName = null;
            return;
        }

        if (!string.Equals(name, _readingName, StringComparison.OrdinalIgnoreCase))
        {
            _readingName = name;
            _readingSeconds = 0;
            _readingCounted = false;
            _readingPath = null;
        }

        _readingSeconds += seconds;
        if (_readingSeconds < ReadingLog.MinSeconds || Interlocked.Exchange(ref _readingBusy, 1) == 1)
        {
            return;
        }

        try
        {
            _readingPath ??= (log.ByName(name) is { } known && File.Exists(known.Path) ? known.Path : FindFrontFile(name)) ?? "";
            if (_readingPath.Length == 0)
            {
                return;
            }

            var (entry, added) = log.Touch(_readingPath, DateTime.Now, _readingCounted ? seconds : _readingSeconds);
            _readingCounted = true;
            RememberText(log, entry, _readingPath, null);
            if (added)
            {
                ledger.Record(DataFlow.Local, "Okuduklarım", $"{name} hatırlanıyor; metni yalnızca bu bilgisayarda saklandı");
                FridayLog.Info($"Okuduklarım: {name}");
            }

            log.Save(force: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException
                                              or FormatException or ArgumentException or IndexOutOfRangeException)
        {
            FridayLog.Warn($"Okuduklarım: {name} okunamadı ({exception.Message})");
            _readingPath = "";
        }
        finally
        {
            Volatile.Write(ref _readingBusy, 0);
        }
    }

    /// <summary>Saklanan metin dosyanın şu anki hâli değilse yeniden okunur. İçerik değiştiyse true.</summary>
    private static bool RememberText(ReadingLog log, ReadEntry entry, string path, string? text)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > ReadingMaxFileBytes || !log.NeedsText(entry, info.LastWriteTimeUtc, info.Length))
        {
            return false;
        }

        text ??= DocumentText.Extract(path, ReadingLog.TextLimit);
        text = text.Trim();
        return text.Length >= 40
            && log.StoreText(entry, text, info.LastWriteTimeUtc, info.Length, !FrontDocument.LooksTurkish(text.Length > 4000 ? text[..4000] : text));
    }

    /// <summary>"Bu ne" ile okunan belge de okunmuş sayılır.</summary>
    private void RememberFront(string path, string text)
    {
        if (Reading is not { } log || !Settings.ReadingMemory)
        {
            return;
        }

        try
        {
            var (entry, _) = log.Touch(path, DateTime.Now, 0);
            RememberText(log, entry, path, text);
        }
        catch (IOException exception)
        {
            FridayLog.Warn($"Okuduklarım: {exception.Message}");
        }
    }

    /// <summary>
    /// Dil modeli boştayken (bakım turunda) özeti yazılmamış bir belgeye Türkçe özet ve anahtar sözcükler yazar; böylece
    /// İngilizce bir "lease agreement" "kira sözleşmesi" diye sorulunca bulunur. Bir şey yazıldıysa true.
    /// </summary>
    public async Task<bool> DigestReadingAsync(CancellationToken cancellationToken)
    {
        if (Reading is not { } log || !Settings.ReadingMemory || log.NextToDigest() is not { } entry || log.Text(entry) is not { } text)
        {
            return false;
        }

        var head = text.Length > 3500 ? text[..3500] : text;
        var output = (await chat.CompleteAsync(Settings.OllamaModel, DigestInstruction, $"Belgenin adı: {entry.Name}\n\n{head}", null, null, cancellationToken)
            .ConfigureAwait(false)).Trim();
        string gist = "", keywords = "";
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var plain = line.TrimStart('*', '-', ' ').Replace("**", "", StringComparison.Ordinal);
            if (plain.StartsWith("ÖZET:", StringComparison.OrdinalIgnoreCase))
            {
                gist = plain[5..].Trim();
            }
            else if (plain.StartsWith("ANAHTAR:", StringComparison.OrdinalIgnoreCase))
            {
                keywords = plain[8..].Trim();
            }
        }

        log.SetDigest(entry, gist, keywords);
        FridayLog.Info($"Okuduklarım özeti: {entry.Name} → {(gist.Length > 0 ? gist : "(yazılamadı)")}");
        return gist.Length > 0;
    }

    private async Task<string> ReadingAsync(string action, string question, string name, string when, CancellationToken cancellationToken)
    {
        if (Reading is not { } log || !Settings.ReadingMemory)
        {
            return "Okuduklarımı hatırlama kapalı. Ayarlar > Belge hafızası bölümünden açabilirsin.";
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var period = ReadingLog.Period($"{when} {question} {name}", today);
        switch (Simplify(action))
        {
            case "liste":
                return ListReading(log, period, when, today);
            case "unut":
                return ForgetReading(log, name);
            case "fark":
                return await CompareVersionsAsync(log, name, today, cancellationToken).ConfigureAwait(false);
            case "ac":
                if (FindRead(log, $"{name} {question}", period) is not { } open)
                {
                    return "Okuduklarında buna uyan bir belge bulamadım.";
                }

                if (!File.Exists(open.Path))
                {
                    return $"{Display(open)} artık eski yerinde değil ama metnini hatırlıyorum; içinde ne aradığını sorabilirsin.";
                }

                OpenFile(open.Path);
                ledger.Record(DataFlow.Local, "Okuduklarım", $"{open.Name} yeniden açıldı");
                return $"{Display(open)} belgesini açıyorum; en son {WhenRead(open.LastSeen, today)} bakmıştın.";
            default:
                return await AskReadingAsync(log, question.Length > 0 ? question : name, period, today, cancellationToken).ConfigureAwait(false);
        }
    }

    private string ListReading(ReadingLog log, (DateOnly From, DateOnly To)? period, string when, DateOnly today)
    {
        var range = period ?? (today.AddDays(-6), today);
        var label = when.Trim().Length > 0 ? when.Trim() : "son bir haftada";
        var entries = log.InPeriod(range.From, range.To);
        if (entries.Count == 0)
        {
            return log.Count == 0
                ? "Henüz okuduğun bir belge kaydetmedim. Bir PDF'e ya da Word belgesine yarım dakikadan uzun baktığında hatırlamaya başlıyorum."
                : $"{TurkishGrammar.Capitalize(label)} okuduğun bir belge görmedim.";
        }

        ShowCard(new AnswerCard("Okuduklarım", "")
        {
            Big = entries.Count.ToString(CultureInfo.InvariantCulture),
            BigCaption = label,
            Rows = [.. entries.Take(MaxCardRows).Select(entry => new CardRow(Display(entry), $"{WhenRead(entry.LastSeen, today)} · {Math.Max(1, (int)Math.Round(entry.Minutes))} dk"))]
        });
        var told = entries.Take(3).Select(entry => entry.Gist.Length > 0 ? $"{Display(entry)}: {FirstSentence(entry.Gist)}" : Display(entry));
        var more = entries.Count > 3 ? $" Ve {entries.Count - 3} belge daha." : "";
        return $"{TurkishGrammar.Capitalize(label)} {entries.Count} belgeye baktın. {string.Join(" ", told.Select(item => item.TrimEnd('.') + "."))}{more}";
    }

    private string ForgetReading(ReadingLog log, string name)
    {
        if (Simplify(name) is "hepsi" or "tumu" or "hepsini")
        {
            var count = log.Clear();
            ledger.Record(DataFlow.Local, "Okuduklarım", $"{count} belge ve metinleri silindi");
            return count == 0 ? "Okuduklarım zaten boş." : $"Okuduklarımı unuttum: {count} belge ve sakladığım metinleri silindi.";
        }

        var entry = Simplify(name) is "" or "bu"
            ? ActiveWindow is { } window && FrontDocument.FileName(window.Title) is { } front ? log.ByName(front) : null
            : FindRead(log, name, null);
        if (entry is null || !log.Forget(entry))
        {
            return "Unutacağım belgeyi bulamadım; belgeyi öne getirip \"bu belgeyi unut\" de.";
        }

        ledger.Record(DataFlow.Local, "Okuduklarım", $"{entry.Name} unutuldu");
        return $"{Display(entry)} belgesini unuttum; metnini de sildim.";
    }

    /// <summary>
    /// Okunmuş bir belgeye dair soru: önce adı, özeti ve anahtar sözcükleri, bulunamazsa sakladığı metinler aranır. Cevap
    /// belgenin kendisine dayanır ve hangi belgeden, ne zaman okunduğu söylenir.
    /// </summary>
    private async Task<string> AskReadingAsync(ReadingLog log, string question, (DateOnly From, DateOnly To)? period, DateOnly today, CancellationToken cancellationToken)
    {
        if (FindRead(log, question, period) is not { } entry)
        {
            var recent = log.Entries.Take(3).Select(Display).ToList();
            return recent.Count == 0
                ? "Henüz okuduğun bir belge kaydetmedim."
                : $"Okuduklarında bununla ilgili bir belge bulamadım. Son baktıkların: {TurkishGrammar.Join(recent)}.";
        }

        var text = log.Text(entry);
        if (text is null && File.Exists(entry.Path))
        {
            text = await Task.Run(() => DocumentText.Extract(entry.Path, ReadingLog.TextLimit), cancellationToken).ConfigureAwait(false);
        }

        if (text is null || text.Trim().Length == 0)
        {
            return $"{Display(entry)} belgesini hatırlıyorum ama metnine artık ulaşamıyorum.";
        }

        // Türkçe soru İngilizce bir metinde aranırken sözcükler eşleşmez: belgenin dilinde arama sözcükleri istenir.
        var query = question + " " + entry.Keywords;
        if (entry.Foreign && text.Length > 6000)
        {
            query += " " + await ChatTermsAsync(question, cancellationToken).ConfigureAwait(false);
        }

        var when = WhenRead(entry.LastSeen, today);
        ledger.Record(DataFlow.Local, "Okuduklarım", $"\"{question}\" için {entry.Name} okundu");
        var answer = await FrontLlmAsync(
            $"Kullanıcı daha önce bilgisayarında okuduğu bir belgeye dair soru soruyor. Belge: \"{Display(entry)}\", en son {when} okudu. " +
            "Soruyu yalnızca aşağıdaki metne dayanarak, Türkçe ve kısa (en fazla 3 cümle) cevapla. Cevaba kaynağı söyleyerek başla " +
            $"(ör. '{TurkishGrammar.Capitalize(when)} okuduğun {Display(entry)} belgesine göre'). Metinde yoksa 'Belgede bununla ilgili bir " +
            "şey göremedim' de. Sayıları ve tarihleri metindeki gibi ver. Markdown yok.\nSoru: " + question.Trim(),
            FrontDocument.Relevant(text, query, 6000), cancellationToken).ConfigureAwait(false);
        return answer;
    }

    /// <summary>Türkçe sorunun belge dilindeki (İngilizce) arama sözcükleri.</summary>
    private async Task<string> ChatTermsAsync(string question, CancellationToken cancellationToken)
    {
        var terms = await chat.CompleteAsync(Settings.OllamaModel,
            "Give 6-10 English search keywords (single words, comma separated) that would find the answer to this question in an English document. Output only the keywords.",
            question, null, null, cancellationToken).ConfigureAwait(false);
        return terms.Replace('\n', ' ').Trim();
    }

    /// <summary>
    /// Soruya en uyan okunmuş belge: ad, özet ve anahtar sözcükler; bulunamazsa saklanan metinler. Soruda belgeyi anlatan
    /// bir sözcük yoksa ("dün okuduğum belgede tarih neydi") o zaman aralığında en çok okunan.
    /// </summary>
    private static ReadEntry? FindRead(ReadingLog log, string question, (DateOnly From, DateOnly To)? period)
    {
        if (log.Search(question, period) is [var best, ..])
        {
            return best.Entry;
        }

        var words = ReadingLog.QueryWords(question);
        var pool = period is { } range ? log.InPeriod(range.From, range.To) : log.Entries;
        if (words.Count > 0)
        {
            var stems = words.Select(word => word.Length > 5 ? word[..5] : word).ToList();
            var found = pool.Take(60)
                .Select(entry => (entry, hits: log.Text(entry) is { } text ? Fold(text) is var folded ? stems.Count(stem => folded.Contains(stem, StringComparison.Ordinal)) : 0 : 0))
                .Where(item => item.hits > 0)
                .OrderByDescending(item => item.hits).ThenByDescending(item => item.entry.LastSeen)
                .Select(item => item.entry).FirstOrDefault();
            if (found is not null)
            {
                return found;
            }
        }

        // "Dün okuduğum belgede tarih neydi": o gün en uzun okunan belge (İngilizce bir belgede Türkçe sözcük bulunmaz;
        // cevapta yoksa model "göremedim" der). "En son okuduğum belge": en son okunan.
        if (period is not null)
        {
            return pool.OrderByDescending(entry => entry.Seconds).ThenByDescending(entry => entry.LastSeen).FirstOrDefault();
        }

        return words.Count == 0 && pool.Count > 0 ? pool[0] : null;
    }

    /// <summary>
    /// "Bunun öncekinden farkı ne": öndeki (ya da adı verilen) belge, aynı dosyanın değişmeden önceki hâliyle ya da başka
    /// adla kaydedilmiş önceki sürümüyle ("teklif_v1.pdf") karşılaştırılır. Farkları kod bulur, model anlatır.
    /// </summary>
    private async Task<string> CompareVersionsAsync(ReadingLog log, string name, DateOnly today, CancellationToken cancellationToken)
    {
        ReadEntry? entry = null;
        if (name.Trim().Length > 0)
        {
            entry = FindRead(log, name, null);
        }
        else if (ActiveWindow is { } window && FrontDocument.FileName(window.Title) is { } front)
        {
            var path = log.ByName(front) is { } known && File.Exists(known.Path) ? known.Path : FindFrontFile(front);
            if (path is not null)
            {
                try
                {
                    // Dosya az önce kaydedildiyse yeni hâli şimdi okunur (eskisi önceki sürüm olarak kalır).
                    var (current, _) = log.Touch(path, DateTime.Now, 0);
                    await Task.Run(() => RememberText(log, current, path, null), cancellationToken).ConfigureAwait(false);
                    entry = current;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
                {
                    FridayLog.Warn($"Sürüm karşılaştırma: {exception.Message}");
                }
            }
        }

        if (entry is null || log.Text(entry) is not { } now)
        {
            return "Hangi belgeyi karşılaştıracağımı bulamadım; belgeyi öne getirip yeniden söyle.";
        }

        string? before = log.PreviousText(entry);
        var since = entry.Changed is { } changed ? $"{WhenRead(changed, today)} değişmeden önceki hâli" : "önceki hâli";
        if (before is null && log.EarlierVersion(entry) is { } older && log.Text(older) is { } olderText)
        {
            before = olderText;
            since = $"{WhenRead(older.LastSeen, today)} okuduğun önceki sürümü ({Display(older)})";
        }

        if (before is null)
        {
            return $"{Display(entry)} belgesinin daha önce okuduğun başka bir sürümünü bilmiyorum. Bir belgenin eski ve yeni hâline baktığında farkı söyleyebilirim.";
        }

        var changes = TextDiff.Compare(before, now);
        if (changes.IsEmpty)
        {
            return $"{Display(entry)} ile {since} arasında metin farkı görmedim.";
        }

        ShowCard(new AnswerCard("Sürüm farkı", "")
        {
            Rows = [new CardRow("Değişen", changes.Changed.Count.ToString(CultureInfo.InvariantCulture)),
                new CardRow("Eklenen", changes.Added.Count.ToString(CultureInfo.InvariantCulture)),
                new CardRow("Çıkarılan", changes.Removed.Count.ToString(CultureInfo.InvariantCulture))]
        });
        ledger.Record(DataFlow.Local, "Okuduklarım", $"{entry.Name} önceki sürümüyle karşılaştırıldı");
        return await FrontLlmAsync(
            $"Kullanıcı \"{Display(entry)}\" belgesinin yeni hâliyle {since} arasındaki farkı soruyor. Aşağıdaki farklar kodla bulundu " +
            "(DEĞİŞTİ: eski → yeni, EKLENDİ, ÇIKARILDI). Türkçe, en fazla 5 cümleyle en önemli değişiklikleri anlat: önce tutar, tarih, " +
            "süre ve şart değişiklikleri; yazım düzeltmelerini atla. Belge İngilizceyse alıntılamadan Türkçe anlat. Markdown yok.",
            changes.Describe(5000), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serbest bir soru okunmuş bir belgeye değiniyorsa ("Mavi Yazılım'ın teklifi ne kadardı", "Jane'in kira sözleşmesi ne
    /// zaman bitiyor") kullanıcı "okuduğum" demese de belge modele hatırlatılır. Yalnızca güçlü eşleşmeler (ad ya da en az iki
    /// anahtar sözcük); yoksa boş.
    /// </summary>
    public string ReadingContext(string question)
    {
        if (Reading is not { } log || !Settings.ReadingMemory)
        {
            return "";
        }

        var hits = log.Search(question, null).Where(hit => hit.Score >= 2 && hit.Entry.Gist.Length > 0).Take(2).ToList();
        if (hits.Count == 0)
        {
            return "";
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        return "\n[Kullanıcının bilgisayarında okuduğu belgelerden bu soruyla ilgili olabilecekler: "
            + string.Join(" | ", hits.Select(hit => $"{hit.Entry.Name} ({WhenRead(hit.Entry.LastSeen, today)} okudu): {hit.Entry.Gist}"))
            + ". Soru bunlardan birine dairse cevabı okuduklarim aracıyla (islem=sor) belgenin kendisinden al.]";
    }

    /// <summary>"Bu ne" cevabına eklenen not: bu belgenin önceki bir sürümü okunmuşsa.</summary>
    private string VersionNote(string name)
    {
        if (Reading is not { } log || name.Length == 0 || log.ByName(name) is not { } entry)
        {
            return "";
        }

        return log.PreviousText(entry) is not null || log.EarlierVersion(entry) is not null
            ? " Bu belgenin önceki bir sürümünü de okumuştun; \"farkı ne\" dersen nelerin değiştiğini söylerim."
            : "";
    }

    /// <summary>"rental-agreement.pdf" → "rental agreement".</summary>
    private static string Display(ReadEntry entry) =>
        Path.GetFileNameWithoutExtension(entry.Name).Replace('_', ' ').Replace('-', ' ').Trim();

    /// <summary>"bugün", "dün", "salı günü", "12 Kasım'da".</summary>
    private static string WhenRead(DateTime at, DateOnly today)
    {
        var day = DateOnly.FromDateTime(at);
        return (today.DayNumber - day.DayNumber) switch
        {
            0 => "bugün",
            1 => "dün",
            < 7 => day.ToString("dddd", Turkish) + " günü",
            _ => day.ToString("d MMMM", Turkish) + " tarihinde"
        };
    }

    /// <summary>İlk cümle ("4.2 milyon" gibi sayılardaki noktada kesilmez).</summary>
    private static string FirstSentence(string text)
    {
        foreach (var stop in new[] { ". ", "! ", "? ", "; " })
        {
            var end = text.IndexOf(stop, StringComparison.Ordinal);
            if (end > 20)
            {
                return text[..end] + ".";
            }
        }

        return text;
    }

    private static string Fold(string text) => text.ToLower(Turkish)
        .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
}
