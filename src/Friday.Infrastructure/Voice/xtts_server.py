"""Friday'in yerel doğal ses sunucusu (XTTS v2).

Yalnızca 127.0.0.1'i dinler. Friday her cümleyi POST /tts ile gönderir; ses 24 kHz, 16 bit, mono ham PCM olarak
parça parça (chunked) döner, böylece ilk hece cümlenin tamamı üretilmeden çalmaya başlar.

  GET  /health   {"loaded": bool, "device": "cuda" | "cpu"}
  POST /load     modeli GPU'ya alır (Friday kısayola basılınca çağırır)
  POST /unload   modeli RAM'e taşır, VRAM'i boşaltır (Friday boşta kalınca çağırır)
  POST /tts      {"text", "language", "speaker", "speaker_wav", "speed"} -> ham PCM akışı
"""

import argparse
import json
import os
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import torch
from TTS.tts.configs.xtts_config import XttsConfig
from TTS.tts.models.xtts import Xtts

SAMPLE_RATE = 24_000

_model = None
_device = "cpu"
_latents = {}
_lock = threading.Lock()


def load_model(model_dir):
    global _model
    config = XttsConfig()
    config.load_json(os.path.join(model_dir, "config.json"))
    model = Xtts.init_from_config(config)
    model.load_checkpoint(config, checkpoint_dir=model_dir, use_deepspeed=False, eval=True)
    model.eval()
    _model = model


def to_gpu():
    global _device
    if _device != "cuda" and torch.cuda.is_available():
        _model.cuda()
        _device = "cuda"
        _latents.clear()


def to_cpu():
    global _device
    if _device != "cpu":
        _model.cpu()
        _device = "cpu"
        _latents.clear()
        torch.cuda.empty_cache()


def conditioning(speaker, speaker_wav):
    """Konuşmacı özellikleri: kullanıcının ses örneği (klonlama) ya da XTTS'in hazır seslerinden biri."""
    key = speaker_wav or speaker
    if key in _latents:
        return _latents[key]

    if speaker_wav:
        latent, embedding = _model.get_conditioning_latents(audio_path=[speaker_wav])
    else:
        voice = _model.speaker_manager.speakers[speaker]
        latent, embedding = voice["gpt_cond_latent"], voice["speaker_embedding"]

    latent, embedding = latent.to(_device), embedding.to(_device)
    _latents[key] = (latent, embedding)
    return latent, embedding


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, format, *args):  # noqa: A002 - BaseHTTPRequestHandler imzası
        pass

    def _json(self, status, body):
        data = json.dumps(body).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _read_body(self):
        length = int(self.headers.get("Content-Length") or 0)
        return json.loads(self.rfile.read(length) or b"{}")

    def do_GET(self):
        if self.path == "/health":
            self._json(200, {"loaded": _model is not None, "device": _device, "speakers": len(_model.speaker_manager.speakers)})
        elif self.path == "/speakers":
            self._json(200, sorted(_model.speaker_manager.speakers.keys()))
        else:
            self._json(404, {"error": "not found"})

    def do_POST(self):
        try:
            if self.path == "/load":
                self._read_body()
                with _lock:
                    to_gpu()
                self._json(200, {"device": _device})
            elif self.path == "/unload":
                self._read_body()
                with _lock:
                    to_cpu()
                self._json(200, {"device": _device})
            elif self.path == "/tts":
                self._tts(self._read_body())
            else:
                self._json(404, {"error": "not found"})
        except (BrokenPipeError, ConnectionResetError):
            # Friday konuşmayı kesince bağlantıyı kapatır; beklenen durum.
            pass
        except Exception as error:  # noqa: BLE001 - hata Friday'e iletilir, sunucu ayakta kalır
            try:
                self._json(500, {"error": str(error)})
            except OSError:
                pass

    def _tts(self, request):
        text = request.get("text", "").strip()
        if not text:
            self._json(400, {"error": "empty text"})
            return

        with _lock:
            to_gpu()
            latent, embedding = conditioning(request.get("speaker", ""), request.get("speaker_wav", ""))
            self.send_response(200)
            self.send_header("Content-Type", "application/octet-stream")
            self.send_header("Transfer-Encoding", "chunked")
            self.end_headers()
            with torch.inference_mode():
                chunks = _model.inference_stream(
                    text,
                    request.get("language", "tr"),
                    latent,
                    embedding,
                    stream_chunk_size=20,
                    enable_text_splitting=False,
                    speed=float(request.get("speed", 1.0)),
                    temperature=0.65,
                    repetition_penalty=5.0,
                )
                for chunk in chunks:
                    pcm = (chunk.squeeze().clamp(-1, 1) * 32767).to(torch.int16).cpu().numpy().tobytes()
                    self.wfile.write(f"{len(pcm):X}\r\n".encode("ascii") + pcm + b"\r\n")
                    self.wfile.flush()
            self.wfile.write(b"0\r\n\r\n")
            self.wfile.flush()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", required=True)
    parser.add_argument("--port", type=int, default=11436)
    parser.add_argument("--gpu", action="store_true", help="Açılışta modeli GPU'ya al")
    args = parser.parse_args()

    load_model(args.model_dir)
    if args.gpu:
        to_gpu()

    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    print(f"ready {args.port} {_device}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    sys.exit(main())
