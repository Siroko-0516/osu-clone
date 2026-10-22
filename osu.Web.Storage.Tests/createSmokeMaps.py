"""Generate tiny, original offline fixtures for all four rulesets."""
import io, math, struct, sys, wave, zipfile
from pathlib import Path
root = Path(sys.argv[1]); root.mkdir(parents=True, exist_ok=True)
sound=io.BytesIO()
with wave.open(sound,'wb') as audio:
    audio.setnchannels(1); audio.setsampwidth(2); audio.setframerate(8000)
    audio.writeframes(b''.join(struct.pack('<h',int(800*math.sin(i*2*math.pi*440/8000))) for i in range(48000)))
for mode in range(4):
    objects='128,192,1000,1,0,0:0:0:0:\n256,192,2000,1,0,0:0:0:0:\n384,192,3000,1,0,0:0:0:0:'
    if mode==0:
        objects='128,192,1000,1,0,0:0:0:0:\n128,192,2000,2,0,L|384:192,1,256\n256,192,4000,1,0,0:0:0:0:'
    if mode==3:
        objects='64,192,1000,1,0,0:0:0:0:\n192,192,2000,128,0,2500:0:0:0:0:\n448,192,3500,1,0,0:0:0:0:'
    data=f'''osu file format v14

[General]
AudioFilename: sound.wav
PreviewTime: 1000
Mode: {mode}

[Metadata]
Title: Browser smoke {mode}
Artist: Test fixture
Creator: Browser tests
Version: Smoke
BeatmapID: 0
BeatmapSetID: -1

[Difficulty]
HPDrainRate: 3
CircleSize: 4
OverallDifficulty: 5
ApproachRate: 5
SliderMultiplier: 1.4
SliderTickRate: 1

[TimingPoints]
0,500,4,2,0,100,1,0

[HitObjects]
{objects}
'''
    with zipfile.ZipFile(root/f'mode-{mode}.osz','w',zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('smoke.osu',data)
        archive.writestr('sound.wav',sound.getvalue())
# A longer object-heavy map exercises decoding/pooling without requiring a long playthrough.
dense = data.replace('Mode: 3', 'Mode: 0').replace('Title: Browser smoke 3', 'Title: Dense loading regression')
dense = dense.split('[HitObjects]')[0] + '[HitObjects]\n' + '\n'.join(
    f'{64 + i % 7 * 64},{96 + i % 3 * 96},{1000 + i * 100},1,0,0:0:0:0:' for i in range(1500))
with zipfile.ZipFile(root/'dense.osz','w',zipfile.ZIP_DEFLATED) as archive:
    archive.writestr('dense.osu',dense)
    archive.writestr('sound.wav',sound.getvalue())
