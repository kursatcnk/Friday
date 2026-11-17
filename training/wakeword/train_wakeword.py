"""
Friday'in "Hey Friday" uyandırma kelimesini eğitir.

openWakeWord'ün hazır özellik çıkarıcısı (melspectrogram.onnx + embedding_model.onnx) aynen kullanılır; yalnızca son
katman olan küçük sınıflandırıcı (16 × 96 → 1) eğitilir. Böylece model Friday'deki WakeWordEngine ile birebir uyumludur.

Veri tamamen bu bilgisayarda üretilir:
  * Olumlu örnekler: Piper'ın çok konuşmacılı İngilizce sesleri (1.013 konuşmacı) "Hey Friday" der; Türkçe aksan için
    Türkçe Piper sesleri "Hey Fraydey" der. Her örnek gürültü, yankı, mikrofon bant genişliği ve ses seviyesiyle bozulur.
  * Olumsuz örnekler: benzer sesli ifadeler ("hey Fred", "it's Friday", "Hey Ferhat"…), Türkçe ve İngilizce binlerce
    rastgele cümle, saf gürültü ve openWakeWord'ün 10,7 saatlik gerçek ses doğrulama verisinin %70'i.
  * Ölçüm: hiç görülmemiş konuşmacılarla yakalama oranı ve doğrulama verisinin ayrılmış %30'unda saatte yanlış uyanma.

Kullanım (ortamı D:'ye yönlendiren betikle): training\\train-wakeword.ps1 [generate|train|all]
"""

from __future__ import annotations

import argparse
import importlib
import json
import math
import os
import random
import re
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np

ROOT = Path(os.environ.get("FRIDAY_TRAINING", r"D:\Friday\training"))
FRIDAY = Path(os.environ.get("FRIDAY_HOME", r"D:\Friday"))
REPO = Path(__file__).resolve().parents[2]
PIPER = FRIDAY / "tools" / "piper" / "piper.exe"
FEATURE_MODELS = FRIDAY / "models" / "wakeword"
CLIPS = ROOT / "clips"
CACHE = ROOT / "cache"
SR = 16_000
WINDOW = 16  # sınıflandırıcının baktığı gömme sayısı (~1,3 sn)

EN_VOICES = {
    "libritts": (ROOT / "voices" / "en_US-libritts_r-medium.onnx", 904),
    "vctk": (ROOT / "voices" / "en_GB-vctk-medium.onnx", 109),
}
TR_VOICES = [FRIDAY / "models" / "piper" / "tr_TR-99eren99.onnx", FRIDAY / "models" / "piper" / "tr_TR-dfki-medium.onnx"]

POSITIVE_EN = ["Hey Friday", "Hey Friday.", "Hey, Friday!", "Hey Friday?", "Hey Friday!", "hey friday,"]
POSITIVE_TR = ["Hey Fraydey", "Hey Fraydey!", "Hey, Fraydey.", "Hey Fraydi", "Hey Frayday", "Hey Friday", "Hey Fraydey?", "Hey Frayde"]
HELD_OUT_TR = {"Hey Frayde", "Hey Fraydey?"}

ADVERSARIAL_EN = [
    "Friday", "It's Friday", "on Friday", "every Friday night", "Thank god it's Friday", "Friday the thirteenth", "see you Friday",
    "Friday afternoon", "hey Fred", "hey Freddie", "hey Frida", "hey fry", "fried rice", "hey friend", "hey there", "hey Ray",
    "high five", "hey Siri", "hey Jarvis", "hey Alexa", "okay Google", "hey Monday", "hey Sunday", "hey Thursday", "free day",
    "payday", "hey fridge", "hey Freya", "hey Ryder", "hey Kyle", "hey buddy", "hey guys", "fried egg", "they fried it",
    "hey Friedman", "hey Fiona", "hey Freddy, how are you", "my Friday plans", "a fried day", "hey frightened", "hey Frank",
    "hey Brian", "hey Sky", "hey wait", "hey why", "hey, fine day", "hey, fly away", "hey, try it", "hey, write it",
]
ADVERSARIAL_TR = [
    "Hey Ferhat", "Hey Fırat", "Hey Ferdi", "Hey Fatih", "Ey Fatih", "Hey Ayda", "Hey Fikri", "Hey Feride", "Hey Freddy", "Frida",
    "Cuma günü", "Cuma", "fırında", "hayırdır", "hey gidi günler", "hay Allah", "Hey Fehmi", "Hey Fadime", "Hey Ferda", "hey Rıdvan",
    "hey Haydar", "Hey Faruk", "hey Ferit", "Fraydey", "Hey Hayri", "Hey Fırıldak", "hey kardeşim", "hey dostum", "hey Yaprak",
    "Ey Ferdi", "Hey Fevzi", "hey Fazıl", "hey Feyza", "hey Ferdane", "Hey Frida", "ayy fıydı", "hey Jarvis",
]

