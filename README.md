# Playmaker.Audio

A modern, end-to-end game audio engine on top of OpenAL Soft. This project aims to give .NET / C# games a developer-first audio layer that feels closer to high-level middleware while staying completely open, hackable, and dependency-light.

Currently under early, fast-moving development. Expect breaking API changes, refactors, renames, and sudden new subsystems. I have yet to add eEFX which will be a huge change! If you use this, make sure to pin a commit for now.

## Why it exists

Most OpenAL wrappers stop at “you can play a buffer.” Full middleware stacks go all the way to authoring tools, banks, and data models, powerful but heavy and require inaccessible software to fully utilize. Playmaker.Audio lives in the sweet spot. You get a cohesive runtime engine (device + mixing graph + voice management + decoding + streaming) instead of a pile of loosely-related helpers, and it scales from a game jam prototype (drop in, play a one-shot) to a complex simulation (hierarchical buses, virtualization, streaming, prioritization, ...) without changing paradigms.

## Abstractions

-   Generator: A lightweight object that feeds PCM data to playback. Two broad categories of generators exist as of now:
    -   Static: Whole file decoded upfront into a single buffer
    -   Streaming: Chunks decoded on demand in the background.
        You almost never build these directly; providers resolve them for you.
-   Voice: A playable instance of a generator (you can start, stop, loop, change gain/pitch, position, etc.). Multiple voices can share a static generator.
-   Emitter: A lightweight object you attach to a game entity (e.g. a component). You update the emitter's transform once per frame and any voices you spawn through it inherit and keep following that position/orientation automatically. This lets you fire lots of one‑shots or loops from a moving entity without manually pushing new coordinates to every individual voice.
-   Bus: A node in a hierarchical mixing tree. Change volume/pitch/whatever at a bus and it cascades to everything under it.
-   Provider: Resolves a URI or path into a ready generator (e.g. a file provider that searches asset folders). Lets you introduce custom data sources without changing core engine code.
-   Probably much more that I forgot.

## Quick example

```csharp
var engine = new AudioEngine(new AudioDeviceSettings(Hrtf: true));
var fileProvider = new FileProvider(engine);
fileProvider.AddSearchPath("Assets/Audio");

engine.GeneratorProviders.Register(fileProvider);
engine.GeneratorProviders.DefaultScheme = "file";

engine.PlayOneShot("sfx/explosion.ogg", configure: v => {
    v.SetGain(0.8f);
    v.SetPriority(10);
    v.SetPosition(new Vector3(3, 0, 0));
});
// Oneshots are owned by the engine, so you don't have to manually dispose them.

var ambient = await engine.CreateVoiceAsync("ambient/forest_loop.ogg");
ambient?.SetLooping(true);
ambient?.Play();
// This one is owned by you! You must Dispose it when you're done.

while (gameRunning)
{
    var dt = GetDeltaTime();
    engine.Update(dt);
}

// And when you're done:
engine.Dispose();
```

## License

This project is licensed under Mozilla Public License 2.0. See [LICENSE](LICENSE)

## Attribution & thanks

-   Powered by the phenomenal work in [OpenAL Soft](https://github.com/kcat/openal-soft/). Huge thanks to its contributors!
-   Default decoder implementation uses [libsndfile](https://github.com/libsndfile/libsndfile)

---

Questions / ideas / "this abstraction is weird"? Open an issue and lets talk!
