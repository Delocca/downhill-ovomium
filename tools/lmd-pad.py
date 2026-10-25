#!/usr/bin/env python3
"""Manette virtuelle (uinput, sans root si /dev/uinput est accessible) pour tester les commandes manette.

Crée une « Xbox 360 Controller » (identifiants Microsoft : Wine/Proton la voit comme manette XInput),
attend que le jeu la détecte, joue une suite d'étapes puis la retire.

Usage : tools/lmd-pad.py [--wait S] ÉTAPE…
        tools/lmd-pad.py --serve FICHIER   (garde la manette ; exécute les étapes écrites dans FICHIER,
                                            une ligne d'étapes à la fois ; « quit » pour finir)
  ÉTAPE = boutons+joints:durée, ex. back:0.3 back+a:0.3 none:1 back+b:2
  boutons : a b x y lb rb back start guide l3 r3 (none = rien d'appuyé)

⚠ Le périphérique est visible de tout le système (pas seulement de l'écran virtuel) : appuis brefs,
retiré à la fin, même sur Ctrl+C.
"""
import fcntl
import os
import struct
import sys
import time

EV_SYN, EV_KEY, EV_ABS = 0, 1, 3
BUTTONS = {
    "a": 0x130, "b": 0x131, "x": 0x133, "y": 0x134, "lb": 0x136, "rb": 0x137,
    "back": 0x13A, "start": 0x13B, "guide": 0x13C, "l3": 0x13D, "r3": 0x13E,
}
# Axes : sticks (X, Y, RX, RY), gâchettes (Z, RZ), croix (HAT0X, HAT0Y)
AXES = {0x00: (-32768, 32767), 0x01: (-32768, 32767), 0x03: (-32768, 32767), 0x04: (-32768, 32767),
        0x02: (0, 255), 0x05: (0, 255), 0x10: (-1, 1), 0x11: (-1, 1)}
UI_SET_EVBIT, UI_SET_KEYBIT, UI_SET_ABSBIT = 0x40045564, 0x40045565, 0x40045567
UI_DEV_CREATE, UI_DEV_DESTROY = 0x5501, 0x5502


def create():
    fd = os.open("/dev/uinput", os.O_WRONLY | os.O_NONBLOCK)
    for ev in (EV_KEY, EV_ABS):
        fcntl.ioctl(fd, UI_SET_EVBIT, ev)
    for code in BUTTONS.values():
        fcntl.ioctl(fd, UI_SET_KEYBIT, code)
    absmax, absmin = [0] * 64, [0] * 64
    for code, (lo, hi) in AXES.items():
        fcntl.ioctl(fd, UI_SET_ABSBIT, code)
        absmin[code], absmax[code] = lo, hi
    name = b"Microsoft X-Box 360 pad (Ovomium test)".ljust(80, b"\0")
    dev = name + struct.pack("<HHHHI", 0x03, 0x045E, 0x028E, 0x0114, 0)
    dev += struct.pack("<64i", *absmax) + struct.pack("<64i", *absmin) + struct.pack("<128i", *([0] * 128))
    os.write(fd, dev)
    fcntl.ioctl(fd, UI_DEV_CREATE)
    return fd


def emit(fd, etype, code, value):
    t = time.time()
    os.write(fd, struct.pack("<qqHHi", int(t), int(t % 1 * 1e6), etype, code, value))


def set_buttons(fd, pressed, current):
    for name, code in BUTTONS.items():
        want = name in pressed
        if want != (name in current):
            emit(fd, EV_KEY, code, 1 if want else 0)
    emit(fd, EV_SYN, 0, 0)
    return set(pressed)


def parse(args):
    steps = []
    for step in args:
        keys, dur = step.rsplit(":", 1)
        names = [] if keys == "none" else keys.split("+")
        bad = [n for n in names if n not in BUTTONS]
        if bad:
            raise ValueError(f"bouton inconnu : {bad}")
        steps.append((names, float(dur)))
    return steps


def play(fd, steps):
    current = set()
    for names, dur in steps:
        print(f"{time.strftime('%H:%M:%S')} {'+'.join(names) or 'rien'} pendant {dur} s", flush=True)
        current = set_buttons(fd, names, current)
        time.sleep(dur)
    set_buttons(fd, [], current)


def serve(fd, path):
    while True:
        if os.path.exists(path):
            with open(path) as f:
                line = f.read().strip()
            os.remove(path)
            if line == "quit":
                return
            try:
                play(fd, parse(line.split()))
            except ValueError as e:
                print(e, flush=True)
        time.sleep(0.1)


def main(argv):
    wait, serve_path = 3.0, None
    if argv[:1] == ["--wait"]:
        wait, argv = float(argv[1]), argv[2:]
    if argv[:1] == ["--serve"]:
        serve_path, argv = argv[1], argv[2:]
    if not argv and not serve_path:
        print(__doc__)
        return 1
    steps = parse(argv)
    fd = create()
    print(f"manette virtuelle créée, {wait} s pour la détection", flush=True)
    try:
        time.sleep(wait)
        if serve_path:
            serve(fd, serve_path)
        else:
            play(fd, steps)
        time.sleep(0.3)
    finally:
        fcntl.ioctl(fd, UI_DEV_DESTROY)
        os.close(fd)
        print("manette virtuelle retirée")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