# "Friday" tek başına uyandırmamalı: cümlenin her yerinde, her bağlamda geçer.
FRIDAY_CONTEXTS = [
    "on Friday", "see you on Friday", "see you Friday", "next Friday", "last Friday", "this Friday", "every Friday",
    "Friday night", "Friday morning", "Friday afternoon", "Friday evening", "on Friday night", "by Friday", "until Friday",
    "Good Friday", "Black Friday", "Friday the thirteenth", "it's Friday", "it is Friday", "thank god it's Friday",
    "happy Friday", "Friday is my favorite day", "we meet on Friday", "the deadline is Friday", "let's do it on Friday",
    "my Friday plans", "a Friday", "Friday", "Fridays are busy", "I'll call you on Friday", "the party is on Friday",
    "Friday, the twentieth", "they said Friday", "maybe Friday", "Friday works for me", "no, Friday", "so, Friday",
    "hey, it's Friday", "hey, see you Friday", "hey, the meeting is on Friday", "okay, Friday then",
]
HEY_NAMES = [
    "Fred", "Freddie", "Freddy", "Frida", "Frank", "Frankie", "Freya", "Fergie", "Friedman", "Brian", "Ryan", "Riley", "Ray",
    "Rider", "Fry", "Frey", "Frye", "Fran", "Fritz", "Frodo", "Fiona", "Fido", "Freda", "Bridget", "Brady", "Grady", "Heidi",
    "Sadie", "Lady", "Daddy", "buddy", "Siri", "Jarvis", "Alexa", "Google", "Monday", "Sunday", "Thursday", "Saturday",
    "fries", "fried", "friend", "fridge", "fright", "Friday's", "Fraser", "Frazier", "Florida",
]
TR_FRIDAY_CONTEXTS = [
    "Cuma günü görüşürüz", "Cumartesi", "Hey Ferhat", "Hey Fırat", "Hey Ferdi", "Hey Fatih", "Hey Fikri", "Hey Feride",
    "Hey Fehmi", "Hey Fadime", "Hey Ferda", "Hey Faruk", "Hey Ferit", "Hey Hayri", "Hey Feyza", "Hey Fevzi", "Hey Freddy",
    "Fraydey", "Fraydey akşamı", "geçen fraydey", "Hey Frida", "Hey Rıdvan", "Hey Haydar", "hey kardeşim", "hey dostum",
    "Fırıldak", "hayırdır", "Ey Fatih", "Hey Aydın", "Hey Fikret",
]

STDLIB = [
    "os", "re", "json", "collections", "itertools", "functools", "pathlib", "asyncio", "logging", "argparse", "datetime",
    "statistics", "random", "string", "textwrap", "unittest", "typing", "decimal", "fractions", "heapq", "bisect", "csv",
    "sqlite3", "zipfile", "tarfile", "shutil", "subprocess", "threading", "queue", "socket", "hashlib", "secrets", "uuid",
    "calendar", "time", "inspect", "pickle", "copy", "pprint", "enum", "dataclasses", "contextlib", "io", "tempfile", "glob",
    "locale", "base64", "struct", "codecs", "difflib", "html", "email", "http.client", "urllib.request", "wave", "colorsys",
]


