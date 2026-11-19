namespace Friday.Core;

/// <summary>Kelime pratiğinin hazır destesi: günlük hayatta, işte ve okurken sık geçen 110 İngilizce kelime.</summary>
public static class StarterWords
{
    public static IReadOnlyList<(string Front, string Back)> All { get; } =
    [
        ("reluctant", "isteksiz, gönülsüz"), ("eager", "istekli, hevesli"), ("thorough", "titiz, eksiksiz, ayrıntılı"),
        ("achieve", "başarmak, elde etmek"), ("approach", "yaklaşım, yaklaşmak"), ("assume", "varsaymak, farz etmek"),
        ("avoid", "kaçınmak, sakınmak"), ("aware", "farkında, haberdar"), ("benefit", "fayda, yarar"),
        ("brief", "kısa, öz"), ("challenge", "zorluk, meydan okumak"), ("claim", "iddia etmek, iddia"),
        ("consider", "düşünmek, göz önünde bulundurmak"), ("consistent", "tutarlı"), ("crucial", "çok önemli, hayati"),
        ("decline", "azalmak, reddetmek, düşüş"), ("deny", "inkar etmek, reddetmek"), ("determine", "belirlemek, saptamak"),
        ("efficient", "verimli, etkili"), ("emphasize", "vurgulamak"), ("encourage", "cesaretlendirmek, teşvik etmek"),
        ("estimate", "tahmin etmek, tahmin"), ("evidence", "kanıt, delil"), ("evolve", "gelişmek, evrimleşmek"),
        ("feasible", "uygulanabilir, mümkün"), ("frequent", "sık, sık sık olan"), ("genuine", "gerçek, samimi, hakiki"),
        ("grateful", "minnettar, müteşekkir"), ("hesitate", "tereddüt etmek, duraksamak"), ("ignore", "görmezden gelmek, göz ardı etmek"),
        ("improve", "geliştirmek, iyileştirmek"), ("inevitable", "kaçınılmaz"), ("influence", "etki, etkilemek"),
        ("insist", "ısrar etmek"), ("maintain", "sürdürmek, korumak, bakımını yapmak"), ("obvious", "açık, belli, apaçık"),
        ("obtain", "elde etmek, edinmek"), ("occur", "meydana gelmek, olmak"), ("opportunity", "fırsat"),
        ("persuade", "ikna etmek"), ("precise", "kesin, tam, net"), ("prevent", "önlemek, engellemek"),
        ("previous", "önceki"), ("purpose", "amaç, maksat"), ("pursue", "peşinden gitmek, sürdürmek, takip etmek"),
        ("recognize", "tanımak, fark etmek"), ("reduce", "azaltmak"), ("rely", "güvenmek, bel bağlamak"),
        ("require", "gerektirmek, istemek"), ("reveal", "ortaya çıkarmak, açığa vurmak"), ("significant", "önemli, kayda değer"),
        ("struggle", "mücadele etmek, uğraşmak, mücadele"), ("sufficient", "yeterli"), ("suggest", "önermek"),
        ("tend", "eğiliminde olmak, meyletmek"), ("thrive", "gelişmek, serpilmek, başarılı olmak"), ("vague", "belirsiz, muğlak"),
        ("various", "çeşitli"), ("wisdom", "bilgelik"), ("withdraw", "geri çekmek, çekilmek, para çekmek"),
        ("abundant", "bol, bereketli"), ("accurate", "doğru, kesin, isabetli"), ("adequate", "yeterli"),
        ("anticipate", "beklemek, öngörmek"), ("appreciate", "takdir etmek, değerini bilmek"), ("ambiguous", "belirsiz, muğlak, iki anlamlı"),
        ("attempt", "girişim, denemek, teşebbüs"), ("cautious", "dikkatli, temkinli"), ("coincidence", "tesadüf, rastlantı"),
        ("comprehensive", "kapsamlı"), ("confident", "kendinden emin, özgüvenli"), ("convenient", "uygun, elverişli"),
        ("curious", "meraklı"), ("deliberate", "kasıtlı, bilerek"), ("diligent", "çalışkan, gayretli"),
        ("distinguish", "ayırt etmek"), ("enormous", "muazzam, devasa, kocaman"), ("essential", "temel, gerekli, zorunlu"),
        ("exhausted", "bitkin, çok yorgun"), ("flexible", "esnek"), ("generous", "cömert"),
        ("humble", "alçakgönüllü, mütevazı"), ("inspire", "ilham vermek"), ("jealous", "kıskanç"),
        ("negotiate", "pazarlık etmek, müzakere etmek"), ("overcome", "üstesinden gelmek, yenmek"), ("patience", "sabır"),
        ("postpone", "ertelemek"), ("reliable", "güvenilir"), ("resilient", "dayanıklı, dirençli"),
        ("sincere", "içten, samimi"), ("spontaneous", "kendiliğinden, doğaçlama, plansız"), ("subtle", "ince, belli belirsiz"),
        ("tremendous", "muazzam, çok büyük"), ("urge", "dürtü, ısrarla istemek, teşvik etmek"), ("versatile", "çok yönlü"),
        ("vulnerable", "savunmasız, kırılgan, hassas"), ("worthwhile", "değer, zahmete değer, değerli"), ("serendipity", "mutlu tesadüf, şans eseri bulma"),
        ("procrastinate", "ertelemek, işi ağırdan almak"), ("meanwhile", "bu arada, o sırada"), ("nevertheless", "yine de, buna rağmen"),
        ("therefore", "bu yüzden, bu nedenle"), ("although", "rağmen, gerçi"), ("whereas", "oysa, halbuki"),
        ("likely", "muhtemel, olası, muhtemelen"), ("rarely", "nadiren"), ("gradually", "yavaş yavaş, giderek, kademeli olarak"),
        ("eventually", "sonunda, nihayetinde"), ("apparently", "görünüşe göre, anlaşılan")
    ];
}
