namespace Friday.Core;

/// <summary>
/// Sentetik sesi daha doğal duyurmak için hafif işlem: alçak gürültüyü keser, sesi biraz ısıtır ve çok hafif bir oda
/// yankısı ekler. Yankısız "kuru" ses kulağa yapay gelir; gerçek konuşma her zaman bir odada duyulur. Etki bilerek
/// küçük tutulur; amaç sesi değiştirmek değil, kuruluğunu almak.
/// </summary>
public static class VoiceWarmth
{
    private static readonly double[] CombDelays = [0.0297, 0.0371, 0.0411, 0.0437];
    private static readonly double[] AllPassDelays = [0.005, 0.0017];

    /// <param name="samples">16 bit mono PCM.</param>
    /// <param name="wet">Yankı oranı (0–1); 0 yalnızca ton düzeltmesi yapar.</param>
    /// <returns>Yankının kuyruğu için ~150 ms uzatılmış ses.</returns>
    public static short[] Process(ReadOnlySpan<short> samples, int sampleRate, double wet = 0.07, double warmth = 0.12)
    {
        var tail = (int)(sampleRate * 0.15);
        var input = new double[samples.Length + tail];
        for (var index = 0; index < samples.Length; index++)
        {
            input[index] = samples[index] / 32768.0;
        }

        HighPass(input, sampleRate, 70);
        Warm(input, sampleRate, warmth);
        var room = wet > 0 ? Room(input, sampleRate) : new double[input.Length];

        var output = new short[input.Length];
        for (var index = 0; index < input.Length; index++)
        {
            var value = (input[index] * (1 - (wet * 0.5))) + (room[index] * wet);
            output[index] = (short)Math.Round(SoftLimit(value) * 32767);
        }

        return output;
    }

    /// <summary>
    /// 0,8'e kadar sesi olduğu gibi bırakır; üstündeki tepeleri yumuşakça bastırır. Sert kırpma (cızırtı) olmaz,
    /// normal seviyede ses yükseltilmez.
    /// </summary>
    private static double SoftLimit(double value)
    {
        const double knee = 0.8;
        var magnitude = Math.Abs(value);
        return magnitude <= knee
            ? value
            : Math.Sign(value) * (knee + ((1 - knee) * Math.Tanh((magnitude - knee) / (1 - knee))));
    }

    /// <summary>Birinci derece yüksek geçiren süzgeç: gürültü ve uğultuyu atar.</summary>
    private static void HighPass(double[] signal, int sampleRate, double cutoff)
    {
        var rc = 1 / (2 * Math.PI * cutoff);
        var alpha = rc / (rc + (1.0 / sampleRate));
        double previousInput = 0, previousOutput = 0;
        for (var index = 0; index < signal.Length; index++)
        {
            var current = signal[index];
            var filtered = alpha * (previousOutput + current - previousInput);
            previousInput = current;
            previousOutput = filtered;
            signal[index] = filtered;
        }
    }

    /// <summary>~250 Hz altına hafif bir gövde ekler; sentetik seslerin "ince" duyulmasını azaltır.</summary>
    private static void Warm(double[] signal, int sampleRate, double amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var alpha = 1 - Math.Exp(-2 * Math.PI * 250 / sampleRate);
        double low = 0;
        for (var index = 0; index < signal.Length; index++)
        {
            low += alpha * (signal[index] - low);
            signal[index] += low * amount;
        }
    }

    /// <summary>Schroeder yankısı: dört paralel tarak ve iki tüm geçiren süzgeç, küçük bir oda gibi.</summary>
    private static double[] Room(double[] input, int sampleRate)
    {
        var sum = new double[input.Length];
        foreach (var delay in CombDelays)
        {
            var length = (int)(delay * sampleRate);
            var buffer = new double[length];
            var position = 0;
            double damped = 0;
            for (var index = 0; index < input.Length; index++)
            {
                var delayed = buffer[position];
                // Geri beslemedeki alçak geçiren, yansımaları gerçek bir odadaki gibi yumuşatır.
                damped = (delayed * 0.6) + (damped * 0.4);
                buffer[position] = input[index] + (damped * 0.62);
                position = (position + 1) % length;
                sum[index] += delayed * 0.25;
            }
        }

        foreach (var delay in AllPassDelays)
        {
            var length = Math.Max(1, (int)(delay * sampleRate));
            var buffer = new double[length];
            var position = 0;
            for (var index = 0; index < sum.Length; index++)
            {
                var delayed = buffer[position];
                var value = -0.7 * sum[index] + delayed;
                buffer[position] = sum[index] + (0.7 * value);
                position = (position + 1) % length;
                sum[index] = value;
            }
        }

        return sum;
    }
}
