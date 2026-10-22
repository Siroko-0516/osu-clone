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

# Long mania maps exercise the production loading path, including simultaneous
# notes, holds, legacy format and a full-length compressed media track.
import subprocess
subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-f','lavfi','-i','sine=frequency=220:duration=275','-filter:a','volume=0.02','-b:a','192k',str(root/'regression.mp3')],check=True)
for keys,count in [(4,1310),(4,3024),(7,6699)]:
    chart=data.split('[HitObjects]')[0].replace('v14','v13').replace('Title: Browser smoke 3',f'Title: Long mania {keys}K {count}').replace('CircleSize: 4',f'CircleSize: {keys}').replace('HPDrainRate: 3','HPDrainRate: 8').replace('OverallDifficulty: 5','OverallDifficulty: 8').replace('sound.wav','regression.mp3')
    notes=[]
    for i in range(count):
        t=2133+(i//2)*260000//(count//2)
        x=int((i%keys+0.5)*512/keys)
        notes.append(f'{x},192,{t},128,0,{t+120}:0:0:0:0:' if i%7==0 else f'{x},192,{t},1,0,0:0:0:0:')
    chart+='[HitObjects]\n'+'\n'.join(notes)
    with zipfile.ZipFile(root/f'long-mania-{keys}-{count}.osz','w',zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('long.osu',chart)
        archive.writestr('regression.mp3',(root/'regression.mp3').read_bytes())
