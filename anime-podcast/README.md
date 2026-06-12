# SPOKE — anime for your ears 🚲🎧

Anime you **listen** to. Built for a bike, a run, a commute — real audio with
lockscreen controls so you never glance at a screen.

This repo contains a complete, working prototype:

- **`web/`** — a mobile-first audio player (vanilla JS, no build step, PWA, works
  offline). Deploy it anywhere static.
- **`pipeline/`** — the generator that produced the audio: script → cast of
  neural voices → fully-synthesized sound design + original theme → three mixes.

The demo episode is **Mobile Frame: EXILE — Ep.1 "Ashfall"**, an *original*
mecha story written for this project (Gundam-flavoured, but original characters
and dialogue) so it's free to publish. **No copyrighted audio is used** — every
voice is neural TTS and every explosion, thruster, comms crackle, and musical
note is synthesized from scratch in `pipeline/sfx.py`.

## Three ways to listen — feel the difference

The *same scene* is rendered three completely different ways. Switch styles
mid-listen and you stay at the same moment, so you can A/B the feel:

| Style | What it is | Best for |
|---|---|---|
| **Pure Cut** | Dialogue + sound design, no narrator. Like watching with your eyes closed. | Maximum immersion |
| **Audio-Described** | A narrator paints every visual beat, like audio description for film. | A long ride — you'll never lose the plot |
| **Recap Hosts** | Two hosts react & joke, with clips dropped in. | Catching up, lightest & most fun |

## Listen locally

```bash
cd web
python3 -m http.server 8099   # then open http://localhost:8099
```

> Note: Python's `http.server` doesn't support HTTP Range requests, so scrubbing
> is limited locally. Any real static host (below) supports it. "Save offline"
> in the app caches everything and makes scrubbing work even with no signal.

## Publish it (pick one)

The site is plain static files in `web/`. Fastest paths:

```bash
# Vercel (the old "now")
npx vercel deploy --prod web

# Netlify
npx netlify deploy --prod --dir web

# Surge
npx surge web your-name.surge.sh
```

Or drag the `web/` folder onto <https://app.netlify.com/drop> for an instant URL —
works from a phone.

`vercel.json` and `netlify.toml` are included with correct headers (audio
caching, service-worker scope, MIME types).

## Rebuild the audio

The MP3s are committed under `web/audio/`, so you don't need to rebuild to
deploy. To regenerate (or after editing the script):

```bash
pip install piper-tts numpy scipy imageio-ffmpeg
python3 pipeline/fetch_voices.py     # downloads the 7 neural voices (~500 MB, not committed)
python3 pipeline/build_audio.py      # renders the 3 mixes + transcripts
```

Outputs land in `web/audio/*.mp3` and `web/data/episodes.json` (transcripts +
per-line timestamps that drive the synced transcript in the player).

### Files

| File | Role |
|---|---|
| `pipeline/script_data.py` | The episode — dialogue, cast→voice mapping, and the three style timelines. Edit this to write new episodes. |
| `pipeline/sfx.py` | Procedural sound design + original theme, all numpy. |
| `pipeline/build_audio.py` | Renders voices with Piper, applies robotic/comms processing, lays everything on a ducked timeline, exports MP3 + cue JSON. |
| `web/app.js` | Player: transcript sync, A/B style switching, MediaSession lockscreen controls, offline caching. |

## Make it *your* anime

Point the pipeline at content you have the rights to (your own scripts, public
domain, or properly licensed material) and it renders the same three styles. The
cast, sound palette, and music are all defined in `pipeline/` — swap voices,
add characters, write new episodes in `script_data.py`.

## How it's built

```
script_data.py ──▶ build_audio.py ──▶ web/audio/*.mp3
   (the scene)      │  Piper TTS (7 voices)     web/data/episodes.json (cues)
                    │  + sfx.py sound design          │
                    │  + ducked timeline mix          ▼
                    └────────────────────────▶  web/  (static PWA player)
```

Built with [Piper](https://github.com/rhasspy/piper) for neural TTS; everything
else is numpy + a static `ffmpeg`.
