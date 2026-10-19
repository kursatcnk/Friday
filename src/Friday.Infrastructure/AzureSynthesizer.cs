using System.Globalization;
using System.Security;
using Microsoft.CognitiveServices.Speech;

namespace Friday.Infrastructure;

/// <summary>
/// Azure Speech'in resmî nöral sesleri (tr-TR-EmelNeural, tr-TR-AhmetNeural). Ses parça parça gelir ve hemen
/// çalınır. Azure'a yalnızca okunacak cevap metni gider; mikrofon sesi gitmez.
/// </summary>
internal sealed class AzureSynthesizer : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer;
    private readonly string _voice;
    private readonly string _rate;
    private Action<byte[]>? _sink;

    public AzureSynthesizer(string key, string region, string voice, double speed)
    {
        var config = SpeechConfig.FromSubscription(key, region);
        config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm);
        // AudioConfig verilmez: ses hoparlöre değil, Friday'in kendi çıkışına (seviye ölçer, kesme) akar.
        _synthesizer = new SpeechSynthesizer(config, null);
        _synthesizer.Synthesizing += (_, e) => _sink?.Invoke(e.Result.AudioData);
        _voice = voice;
        var percent = (int)Math.Round((Math.Clamp(speed, 0.5, 2) - 1) * 100);
        _rate = (percent >= 0 ? "+" : "") + percent.ToString(CultureInfo.InvariantCulture) + "%";
    }

    public static int SampleRate => 24_000;

    /// <summary>Bağlantıyı önceden açar; ilk cümlede el sıkışma beklenmez.</summary>
    public void WarmUp() => Connection.FromSpeechSynthesizer(_synthesizer).Open(true);

    public async Task SynthesizeAsync(string text, Action<byte[]> sink, CancellationToken cancellationToken)
    {
        var ssml =
            $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='tr-TR'>" +
            $"<voice name='{_voice}'><prosody rate='{_rate}'>{SecurityElement.Escape(text)}</prosody></voice></speak>";
        _sink = sink;
        using var registration = cancellationToken.Register(() => _ = _synthesizer.StopSpeakingAsync());
        try
        {
            using var result = await _synthesizer.SpeakSsmlAsync(ssml).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Reason == ResultReason.Canceled)
            {
                var details = SpeechSynthesisCancellationDetails.FromResult(result);
                throw new InvalidOperationException($"Azure ses hatası: {details.Reason} {details.ErrorDetails}");
            }
        }
        finally
        {
            _sink = null;
        }
    }

    public void Dispose() => _synthesizer.Dispose();
}