# ----------------------------------------------------------------------------------------------------------------------
# Metin kaynakları
# ----------------------------------------------------------------------------------------------------------------------

def english_sentences(limit: int, rng: random.Random) -> list[str]:
    """Python standart kitaplığının belge metinlerinden düzgün İngilizce cümleler."""
    found: set[str] = set()
    for name in STDLIB:
        try:
            module = importlib.import_module(name)
        except Exception:  # noqa: BLE001 - bazı modüller bu sistemde olmayabilir
            continue
        texts = [module.__doc__ or ""]
        for item in vars(module).values():
            doc = getattr(item, "__doc__", None)
            if isinstance(doc, str):
                texts.append(doc)
        for text in texts:
            for sentence in re.split(r"(?<=[.!?])\s+", " ".join(text.split())):
                if 4 <= len(sentence.split()) <= 22 and re.fullmatch(r"[A-Za-z ,.'!?;-]+", sentence):
                    found.add(sentence)
    sentences = sorted(found)
    rng.shuffle(sentences)
    return sentences[:limit]


def turkish_sentences(limit: int, rng: random.Random) -> list[str]:
    """Friday'in kendi Türkçe metinleri: README ve koddaki açıklamalar."""
    texts = [(REPO / "README.md").read_text(encoding="utf-8")]
    for file in (REPO / "src").rglob("*.cs"):
        for line in file.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line.startswith("///") or line.startswith("//"):
                texts.append(re.sub(r"<[^>]+>", "", line.lstrip("/ ")))
    found: set[str] = set()
    for text in texts:
        for sentence in re.split(r"(?<=[.!?])\s+", " ".join(text.split())):
            sentence = sentence.strip(" -*|#`")
            if 4 <= len(sentence.split()) <= 20 and re.fullmatch(r"[A-Za-zÇĞİÖŞÜçğıöşüâî ,.'!?;:-]+", sentence):
                found.add(sentence)
    sentences = sorted(found)
    rng.shuffle(sentences)
    return sentences[:limit]


# ----------------------------------------------------------------------------------------------------------------------
# Ses üretimi (Piper)
# ----------------------------------------------------------------------------------------------------------------------

def piper(model: Path, lines: list[dict], length: float, noise: float = 0.667, noise_w: float = 0.8) -> None:
    """Tek bir Piper sürecine çok satır verir; her satır kendi konuşmacısı ve dosyasıyla üretilir."""
    todo = [line for line in lines if not Path(line["output_file"]).exists()]
    if not todo:
        return
    for line in todo:
        Path(line["output_file"]).parent.mkdir(parents=True, exist_ok=True)
    payload = "\n".join(json.dumps(line, ensure_ascii=False) for line in todo) + "\n"
    subprocess.run(
        [str(PIPER), "--model", str(model), "--json-input", "--length_scale", f"{length:.2f}",
         "--noise_scale", f"{noise:.3f}", "--noise_w", f"{noise_w:.3f}", "--sentence_silence", "0.1"],
        input=payload.encode("utf-8"), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True, cwd=str(PIPER.parent))


