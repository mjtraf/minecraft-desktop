"""Original deterministic pixel textures and synthesized effects; no Minecraft assets."""
from pathlib import Path
import random, wave, math, struct
from PIL import Image, ImageDraw
root = Path(__file__).resolve().parents[1] / 'Assets'
(root / 'Textures').mkdir(parents=True, exist_ok=True)
(root / 'Audio').mkdir(parents=True, exist_ok=True)
rng = random.Random(41)
palettes = {'stone': (85, 91, 88), 'moss': (75, 96, 58), 'oak': (143, 99, 48), 'timber': (77, 53, 32), 'planks': (132, 96, 54), 'iron': (88, 86, 73), 'water': (41, 105, 116), 'dirt': (87, 71, 50), 'leaves': (69, 100, 52), 'brick': (119, 85, 58)}
for name, rgb in palettes.items():
    image = Image.new('RGB', (16, 16))
    for y in range(16):
        for x in range(16):
            noise = rng.randrange(-12, 13)
            if name in ('oak', 'planks', 'timber'):
                noise += -22 if y % 4 == 0 else (8 if y % 4 == 1 else 0)
                if (x + (y // 4) * 5) % 16 == 0: noise -= 13
            elif name == 'stone':
                if (y == 0 or y == 8) or (x == (0 if y < 8 else 7)): noise -= 15
            elif name == 'brick':
                if y % 6 == 0 or (x + (y // 6) * 7) % 12 == 0: noise -= 29
            elif name == 'water': noise += int(math.sin(x * .7 + y * .9) * 9)
            image.putpixel((x, y), tuple(max(0, min(255, c + noise)) for c in rgb))
    image.save(root / 'Textures' / f'{name}.png')
def sound(name, seconds, sample):
    rate = 22050
    with wave.open(str(root / 'Audio' / f'{name}.wav'), 'wb') as w:
        w.setparams((1, 2, rate, 0, 'NONE', 'not compressed'))
        w.writeframes(b''.join(struct.pack('<h', int(max(-1, min(1, sample(i/rate, seconds))) * 22000)) for i in range(int(seconds*rate))))
sound('step', .15, lambda t, d: (rng.uniform(-1, 1) * .38 + math.sin(t*math.tau*110)*.4) * math.exp(-t*32))
sound('open', .38, lambda t, d: (math.sin(t*math.tau*(210-90*t))*.17 + rng.uniform(-1,1)*.06)*math.sin(math.pi*t/d) + math.sin(t*math.tau*90)*.3*math.exp(-t*40))
sound('close', .22, lambda t, d: (math.sin(t*math.tau*85)*.7+rng.uniform(-1,1)*.15)*math.exp(-t*24))
sound('place', .13, lambda t, d: (math.sin(t*math.tau*160)*.45+rng.uniform(-1,1)*.3)*math.exp(-t*35))
sound('ambience', 12, lambda t, d: (rng.uniform(-1,1)*.035 + math.sin(t*math.tau*55)*.012) * min(1, t*2, (d-t)*2))
print('Generated 10 original textures and 5 sound effects.')
icon = Image.new('RGBA', (32, 32), (0, 0, 0, 0))
draw = ImageDraw.Draw(icon)
draw.rectangle((3, 10, 28, 28), fill='#382b20')
draw.rectangle((5, 12, 26, 26), fill='#a5753a')
draw.rectangle((3, 5, 28, 13), fill='#4d3623')
draw.rectangle((5, 7, 26, 11), fill='#c2914b')
draw.line((5, 19, 26, 19), fill='#8a5f30', width=2)
draw.rectangle((14, 11, 18, 19), fill='#e3d19b')
draw.rectangle((15, 13, 16, 16), fill='#ad945e')
icon.resize((256, 256), Image.Resampling.NEAREST).save(root / 'chest.ico', sizes=[(16,16), (32,32), (48,48), (64,64), (128,128), (256,256)])
icon.resize((128, 128), Image.Resampling.NEAREST).save(root / 'icon.png')
