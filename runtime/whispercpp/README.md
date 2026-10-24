# whisper.cpp Vulkan runtime

Plaats hier optioneel een Windows-build van `whisper.cpp` met Vulkan-ondersteuning:

```text
runtime\whispercpp\whisper-cli.exe
runtime\whispercpp\models\ggml-large-v3-turbo-q5_0.bin
```

Captivox gebruikt de AMD-route primair via FFmpeg Whisper GPU/Vulkan. De helper accepteert een AMD GPU-run pas wanneer Vulkan/GPU-gebruik tijdens de run zelf is bevestigd. Ontbreekt de runtime of het model, dan volgt CPU fallback met expliciete metadata.

## Runtimepin
Voor AMD/Vulkan blijft de runtime gepind op de gevalideerde Windows/Vulkan-route. Gebruik geen ongepinde master voor productietests; nieuwe upstream builds moeten eerst apart door `tools\Test-WhisperCppVulkanRuntime.ps1` en een echte sessierun heen.