def generate(seed: int) -> None:
    rng = random.Random(seed)
    english = english_sentences(2600, rng)
    turkish = turkish_sentences(900, rng)
    print(f"cümleler: {len(english)} İngilizce, {len(turkish)} Türkçe")

    def out(kind: str, name: str) -> str:
        return str(CLIPS / kind / f"{name}.wav")

    # Olumlu: İngilizce konuşmacılar (son konuşmacılar ölçüm için ayrılır).
    for voice, (model, speakers) in EN_VOICES.items():
        for length in (0.85, 1.0, 1.2):
            lines = []
            for speaker in range(speakers):
                for _ in range(2 if voice == "libritts" else 3):
                    text = rng.choice(POSITIVE_EN)
                    lines.append({"text": text, "speaker_id": speaker,
                                  "output_file": out("positive", f"{voice}-{speaker:03d}-{length:.2f}-{rng.randrange(10**6):06d}")})
            piper(model, lines, length, noise=rng.choice((0.5, 0.667, 0.8)), noise_w=rng.choice((0.6, 0.8, 1.0)))
        print(f"olumlu {voice} tamam")

    # Olumlu: Türkçe aksan.
    for index, model in enumerate(v for v in TR_VOICES if v.exists()):
        for length in (0.75, 0.85, 0.95, 1.05, 1.15, 1.3):
            for noise in (0.5, 0.667, 0.85):
                for noise_w in (0.6, 0.9):
                    lines = [{"text": text, "output_file": out("positive", f"tr{index}-{length:.2f}-{noise:.2f}-{noise_w:.1f}-{i}" + ("-heldout" if text in HELD_OUT_TR else ""))}
                             for i, text in enumerate(POSITIVE_TR)]
                    piper(model, lines, length, noise, noise_w)
    print("olumlu Türkçe tamam")

    # Olumsuz: benzer sesler ve sıradan konuşma.
    for voice, (model, speakers) in EN_VOICES.items():
        count = 900 if voice == "libritts" else 300
        lines = [{"text": rng.choice(ADVERSARIAL_EN), "speaker_id": rng.randrange(speakers), "output_file": out("adversarial", f"{voice}-{i:04d}")} for i in range(count)]
        piper(model, lines, rng.choice((0.9, 1.0, 1.1)))
        talk = english[: 1100 if voice == "libritts" else 600]
        lines = [{"text": text, "speaker_id": rng.randrange(speakers), "output_file": out("speech", f"{voice}-{i:04d}")} for i, text in enumerate(talk)]
        piper(model, lines, 1.0)
        english = english[len(talk):]
    for index, model in enumerate(v for v in TR_VOICES if v.exists()):
        for length in (0.85, 1.0, 1.15):
            lines = [{"text": text, "output_file": out("adversarial", f"tr{index}-{length:.2f}-{i:03d}")} for i, text in enumerate(ADVERSARIAL_TR)]
            piper(model, lines, length)
        lines = [{"text": text, "output_file": out("speech", f"tr{index}-{i:04d}")} for i, text in enumerate(turkish)]
        piper(model, lines, 1.0)
    print("olumsuz tamam")


# ----------------------------------------------------------------------------------------------------------------------
# Ses işleme ve bozma
# ----------------------------------------------------------------------------------------------------------------------

def read_wav(path: Path) -> np.ndarray:
    """16 kHz tek kanal float (−1…1); Piper'ın 22 kHz çıkışı yeniden örneklenir."""
    from scipy.signal import resample_poly

    with wave.open(str(path), "rb") as file:
        rate = file.getframerate()
        data = np.frombuffer(file.readframes(file.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    if rate != SR:
        divisor = math.gcd(rate, SR)
        data = resample_poly(data, SR // divisor, rate // divisor).astype(np.float32)
    return data


def trim(audio: np.ndarray) -> np.ndarray:
    """Baş ve sondaki sessizliği atar (kelimenin bittiği an önemli)."""
    frame = 160
    energy = np.sqrt(np.convolve(audio ** 2, np.ones(frame) / frame, mode="same"))
    loud = np.where(energy > max(0.01, energy.max() * 0.05))[0]
    return audio[loud[0]: loud[-1] + 1] if len(loud) else audio


def colored_noise(length: int, rng: np.random.Generator, kind: str) -> np.ndarray:
    white = rng.standard_normal(length).astype(np.float32)
    if kind == "white":
        return white
    spectrum = np.fft.rfft(white)
    frequencies = np.maximum(np.fft.rfftfreq(length), 1e-4)
    spectrum /= np.sqrt(frequencies) if kind == "pink" else frequencies
    noise = np.fft.irfft(spectrum, n=length).astype(np.float32)
    return noise / (np.abs(noise).max() + 1e-6)


def background(length: int, rng: np.random.Generator, speech: list[np.ndarray]) -> np.ndarray:
    kind = rng.choice(["white", "pink", "brown", "babble", "babble", "hum", "fan"])
    if kind == "babble" and speech:
        mix = np.zeros(length, dtype=np.float32)
        for _ in range(rng.integers(2, 5)):
            clip = speech[rng.integers(len(speech))]
            offset = rng.integers(-len(clip) // 2, length)
            start, end = max(0, offset), min(length, offset + len(clip))
            if end > start:
                mix[start:end] += clip[start - offset: end - offset] * rng.uniform(0.3, 1.0)
        return mix
    if kind == "hum":
        t = np.arange(length) / SR
        base = rng.choice([50.0, 60.0])
        return sum(np.sin(2 * np.pi * base * h * t) / h for h in range(1, 6)).astype(np.float32)
    if kind == "fan":
        from scipy.signal import butter, sosfilt

        return sosfilt(butter(4, rng.uniform(300, 1500), "lowpass", fs=SR, output="sos"), colored_noise(length, rng, "white")).astype(np.float32)
    return colored_noise(length, rng, kind)


def augment(audio: np.ndarray, rng: np.random.Generator, noise: np.ndarray) -> np.ndarray:
    """Oda yankısı, mikrofon bant genişliği, gürültü ve ses seviyesi."""
    from scipy.signal import butter, fftconvolve, sosfilt

    out = audio.copy()
    if rng.random() < 0.5:
        rt60 = rng.uniform(0.12, 0.7)
        t = np.arange(int(SR * rt60)) / SR
        ir = rng.standard_normal(len(t)).astype(np.float32) * np.exp(-6.9 * t / rt60).astype(np.float32) * rng.uniform(0.1, 0.5)
        ir[0] = 1.0
        out = fftconvolve(out, ir)[: len(out)].astype(np.float32)
    if rng.random() < 0.4:
        out = sosfilt(butter(4, rng.uniform(3400, 7500), "lowpass", fs=SR, output="sos"), out).astype(np.float32)
    if rng.random() < 0.85:
        signal = np.sqrt(np.mean(out ** 2) + 1e-9)
        level = np.sqrt(np.mean(noise ** 2) + 1e-9)
        snr = rng.uniform(3, 28)
        out = out + noise * (signal / level) / (10 ** (snr / 20))
    peak = np.abs(out).max() + 1e-6
    return (out / peak * rng.uniform(0.03, 0.9)).astype(np.float32)


# ----------------------------------------------------------------------------------------------------------------------
# openWakeWord özellikleri (Friday'deki WakeWordEngine ile aynı modeller)
# ----------------------------------------------------------------------------------------------------------------------

class Features:
    def __init__(self) -> None:
        import onnxruntime as ort

        options = ort.SessionOptions()
        options.intra_op_num_threads = max(1, (os.cpu_count() or 4) - 2)
        self.mel = ort.InferenceSession(str(FEATURE_MODELS / "melspectrogram.onnx"), options, providers=["CPUExecutionProvider"])
        self.embedding = ort.InferenceSession(str(FEATURE_MODELS / "embedding_model.onnx"), options, providers=["CPUExecutionProvider"])
        self.mel_input = self.mel.get_inputs()[0].name
        self.embedding_input = self.embedding.get_inputs()[0].name

    def __call__(self, audio: np.ndarray) -> np.ndarray:
        """Ses (−1…1) → gömme dizisi [n, 96], 80 ms adımlarla."""
        scaled = (np.clip(audio, -1, 1) * 32767).astype(np.float32)[None, :]
        mel = np.squeeze(self.mel.run(None, {self.mel_input: scaled})[0]) / 10 + 2
        count = (len(mel) - 76) // 8 + 1
        if count <= 0:
            return np.zeros((0, 96), dtype=np.float32)
        windows = np.stack([mel[i * 8: i * 8 + 76] for i in range(count)])[:, :, :, None].astype(np.float32)
        return self.embedding.run(None, {self.embedding_input: windows})[0].reshape(count, 96)


def windows(embeddings: np.ndarray) -> np.ndarray:
    if len(embeddings) < WINDOW:
        return np.zeros((0, WINDOW, 96), dtype=np.float32)
    return np.lib.stride_tricks.sliding_window_view(embeddings, (WINDOW, 96)).reshape(-1, WINDOW, 96).copy()


def build(seed: int) -> None:
    """Klipleri bozar, özelliklerini çıkarır ve eğitim/ölçüm dizilerini önbelleğe yazar."""
    rng = np.random.default_rng(seed)
    features = Features()
    CACHE.mkdir(parents=True, exist_ok=True)
    speech = [trim(read_wav(path)) for path in sorted((CLIPS / "speech").glob("*.wav"))]
    print(f"konuşma klipleri: {len(speech)}")

    train_pos, test_pos_streams, train_neg = [], [], []
    positives = sorted((CLIPS / "positive").glob("*.wav"))
    for index, path in enumerate(positives):
        name = path.stem
        held_out = ("-heldout" in name or (name.startswith("libritts-") and int(name.split("-")[1]) >= 820)
                    or (name.startswith("vctk-") and int(name.split("-")[1]) >= 98))
        word = trim(read_wav(path))
        if held_out:
            # Ölçüm: kelime 2,0 sn'de biter (16 gömmelik ilk pencere 1,96 sn'de dolar), ardından 1 sn gürültü;
            # Friday'in akışında olduğu gibi taranır.
            total = int(SR * 3.0)
            buffer = np.zeros(total, dtype=np.float32)
            end = int(SR * 2.0)
            buffer[max(0, end - len(word)): end] = word[-end:]
            noisy = augment(buffer, rng, background(total, rng, speech))
            test_pos_streams.append((features(noisy), name))
            continue
        for _ in range(3 if name.startswith("tr") else 2):
            total = int(SR * 3.0)
            jitter = int(SR * rng.uniform(0.0, 0.3))
            buffer = np.zeros(total, dtype=np.float32)
            end = total - jitter
            buffer[max(0, end - len(word)): end] = word[-end:]
            embeddings = features(augment(buffer, rng, background(total, rng, speech)))
            if len(embeddings) >= WINDOW:
                train_pos.append(embeddings[-WINDOW:])
                # Kelimenin bitmesinden önceki pencereler olumsuzdur: "hey fri…" henüz uyandırmamalı.
                early = len(embeddings) - WINDOW - int(0.45 * 12.5) - jitter // 1280
                if early >= 0:
                    train_neg.append(embeddings[early: early + WINDOW])
        if index % 500 == 0:
            print(f"olumlu {index}/{len(positives)}")

    for kind in ("adversarial", "speech"):
        clips = sorted((CLIPS / kind).glob("*.wav"))
        for index, path in enumerate(clips):
            clip = trim(read_wav(path))
            pad = lambda: np.zeros(int(SR * rng.uniform(0.3, 1.2)), dtype=np.float32)  # noqa: E731
            audio = np.concatenate([pad(), clip, pad()])
            noisy = augment(audio, rng, background(len(audio), rng, speech))
            train_neg.extend(windows(features(noisy)))
            if index % 1000 == 0:
                print(f"{kind} {index}/{len(clips)}")
    for _ in range(400):
        length = int(SR * rng.uniform(2, 6))
        noise = background(length, rng, speech) * rng.uniform(0.01, 0.6)
        train_neg.extend(windows(features(noise.astype(np.float32))))

    np.save(CACHE / "train_pos.npy", np.asarray(train_pos, dtype=np.float32))
    np.save(CACHE / "train_neg.npy", np.asarray(train_neg, dtype=np.float32))
    np.save(CACHE / "test_pos.npy", np.stack([s for s, _ in test_pos_streams]).astype(np.float32))
    print(f"olumlu {len(train_pos)} · olumsuz {len(train_neg)} · ölçüm {len(test_pos_streams)}")


def generate_traps(seed: int) -> None:
    """Geniş tuzak seti: "Friday" cümle içinde ve "Hey" + benzer sesli isimler."""
    rng = random.Random(seed + 1)
    phrases = FRIDAY_CONTEXTS + [f"hey {name}" for name in HEY_NAMES] + [f"hey, {name}!" for name in HEY_NAMES[:20]]
    for voice, (model, speakers) in EN_VOICES.items():
        count = 2600 if voice == "libritts" else 900
        for length in (0.9, 1.05, 1.2):
            lines = [{"text": rng.choice(phrases), "speaker_id": rng.randrange(speakers),
                      "output_file": str(CLIPS / "traps" / f"{voice}-{length:.2f}-{i:04d}.wav")} for i in range(count // 3)]
            piper(model, lines, length, noise=rng.choice((0.5, 0.667, 0.8)), noise_w=rng.choice((0.6, 0.8, 1.0)))
    for index, model in enumerate(v for v in TR_VOICES if v.exists()):
        for length in (0.8, 0.95, 1.1, 1.25):
            lines = [{"text": text, "output_file": str(CLIPS / "traps" / f"tr{index}-{length:.2f}-{i:03d}.wav")} for i, text in enumerate(TR_FRIDAY_CONTEXTS)]
            piper(model, lines, length)
    print(f"tuzaklar: {len(list((CLIPS / 'traps').glob('*.wav')))}")


def build_traps(seed: int) -> None:
    """Tuzak kliplerini üç farklı bozmayla özelliklere çevirir (CACHE/train_traps.npy)."""
    rng = np.random.default_rng(seed + 1)
    features = Features()
    speech = [trim(read_wav(path)) for path in sorted((CLIPS / "speech").glob("*.wav"))[:600]]
    traps = []
    clips = sorted((CLIPS / "traps").glob("*.wav")) + sorted((CLIPS / "adversarial").glob("*.wav"))
    for index, path in enumerate(clips):
        clip = trim(read_wav(path))
        for _ in range(3):
            pad = lambda: np.zeros(int(SR * rng.uniform(0.3, 1.2)), dtype=np.float32)  # noqa: E731
            audio = np.concatenate([pad(), clip, pad()])
            traps.extend(windows(features(augment(audio, rng, background(len(audio), rng, speech)))))
        if index % 1000 == 0:
            print(f"tuzak {index}/{len(clips)}")
    np.save(CACHE / "train_traps.npy", np.asarray(traps, dtype=np.float32))
    print(f"tuzak pencereleri: {len(traps)}")


# ----------------------------------------------------------------------------------------------------------------------
# Eğitim, ölçüm ve dışa aktarma
# ----------------------------------------------------------------------------------------------------------------------

def train(seed: int, steps: int) -> None:
    import torch
    from torch import nn

    torch.manual_seed(seed)
    device = "cuda" if torch.cuda.is_available() else "cpu"
    positives = torch.tensor(np.load(CACHE / "train_pos.npy"), device=device)
    synthetic = torch.tensor(np.load(CACHE / "train_neg.npy"), device=device)
    traps_file = CACHE / "train_traps.npy"
    traps = torch.tensor(np.load(traps_file), device=device) if traps_file.exists() else synthetic
    validation = np.load(ROOT / "features" / "validation_set_features.npy", mmap_mode="r")
    split = int(len(validation) * 0.7)
    real = torch.tensor(np.asarray(validation[:split]), device=device)
    held_out = np.asarray(validation[split:])
    tests = np.load(CACHE / "test_pos.npy")
    print(f"cihaz {device} · olumlu {len(positives)} · sentetik olumsuz {len(synthetic)} · gerçek ses {split * 0.08 / 3600:.1f} sa")

    model = nn.Sequential(
        nn.Flatten(), nn.Linear(WINDOW * 96, 128), nn.LayerNorm(128), nn.ReLU(), nn.Dropout(0.3),
        nn.Linear(128, 128), nn.LayerNorm(128), nn.ReLU(), nn.Dropout(0.3), nn.Linear(128, 1)).to(device)
    optimizer = torch.optim.AdamW(model.parameters(), lr=1e-3, weight_decay=1e-4)
    schedule = torch.optim.lr_scheduler.OneCycleLR(optimizer, max_lr=1e-3, total_steps=steps, pct_start=0.1)
    loss_fn = nn.BCEWithLogitsLoss(reduction="none")
    starts = torch.arange(0, split - WINDOW, device=device)

    for step in range(steps):
        pos = positives[torch.randint(len(positives), (512,), device=device)]
        neg = synthetic[torch.randint(len(synthetic), (512,), device=device)]
        trap = traps[torch.randint(len(traps), (512,), device=device)]
        offsets = starts[torch.randint(len(starts), (768,), device=device)]
        real_neg = real[offsets[:, None] + torch.arange(WINDOW, device=device)[None, :]]
        x = torch.cat([pos, neg, trap, real_neg])
        y = torch.cat([torch.ones(len(pos), device=device), torch.zeros(len(neg) + len(trap) + len(real_neg), device=device)])
        # Olumsuzların ağırlığı eğitim boyunca artar: önce kelimeyi öğrenir, sonra yanlış uyanmayı bastırır. Tuzaklar
        # ("see you on Friday", "hey Fred") iki kat ağırdır.
        ramp = 1 + 19 * step / steps
        weight = torch.ones_like(y)
        weight[len(pos):] = ramp
        weight[len(pos) + len(neg): len(pos) + len(neg) + len(trap)] = ramp * 2
        loss = (loss_fn(model(x).squeeze(1), y) * weight).mean()
        optimizer.zero_grad()
        loss.backward()
        optimizer.step()
        schedule.step()
        if step % 2000 == 0 or step == steps - 1:
            print(f"adım {step} · kayıp {loss.item():.4f}")

    model.eval()

    def scores(sequence: np.ndarray) -> np.ndarray:
        with torch.no_grad():
            batch = torch.tensor(windows(sequence), device=device)
            return torch.sigmoid(model(batch)).squeeze(1).cpu().numpy() if len(batch) else np.zeros(0)

    held_scores = scores(held_out)
    hours = len(held_out) * 0.08 / 3600
    report = []
    for threshold in (0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9):
        caught = sum(detections(scores(sequence), threshold) > 0 for sequence in tests)
        false = detections(held_scores, threshold)
        report.append({"esik": threshold, "yakalama": round(caught / len(tests), 3), "saatte_yanlis": round(false / hours, 2)})
        print(f"eşik {threshold:.1f} · yakalama %{100 * caught / len(tests):.1f} · saatte yanlış uyanma {false / hours:.2f} ({hours:.1f} sa gerçek ses)")
    (ROOT / "report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    export(model.cpu())


def detections(scores: np.ndarray, threshold: float, hits: int = 2, cooldown: int = 25) -> int:
    """Friday'deki kural: eşiği art arda iki parça geçerse uyanır, sonra 2 sn bekler."""
    count, run, quiet = 0, 0, 0
    for score in scores:
        if quiet > 0:
            quiet -= 1
            run = 0
            continue
        run = run + 1 if score >= threshold else 0
        if run >= hits:
            count += 1
            run = 0
            quiet = cooldown
    return count


def export(model) -> None:
    import onnxruntime as ort
    import torch

    class Scored(torch.nn.Module):
        def __init__(self, inner):
            super().__init__()
            self.inner = inner

        def forward(self, x):
            return torch.sigmoid(self.inner(x))

    target = ROOT / "hey_friday_v0.1.onnx"
    sample = torch.randn(1, WINDOW, 96)
    torch.onnx.export(Scored(model), sample, str(target), input_names=["x"], output_names=["score"], opset_version=13, dynamo=False)
    check = ort.InferenceSession(str(target), providers=["CPUExecutionProvider"])
    expected = torch.sigmoid(model(sample)).detach().numpy()
    actual = check.run(None, {"x": sample.numpy()})[0]
    assert np.allclose(expected, actual, atol=1e-5), "ONNX çıktısı PyTorch ile uyuşmuyor"
    print(f"model: {target} ({target.stat().st_size // 1024} KB)")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("stage", choices=["generate", "build", "traps", "train", "all"], nargs="?", default="all")
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--steps", type=int, default=20_000)
    args = parser.parse_args()
    if args.stage in ("generate", "all"):
        generate(args.seed)
    if args.stage in ("build", "all"):
        build(args.seed)
    if args.stage in ("traps", "all"):
        generate_traps(args.seed)
        build_traps(args.seed)
    if args.stage in ("train", "all"):
        train(args.seed, args.steps)


if __name__ == "__main__":
    sys.exit(main())
